namespace TedToolkit.DependencyInjection.Tests;

internal sealed class MetadataTests
{
    /// <summary>
    /// Verifies that compile-time lifetime declarations remain usable while emitted metadata follows the opt-in symbol.
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_emit_lifetime_metadata_only_when_preservation_is_enabled(bool preserve)
    {
        var source = (preserve ? "#define TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES\n" : "") + $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            [Scoped<IService>] internal sealed class Service : IService {}
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddMetadataModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetRequiredService<IService>() is Service
                        && typeof(Service).GetCustomAttributes(false).OfType<ScopedAttribute<IService>>().Any() == {{(preserve ? "true" : "false")}};
                }
            }
            """;

        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Metadata", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that referenced modules with the same normalized project name are diagnosed before generation.
    /// </summary>
    [Test]
    public async Task Should_report_module_collision_when_dependencies_share_project_name()
    {
        var first = GeneratorFixture.Compile("First", "", "Same.Name");
        var second = GeneratorFixture.Compile("Second", "", "Same.Name");
        var result = GeneratorFixture.Run("Root", "", out _, null, first.Reference, second.Reference);

        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == "TDI010")).IsTrue();
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    /// <summary>
    /// Verifies that project names containing no letters or digits produce a diagnostic without generator output.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("...---")]
    public async Task Should_reject_project_name_when_normalized_name_is_empty(string projectName)
    {
        var result = GeneratorFixture.Run("ValidAssembly", "", out _, projectName);

        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == "TDI010")).IsTrue();
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }
}
