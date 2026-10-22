namespace TedToolkit.DependencyInjection;

/// <summary>Represents a class or static factory registration that creates an instance for each resolution.</summary>
/// <param name="serviceType">The service contract, or null to register the annotated type or member's result type.</param>
/// <remarks>
/// Supports self registration and open generic class contracts. MSDI owns and disposes instances it creates.
/// Static instance fields and properties require <see cref="SingletonAttribute"/> instead.
/// </remarks>
[System.Diagnostics.Conditional("TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES")]
public sealed class TransientAttribute(Type? serviceType = null) : ServiceRegistrationAttribute(serviceType);

/// <summary>Represents a class or static factory registration that creates an instance for each resolution.</summary>
/// <typeparam name="TService">The service contract.</typeparam>
/// <remarks>MSDI owns and disposes instances it creates. Registration matching follows <see cref="ServiceRegistrationAttribute.Mode"/>.</remarks>
[System.Diagnostics.Conditional("TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES")]
public sealed class TransientAttribute<TService> : ServiceRegistrationAttribute<TService>
    where TService : class;
