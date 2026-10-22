namespace TedToolkit.DependencyInjection;

/// <summary>Represents a class, static factory, or existing instance registration shared throughout the container.</summary>
/// <param name="serviceType">The service contract, or null to register the annotated type or member's result type.</param>
/// <remarks>
/// Supports self registration and open generic class contracts. MSDI owns instances created by a constructor
/// or factory. An existing instance from a static field or readable property is captured during module
/// registration and remains owned by the caller; the container does not dispose it.
/// </remarks>
[System.Diagnostics.Conditional("TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES")]
public sealed class SingletonAttribute(Type? serviceType = null) : ServiceRegistrationAttribute(serviceType);

/// <summary>Represents a class, static factory, or existing instance registration shared throughout the container.</summary>
/// <typeparam name="TService">The service contract.</typeparam>
/// <remarks>
/// MSDI owns instances created by a constructor or factory. Existing instances from static fields and readable
/// properties remain caller-owned. Registration matching follows <see cref="ServiceRegistrationAttribute.Mode"/>.
/// </remarks>
[System.Diagnostics.Conditional("TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES")]
public sealed class SingletonAttribute<TService> : ServiceRegistrationAttribute<TService>
    where TService : class;
