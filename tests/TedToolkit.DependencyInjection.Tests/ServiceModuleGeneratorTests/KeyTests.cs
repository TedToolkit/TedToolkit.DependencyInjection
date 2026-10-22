namespace TedToolkit.DependencyInjection.Tests;

internal sealed class KeyTests
{
    /// <summary>Verifies that constant keys preserve their CLR types when boxed for MSDI lookup.</summary>
    /// <param name="key">The attribute and lookup key expression.</param>
    [Test]
    [Arguments("(byte)7")]
    [Arguments("(short)7")]
    [Arguments("7L")]
    [Arguments("7UL")]
    [Arguments("7U")]
    [Arguments("7.5F")]
    [Arguments("7.5D")]
    [Arguments("double.NaN")]
    [Arguments("double.PositiveInfinity")]
    [Arguments("float.NegativeInfinity")]
    [Arguments("true")]
    [Arguments("'x'")]
    [Arguments("typeof(string)")]
    public async Task Should_preserve_key_type_when_constant_is_emitted(string key)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using TedToolkit.DependencyInjection;
            [Singleton(Key = {{key}})] internal sealed class Service {}
            public static class Probe
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    services.AddConstantKeysModule();
                    using var provider = services.BuildServiceProvider();
                    return provider.GetRequiredKeyedService<Service>({{key}}) is not null;
                }
            }
            """;
        await Assert.That(GeneratorFixture.Execute(GeneratorFixture.Compile("Constant.Keys", source))).IsTrue();
    }
}
