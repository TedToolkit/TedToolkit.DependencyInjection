namespace TedToolkit.DependencyInjection.Tests;

internal sealed class OpenGenericTests
{
    /// <summary>Verifies that open generic contracts preserve the implementation parameter positions.</summary>
    /// <param name="declaration">The transformed or reordered generic contract.</param>
    [Test]
    [Arguments("public interface IRepository<T> {} [Scoped(typeof(IRepository<>))] internal sealed class Repository<T> : IRepository<System.Collections.Generic.List<T>> {}")]
    [Arguments("public interface IRepository<T, U> {} [Scoped(typeof(IRepository<,>))] internal sealed class Repository<T, U> : IRepository<U, T> {}")]
    public async Task Should_reject_open_contract_when_generic_parameters_are_transformed(string declaration)
    {
        var result = GeneratorFixture.Run("GenericMapping", "using TedToolkit.DependencyInjection; " + declaration, out _);
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == "TDI004")).IsTrue();
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    /// <summary>Verifies that identity parameter mappings through generic base classes remain supported.</summary>
    [Test]
    public async Task Should_resolve_open_service_when_contract_is_inherited()
    {
        var module = GeneratorFixture.Compile("InheritedGeneric", """
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            public interface IRepository<T> {}
            public class BaseRepository<T> : IRepository<T> {}
            [Scoped(typeof(IRepository<>))]
            [Scoped(typeof(BaseRepository<>))]
            internal sealed class Repository<T> : BaseRepository<T> {}
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddInheritedGenericModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetRequiredService<IRepository<string>>() is Repository<string>
                        && provider.GetRequiredService<BaseRepository<string>>() is Repository<string>;
                }
            }
            """);
        await Assert.That(GeneratorFixture.Execute(module)).IsTrue();
    }

    /// <summary>Verifies that open generic registrations resolve closed services through the real container.</summary>
    /// <param name="mode">The registration operation.</param>
    /// <param name="keyed">Whether to register a service key.</param>
    [Test]
    [Arguments("TryAdd", false)]
    [Arguments("Add", false)]
    [Arguments("TryAddEnumerable", false)]
    [Arguments("Replace", false)]
    [Arguments("TryAdd", true)]
    [Arguments("Add", true)]
    [Arguments("TryAddEnumerable", true)]
    [Arguments("Replace", true)]
    public async Task Should_resolve_closed_service_when_open_generic_is_registered(string mode, bool keyed)
    {
        var source = $$"""
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            namespace Example.@event
            {
                public interface IRepository<T> {}
                [Scoped(typeof(IRepository<>), Mode = ServiceRegistrationMode.{{mode}}{{(keyed ? ", Key = \"blue\"" : "")}})]
                internal sealed class Repository<T> : IRepository<T> {}
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddGenericModule().AddGenericModule();
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider.{{(keyed ? "GetKeyedServices<Example.@event.IRepository<string>>(\"blue\")" : "GetServices<Example.@event.IRepository<string>>()")}}
                        .Count() == {{(mode == "Add" ? 2 : 1)}};
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Generic", source))).IsTrue();
    }
}
