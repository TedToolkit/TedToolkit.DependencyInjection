using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TedToolkit.DependencyInjection.Tests.SelfRegistrationAnalyzerTests;

internal sealed class SelfRegistrationTests
{
    /// <summary>Preserves conditional contracts that may register a different service type in another build configuration.</summary>
    [Test]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    [Arguments("Transient")]
    public async Task Should_not_suggest_simplification_when_type_arguments_contain_preprocessor_directives(string lifetime)
    {
        var source = $$"""
            using TedToolkit.DependencyInjection;
            public interface IContract {}
            [{{lifetime}}<
            #if CONTRACT
                IContract
            #else
                Service
            #endif
            >]
            public class Service : IContract;
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        await SimplificationFixture.AssertCompilesAsync(document);
        await Assert.That(await SimplificationFixture.AnalyzeAsync(document)).IsEmpty();
        var alternative = document.Project.WithParseOptions(
            ((CSharpParseOptions)document.Project.ParseOptions!).WithPreprocessorSymbols("CONTRACT")).GetDocument(document.Id)!;
        await SimplificationFixture.AssertCompilesAsync(alternative);
        await Assert.That(await SimplificationFixture.AnalyzeAsync(alternative)).IsEmpty();
    }

    /// <summary>Reports an enabled Info suggestion on the redundant type argument for every lifetime.</summary>
    [Test]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    [Arguments("Transient")]
    [Arguments("ScopedAttribute")]
    [Arguments("global::TedToolkit.DependencyInjection.Singleton")]
    [Arguments("di::Transient")]
    public async Task Should_suggest_non_generic_attribute_when_contract_is_the_annotated_class(string lifetime)
    {
        var source = $$"""
            using TedToolkit.DependencyInjection;
            using di = TedToolkit.DependencyInjection;
            using Self = Service;
            [{{lifetime}}<Self>(Key = "primary", Mode = ServiceRegistrationMode.Add)]
            public sealed class Service;
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        await SimplificationFixture.AssertCompilesAsync(document);
        var diagnostics = await SimplificationFixture.AnalyzeAsync(document);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("TDI013");
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostics[0].Descriptor.IsEnabledByDefault).IsTrue();
        await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("<Self>");
    }

    /// <summary>Preserves explicit contracts, member registrations, and unrelated attributes without suggestions.</summary>
    [Test]
    [Arguments("[Scoped] public class Service;")]
    [Arguments("[Scoped(typeof(Service))] public class Service;")]
    [Arguments("public interface IContract {} [Scoped<IContract>] public class Service : IContract;")]
    [Arguments("public class Base {} [Scoped<Base>] public class Service : Base;")]
    [Arguments("namespace Other { public class Service; } [Scoped<Other.Service>] public class Service : Other.Service;")]
    [Arguments("public static class Factory { [Scoped<Service>] public static Service Create() => new(); } public class Service;")]
    [Arguments("public static class Factory { [Singleton<Service>] public static Service Value => new(); } public class Service;")]
    [Arguments("public static class Factory { [Singleton<Service>] public static Service Value = new(); } public class Service;")]
    [Arguments("public class ScopedAttribute<T> : System.Attribute; [Scoped<Service>] public class Service;")]
    [Arguments("public class TransientAttribute<T> : System.Attribute; [Transient<Service>] public struct Service;")]
    public async Task Should_not_suggest_simplification_when_registration_is_outside_class_self_registration(string declaration)
    {
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, "using TedToolkit.DependencyInjection; " + declaration);
        await SimplificationFixture.AssertCompilesAsync(document);
        await Assert.That(await SimplificationFixture.AnalyzeAsync(document)).IsEmpty();
    }

    /// <summary>Reports each source attribute once across nested records and partial class declarations.</summary>
    [Test]
    public async Task Should_report_each_attribute_once_when_classes_are_nested_or_partial()
    {
        const string source = """
            using TedToolkit.DependencyInjection;
            [Scoped<Service>] public partial class Service;
            [Transient<Service>] public partial class Service;
            public class Container { [Singleton<Nested>] public record Nested; }
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        await SimplificationFixture.AssertCompilesAsync(document);
        var diagnostics = await SimplificationFixture.AnalyzeAsync(document);
        await Assert.That(diagnostics.Length).IsEqualTo(3);
        await Assert.That(diagnostics.Select(value => value.Location.SourceSpan).Distinct().Count()).IsEqualTo(3);
    }

    /// <summary>Skips generated source and honors an explicit diagnostic suppression.</summary>
    [Test]
    [Arguments("// <auto-generated/>\n", "Services.cs")]
    [Arguments("", "Services.g.cs")]
    [Arguments("#pragma warning disable TDI013\n", "Services.cs")]
    public async Task Should_not_report_suggestions_when_analysis_is_disabled(string prefix, string path)
    {
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace,
            prefix + "using TedToolkit.DependencyInjection; [Scoped<Service>] public class Service;", path);
        await Assert.That(await SimplificationFixture.AnalyzeAsync(document)).IsEmpty();
    }
}
