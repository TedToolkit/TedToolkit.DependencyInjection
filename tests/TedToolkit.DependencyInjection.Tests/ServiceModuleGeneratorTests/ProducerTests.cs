namespace TedToolkit.DependencyInjection.Tests;

internal sealed class ProducerTests
{
    /// <summary>Verifies factory lifetime and disposal across repeated resolutions and separate scopes.</summary>
    /// <param name="lifetime">The requested factory lifetime.</param>
    /// <param name="keyed">Whether the factory has a service key.</param>
    [Test]
    [Arguments("Singleton", false)]
    [Arguments("Scoped", false)]
    [Arguments("Transient", false)]
    [Arguments("Singleton", true)]
    [Arguments("Scoped", true)]
    [Arguments("Transient", true)]
    public async Task Should_preserve_factory_lifetime_when_resolving_across_scopes(string lifetime, bool keyed)
    {
        var module = GeneratorFixture.Compile("FactoryLifetimes", $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            internal sealed class Service : IService, IDisposable
            {
                internal static int Disposed;
                public void Dispose() { Disposed++; }
            }
            internal static class Producers
            {
                [{{lifetime}}<IService>({{(keyed ? "Key = \"blue\"" : "")}})]
                internal static Service Create() => new();
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddFactoryLifetimesModule();
                    using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
                    {
                        using (var first = provider.CreateScope())
                        using (var second = provider.CreateScope())
                        {
                            var a = first.ServiceProvider.{{(keyed ? "GetRequiredKeyedService<IService>(\"blue\")" : "GetRequiredService<IService>()")}};
                            var b = first.ServiceProvider.{{(keyed ? "GetRequiredKeyedService<IService>(\"blue\")" : "GetRequiredService<IService>()")}};
                            var c = second.ServiceProvider.{{(keyed ? "GetRequiredKeyedService<IService>(\"blue\")" : "GetRequiredService<IService>()")}};
                            if (ReferenceEquals(a, b) != {{(lifetime != "Transient" ? "true" : "false")}}
                                || ReferenceEquals(a, c) != {{(lifetime == "Singleton" ? "true" : "false")}}) return false;
                        }
                        if (Service.Disposed != {{(lifetime == "Singleton" ? 0 : lifetime == "Scoped" ? 2 : 3)}}) return false;
                    }
                    return Service.Disposed == {{(lifetime == "Singleton" ? 1 : lifetime == "Scoped" ? 2 : 3)}};
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies that service contracts require runtime assignability rather than numeric conversion.</summary>
    /// <param name="declaration">The invalid factory or instance member.</param>
    [Test]
    [Arguments("[Singleton(typeof(long))] internal static int Create() => 42;")]
    [Arguments("[Singleton(typeof(long))] internal static int Shared { get; } = 42;")]
    [Arguments("[Singleton(typeof(long))] internal static readonly int Shared = 42;")]
    public async Task Should_reject_numeric_conversion_when_member_result_is_not_assignable(string declaration)
    {
        var result = GeneratorFixture.Run("Numeric", "using TedToolkit.DependencyInjection; internal static class Producers { " + declaration + " }", out _);
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == "TDI004")).IsTrue();
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    /// <summary>Verifies valid boxing and nullable wrapping for factory service contracts.</summary>
    /// <param name="contract">The runtime compatible contract.</param>
    [Test]
    [Arguments("object")]
    [Arguments("System.IComparable")]
    [Arguments("int?")]
    public async Task Should_preserve_boxed_value_when_contract_is_runtime_compatible(string contract)
    {
        var module = GeneratorFixture.Compile("Boxed", $$"""
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            internal static class Producers
            {
                [Singleton(typeof({{contract}}))] internal static int Create() => 42;
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddBoxedModule();
                    using var provider = services.BuildServiceProvider();
                    return Equals(provider.GetRequiredService<{{contract}}>(), 42);
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies that an explicit keyed provider parameter takes precedence over the current-provider shortcut.</summary>
    /// <param name="factory">Whether to inject a factory instead of an internal constructor.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_resolve_requested_key_when_parameter_is_service_provider(bool factory)
    {
        var module = GeneratorFixture.Compile("ProviderKey", $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            {{(factory ? "" : "[Singleton]")}}
            internal sealed class Service
            {
                internal Service({{(factory ? "" : "[FromKeyedServices(\"external\")]")}} IServiceProvider other) { Other = other; }
                internal IServiceProvider Other { get; }
            }
            internal static class Setup
            {
                internal static readonly IServiceProvider External = new ServiceCollection().BuildServiceProvider();
                [ModuleConfiguration]
                internal static void Configure(IServiceCollection services) => services.AddKeyedSingleton<IServiceProvider>("external", External);
                {{(factory ? "[Singleton] internal static Service Create([FromKeyedServices(\"external\")] IServiceProvider other) => new(other);" : "")}}
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddProviderKeyModule();
                    using var provider = services.BuildServiceProvider();
                    return ReferenceEquals(provider.GetRequiredService<Service>().Other, Setup.External);
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies static factories, injected providers, keyed parameters, optional values, and container-owned factory results.</summary>
    /// <param name="keyed">Whether the factory is keyed.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_invoke_static_factory_when_service_is_resolved(bool keyed)
    {
        var source = $$"""
            #nullable enable
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public sealed class Dependency {}
            public interface IService { int Number { get; } object? Key { get; } IServiceProvider Provider { get; } }
            internal sealed class Service(IServiceProvider provider, object? key, Dependency dependency, int number) : IService, IDisposable
            {
                public static int Disposed;
                public int Number => number;
                public object? Key => key;
                public IServiceProvider Provider => provider;
                public Dependency Dependency => dependency;
                public void Dispose() { Disposed++; }
            }
            internal static class Producers
            {
                [Singleton<Dependency>(Key = "dependency")]
                internal static readonly Dependency Shared = new();
                [Scoped<IService>(Mode = ServiceRegistrationMode.TryAddEnumerable{{(keyed ? ", Key = \"blue\"" : "")}})]
                internal static Service Create(IServiceProvider provider, {{(keyed ? "[ServiceKey] object? key," : "")}}
                    [FromKeyedServices("dependency")] Dependency dependency, int number = 42)
                    => new(provider, {{(keyed ? "key" : "null")}}, dependency, number);
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddProducersModule().AddProducersModule();
                    using var provider = services.BuildServiceProvider();
                    using (var scope = provider.CreateScope())
                    {
                        var service = scope.ServiceProvider.{{(keyed ? "GetRequiredKeyedService<IService>(\"blue\")" : "GetRequiredService<IService>()")}};
                        if (service.Number != 42 || !ReferenceEquals(service.Provider, scope.ServiceProvider)
                            || !ReferenceEquals(((Service)service).Dependency, Producers.Shared)
                            || !Equals(service.Key, {{(keyed ? "\"blue\"" : "null")}})) return false;
                    }
                    return Service.Disposed == 1;
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Producers", source))).IsTrue();
    }

    /// <summary>Verifies that attributed singleton fields and properties register existing instances without transferring disposal ownership.</summary>
    /// <param name="property">Whether to annotate a property instead of a field.</param>
    /// <param name="keyed">Whether to register a service key.</param>
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Should_preserve_instance_ownership_when_singleton_member_is_registered(bool property, bool keyed)
    {
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            internal sealed class Instance : IService, IDisposable
            {
                public bool Disposed;
                public void Dispose() { Disposed = true; }
            }
            internal static class Producers
            {
                [Singleton<IService>({{(keyed ? "Key = \"blue\"" : "")}})]
                internal static {{(property ? "Instance Shared { get; } = new();" : "readonly Instance Shared = new();")}}
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddInstancesModule();
                    using (var provider = services.BuildServiceProvider())
                    {
                        if (!ReferenceEquals(Producers.Shared, provider.{{(keyed ? "GetRequiredKeyedService<IService>(\"blue\")" : "GetRequiredService<IService>()")}})) return false;
                    }
                    return !Producers.Shared.Disposed;
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Instances", source))).IsTrue();
    }

    /// <summary>Verifies non-generic declarations and boxed value registrations from static factories.</summary>
    [Test]
    public async Task Should_register_value_service_when_non_generic_factory_is_annotated()
    {
        var module = GeneratorFixture.Compile("Values", """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            internal static class Producers
            {
                [Singleton(typeof(int))] internal static int Create() => 42;
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddValuesModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetRequiredService<int>() == 42;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }
}
