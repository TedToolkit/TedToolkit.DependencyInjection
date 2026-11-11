using System.Collections.Immutable;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using TedToolkit.DependencyInjection.Analyzer;

namespace TedToolkit.DependencyInjection.CodeFixes;

/// <summary>Removes redundant lifetime attribute type arguments from class self registrations.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SelfRegistrationCodeFixProvider))]
[Shared]
public sealed class SelfRegistrationCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(SelfRegistrationAnalyzer.DiagnosticId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var generic = root?.FindNode(context.Span).FirstAncestorOrSelf<GenericNameSyntax>();
        if (generic?.FirstAncestorOrSelf<AttributeSyntax>() is not { } attribute)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use non-generic lifetime attribute",
                token => SimplifyAsync(context.Document, attribute, generic, token),
                equivalenceKey: nameof(SelfRegistrationCodeFixProvider)),
            context.Diagnostics);
    }

    private static async Task<Document> SimplifyAsync(
        Document document, AttributeSyntax attribute, GenericNameSyntax generic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var marker = new SyntaxAnnotation();
        var identifier = SyntaxFactory.IdentifierName(generic.Identifier)
            .WithTrailingTrivia(generic.Identifier.TrailingTrivia.AddRange(generic.TypeArgumentList.DescendantTrivia()));
        var simplified = attribute.ReplaceNode(generic, identifier)
            .WithAdditionalAnnotations(marker);
        var updated = document.WithSyntaxRoot(root.ReplaceNode(attribute, simplified));
        var updatedRoot = await updated.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await updated.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var updatedAttribute = updatedRoot!.GetAnnotatedNodes(marker).OfType<AttributeSyntax>().Single();
        var metadataName = "TedToolkit.DependencyInjection." + generic.Identifier.ValueText;
        if (!metadataName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            metadataName += "Attribute";
        }

        var target = semanticModel!.Compilation.GetTypeByMetadataName(metadataName);
        if (target is not null
            && semanticModel.GetSymbolInfo(updatedAttribute, cancellationToken).Symbol is IMethodSymbol constructor
            && SymbolEqualityComparer.Default.Equals(constructor.ContainingType, target))
        {
            return updated;
        }

        // A local non-generic attribute may shadow the intended lifetime attribute after simplification.
        var qualified = simplified.WithName(SyntaxFactory.ParseName("global::" + metadataName).WithTriviaFrom(simplified.Name));
        return document.WithSyntaxRoot(root.ReplaceNode(attribute, qualified));
    }
}
