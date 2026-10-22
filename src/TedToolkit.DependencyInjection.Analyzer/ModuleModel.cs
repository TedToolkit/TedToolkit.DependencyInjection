// -----------------------------------------------------------------------
// <copyright file="ModuleModel.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.DependencyInjection.Analyzer;

internal sealed class ModuleModel(Compilation compilation, string projectName, SourceProductionContext context)
{
    internal Compilation Compilation { get; } = compilation;

    internal string ComponentName { get; } = string.Concat(projectName.Where(char.IsLetterOrDigit));

    internal string MethodName => "Add" + ComponentName + "Module";

    internal string TypeName => (char.IsDigit(ComponentName[0]) ? "Project" : "") + ComponentName + "ModuleExtensions";

    internal List<ServiceModel> Services { get; } = [];

    internal List<IMethodSymbol> Dependencies { get; } = [];

    internal List<IMethodSymbol> Configurations { get; } = [];

    internal List<AttributeData> Removals { get; } = [];

    internal bool HasErrors { get; private set; }

    internal static ModuleModel Create(
        Compilation compilation,
        string projectName,
        ImmutableArray<ISymbol?> symbols,
        SourceProductionContext context)
    {
        var model = new ModuleModel(compilation, projectName, context);
        if (model.ComponentName.Length == 0)
        {
            model.Report("TDI010", null, "The project name must contain a letter or digit for the module entry point.");
            return model;
        }

        foreach (var symbol in symbols.Distinct(SymbolEqualityComparer.Default))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (symbol is not null)
            {
                ServiceDiscovery.Add(model, symbol);
            }

            if (symbol is IMethodSymbol method)
            {
                model.AddConfiguration(method);
            }
        }

        model.AddDependencies();
        var removalType = compilation.GetTypeByMetadataName(Names.REMOVE_ATTRIBUTE);
        foreach (var attribute in compilation.Assembly.GetAttributes().Where(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, removalType)))
        {
            var key = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "Key").Value;
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not ITypeSymbol contract || !model.IsAccessible(contract)
                || key.Kind == TypedConstantKind.Array)
            {
                model.Report("TDI012", null, "RemoveAll requires an accessible service type and a scalar service key.");
                continue;
            }

            model.Removals.Add(attribute);
        }

        var orderedServices = model.Services.OrderBy(service => service.Declaration.ToDisplayString(), StringComparer.Ordinal).ToArray();
        model.Services.Clear();
        model.Services.AddRange(orderedServices);
        model.Configurations.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.ToDisplayString(), right.ToDisplayString()));
        return model;
    }

    internal void Report(string id, ISymbol? symbol, string message)
    {
        HasErrors = true;
        var rule = new DiagnosticDescriptor(
            id,
            "Invalid service module registration",
            "{0}",
            "TedToolkit.DependencyInjection",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        context.ReportDiagnostic(Diagnostic.Create(rule, symbol?.Locations.FirstOrDefault(location => location.IsInSource), message));
    }

    internal bool IsAccessible(ISymbol symbol)
    {
        for (var type = symbol as INamedTypeSymbol ?? symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsFileLocal)
            {
                return false;
            }
        }

        return Compilation.IsSymbolAccessibleWithin(symbol, Compilation.Assembly);
    }

    private void AddConfiguration(IMethodSymbol method)
    {
        var configurationAttribute = Compilation.GetTypeByMetadataName(Names.CONFIGURATION_ATTRIBUTE);
        if (!method.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, configurationAttribute)))
        {
            return;
        }

        if (!method.IsStatic || method.IsGenericMethod || !method.ReturnsVoid || method.Parameters.Length != 1
            || method.Parameters[0].RefKind != RefKind.None
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, Compilation.GetTypeByMetadataName(Names.COLLECTION))
            || !IsAccessible(method) || ServiceDiscovery.IsOpen(method.ContainingType))
        {
            Report("TDI008", method, "A module configuration method must be accessible, non-generic, static, return void, and accept exactly one IServiceCollection parameter.");
            return;
        }

        Configurations.Add(method);
    }

    private void AddDependencies()
    {
        var moduleAttribute = Compilation.GetTypeByMetadataName(Names.MODULE_ATTRIBUTE);
        if (Compilation.Assembly.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, moduleAttribute)))
        {
            Report("TDI009", null, "Module metadata for the current assembly is owned by the generator and must not be declared manually.");
        }

        var typeNames = new HashSet<string>(StringComparer.Ordinal) { Names.GENERATED_NAMESPACE + "." + TypeName, };
        var assemblies = Compilation.SourceModule.ReferencedAssemblySymbols
            .OrderBy(value => value.Identity.ToString(), StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            var attributes = assembly.GetAttributes()
                .Where(value => SymbolEqualityComparer.Default.Equals(value.AttributeClass, moduleAttribute));
            foreach (var attribute in attributes)
            {
                AddDependency(assembly, attribute, typeNames);
            }
        }

        var existing = Compilation.GetTypeByMetadataName(Names.GENERATED_NAMESPACE + "." + TypeName);
        if (existing is null || (existing.DeclaringSyntaxReferences.Length > 0 && existing.IsStatic
            && existing.DeclaredAccessibility == Accessibility.Public
            && existing.DeclaringSyntaxReferences.All(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))))
        {
            return;
        }

        Report("TDI010", null, "The generated module type conflicts with an existing type. Rename the project or the existing type.");
    }

    private void AddDependency(IAssemblySymbol assembly, AttributeData attribute, HashSet<string> typeNames)
    {
        if (attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not string typeName
            || attribute.ConstructorArguments[1].Value is not string methodName)
        {
            Report("TDI009", null, "The referenced module has invalid entry point metadata: " + assembly.Name);
            return;
        }

        var type = assembly.GetTypeByMetadataName(typeName);
        var methods = type?.GetMembers(methodName).OfType<IMethodSymbol>().Where(IsModuleEntry).ToArray();
        if (methods is not { Length: 1, })
        {
            Report("TDI009", null, "The referenced module must expose exactly one valid public IServiceCollection extension entry point: " + assembly.Name);
        }
        else if (!typeNames.Add(typeName))
        {
            Report("TDI010", null, "Referenced module types conflict. Use distinct project names: " + typeName);
        }
        else
        {
            Dependencies.Add(methods[0]);
        }
    }

    private bool IsModuleEntry(IMethodSymbol method)
    {
        var collection = Compilation.GetTypeByMetadataName(Names.COLLECTION);
        return method.IsStatic && method.IsExtensionMethod && !method.IsGenericMethod
            && method.DeclaredAccessibility == Accessibility.Public
            && method.ContainingType.DeclaredAccessibility == Accessibility.Public && IsAccessible(method)
            && method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, collection)
            && SymbolEqualityComparer.Default.Equals(method.ReturnType, collection);
    }
}
