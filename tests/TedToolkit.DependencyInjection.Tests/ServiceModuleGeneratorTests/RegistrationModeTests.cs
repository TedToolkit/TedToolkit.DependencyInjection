namespace TedToolkit.DependencyInjection.Tests;

internal sealed class RegistrationModeTests
{
    /// <summary>Verifies that indistinguishable implementation identities keep MSDI's normal TryAddEnumerable rejection.</summary>
    /// <param name="factory">Whether the indistinguishable descriptor comes from an interface-returning factory.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_propagate_msdi_error_when_enumerable_implementation_is_indistinguishable(bool factory)
    {
        var module = GeneratorFixture.Compile("Indistinguishable", $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            {{(factory ? "" : "[Singleton(Mode = ServiceRegistrationMode.TryAddEnumerable)]")}}
            internal sealed class Service : IService {}
            internal static class Producers
            {
                {{(factory ? "[Singleton<IService>(Mode = ServiceRegistrationMode.TryAddEnumerable)] internal static IService Create() => new Service();" : "")}}
            }
            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        new ServiceCollection().AddIndistinguishableModule();
                        return false;
                    }
                    catch (ArgumentException) { return true; }
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies that each registration mode matches MSDI descriptor ordering for keyed and unkeyed services.</summary>
    /// <param name="lifetime">The requested lifetime.</param>
    /// <param name="mode">The descriptor registration operation.</param>
    /// <param name="keyed">Whether to register an enum service key.</param>
    [Test]
    [Arguments("Transient", "TryAdd", false)]
    [Arguments("Scoped", "TryAdd", false)]
    [Arguments("Singleton", "TryAdd", false)]
    [Arguments("Transient", "Add", false)]
    [Arguments("Scoped", "Add", false)]
    [Arguments("Singleton", "Add", false)]
    [Arguments("Transient", "TryAddEnumerable", false)]
    [Arguments("Scoped", "TryAddEnumerable", false)]
    [Arguments("Singleton", "TryAddEnumerable", false)]
    [Arguments("Transient", "Replace", false)]
    [Arguments("Scoped", "Replace", false)]
    [Arguments("Singleton", "Replace", false)]
    [Arguments("Transient", "TryAdd", true)]
    [Arguments("Scoped", "TryAdd", true)]
    [Arguments("Singleton", "TryAdd", true)]
    [Arguments("Transient", "Add", true)]
    [Arguments("Scoped", "Add", true)]
    [Arguments("Singleton", "Add", true)]
    [Arguments("Transient", "TryAddEnumerable", true)]
    [Arguments("Scoped", "TryAddEnumerable", true)]
    [Arguments("Singleton", "TryAddEnumerable", true)]
    [Arguments("Transient", "Replace", true)]
    [Arguments("Scoped", "Replace", true)]
    [Arguments("Singleton", "Replace", true)]
    public async Task Should_match_msdi_when_registration_mode_is_selected(string lifetime, string mode, bool keyed)
    {
        var expected = mode switch
        {
            "TryAdd" => "0",
            "Add" => "0, 1, 2, 1, 2",
            "TryAddEnumerable" => "0, 1, 2",
            _ => "2",
        };
        var source = $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public enum Key { Blue }
            public interface IService { int Kind { get; } }
            internal sealed class Host : IService { public int Kind => 0; }
            [{{lifetime}}<IService>(Mode = ServiceRegistrationMode.{{mode}}{{(keyed ? ", Key = Key.Blue" : "")}})]
            internal sealed class AFirst : IService { public int Kind => 1; }
            [{{lifetime}}<IService>(Mode = ServiceRegistrationMode.{{mode}}{{(keyed ? ", Key = Key.Blue" : "")}})]
            internal sealed class BSecond : IService { public int Kind => 2; }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    {{(keyed ? "services.AddKeyedSingleton<IService>(Key.Blue, new Host());" : "services.AddSingleton<IService>(new Host());")}}
                    services.AddModesModule().AddModesModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.{{(keyed ? "GetKeyedServices<IService>(Key.Blue)" : "GetServices<IService>()")}}
                        .Select(value => value.Kind).SequenceEqual(new[] { {{expected}} });
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Modes", source))).IsTrue();
    }

    /// <summary>Verifies that generated internal-constructor factories retain implementation identity for TryAddEnumerable.</summary>
    /// <param name="keyed">Whether the implementation has a service key.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_deduplicate_factories_when_try_add_enumerable_is_selected(bool keyed)
    {
        var source = $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            [Scoped<IService>(Mode = ServiceRegistrationMode.TryAddEnumerable{{(keyed ? ", Key = \"blue\"" : "")}})]
            internal sealed class Service : IService { internal Service() {} }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddFactoriesModule().AddFactoriesModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.{{(keyed ? "GetKeyedServices<IService>(\"blue\")" : "GetServices<IService>()")}}.Count() == 1;
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Factories", source))).IsTrue();
    }
}
