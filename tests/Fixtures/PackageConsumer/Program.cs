using Microsoft.Extensions.DependencyInjection;

using TedToolkit.DependencyInjection;

[assembly: RemoveAll(typeof(IRemoved), Key = "old")]

internal static class Program
{
    public static void Main()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReplaced>(new PreviousService());
        services.AddKeyedSingleton<IRemoved>("old", new RemovedService());
        services.AddExampleAppModule().AddExampleAppModule();
        if (services.Count(item => item.ServiceType == typeof(ConfigurationStamp)) != 2)
        {
            throw new InvalidOperationException("Module configuration must run for each module call.");
        }

        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        {
            IScoped first;
            using (var scope = provider.CreateScope())
            {
                var current = scope.ServiceProvider;
                first = current.GetRequiredService<IScoped>();
                if (!ReferenceEquals(first, current.GetRequiredService<IScoped>())
                    || ReferenceEquals(current.GetRequiredService<ITransient>(), current.GetRequiredService<ITransient>())
                    || !ReferenceEquals(current.GetRequiredService<ISingleton>(), provider.GetRequiredService<ISingleton>())
                    || current.GetServices<IReader>().Count() != 4
                    || current.GetServices<IPipeline>().Count() != 2
                    || current.GetRequiredService<IReplaced>().Name != "replacement"
                    || current.GetRequiredKeyedService<KeyedConsumer>("blue").Key != "blue"
                    || current.GetRequiredService<IRepository<string>>().Name != "repository"
                    || current.GetRequiredKeyedService<IRepository<string>>("open").Name != "repository"
                    || current.GetRequiredService<IClosedRepository<int>>().Value != 42
                    || current.GetRequiredKeyedService<IClosedRepository<int>>("closed").Value != 42
                    || current.GetRequiredKeyedService<FactoryResult>("factory").Number != 42
                    || current.GetRequiredService<SelectedConstructor>().Number != 42
                    || current.GetRequiredKeyedService<InternalConsumer>("blue").Key != "blue"
                    || current.GetRequiredKeyedService<InternalConsumer>("blue").Number != 42
                    || !ReferenceEquals(current.GetRequiredKeyedService<InternalConsumer>("blue").Provider, current)
                    || !ReferenceEquals(current.GetRequiredKeyedService<InternalConsumer>("blue").Dependency,
                        current.GetRequiredKeyedService<KeyedDependency>("blue"))
                    || !ReferenceEquals(current.GetRequiredService<IExisting>(), Producers.Existing)
                    || current.GetRequiredService<long>() != 42L
                    || !ReferenceEquals(current.GetRequiredKeyedService<IExisting>(Configuration.RuntimeKey), Producers.Existing)
                    || current.GetKeyedService<IRemoved>("old") is not null)
                {
                    throw new InvalidOperationException("Generated registrations did not preserve service lifetimes or appended implementations.");
                }
            }

            using (var scope = provider.CreateScope())
            {
                if (ReferenceEquals(first, scope.ServiceProvider.GetRequiredService<IScoped>()))
                {
                    throw new InvalidOperationException("Scoped services must not be shared between scopes.");
                }
            }

#if NET8_0_OR_GREATER
            VerifyNativeBoundaries(provider);
#endif
        }

        if (ScopedService.Disposed != 2)
        {
            throw new InvalidOperationException("Each scoped instance must be disposed exactly once.");
        }

        if (Producers.Existing.Disposed)
        {
            throw new InvalidOperationException("MSDI must not dispose externally owned singleton instances.");
        }

        if (InternalConsumer.Disposed != 1 || FactoryResult.Disposed != 1)
        {
            throw new InvalidOperationException("Generated factory results must be disposed exactly once by their scope.");
        }

#if NETFRAMEWORK
        // Ordinary reflection must remain safe on older runtimes.
        _ = typeof(ScopedService).GetCustomAttributes(inherit: false);
#endif
        Console.WriteLine("Package consumer passed on CLR " + Environment.Version + ".");
    }

