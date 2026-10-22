using Microsoft.Extensions.DependencyInjection;

using TedToolkit.DependencyInjection;

[assembly: RemoveAll(typeof(IRemoved))]

public static class Registrations
{
    public static IServiceCollection CreateServices()
    {
        return new ServiceCollection().AddRegistrationOnlyModule();
    }
}

internal interface IService;

[Scoped<IService>]
internal sealed class DefaultService : IService;

[Scoped<IService>(Mode = ServiceRegistrationMode.Add, Key = "blue")]
internal sealed class AppendedService : IService;

[Scoped<IService>(Mode = ServiceRegistrationMode.TryAddEnumerable)]
internal sealed class EnumerableService : IService;

[Scoped<IService>(Mode = ServiceRegistrationMode.Replace)]
internal sealed class ReplacementService : IService;

internal interface IRepository<T>;

[Scoped(typeof(IRepository<>))]
internal sealed class Repository<T> : IRepository<T>;

internal interface IRemoved;

internal static class Producers
{
    [Singleton]
    internal static readonly object Existing = new();

    [Transient<IService>(Key = "factory")]
    internal static DefaultService Create([FromKeyedServices("blue")] IService dependency, int count = 42) => new();
}
