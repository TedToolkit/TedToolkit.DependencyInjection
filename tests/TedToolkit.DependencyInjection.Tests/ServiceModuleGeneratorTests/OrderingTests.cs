namespace TedToolkit.DependencyInjection.Tests;

internal sealed class OrderingTests
{
    /// <summary>Verifies local-first defaults, deferred replacements, and final configuration hooks across assembly boundaries.</summary>
    /// <param name="localMode">The local registration operation.</param>
    /// <param name="dependencyMode">The dependency registration operation.</param>
    /// <param name="expectedOwner">The implementation selected by MSDI after registration.</param>
    [Test]
    [Arguments("TryAdd", "TryAdd", "host")]
    [Arguments("Add", "TryAdd", "host")]
    [Arguments("TryAdd", "Add", "dependency")]
    [Arguments("Replace", "TryAdd", "host")]
    [Arguments("Replace", "Add", "host")]
    public async Task Should_respect_registration_phases_when_modules_share_contract(string localMode, string dependencyMode, string expectedOwner)
    {
        var dependency = GeneratorFixture.Compile("Dependency", $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService { string Owner { get; } }
            public static class Log { public static readonly List<string> Calls = new(); }
            [Scoped<IService>(Mode = ServiceRegistrationMode.{{dependencyMode}})]
            internal sealed class DependencyService : IService { public string Owner => "dependency"; }
            internal static class Setup
            {
                [ModuleConfiguration] internal static void Configure(IServiceCollection services) => Log.Calls.Add("dependency");
            }
            """);
        var host = GeneratorFixture.Compile("Host", $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            [Scoped<IService>(Mode = ServiceRegistrationMode.{{localMode}})]
            internal sealed class HostService : IService { public string Owner => "host"; }
            internal static class Setup
            {
                [ModuleConfiguration] internal static void Configure(IServiceCollection services) => Log.Calls.Add("host");
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static partial class HostModuleExtensions
                {
                    static partial void ConfigureServices(IServiceCollection services) => Log.Calls.Add("hook");
                }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddHostModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<IService>().Owner == "{{expectedOwner}}"
                        && Log.Calls.SequenceEqual(new[] { "dependency", "host", "hook" });
                }
            }
            """, null, dependency.Reference);
        await Assert.That(GeneratorFixture.Execute(host, dependency)).IsTrue();
    }

    /// <summary>Verifies that Replace removes only the first matching descriptor and preserves other keys.</summary>
    [Test]
    public async Task Should_follow_msdi_replace_semantics_when_multiple_descriptors_exist()
    {
        var module = GeneratorFixture.Compile("Replacement", """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService { int Kind { get; } }
            internal sealed class Existing(int kind) : IService { public int Kind => kind; }
            [Scoped<IService>(Mode = ServiceRegistrationMode.Replace, Key = "blue")]
            internal sealed class Replacement : IService { public int Kind => 3; }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddKeyedSingleton<IService>("blue", new Existing(1));
                    services.AddKeyedSingleton<IService>("blue", new Existing(2));
                    services.AddKeyedSingleton<IService>("red", new Existing(4));
                    services.AddReplacementModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.GetKeyedServices<IService>("blue").Select(value => value.Kind).SequenceEqual(new[] { 2, 3 })
                        && provider.GetRequiredKeyedService<IService>("red").Kind == 4;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }
}
