using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TedToolkit.DependencyInjection;

/// <summary>Represents the MSDI operation applied to a generated service descriptor.</summary>
/// <remarks>
/// The lifetime attribute controls the descriptor's lifetime. This value controls how the descriptor
/// changes the service collection. Matching uses the service type and key; a null key denotes an unkeyed service.
/// </remarks>
public enum ServiceRegistrationMode
{
    /// <summary>
    /// Calls <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>
    /// to add the descriptor only when its service type and key have no existing registration.
    /// </summary>
    /// <remarks>
    /// This is the default mode. An existing registration wins regardless of its implementation or lifetime.
    /// Local defaults are registered before dependency modules, so they take precedence over dependency defaults.
    /// </remarks>
    TryAdd,

    /// <summary>
    /// Calls <see cref="ServiceCollectionDescriptorExtensions.Add(IServiceCollection, ServiceDescriptor)"/>
    /// to append the descriptor on every module call.
    /// </summary>
    /// <remarks>
    /// Existing registrations are preserved. MSDI resolves the last matching descriptor for a single service
    /// and all matching descriptors, in registration order, for an enumerable service.
    /// </remarks>
    Add,

    /// <summary>
    /// Calls <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// to add the descriptor only when the service type, key, and implementation are not already registered together.
    /// </summary>
    /// <remarks>
    /// Use this mode for multiple implementations of the same service contract. Lifetime is not part of
    /// the duplicate check. For factories, declare a concrete implementation return type so MSDI can
    /// distinguish implementations. MSDI throws ArgumentException when the implementation identity is
    /// object or the service contract itself; self registrations should use TryAdd instead.
    /// This mode registers the declared service contract, not an IEnumerable contract.
    /// </remarks>
    TryAddEnumerable,

    /// <summary>
    /// Calls <see cref="ServiceCollectionDescriptorExtensions.Replace(IServiceCollection, ServiceDescriptor)"/>
    /// to remove the first descriptor with the same service type and key, then append the replacement.
    /// </summary>
    /// <remarks>
    /// Replacement runs after dependency modules and assembly-level <see cref="RemoveAllAttribute"/>
    /// directives, before configuration methods and the partial configuration hook. If no descriptor
    /// matches, the replacement is still appended. Additional matching descriptors remain;
    /// combine this mode with <see cref="RemoveAllAttribute"/> to remove all inherited implementations.
    /// </remarks>
    Replace,
}
