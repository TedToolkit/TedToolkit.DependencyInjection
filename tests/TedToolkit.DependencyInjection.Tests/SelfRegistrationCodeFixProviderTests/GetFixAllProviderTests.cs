using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

using TedToolkit.DependencyInjection.CodeFixes;

namespace TedToolkit.DependencyInjection.Tests.SelfRegistrationCodeFixProviderTests;

internal sealed class GetFixAllProviderTests
{
    /// <summary>Applies the same simplification to all three lifetimes in one document without changing registration options.</summary>
    [Test]
    public async Task Should_simplify_all_self_registrations_when_fix_all_is_requested()
    {
        const string source = """
            using TedToolkit.DependencyInjection;
            [Scoped<Service>(Key = "scoped"), Singleton<Service>, Transient<Service>(Mode = ServiceRegistrationMode.Add)]
            public class Service;
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        var diagnostics = await SimplificationFixture.AnalyzeAsync(document);
        await Assert.That(diagnostics.Length).IsEqualTo(3);
        var provider = new SelfRegistrationCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document,
            nameof(SelfRegistrationCodeFixProvider), provider.FixableDiagnosticIds,
            new DocumentDiagnostics(document.Id, diagnostics), CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        await Assert.That(action).IsNotNull();
        var updated = await SimplificationFixture.ApplyAsync(document, action!);
        await Assert.That((await updated.GetTextAsync()).ToString()).IsEqualTo(source.Replace("<Service>", ""));
        await SimplificationFixture.AssertCompilesAsync(updated);
        await Assert.That(await SimplificationFixture.AnalyzeAsync(updated)).IsEmpty();
    }

    private sealed class DocumentDiagnostics(DocumentId documentId, ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(document.Id == documentId ? diagnostics : ImmutableArray<Diagnostic>.Empty);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
