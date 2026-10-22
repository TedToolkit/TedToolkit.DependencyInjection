// -----------------------------------------------------------------------
// <copyright file="RegistrationTests.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace TedToolkit.DependencyInjection.Tests;

[Category("Acceptance")]
internal sealed class RegistrationTests
{
    /// <summary>
    /// Verifies all three lifetimes and exactly one disposal for each scoped service instance.
    /// </summary>
    [Test]
    public async Task Should_preserve_lifetimes_and_disposal_when_generated_services_are_resolved()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IDomain {}
            [Scoped<IDomain>] internal sealed class Domain : IDomain {}
            public interface IApp { IDomain Domain { get; } }
            [Scoped<IApp>] internal sealed class App(IDomain domain) : IApp
            { public IDomain Domain { get; } = domain; }
            public interface ITransient {}
            [Transient<ITransient>] internal sealed class Transient : ITransient {}
            public interface ISingleton {}
            [Singleton<ISingleton>] internal sealed class Singleton : ISingleton {}
            public interface IOwned {}
            [Scoped<IOwned>] internal sealed class Owned : IOwned, IDisposable
            { public static int Disposed; public void Dispose() { Disposed++; } }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddOrdersRuntimeModule().AddOrdersRuntimeModule();
                    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                    object domain;
                    using (var scope = provider.CreateScope())
                    {
                        var current = scope.ServiceProvider;
                        domain = current.GetRequiredService<IDomain>();
                        if (!ReferenceEquals(domain, current.GetRequiredService<IApp>().Domain)
                            || ReferenceEquals(current.GetRequiredService<ITransient>(), current.GetRequiredService<ITransient>())
                            || !ReferenceEquals(current.GetRequiredService<ISingleton>(), provider.GetRequiredService<ISingleton>()))
                        { return false; }
                        _ = current.GetRequiredService<IOwned>();
                        _ = current.GetRequiredService<IOwned>();
                    }
                    using var otherScope = provider.CreateScope();
                    return !ReferenceEquals(domain, otherScope.ServiceProvider.GetRequiredService<IDomain>()) && Owned.Disposed == 1;
                }
            }
            """;
        var module = GeneratorFixture.Compile("Orders.Runtime", source);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>
    /// Verifies that module naming uses the project name when the assembly name is overridden.
    /// </summary>
    [Test]
    public async Task Should_use_project_name_when_assembly_name_is_overridden()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    return ReferenceEquals(services, services.AddTedToolkitOrdersHostModule());
                }
            }
            """;
        var module = GeneratorFixture.Compile("Custom.Binary.Identity", source, "TedToolkit.Orders.Host");
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>
    /// Verifies that an implementation is registered only for its explicitly declared contract.
    /// </summary>
    [Test]
    public async Task Should_keep_explicit_contract_when_service_has_multiple_interfaces()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IFirst {}
            public interface ISecond {}
            [Scoped<ISecond>] internal sealed class Example : IFirst, ISecond {}
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddContractChoiceModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetService<IFirst>() is null && provider.GetRequiredService<ISecond>() is not null;
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Contract.Choice", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that a host registration overrides the generated default implementation.
    /// </summary>
    [Test]
    public async Task Should_preserve_host_override_when_module_is_registered()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            [Scoped<IService>] internal sealed class Generated : IService {}
            internal sealed class Custom : IService {}
            public static class Probe
            {
                public static bool Run()
                {
                    var custom = new Custom();
                    var services = new ServiceCollection();
                    services.AddSingleton<IService>(custom);
                    services.AddHostOverrideModule();
                    using var provider = services.BuildServiceProvider();
                    return ReferenceEquals(custom, provider.GetRequiredService<IService>());
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Host.Override", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that generated factories preserve nullable closed generic types and escaped identifiers.
    /// </summary>
    [Test]
    public async Task Should_preserve_qualified_generic_types_when_constructor_parameter_is_nullable()
    {
        const string source = """
            #nullable enable
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            namespace Business.@event
            {
                public interface IValue<T> {}
                [Singleton<IValue<string>>] internal sealed class @class : IValue<string> {}
                [Transient<Consumer>] internal sealed class Consumer(IValue<string?>? value) { public object? Value { get; } = value; }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddGenericConsumerModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetRequiredService<Business.@event.Consumer>().Value is not null;
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Generic.Consumer", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that a project name beginning with a digit produces a valid module type.
    /// </summary>
    [Test]
    public async Task Should_generate_valid_type_when_project_name_starts_with_digit()
    {
        var module = GeneratorFixture.Compile("Digits", """
            using Microsoft.Extensions.DependencyInjection;
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    return ReferenceEquals(services, services.Add123Module());
                }
            }
            """, "123");
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>
    /// Verifies source diagnostics and suppressed output for inaccessible file-local declarations.
    /// </summary>
    [Test]
    [Arguments("TDI003", "[Scoped<Example>] file sealed class Example {}")]
    [Arguments("TDI003", "file class Container { [Scoped<Example>] internal sealed class Example {} }")]
    [Arguments("TDI008", "file static class Configuration { [ModuleConfiguration] internal static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {} }")]
    [Arguments("TDI008", "file static class Container { internal static class Configuration { [ModuleConfiguration] internal static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {} } }")]
    public async Task Should_report_source_diagnostic_when_registration_uses_file_local_type(string expected, string declaration)
    {
        var result = GeneratorFixture.Run("FileLocal", "using TedToolkit.DependencyInjection; " + declaration, out _);

        await Assert.That(result.Diagnostics.Count(diagnostic => diagnostic.Id == expected && diagnostic.Location.IsInSource)).IsEqualTo(1);
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    /// <summary>
    /// Verifies the expected contract, constructor, or module diagnostic for invalid declarations.
    /// </summary>
    [Test]
    [Arguments("TDI003", "[Scoped<Example>] internal abstract class Example {}")]
    [Arguments("TDI004", "[Scoped<object>] internal sealed class Example<T> {}")]
    [Arguments("TDI003", "internal class Container { [Scoped<Example>] private sealed class Example {} }")]
    [Arguments("TDI004", "interface IA {} interface IB {} [Scoped<IA>] internal sealed class Example : IB {}")]
    [Arguments("TDI004", "[Scoped<string>] internal sealed class Example {}")]
    [Arguments("TDI005", "[Scoped<Example>] internal sealed class Example { internal Example() {} internal Example(string value) {} }")]
    [Arguments("TDI008", "internal static class Configuration { [ModuleConfiguration] internal static void Configure() {} }")]
    [Arguments("TDI010", "namespace Microsoft.Extensions.DependencyInjection { public class BadModuleExtensions {} }")]
    public async Task Should_report_targeted_diagnostic_when_registration_is_invalid(string expected, string declaration)
    {
        var result = GeneratorFixture.Run("Bad", "using TedToolkit.DependencyInjection; " + declaration, out _);
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == expected)).IsTrue();
    }
}
