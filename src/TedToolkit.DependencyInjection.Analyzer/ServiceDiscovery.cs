using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TedToolkit.DependencyInjection.Analyzer;

internal static class ServiceDiscovery
{
    internal static void Add(ModuleModel model, ISymbol declaration)
    {
        foreach (var attribute in declaration.GetAttributes().Where(value => IsLifetime(model.Compilation, value)))
        {
            AddRegistration(model, declaration, attribute);
        }
    }

    private static void AddRegistration(ModuleModel model, ISymbol declaration, AttributeData attribute)
    {
        var implementation = declaration switch
        {
            INamedTypeSymbol type => type,
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        if (implementation is null || !model.IsAccessible(declaration) || !IsSupportedDeclaration(model, declaration))
        {
            model.Report("TDI003", declaration, "A registration requires an accessible concrete class, static factory, or static singleton instance member.");
            return;
        }

        var lifetime = attribute.AttributeClass!.Name.Replace("Attribute", "");
        if (declaration is IPropertySymbol or IFieldSymbol && lifetime != "Singleton")
        {
            model.Report("TDI005", declaration, "Existing instances must use Singleton. Use a static method for scoped or transient factories.");
            return;
        }

        var contract = attribute.AttributeClass.TypeArguments.FirstOrDefault()
            ?? attribute.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol
            ?? implementation;
        if (declaration is INamedTypeSymbol open && IsOpen(open) && SymbolEqualityComparer.Default.Equals(contract, open))
        {
            contract = open.ConstructUnboundGenericType();
        }

        if (!model.IsAccessible(contract) || !IsAssignable(model.Compilation, implementation, contract))
        {
            model.Report("TDI004", declaration, "The service contract must be accessible and assignable from the implementation or member result. Open generic contracts must preserve the implementation's generic parameters in order.");
            return;
        }

        var modeValue = attribute.NamedArguments.FirstOrDefault(value => value.Key == "Mode").Value.Value;
        var mode = (modeValue is int value ? value : 0) switch
        {
            0 => "TryAdd",
            1 => "Add",
            2 => "TryAddEnumerable",
            3 => "Replace",
            _ => null,
        };
        if (mode is null)
        {
            model.Report("TDI011", declaration, "The registration mode must be Add, TryAdd, TryAddEnumerable, or Replace.");
            return;
        }

        var keyArgument = attribute.NamedArguments.FirstOrDefault(value => value.Key == "Key").Value;
        if (keyArgument.Kind == TypedConstantKind.Array
            || keyArgument.Type is not null && !model.IsAccessible(keyArgument.Type)
            || keyArgument.Kind == TypedConstantKind.Type && keyArgument.Value is ITypeSymbol keyType && !model.IsAccessible(keyType))
        {
            model.Report("TDI012", declaration, "Attribute service keys must be scalar constants, enums, or types. Configure runtime object keys with ModuleConfiguration.");
            return;
        }

        TypedConstant? key = keyArgument.IsNull || keyArgument.Value is null ? null : keyArgument;
        IMethodSymbol? constructor = null;
        if (declaration is INamedTypeSymbol service && !service.InstanceConstructors.Any(value => value.DeclaredAccessibility == Accessibility.Public))
        {
            var constructors = service.InstanceConstructors.Where(model.IsAccessible).ToArray();
            if (IsOpen(service) || constructors.Length != 1 || !SupportsParameters(constructors[0]) || !InitializesRequiredMembers(service, constructors[0]))
            {
                model.Report("TDI005", declaration, "A class without public constructors needs one accessible constructor without by-reference or ref-like parameters, and must initialize required members. Open generic implementations need public constructors.");
                return;
            }

            constructor = constructors[0];
        }

        model.Services.Add(new ServiceModel(declaration, implementation, contract, lifetime, mode, key, constructor));
    }

    private static bool IsSupportedDeclaration(ModuleModel model, ISymbol declaration)
    {
        return declaration switch
        {
            INamedTypeSymbol type => type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic
                && (type.ContainingType is null || !IsOpen(type.ContainingType)),
            IMethodSymbol method => method.IsStatic && !method.IsGenericMethod && !method.ReturnsVoid
                && !method.ReturnsByRef && !method.ReturnsByRefReadonly && !IsOpen(method.ContainingType)
                && SupportsParameters(method) && !method.ReturnType.IsRefLikeType,
            IPropertySymbol property => property.IsStatic && !property.IsIndexer && property.GetMethod is { } getter
                && model.IsAccessible(getter) && !property.ReturnsByRef && !property.ReturnsByRefReadonly
                && !IsOpen(property.ContainingType) && !property.Type.IsRefLikeType,
            IFieldSymbol field => field.IsStatic && !IsOpen(field.ContainingType) && !field.Type.IsRefLikeType,
            _ => false,
        };
    }

    private static bool SupportsParameters(IMethodSymbol method)
    {
        return method.Parameters.All(parameter => parameter.RefKind == RefKind.None && !parameter.Type.IsRefLikeType
            && parameter.Type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer));
    }

    private static bool InitializesRequiredMembers(INamedTypeSymbol type, IMethodSymbol constructor)
    {
        if (constructor.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
        {
            return true;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsOpen(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsUnboundGenericType || current.TypeArguments.Any(IsOpenArgument))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOpenArgument(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.TypeParameter
            || type is INamedTypeSymbol named && IsOpen(named)
            || type is IArrayTypeSymbol array && IsOpenArgument(array.ElementType);
    }

    private static bool IsAssignable(Compilation compilation, ITypeSymbol implementation, ITypeSymbol contract)
    {
        if (implementation is INamedTypeSymbol open && IsOpen(open))
        {
            if (contract is not INamedTypeSymbol { IsUnboundGenericType: true } generic || generic.Arity != open.Arity)
            {
                return false;
            }

            return MatchesOpenContract(open, generic, open.TypeParameters)
                || open.AllInterfaces.Any(value => MatchesOpenContract(value, generic, open.TypeParameters))
                || HasBaseType(open, generic);
        }

        var conversion = ((CSharpCompilation)compilation).ClassifyConversion(implementation, contract);
        return conversion.IsImplicit && (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing
            || contract is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && SymbolEqualityComparer.Default.Equals(implementation, nullable.TypeArguments[0]));
    }

    private static bool MatchesOpenContract(INamedTypeSymbol candidate, INamedTypeSymbol contract, System.Collections.Immutable.ImmutableArray<ITypeParameterSymbol> parameters)
    {
        return SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, contract.OriginalDefinition)
            && candidate.TypeArguments.Select((argument, index) => SymbolEqualityComparer.Default.Equals(argument, parameters[index])).All(matches => matches);
    }

    private static bool HasBaseType(INamedTypeSymbol implementation, INamedTypeSymbol contract)
    {
        for (var current = implementation.BaseType; current is not null; current = current.BaseType)
        {
            if (MatchesOpenContract(current, contract, implementation.TypeParameters))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLifetime(Compilation compilation, AttributeData attribute)
    {
        return attribute.AttributeClass is { } type && type.Name is "ScopedAttribute" or "TransientAttribute" or "SingletonAttribute"
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
                compilation.GetTypeByMetadataName("TedToolkit.DependencyInjection." + type.MetadataName));
    }
}
