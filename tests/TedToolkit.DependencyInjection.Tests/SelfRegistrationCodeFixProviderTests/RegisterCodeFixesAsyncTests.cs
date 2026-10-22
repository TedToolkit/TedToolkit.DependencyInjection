using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

using TedToolkit.DependencyInjection.CodeFixes;

namespace TedToolkit.DependencyInjection.Tests.SelfRegistrationCodeFixProviderTests;

internal sealed class RegisterCodeFixesAsyncTests
{
    /// <summary>Retains comments inside the removed type argument list and between the attribute name and its arguments.</summary>
    [Test]
    public async Task Should_preserve_comments_when_removing_type_arguments()
    {
        const string source = """
            using TedToolkit.DependencyInjection;
            [Scoped/* lifetime */</* self */Service/* contract */>(Key = "primary")]
            public class Service;
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        var actions = new List<CodeAction>();
        var diagnostic = (await SimplificationFixture.AnalyzeAsync(document)).Single();
        await new SelfRegistrationCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var updated = await SimplificationFixture.ApplyAsync(document, actions.Single());
        await Assert.That((await updated.GetTextAsync()).ToString()).IsEqualTo(
            source.Replace("</* self */Service/* contract */>", "/* self *//* contract */"));
        await SimplificationFixture.AssertCompilesAsync(updated);
    }

    /// <summary>Removes only the redundant type argument while preserving lifetime, options, comments, and generated registrations.</summary>
    [Test]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    [Arguments("Transient")]
    [Arguments("ScopedAttribute")]
    [Arguments("global::TedToolkit.DependencyInjection.Singleton")]
    [Arguments("di::Transient")]
    public async Task Should_preserve_registration_when_self_registration_is_simplified(string lifetime)
    {
        var source = $$"""
            using TedToolkit.DependencyInjection;
            using di = TedToolkit.DependencyInjection;
            // Register the primary instance.
            [{{lifetime}}<Service>(Key = "primary", Mode = ServiceRegistrationMode.Add)] // Keep these options.
            public class Service;
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        var diagnostics = await SimplificationFixture.AnalyzeAsync(document);
        var actions = new List<CodeAction>();
        await new SelfRegistrationCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostics.Single(), (action, _) => actions.Add(action), CancellationToken.None));
        await Assert.That(actions.Count).IsEqualTo(1);
        var updated = await SimplificationFixture.ApplyAsync(document, actions.Single());
        var fixedSource = (await updated.GetTextAsync()).ToString();
        await Assert.That(fixedSource).IsEqualTo(source.Replace("<Service>", ""));
        await SimplificationFixture.AssertCompilesAsync(updated);
        await Assert.That(await SimplificationFixture.AnalyzeAsync(updated)).IsEmpty();

        var before = GeneratorFixture.Run("Simplification", source, out _);
        var after = GeneratorFixture.Run("Simplification", fixedSource, out _);
        await Assert.That(before.Diagnostics).IsEmpty();
        await Assert.That(after.Diagnostics).IsEmpty();
        await Assert.That(after.GeneratedTrees.Select(tree => tree.ToString()).ToArray())
            .IsEquivalentTo(before.GeneratedTrees.Select(tree => tree.ToString()).ToArray());
    }

    /// <summary>Qualifies the replacement when a local non-generic attribute would change attribute binding.</summary>
    [Test]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    [Arguments("Transient")]
    public async Task Should_qualify_attribute_when_a_local_attribute_shadows_the_non_generic_lifetime(string lifetime)
    {
        var source = $$"""
            using TedToolkit.DependencyInjection;
            public class {{lifetime}}Attribute : System.Attribute;
            [{{lifetime}}<Service>] public class Service;
            """;
        using var workspace = new AdhocWorkspace();
        var document = SimplificationFixture.CreateDocument(workspace, source);
        await SimplificationFixture.AssertCompilesAsync(document);
        var actions = new List<CodeAction>();
        var diagnostic = (await SimplificationFixture.AnalyzeAsync(document)).Single();
        await new SelfRegistrationCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var updated = await SimplificationFixture.ApplyAsync(document, actions.Single());
        var fixedSource = (await updated.GetTextAsync()).ToString();
        await Assert.That(fixedSource).IsEqualTo(source.Replace(lifetime + "<Service>",
            "global::TedToolkit.DependencyInjection." + lifetime + "Attribute"));
        await SimplificationFixture.AssertCompilesAsync(updated);
        var before = GeneratorFixture.Run("Simplification", source, out _);
        var after = GeneratorFixture.Run("Simplification", fixedSource, out _);
        await Assert.That(after.GeneratedTrees.Select(tree => tree.ToString()).ToArray())
            .IsEquivalentTo(before.GeneratedTrees.Select(tree => tree.ToString()).ToArray());
    }
}
