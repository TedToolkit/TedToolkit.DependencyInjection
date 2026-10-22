# TedToolkit.DependencyInjection

Declare ordinary Microsoft dependency injection registrations with attributes. The source generator emits ServiceDescriptor and IServiceCollection calls; the runtime library contains declaration attributes only.

## Quick start

Reference this package and Microsoft.Extensions.DependencyInjection in an application that builds a container. Use the .NET 10 SDK or another Roslyn 5 compiler host and C# 11 or later. Runtime assets cover .NET Standard 2.0/2.1, .NET Framework 4.6.2/4.7.2/4.8, and .NET 6 through 10. Older targets should explicitly select a current C# language version.

For a library that only declares or registers services, referencing TedToolkit.DependencyInjection alone
is sufficient. It transitively provides Microsoft.Extensions.DependencyInjection.Abstractions, which contains
every API used by generated registrations, including descriptor operations and factory lookup extensions.
The default container implementation and BuildServiceProvider are in Microsoft.Extensions.DependencyInjection;
an application must reference that package unless its host already supplies it. No custom container
implementation is required. The registration-only package fixture checks that the default container package
is absent from the dependency graph while generated registration code still compiles.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.DependencyInjection;

[Scoped<IOrderService>]
internal sealed class OrderService : IOrderService { }

// In Example.App.csproj:
// services.AddExampleAppModule();
```

## Lifetimes and service contracts

| Declaration | Behavior |
| --- | --- |
| [Transient<IService>] | Creates an instance per resolution |
| [Scoped<IService>] | Shares an instance within a scope |
| [Singleton<IService>] | Shares an instance within a container |
| [Scoped] | Registers the annotated type or member result as itself |
| [Scoped(typeof(IRepository<>))] | Registers an open generic contract |

Use the generic form for closed reference-type contracts and the Type-based form for open generics or value services produced by a factory. A class may declare multiple explicit contracts and keys. Each declaration creates an independent descriptor with standard MSDI instance and disposal behavior. Additional interfaces are never registered implicitly.

Public constructors are handled by MSDI, including constructor selection, optional arguments, nullable value dependencies, and keyed injection. A class with no public constructors can use one accessible internal constructor; the generator emits a direct factory for it. That constructor must initialize required members, and cannot have by-reference, pointer, or ref-like parameters. Open generic implementations need public constructors and matching service/implementation generic arity, with the same generic parameters in the same order on the implemented contract. MSDI performs its normal generic constraint and activation checks.

## Registration operations

Set Mode on any lifetime declaration:

```csharp
[Scoped<IOrderService>(Mode = ServiceRegistrationMode.Replace)]
internal sealed class CustomOrderService : IOrderService { }
```

| Mode | Corresponding MSDI operation |
| --- | --- |
| TryAdd (default) | Adds only when the service type and key have no existing descriptor |
| Add | Appends a descriptor on every module call |
| TryAddEnumerable | Adds only when the same service type, key, and implementation are absent |
| Replace | Removes the first matching descriptor and appends the replacement |

Multiple defaults follow TryAdd ordering; they are not a generator error. TryAddEnumerable uses MSDI implementation identity. Factories should declare a concrete implementation return type when using that mode. MSDI throws ArgumentException if the implementation identity is object or the service contract itself; use TryAdd for self registrations. This mode registers implementations of the declared contract, not an IEnumerable contract.

```csharp
[Singleton<IHandler>(Mode = ServiceRegistrationMode.TryAddEnumerable)]
internal sealed class EmailHandler : IHandler { }

