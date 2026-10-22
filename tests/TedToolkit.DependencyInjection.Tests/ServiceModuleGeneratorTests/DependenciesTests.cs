// -----------------------------------------------------------------------
// <copyright file="DependenciesTests.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace TedToolkit.DependencyInjection.Tests;

[Category("Acceptance")]
internal sealed class DependenciesTests
{
    /// <summary>Verifies escaped entry point names read from external module metadata.</summary>
    [Test]
    public async Task Should_call_external_entry_when_metadata_names_keyword_method()
    {
        _ = GeneratorFixture.Run("External", """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            [assembly: GeneratedServiceModule("ExternalEntry", "class")]
            public sealed class Stamp {}
            public static class ExternalEntry
            {
                public static IServiceCollection @class(this IServiceCollection services) => services.AddSingleton(new Stamp());
            }
            """, out var output);
        using var stream = new MemoryStream();
        await Assert.That(output.Emit(stream).Success).IsTrue();
        var external = new GeneratorFixture.CompiledModule(stream.ToArray());
        var root = GeneratorFixture.Compile("KeywordDependency", """
            using Microsoft.Extensions.DependencyInjection;
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddKeywordDependencyModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetRequiredService<Stamp>() is not null;
                }
            }
            """, null, external.Reference);
        await Assert.That(GeneratorFixture.Execute(root, external)).IsTrue();
    }

    /// <summary>
    /// Verifies that a partial configuration hook runs for every module call.
    /// </summary>
    [Test]
    public async Task Should_run_manual_hook_each_time_when_module_is_called_repeatedly()
    {
        var module = GeneratorFixture.Compile("Manual", """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Marker {}
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static partial class ManualModuleExtensions
                {
                    static partial void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton(new Marker());
                    }
                }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddManualModule().AddManualModule();
                    using var provider = services.BuildServiceProvider();
                    return services.Count(item => item.ServiceType == typeof(Marker)) == 2
                        && provider.GetRequiredService<Marker>() is not null;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>
    /// Verifies that TryAdd preserves one default descriptor and a shared scoped instance in a diamond dependency graph.
    /// </summary>
    [Test]
    public async Task Should_preserve_default_services_when_dependency_graph_is_diamond()
    {
        var shared = GeneratorFixture.Compile("Shared", """
            using TedToolkit.DependencyInjection;
            public interface IShared {}
            [Scoped<IShared>] internal sealed class Shared : IShared {}
            """);
        var left = GeneratorFixture.Compile("Left", """
            using TedToolkit.DependencyInjection;
            public interface ILeft { IShared Shared { get; } }
            [Scoped<ILeft>] internal sealed class Left(IShared shared) : ILeft { public IShared Shared { get; } = shared; }
            """, null, shared.Reference);
        var right = GeneratorFixture.Compile("Right", """
            using TedToolkit.DependencyInjection;
            public interface IRight { IShared Shared { get; } }
            [Scoped<IRight>] internal sealed class Right(IShared shared) : IRight { public IShared Shared { get; } = shared; }
            """, null, shared.Reference);
        var root = GeneratorFixture.Compile("Root", """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            public static class Probe
            {
                public static bool Run()
                {
                    var first = new ServiceCollection();
                    var second = new ServiceCollection();
                    first.AddRootModule().AddRootModule();
                    second.AddRootModule();
                    using var provider = first.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    var services = scope.ServiceProvider;
                    return first.Count(item => item.ServiceType == typeof(IShared)) == 1
                        && second.Count(item => item.ServiceType == typeof(IShared)) == 1
                        && ReferenceEquals(services.GetRequiredService<ILeft>().Shared, services.GetRequiredService<IRight>().Shared);
                }
            }
            """, null, shared.Reference, left.Reference, right.Reference);
        await Assert.That(GeneratorFixture.Execute(root, shared, left, right)).IsTrue();
    }

    /// <summary>
    /// Verifies that damaged dependency metadata produces a module entry point diagnostic.
    /// </summary>
    [Test]
    public async Task Should_diagnose_invalid_entry_when_referenced_metadata_is_damaged()
    {
        // Simulate a damaged external module with manually declared invalid entry point metadata.
        _ = GeneratorFixture.Run("Broken", """
            using TedToolkit.DependencyInjection;
            [assembly: GeneratedServiceModule("Missing.Entry", "Register")]
            """, out var output);
        var stream = new MemoryStream();
        await using var streamLifetime = stream.ConfigureAwait(false);

        // Emit only the original tree so generated assembly attributes cannot mask the invalid input.
        var raw = output.RemoveSyntaxTrees(output.SyntaxTrees.Skip(1));
        var emitted = raw.Emit(stream);
        await Assert.That(emitted.Success).IsTrue();
        var reference = Microsoft.CodeAnalysis.MetadataReference.CreateFromImage(stream.ToArray());
        var result = GeneratorFixture.Run("Root", "", out _, null, reference);
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == "TDI009")).IsTrue();
    }
}
