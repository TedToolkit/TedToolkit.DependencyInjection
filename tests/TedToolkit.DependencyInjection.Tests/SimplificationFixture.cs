using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using TedToolkit.DependencyInjection.Analyzer;

namespace TedToolkit.DependencyInjection.Tests;

internal static class SimplificationFixture
{
    internal static Document CreateDocument(AdhocWorkspace workspace, string source, string path = "Services.cs")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "Simplification", "Simplification", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest), metadataReferences: references));
        return workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project.Id), path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())), filePath: path));
    }

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        return await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SelfRegistrationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    internal static async Task<Document> ApplyAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
    }

    internal static async Task AssertCompilesAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        await Assert.That(compilation!.GetDiagnostics().Where(value => value.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
    }
}