[Singleton<IHandler>(Mode = ServiceRegistrationMode.TryAddEnumerable)]
internal sealed class SmsHandler : IHandler { }
```

Both implementations are available through GetServices<IHandler>(). Repeating the module call does not add either implementation again.

The former TryAdd boolean is replaced by Mode. Use Mode = ServiceRegistrationMode.Add for an unconditional registration.

## Module order and overrides

Each generated module executes these phases:

1. Local Add, TryAdd, and TryAddEnumerable declarations.
2. Referenced dependency modules.
3. Local assembly-level RemoveAll directives.
4. Local Replace declarations.
5. Attributed ModuleConfiguration methods.
6. The optional partial ConfigureServices hook.

Local defaults therefore take precedence over dependency defaults. An existing host registration made before the module call is preserved by TryAdd. A dependency's explicit Add still appends a descriptor; use local Replace for an intentional override after dependencies. Resolving one service follows MSDI's last-registration-wins behavior, and collection resolution preserves descriptor order.

Declarations within a phase are ordered by fully qualified declaration name, retaining source attribute order on the same declaration. Dependency modules are ordered by assembly identity, and configuration methods by fully qualified name. Use explicit Replace or final configuration for overrides rather than relying on file or sibling-module order.

Module calls do not track execution state. Repeated or diamond dependency calls follow the selected MSDI operation, and hooks execute on every call. Configuration exceptions propagate and leave previously added descriptors in the collection.

## Keyed services

A non-null Key creates a keyed descriptor:

```csharp
[Singleton<ICache>(Key = "fast")]
internal sealed class FastCache : ICache { }

[Scoped(Key = "fast")]
internal sealed class Consumer(
    [FromKeyedServices("fast")] ICache cache,
    [ServiceKey] string key);
```

Keys preserve scalar CLR types, enums, and Type values. Null denotes an unkeyed service. Constructor and factory parameters support FromKeyedServices and ServiceKey; a parameterless FromKeyedServices inherits the registration key. Arbitrary runtime object keys, including KeyedService.AnyKey, can be configured through an attributed ModuleConfiguration method because C# attribute arguments must be compile-time values. Array keys are diagnosed rather than generating unstable object identities.

## Static factories and existing instances

Annotate accessible static methods to declare factories. Parameters are injected; IServiceProvider receives the resolving provider, keyed parameters use their requested key, and optional parameters use their default when no service is available.

```csharp
internal static class Factories
{
    [Scoped<ICache>(Key = "fast")]
    internal static RedisCache Create(
        IServiceProvider provider,
        [FromKeyedServices("redis")] Connection connection,
        int retryCount = 3) => new(connection, retryCount);

    [Singleton<IClock>]
    internal static IClock Clock { get; } = new SystemClock();
}
```

Static fields and readable static properties describe existing singleton instances. Their values are captured when registration executes, and the container does not own or dispose those instances. Factory-created services have the selected lifetime and are owned and disposed by the container. Factories are synchronous and non-generic; asynchronous initialization and arbitrary runtime composition belong in ordinary application code or explicit configuration.

## Remove registrations

```csharp
[assembly: RemoveAll(typeof(IOrderService))]
[assembly: RemoveAll(typeof(ICache), Key = "old")]
```

Directives remove every descriptor matching the service type and key after dependency registration. Combine a directive with a local Replace declaration when all inherited implementations should be removed before adding one replacement. Unrelated keys are preserved.

## Explicit configuration

An attributed method is the escape hatch for the entire MSDI registration API, including runtime instances, arbitrary keys, options, external containers, and application-specific setup:

```csharp
internal static class Setup
{
    [ModuleConfiguration]
    internal static void Configure(IServiceCollection services)
    {
        services.AddKeyedSingleton<ICache>(KeyedService.AnyKey, (provider, key) => CreateCache(key));
    }
}
```

The method must be accessible, static, non-generic, return void, and accept one IServiceCollection. A final partial hook is also available:

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static partial class ExampleAppModuleExtensions
{
    static partial void ConfigureServices(IServiceCollection services)
    {
        // Apply final application configuration here.
    }
}
```

Container creation, scopes, resolution, validation, and disposal use Microsoft's APIs directly.

## Generated module metadata

Example.App.csproj generates AddExampleAppModule and ExampleAppModuleExtensions in Microsoft.Extensions.DependencyInjection. A project name beginning with a digit adds Project to the generated type name. MSBuildProjectName controls naming even when AssemblyName differs.

