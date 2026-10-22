using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TedToolkit.DependencyInjection;

/// <summary>Represents an assembly directive that removes all descriptors matching a service type and key.</summary>
/// <param name="serviceType">The service contract to remove.</param>
/// <remarks>
/// Calls <see cref="ServiceCollectionDescriptorExtensions.RemoveAll(IServiceCollection, Type)"/> for a null key,
/// or <see cref="ServiceCollectionDescriptorExtensions.RemoveAllKeyed(IServiceCollection, Type, object)"/> for a non-null key.
/// Runs after local ordinary registrations and dependency modules, before local replacements and configuration.
/// Descriptors for other keys are preserved. The operation changes registrations without disposing existing instances.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RemoveAllAttribute(Type serviceType) : Attribute
{
    /// <summary>Gets the service contract to remove.</summary>
    public Type ServiceType { get; } = serviceType;

    /// <summary>Gets or sets the service key. Null removes unkeyed descriptors.</summary>
    /// <value>A scalar attribute constant, enum, or Type value; null by default.</value>
    public object? Key { get; set; }
}
