// -----------------------------------------------------------------------
// <copyright file="TryAddTests.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace TedToolkit.DependencyInjection.Tests;

/// <summary>
/// Compiles and runs consumer code to verify default and appended service registrations.
/// </summary>
[Category("Acceptance")]
internal sealed class TryAddTests
{
    /// <summary>
    /// Verifies that repeated module calls append every implementation through ordinary Add registrations.
    /// </summary>
    /// <param name="lifetime">The lifetime to verify.</param>
    [Test]
    [Arguments("Transient")]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    public async Task Should_append_implementations_again_when_module_is_called_twice(string lifetime)
    {
        var source = $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IReader { int Kind { get; } }
            [{{lifetime}}<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class First : IReader { public int Kind => 1; }
            [{{lifetime}}<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class Second : IReader { public int Kind => 2; }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddRepeatedReadersModule().AddRepeatedReadersModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return services.Count == 4
                        && scope.ServiceProvider.GetServices<IReader>().Select(reader => reader.Kind).Order()
                            .SequenceEqual(new[] { 1, 1, 2, 2 });
                }
            }
            """;

        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Repeated.Readers", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that appended implementations preserve the host registration and declared lifetimes.
    /// </summary>
    /// <param name="lifetime">The lifetime to verify.</param>
    [Test]
    [Arguments("Transient")]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    public async Task Should_append_all_implementations_when_try_add_is_disabled(string lifetime)
    {
        var source = $$"""
            using System;
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IReader { int Kind { get; } }
            internal sealed class Host : IReader { public int Kind => 0; }
            [{{lifetime}}<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class First : IReader { public int Kind => 1; }
            [{{lifetime}}<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class Second : IReader { public int Kind => 2; }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<IReader>(new Host());
                    services.AddAppendReadersModule();
                    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                    using var firstScope = provider.CreateScope();
                    using var secondScope = provider.CreateScope();
                    var first = firstScope.ServiceProvider.GetServices<IReader>().ToArray();
                    var repeated = firstScope.ServiceProvider.GetServices<IReader>().ToArray();
                    var other = secondScope.ServiceProvider.GetServices<IReader>().ToArray();
                    if (first.Length != 3 || !first.Select(reader => reader.Kind).Order().SequenceEqual(new[] { 0, 1, 2 })
                        || firstScope.ServiceProvider.GetRequiredService<IReader>().Kind != first[^1].Kind)
                    { return false; }
                    return first.Where(reader => reader.Kind != 0).All(reader =>
                        ReferenceEquals(reader, repeated.Single(value => value.Kind == reader.Kind)) == ("{{lifetime}}" != "Transient")
                        && ReferenceEquals(reader, other.Single(value => value.Kind == reader.Kind)) == ("{{lifetime}}" == "Singleton"));
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Append.Readers", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that default and explicit TryAdd preserve host descriptors and that the public default is TryAdd.
    /// </summary>
    /// <param name="lifetime">The lifetime to verify.</param>
    /// <param name="explicitValue">Whether to explicitly specify the default value.</param>
    [Test]
    [Arguments("Transient", false)]
    [Arguments("Transient", true)]
    [Arguments("Scoped", false)]
    [Arguments("Scoped", true)]
    [Arguments("Singleton", false)]
    [Arguments("Singleton", true)]
    public async Task Should_preserve_host_registration_when_try_add_is_default_or_enabled(string lifetime, bool explicitValue)
    {
        var option = explicitValue ? "(Mode = ServiceRegistrationMode.TryAdd)" : "";
        var source = $$"""
            using System;
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IReader {}
            internal sealed class Host : IReader {}
            [{{lifetime}}<IReader>{{option}}] internal sealed class Generated : IReader {}
            public static class Probe
            {
                public static bool Run()
                {
                    var host = new Host();
                    var services = new ServiceCollection();
                    services.AddSingleton<IReader>(host);
                    services.AddDefaultReadersModule().AddDefaultReadersModule();
                    using var provider = services.BuildServiceProvider();
                    return new {{lifetime}}Attribute<IReader>().Mode == ServiceRegistrationMode.TryAdd
                        && ReferenceEquals(host, provider.GetServices<IReader>().Single())
                        && ReferenceEquals(host, provider.GetRequiredService<IReader>());
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Default.Readers", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that default registrations respect host overrides while appended services join the collection.
    /// </summary>
    [Test]
    public async Task Should_allow_appended_and_default_services_when_one_contract_is_shared()
    {
        const string source = """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IReader { int Kind { get; } }
            internal sealed class Host : IReader { public int Kind => 0; }
            [Transient<IReader>] internal sealed class Default : IReader { public int Kind => 1; }
            [Transient<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class Appended : IReader { public int Kind => 2; }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<IReader>(new Host());
                    services.AddMixedReadersModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetServices<IReader>().Select(reader => reader.Kind).SequenceEqual(new[] { 0, 2 });
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Mixed.Readers", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that multiple defaults follow TryAdd ordering while explicit appended implementations remain available.
    /// </summary>
    /// <param name="lifetime">The lifetime of the first default implementation.</param>
    /// <param name="append">Whether to include an explicitly appended implementation.</param>
    [Test]
    [Arguments("Transient", false)]
    [Arguments("Transient", true)]
    [Arguments("Scoped", false)]
    [Arguments("Scoped", true)]
    [Arguments("Singleton", false)]
    [Arguments("Singleton", true)]
    public async Task Should_keep_first_default_when_contract_is_registered_more_than_once(string lifetime, bool append)
    {
        var appended = append
            ? "[Transient<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class ZAppended : IReader { public int Kind => 3; }"
            : "";
        var source = $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IReader { int Kind { get; } }
            [{{lifetime}}<IReader>] internal sealed class AFirst : IReader { public int Kind => 1; }
            [Scoped<IReader>(Mode = ServiceRegistrationMode.TryAdd)] internal sealed class BSecond : IReader { public int Kind => 2; }
            {{appended}}
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddDuplicateDefaultsModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.GetServices<IReader>().Select(reader => reader.Kind)
                        .SequenceEqual(new[] { {{(append ? "1, 3" : "1")}} });
                }
            }
            """;

        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Duplicate.Defaults", source))).IsTrue();
    }

    /// <summary>
    /// Verifies that either module order preserves implementations and repeated calls append descriptors again.
    /// </summary>
    /// <param name="reverse">Whether to reverse the module entry point order.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_preserve_both_readers_when_module_order_changes(bool reverse)
    {
        var contracts = GeneratorFixture.Compile("Reader.Contracts", "public interface IReader { int Kind { get; } }");
        var first = GeneratorFixture.Compile(
            "First.Reader",
            """
            using TedToolkit.DependencyInjection;
            [Transient<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class First : IReader { public int Kind => 1; }
            """,
            null,
            contracts.Reference);
        var second = GeneratorFixture.Compile(
            "Second.Reader",
            """
            using TedToolkit.DependencyInjection;
            [Transient<IReader>(Mode = ServiceRegistrationMode.Add)] internal sealed class Second : IReader { public int Kind => 2; }
            """,
            null,
            contracts.Reference);
        var registration = reverse
            ? "services.AddSecondReaderModule().AddFirstReaderModule();"
            : "services.AddFirstReaderModule().AddSecondReaderModule();";
        var root = GeneratorFixture.Compile(
            "Reader.Host",
            $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    {{registration}}
                    services.AddReaderHostModule().AddReaderHostModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetServices<IReader>().Select(reader => reader.Kind).Order().SequenceEqual(new[] { 1, 1, 1, 2, 2, 2 });
                }
            }
            """,
            null,
            contracts.Reference,
            first.Reference,
            second.Reference);
        await Assert.That(GeneratorFixture.Execute(root, contracts, first, second)).IsTrue();
    }
}
