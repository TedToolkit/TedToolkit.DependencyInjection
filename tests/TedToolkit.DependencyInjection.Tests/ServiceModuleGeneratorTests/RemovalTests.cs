namespace TedToolkit.DependencyInjection.Tests;

internal sealed class RemovalTests
{
    /// <summary>Verifies that removal directives run after dependencies, preserve unrelated keys, and precede local replacements.</summary>
    /// <param name="keyed">Whether to remove a keyed registration.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Should_remove_dependency_registrations_before_local_replacement(bool keyed)
    {
        var dependency = GeneratorFixture.Compile("Dependency", $$"""
            using TedToolkit.DependencyInjection;
            public interface IService { int Kind { get; } }
            [Singleton<IService>(Mode = ServiceRegistrationMode.Add{{(keyed ? ", Key = \"blue\"" : "")}})]
            internal sealed class AFirst : IService { public int Kind => 1; }
            [Singleton<IService>(Mode = ServiceRegistrationMode.Add{{(keyed ? ", Key = \"blue\"" : "")}})]
            internal sealed class BSecond : IService { public int Kind => 2; }
            [Singleton<IService>(Key = "red")] internal sealed class Red : IService { public int Kind => 4; }
            """);
        var host = GeneratorFixture.Compile("Host", $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            [assembly: RemoveAll(typeof(IService){{(keyed ? ", Key = \"blue\"" : "")}})]
            [Singleton<IService>(Mode = ServiceRegistrationMode.Replace{{(keyed ? ", Key = \"blue\"" : "")}})]
            internal sealed class HostService : IService { public int Kind => 3; }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddHostModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.{{(keyed ? "GetKeyedServices<IService>(\"blue\")" : "GetServices<IService>()")}}.Single().Kind == 3
                        && provider.GetRequiredKeyedService<IService>("red").Kind == 4;
                }
            }
            """, null, dependency.Reference);
        await Assert.That(GeneratorFixture.Execute(host, dependency)).IsTrue();
    }
}
