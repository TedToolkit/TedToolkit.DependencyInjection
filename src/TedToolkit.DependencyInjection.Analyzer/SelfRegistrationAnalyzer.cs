using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TedToolkit.DependencyInjection.Analyzer;

/// <summary>Suggests the non-generic lifetime attribute for class self registrations.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SelfRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic identifier for redundant self registration type arguments.</summary>
    public const string DiagnosticId = "TDI013";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Simplify self registration attribute",
        "Use the non-generic '{0}' attribute for self registration",
        "Simplification",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A lifetime attribute without a service type registers the annotated class itself.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        if (attribute.Parent?.Parent is not TypeDeclarationSyntax declaration
            || declaration is not (ClassDeclarationSyntax or RecordDeclarationSyntax)
            || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { TypeKind: TypeKind.Class } implementation
            || context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor)
        {
            return;
        }

        var lifetime = constructor.ContainingType;
        if (lifetime.Arity != 1
            || lifetime.Name is not ("ScopedAttribute" or "SingletonAttribute" or "TransientAttribute")
            || !SymbolEqualityComparer.Default.Equals(lifetime.OriginalDefinition,
                context.Compilation.GetTypeByMetadataName("TedToolkit.DependencyInjection." + lifetime.MetadataName))
            || !SymbolEqualityComparer.Default.Equals(lifetime.TypeArguments[0], implementation))
        {
            return;
        }

        SimpleNameSyntax? name = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            AliasQualifiedNameSyntax qualified => qualified.Name,
            SimpleNameSyntax simple => simple,
            _ => null,
        };
        // Removing conditional arguments could change the contract in another build configuration.
        if (name is GenericNameSyntax generic && !generic.TypeArgumentList.ContainsDirectives)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, generic.TypeArgumentList.GetLocation(), generic.Identifier.ValueText));
        }
    }
}