#if NET8_0_OR_GREATER
    private static void VerifyNativeBoundaries(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            if (services.GetRequiredService<IRepository<int>>().Name != "repository"
                || services.GetRequiredKeyedService<IRepository<int>>("open").Name != "repository"
                || services.GetServices<long>().Single() != 42L)
            {
                throw new InvalidOperationException("The JIT control cases must support generic value arguments and value collections.");
            }
        }
        else
        {
            ExpectNativeRejection(() => services.GetRequiredService<IRepository<int>>());
            ExpectNativeRejection(() => services.GetRequiredKeyedService<IRepository<int>>("open"));
            ExpectNativeRejection(() => services.GetServices<long>());
        }
    }

    private static void ExpectNativeRejection(Action operation)
    {
        try
        {
            operation();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("MSDI must reject this unsupported Native AOT resolution.");
    }
#endif
}

internal interface IScoped;

[Scoped<IScoped>]
internal sealed class ScopedService : IScoped, IDisposable
{
    internal static int Disposed { get; private set; }

    public void Dispose() => Disposed++;
}

internal interface ITransient;

[Transient<ITransient>]
internal sealed class TransientService : ITransient;

internal interface ISingleton;

[Singleton<ISingleton>]
internal sealed class SingletonService : ISingleton;

internal interface IReader;

[Transient<IReader>(Mode = ServiceRegistrationMode.Add)]
internal sealed class FirstReader : IReader;

[Transient<IReader>(Mode = ServiceRegistrationMode.Add)]
internal sealed class SecondReader : IReader;

internal sealed class ConfigurationStamp;

internal static class Configuration
{
    internal static readonly object RuntimeKey = new();

    [ModuleConfiguration]
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton(new ConfigurationStamp());
        services.AddKeyedSingleton<IExisting>(RuntimeKey, Producers.Existing);
    }
}

internal interface IReplaced { string Name { get; } }

internal sealed class PreviousService : IReplaced { public string Name => "previous"; }

[Singleton<IReplaced>(Mode = ServiceRegistrationMode.Replace)]
internal sealed class ReplacementService : IReplaced { public string Name => "replacement"; }

[Singleton(Key = "blue")]
internal sealed class KeyedDependency;

[Scoped(Key = "blue")]
internal sealed class KeyedConsumer([FromKeyedServices] KeyedDependency dependency, [ServiceKey] string key)
{
    public string Key { get; } = key;
    public KeyedDependency Dependency { get; } = dependency;
}

internal interface IRepository<T> { string Name { get; } }

[Scoped(typeof(IRepository<>))]
[Scoped(typeof(IRepository<>), Key = "open")]
internal sealed class Repository<T> : IRepository<T> { public string Name => "repository"; }

internal interface IClosedRepository<T> { int Value { get; } }

[Scoped<IClosedRepository<int>>]
[Scoped<IClosedRepository<int>>(Key = "closed")]
internal sealed class ClosedRepository : IClosedRepository<int> { public int Value => 42; }

[Scoped]
internal sealed class SelectedConstructor
{
    public SelectedConstructor() { Number = 1; }
    public SelectedConstructor([FromKeyedServices("blue")] KeyedDependency dependency, int number = 42) { Number = number; }
    public int Number { get; }
}

[Scoped(Key = "blue")]
internal sealed class InternalConsumer : IDisposable
{
    internal InternalConsumer([FromKeyedServices] KeyedDependency dependency, [ServiceKey] string key,
        IServiceProvider provider, int number = 42)
    {
        Dependency = dependency;
        Key = key;
        Provider = provider;
        Number = number;
    }

    internal static int Disposed { get; private set; }
    internal KeyedDependency Dependency { get; }
    internal string Key { get; }
    internal IServiceProvider Provider { get; }
    internal int Number { get; }
    public void Dispose() => Disposed++;
}

internal interface IPipeline;

[Scoped<IPipeline>(Mode = ServiceRegistrationMode.TryAddEnumerable)]
internal sealed class FirstPipeline : IPipeline;

[Scoped<IPipeline>(Mode = ServiceRegistrationMode.TryAddEnumerable)]
internal sealed class SecondPipeline : IPipeline;

internal sealed class FactoryResult(int number) : IDisposable
{
    internal static int Disposed { get; private set; }
    public int Number { get; } = number;
    public void Dispose() => Disposed++;
}

internal interface IExisting;

internal sealed class ExistingInstance : IExisting, IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal static class Producers
{
    [Singleton<IExisting>]
    internal static ExistingInstance Existing { get; } = new();

    [Scoped<FactoryResult>(Key = "factory")]
    internal static FactoryResult Create(int number = 42) => new(number);

    [Singleton(typeof(long))]
    internal static long CreateValue() => 42L;
}

internal interface IRemoved;

internal sealed class RemovedService : IRemoved;
