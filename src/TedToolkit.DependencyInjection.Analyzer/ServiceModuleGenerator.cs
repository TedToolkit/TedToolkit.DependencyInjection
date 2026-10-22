// -----------------------------------------------------------------------
// <copyright file="ServiceModuleGenerator.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.DependencyInjection.Analyzer;

/// <summary>
/// Generates service registrations and dependency module entry points for the current project.
/// </summary>
[Generator]
public sealed class ServiceModuleGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0, }
                or RecordDeclarationSyntax { AttributeLists.Count: > 0, }
                or MethodDeclarationSyntax { AttributeLists.Count: > 0, }
                or PropertyDeclarationSyntax { AttributeLists.Count: > 0, }
                or VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax { AttributeLists.Count: > 0, } },
            static (syntax, token) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, token))
            .Collect();
        var inputs = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider).Combine(declarations);
        context.RegisterSourceOutput(inputs, static (production, input) =>
        {
            var ((compilation, options), symbols) = input;
            if (compilation.GetTypeByMetadataName(Names.REGISTRATION_ATTRIBUTE) is null)
            {
                return;
            }

            options.GlobalOptions.TryGetValue("build_property.MSBuildProjectName", out var projectName);
            var model = ModuleModel.Create(compilation, projectName ?? compilation.AssemblyName ?? "Project", symbols, production);
            if (model.HasErrors)
            {
                return;
            }

            ModuleEmitter.Emit(model, production);
        });
    }
}
