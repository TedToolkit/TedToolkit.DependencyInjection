namespace TedToolkit.DependencyInjection.Tests;

internal sealed class ConfigurationTests
{
    /// <summary>Verifies escaped method identifiers and arbitrary runtime keys through explicit configuration.</summary>
    [Test]
    public async Task Should_register_runtime_key_when_configuration_method_has_keyword_name()
    {
        var module = GeneratorFixture.Compile("KeywordConfiguration", """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public sealed class Service {}
            internal static class Setup
            {
                internal static readonly object Key = new();
                internal static readonly Service Shared = new();
                [ModuleConfiguration]
                internal static void @class(IServiceCollection services) => services.AddKeyedSingleton<Service>(Key, Shared);
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddKeywordConfigurationModule();
                    using var provider = services.BuildServiceProvider();
                    return ReferenceEquals(provider.GetRequiredKeyedService<Service>(Setup.Key), Setup.Shared);
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>
    /// Verifies that configuration exceptions propagate unchanged and previously added descriptors remain in the collection.
    /// </summary>
    [Test]
    public async Task Should_preserve_partial_registrations_when_configuration_throws()
    {
        var module = GeneratorFixture.Compile("Failing.Configuration", """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IService {}
            [Scoped<IService>] internal sealed class Service : IService {}
            internal sealed class Stamp {}
            internal static class Setup
            {
                internal static readonly InvalidOperationException Failure = new("Expected configuration failure.");
                [ModuleConfiguration]
                internal static void Configure(IServiceCollection services)
                {
                    services.AddSingleton(new Stamp());
                    throw Failure;
                }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    try
                    {
                        services.AddFailingConfigurationModule();
                        return false;
                    }
                    catch (InvalidOperationException error)
                    {
                        using var provider = services.BuildServiceProvider();
                        using var scope = provider.CreateScope();
                        return ReferenceEquals(error, Setup.Failure) && services.Count == 2
                            && scope.ServiceProvider.GetRequiredService<IService>() is Service
                            && provider.GetRequiredService<Stamp>() is not null;
                    }
                }
            }
            """);

        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }
}
