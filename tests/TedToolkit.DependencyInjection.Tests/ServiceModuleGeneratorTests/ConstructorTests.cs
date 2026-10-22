namespace TedToolkit.DependencyInjection.Tests;

internal sealed class ConstructorTests
{
    /// <summary>Verifies that MSDI selects the longest satisfiable public constructor and supplies optional defaults.</summary>
    [Test]
    public async Task Should_use_msdi_constructor_selection_when_public_overloads_exist()
    {
        var module = GeneratorFixture.Compile("Constructors", """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            [Singleton] internal sealed class Dependency {}
            [Scoped] internal sealed class Service
            {
                public int Number { get; }
                public Service() { Number = 1; }
                public Service(Dependency dependency, int number = 42) { Number = number; }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddConstructorsModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<Service>().Number == 42;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies keyed dependency lookup, inherited keys, and injected keys in ordinary public constructors.</summary>
    [Test]
    public async Task Should_inject_keyed_dependencies_when_key_attributes_are_present()
    {
        var module = GeneratorFixture.Compile("Keys", """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            [Singleton(Key = "blue")] internal sealed class Dependency {}
            [Scoped(Key = "blue")] internal sealed class Service(
                [FromKeyedServices] Dependency inherited,
                [FromKeyedServices("blue")] Dependency explicitValue,
                [ServiceKey] string key)
            {
                public bool Valid => ReferenceEquals(inherited, explicitValue) && key == "blue";
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddKeysModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.GetRequiredKeyedService<Service>("blue").Valid;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies that multiple explicit contracts create separate descriptors with normal MSDI instance ownership.</summary>
    [Test]
    public async Task Should_register_each_contract_when_implementation_has_multiple_declarations()
    {
        var module = GeneratorFixture.Compile("Contracts", """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IFirst {}
            public interface ISecond {}
            [Scoped<IFirst>, Scoped<ISecond>]
            internal sealed class Service : IFirst, ISecond, IDisposable
            {
                public static int Disposed;
                public void Dispose() { Disposed++; }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddContractsModule();
                    using var provider = services.BuildServiceProvider();
                    using (var scope = provider.CreateScope())
                    {
                        if (ReferenceEquals(scope.ServiceProvider.GetRequiredService<IFirst>(), scope.ServiceProvider.GetRequiredService<ISecond>())) return false;
                    }
                    return Service.Disposed == 2;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }
}
