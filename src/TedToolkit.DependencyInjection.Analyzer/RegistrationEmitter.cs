using Microsoft.CodeAnalysis;

using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;

using static TedToolkit.RoslynHelper.SourceComposer<TedToolkit.DependencyInjection.Analyzer.ServiceModuleGenerator>;

namespace TedToolkit.DependencyInjection.Analyzer;

internal static class RegistrationEmitter
{
    internal static void Add(ServiceModel service, int index, Compilation compilation, TypeDeclaration type, Method entry)
    {
        var descriptorType = DataType.FromSymbol(compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceDescriptor")!, compilation);
        IExpression descriptor;
        var key = service.Key is { } constant ? Constant(constant, compilation) : null;
        if (service.Declaration is IFieldSymbol or IPropertySymbol)
        {
            var instance = DataType.FromSymbol(service.Declaration.ContainingType, compilation).Type.Sub(service.Declaration.Name.ToValidIdentifier());
            var creation = new ObjectCreationExpression(descriptorType).AddArgument(TypeOf(service.Contract, compilation));
            if (key is not null) creation.AddArgument(key);
            descriptor = creation.AddArgument(instance);
        }
        else if (service.Constructor is not null || service.Declaration is IMethodSymbol)
        {
            var factoryName = "CreateService" + index;
            var factory = CreateFactory(service, factoryName, compilation);
            type.AddMember(factory);
            var resultSymbol = service.Implementation.IsValueType || service.Implementation.TypeKind == TypeKind.Dynamic ? compilation.GetSpecialType(SpecialType.System_Object)
                : service.Implementation.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var providerSymbol = compilation.GetTypeByMetadataName(Names.PROVIDER)!;
            var delegateSymbol = key is null
                ? compilation.GetTypeByMetadataName("System.Func`2")!.Construct(providerSymbol, resultSymbol)
                : compilation.GetTypeByMetadataName("System.Func`3")!.Construct(providerSymbol,
                    compilation.GetSpecialType(SpecialType.System_Object).WithNullableAnnotation(NullableAnnotation.Annotated), resultSymbol);
            var factoryDelegate = new ObjectCreationExpression(DataType.FromSymbol(delegateSymbol, compilation)).AddArgument(factoryName.ToSimpleName());
            var creation = new ObjectCreationExpression(descriptorType).AddArgument(TypeOf(service.Contract, compilation));
            if (key is not null) creation.AddArgument(key);
            descriptor = creation.AddArgument(factoryDelegate).AddArgument(Lifetime(service, compilation));
        }
        else
        {
            var call = descriptorType.Type.Sub(key is null ? "Describe" : "DescribeKeyed");
            descriptor = key is null
                ? call.Invoke(TypeOf(service.Contract, compilation), TypeOf(service.Implementation, compilation), Lifetime(service, compilation))
                : call.Invoke(TypeOf(service.Contract, compilation), key, TypeOf(service.Implementation, compilation), Lifetime(service, compilation));
        }

        var extensions = DataType.FromSymbol(compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions")!, compilation);
        entry.AddStatement(extensions.Type.Sub(service.Mode).Invoke("services".ToSimpleName(), descriptor));
    }

    private static IExpression Lifetime(ServiceModel service, Compilation compilation)
    {
        return DataType.FromSymbol(compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime")!, compilation).Type.Sub(service.Lifetime);
    }

    private static Method CreateFactory(ServiceModel service, string name, Compilation compilation)
    {
        var provider = DataType.FromSymbol(compilation.GetTypeByMetadataName(Names.PROVIDER)!, compilation);
        var resultType = service.Implementation.IsValueType || service.Implementation.TypeKind == TypeKind.Dynamic ? DataType.Object
            : DataType.FromSymbol(service.Implementation.WithNullableAnnotation(NullableAnnotation.NotAnnotated), compilation);
        var factory = Method(name, new ReturnType(resultType)).Private.Static.AddParameter(new Parameter(provider, "provider"));
        if (service.Key is not null) factory.AddParameter(new Parameter(DataType.Object.Null, "key"));
        var method = service.Constructor ?? (IMethodSymbol)service.Declaration;
        var arguments = method.Parameters.Select(parameter => Resolve(parameter, service.Key is not null, compilation)).ToArray();
        IExpression creation = service.Constructor is not null
            ? new ObjectCreationExpression(DataType.FromSymbol(service.Implementation, compilation)).AddArguments(arguments)
            : DataType.FromSymbol(method.ContainingType, compilation).Type.Sub(method.Name.ToValidIdentifier()).Invoke(arguments);
        factory.AddStatement(creation.SuppressNullableWarning().Return);
        return factory;
    }

    private static IExpression Resolve(IParameterSymbol parameter, bool keyed, Compilation compilation)
    {
        var type = parameter.Type;
        var keyAttribute = parameter.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString()
            == "Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute");
        IExpression currentKey = keyed ? "key".ToSimpleName() : new CastExpression(DataType.Object.Null, SimpleNameExpression.Null);
        if (keyAttribute is not null) return new CastExpression(DataType.FromSymbol(type, compilation), currentKey.SuppressNullableWarning());
        var fromKey = parameter.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString()
            == "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute");
        IExpression resolved;
        if (fromKey is not null)
        {
            var key = fromKey.ConstructorArguments.Length == 0 ? currentKey : Constant(fromKey.ConstructorArguments[0], compilation);
            var extensions = DataType.FromSymbol(compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions")!, compilation);
            resolved = extensions.Type.Sub(parameter.HasExplicitDefaultValue ? "GetKeyedService" : "GetRequiredKeyedService")
                .Invoke("provider".ToSimpleName(), TypeOf(type, compilation), key);
        }
        else if (type.SpecialType == SpecialType.None && type.ToDisplayString() == Names.PROVIDER)
        {
            return "provider".ToSimpleName();
        }
        else if (parameter.HasExplicitDefaultValue)
        {
            resolved = "provider".ToSimpleName().Sub("GetService").Invoke(TypeOf(type, compilation));
        }
        else
        {
            var extensions = DataType.FromSymbol(compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions")!, compilation);
            resolved = extensions.Type.Sub("GetRequiredService").Invoke("provider".ToSimpleName(), TypeOf(type, compilation));
        }

        if (parameter.HasExplicitDefaultValue)
        {
            var fallback = parameter.ExplicitDefaultValue is null && type.IsValueType
                ? "default".ToSimpleName().Invoke(DataType.FromSymbol(type, compilation).Type)
                : Literal(parameter.ExplicitDefaultValue, type, compilation);
            resolved = resolved.Coalesce(fallback);
        }

        return new CastExpression(DataType.FromSymbol(type, compilation), resolved.Wrap);
    }

    internal static IExpression Constant(TypedConstant constant, Compilation compilation)
    {
        return Literal(constant.Value, constant.Type, compilation);
    }

    private static IExpression Literal(object? value, ITypeSymbol? type, Compilation compilation)
    {
        IExpression expression = value switch
        {
            null => SimpleNameExpression.Null,
            ITypeSymbol symbol => TypeOf(symbol, compilation),
            string text => new LiteralExpression(text),
            char character => new LiteralExpression(character),
            bool boolean => new LiteralExpression(boolean),
            byte number => new LiteralExpression(number),
            sbyte number => new LiteralExpression(number),
            short number => new LiteralExpression(number),
            ushort number => new LiteralExpression(number),
            int number => new LiteralExpression(number),
            uint number => new LiteralExpression(number),
            long number => new LiteralExpression(number),
            ulong number => new LiteralExpression(number),
            float number when float.IsNaN(number) => DataType.Float.Type.Sub("NaN"),
            float number when float.IsPositiveInfinity(number) => DataType.Float.Type.Sub("PositiveInfinity"),
            float number when float.IsNegativeInfinity(number) => DataType.Float.Type.Sub("NegativeInfinity"),
            float number => new LiteralExpression(number),
            double number when double.IsNaN(number) => DataType.Double.Type.Sub("NaN"),
            double number when double.IsPositiveInfinity(number) => DataType.Double.Type.Sub("PositiveInfinity"),
            double number when double.IsNegativeInfinity(number) => DataType.Double.Type.Sub("NegativeInfinity"),
            double number => new LiteralExpression(number),
            decimal number => new LiteralExpression(number),
            _ => throw new InvalidOperationException("Unsupported attribute constant."),
        };
        return type is not null && (type.TypeKind == TypeKind.Enum || type.SpecialType is
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Decimal)
            ? new CastExpression(DataType.FromSymbol(type, compilation), expression) : expression;
    }

    internal static IExpression TypeOf(ITypeSymbol symbol, Compilation compilation)
    {
        IExpression type;
        if (symbol is INamedTypeSymbol named && ServiceDiscovery.IsOpen(named))
        {
            var parts = new Stack<string>();
            for (var current = named.ContainingNamespace; !current.IsGlobalNamespace; current = current.ContainingNamespace)
                parts.Push(current.Name.ToValidIdentifier());
            if (named.ContainingType is not null)
            {
                type = DataType.FromSymbol(named.ContainingType, compilation).Type.Sub(named.Name.ToValidIdentifier());
            }
            else if (parts.Count == 0)
            {
                type = new AliasExpression("global", named.Name.ToValidIdentifier().ToSimpleName());
            }
            else
            {
                type = new AliasExpression("global", parts.Pop().ToSimpleName());
                while (parts.Count > 0) type = type.Sub(parts.Pop());
                type = type.Sub(named.Name.ToValidIdentifier());
            }

            type = type.Generic(Enumerable.Range(0, named.Arity).Select(_ => new DataType(new SimpleNameExpression(""))).ToArray());
        }
        else
        {
            type = symbol.TypeKind == TypeKind.Dynamic ? DataType.Object.Type
                : DataType.FromSymbol(symbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated), compilation).Type;
        }

        // typeof is a language operator; compose its operand structurally through RoslynHelper.
        return "typeof".ToSimpleName().Invoke(type);
    }
}