The generator also emits GeneratedServiceModuleAttribute on the assembly. It records the generated type and method names. Generators in referencing projects read this metadata and emit direct calls to that entry point, so dependencies connect at compile time without runtime assembly scanning. It is generated metadata: application code does not apply it manually. Packages without this metadata retain their normal registration entry points.

Lifetime declaration attributes are omitted from emitted consumer metadata by default, keeping generic attribute reflection safe on older runtimes. The generator still reads source declarations. Define TEDTOOLKIT_DI_PRESERVE_ATTRIBUTES only for runtime inspection on a runtime supporting generic attributes. Assembly-level generated module metadata is always retained.

## Diagnostics and boundaries

### Native AOT boundaries

Generated registrations use direct type references or static factories. Public constructors use MSDI's
trim-annotated descriptor APIs; internal constructors and static factories are called directly. The
generator itself runs in the compiler and is not loaded by the published application. Package consumers
exercise Windows x64 Native AOT on .NET 8, 9, and 10 with linker and native compiler warnings treated as errors.
Other operating systems and architectures have not been verified by this repository's native matrix.

MSDI retains its own Native AOT resolution limits:

- Open generic registrations support reference-type arguments. Resolving an open generic service with
  a value-type argument, such as IRepository<int>, throws InvalidOperationException under Native AOT.
  Register the required closed contract explicitly, such as [Scoped<IRepository<int>>], on a concrete
  implementation or factory instead. Both keyed and unkeyed forms follow this rule.
- A value-type service produced by a static factory can be resolved individually. Resolving
  IEnumerable<int> or another collection whose service element is a value type is rejected by MSDI
  under Native AOT. Reference-type contracts provide a collection-compatible alternative.
- Custom ModuleConfiguration code, factory bodies, and their dependencies must satisfy their own
  trimming and AOT requirements. The attributes do not remove those application requirements.
- Trimmed open generic registrations must have compatible generic-parameter preservation annotations
  and new() constraints on their service and implementation definitions; MSDI validates this compatibility.

The package fixture checks both successful closed-generic/value-service paths and expected native
rejections, with matching JIT control cases. See the [MSDI implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/CallSiteFactory.cs)
for the container's generic and collection guards.

### Declaration diagnostics

Invalid declarations suppress module generation and produce a source diagnostic where possible.

| ID | Invalid declaration |
| --- | --- |
| TDI003 | An inaccessible or unsupported class, factory, or instance member |
| TDI004 | An incompatible or inaccessible contract or invalid generic arity |
| TDI005 | An unsupported internal constructor or non-singleton instance member |
| TDI008 | An invalid explicit configuration method |
| TDI009 | Manual or damaged generated module metadata |
| TDI010 | An invalid project name or module type collision |
| TDI011 | An invalid registration mode |
| TDI012 | An unsupported attribute key or removal directive |

Normal MSDI runtime behavior remains authoritative, including constructor ambiguity, missing services, generic constraints, collection ordering, disposal, and first-match Replace semantics. Source generation does not add rollback, whole-module deduplication, alias sharing, or a second container.

## Source project references

NuGet loads the bundled generator automatically. Source consumers reference both projects and expose the project name:

```xml
<ItemGroup>
  <ProjectReference Include="../TedToolkit.DependencyInjection/TedToolkit.DependencyInjection.csproj" />
  <ProjectReference Include="../TedToolkit.DependencyInjection.Analyzer/TedToolkit.DependencyInjection.Analyzer.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />
  <CompilerVisibleProperty Include="MSBuildProjectName" />
</ItemGroup>
```

The analyzer is not published independently. The main package includes its assembly, TedToolkit.RoslynHelper, ZString, and System.Memory under analyzers/dotnet/cs. Compiler dependencies do not become runtime dependencies. The runtime package depends on Microsoft.Extensions.DependencyInjection.Abstractions.
