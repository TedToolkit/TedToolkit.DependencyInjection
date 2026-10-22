namespace TedToolkit.DependencyInjection;

/// <summary>Represents a service registration declared on a class, static factory, or singleton instance member.</summary>
/// <param name="serviceType">The service contract, or null to use the annotated type or member's result type.</param>
/// <remarks>
/// Each attribute creates an independent MSDI descriptor. Additional interfaces are not registered implicitly,
/// and separate contracts do not share an instance automatically. Public constructors use normal MSDI activation;
/// a class without public constructors must have one accessible constructor supported by the generator.
/// Static factory parameters are injected when the service is resolved. Static fields and readable properties
/// are supported only by singleton declarations and capture their instance when the module is called.
/// Lifetime attributes are omitted from emitted metadata unless TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES is defined;
/// source generation does not require that symbol.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public abstract class ServiceRegistrationAttribute(Type? serviceType = null) : Attribute
{
    /// <summary>Gets the explicit service contract, or null to use the annotated type or member's result type.</summary>
    /// <remarks>
    /// The implementation or result must be assignable to this contract without a numeric or user-defined conversion.
    /// Open generic contracts require matching arity and the implementation's generic parameters in the same order.
    /// </remarks>
    public Type? ServiceType { get; } = serviceType;

    /// <summary>Gets or sets the MSDI operation used to insert or replace this registration.</summary>
    /// <value><see cref="ServiceRegistrationMode.TryAdd"/> by default.</value>
    /// <remarks>
    /// Local Add, TryAdd, and TryAddEnumerable registrations run before dependency modules.
    /// RemoveAll directives and local Replace registrations run afterward, followed by
    /// <see cref="ModuleConfigurationAttribute"/> methods and the partial configuration hook.
    /// Each enum member documents the corresponding MSDI method and its matching rules.
    /// </remarks>
    public ServiceRegistrationMode Mode { get; set; } = ServiceRegistrationMode.TryAdd;

    /// <summary>Gets or sets the key used by the generated service descriptor and registration operation.</summary>
    /// <value>A scalar attribute constant, enum, or Type value; null, the default, creates an unkeyed registration.</value>
    /// <remarks>
    /// Key values retain their CLR types, so the integer 1 and the string "1" identify different services.
    /// Use Microsoft's FromKeyedServices and ServiceKey parameter attributes for keyed injection.
    /// Array keys are unsupported. Configure arbitrary runtime object keys through a
    /// <see cref="ModuleConfigurationAttribute"/> method instead.
    /// </remarks>
    public object? Key { get; set; }
}

/// <summary>Represents a service registration with a closed reference-type contract.</summary>
/// <typeparam name="TService">The assignable service contract; additional implemented interfaces are not registered.</typeparam>
/// <remarks>Use a non-generic lifetime attribute with a Type argument for open generic or value-type contracts.</remarks>
public abstract class ServiceRegistrationAttribute<TService>() : ServiceRegistrationAttribute(typeof(TService))
    where TService : class;
