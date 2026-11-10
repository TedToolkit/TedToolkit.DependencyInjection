---
name: register-tedtoolkit-di-services
description: >-
  Declare generated Microsoft.Extensions.DependencyInjection registrations with TedToolkit.DependencyInjection. Use for service lifetimes, module composition, keyed services, and missing generated registration APIs.
---

# Register TedToolkit DI Services

Start at the registration project. Identify the service contract, implementation or factory, lifetime, and composition root before changing attributes.

## Workflow

- Choose a lifetime and an explicit contract such as `[Scoped<IOrderService>]`. Use an untyped lifetime attribute when registration as the implementation itself is intended.
- Build the registration project and call its generated `Add<Project>Module` entry point from the composition root. The MSBuild project name drives the entry point and extension type.
- Use `ServiceRegistrationMode.Replace` only when replacing existing descriptors is intended. Match keyed registration and lookup keys and verify open generic contracts using the intended closed type.
- Build the consumer and resolve the service through Microsoft DI. For scoped registrations, verify identity within one scope and isolation across two scopes.

## Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.DependencyInjection;

[Scoped<IOrderService>]
internal sealed class OrderService : IOrderService { }

// In Example.App.csproj:
// services.AddExampleAppModule();
```

## Check the result

Inspect the generated module name when the consumer cannot see `Add<Project>Module`. For replacements and keyed registrations, test the exact key and the order in which descriptors are registered. Build with the required compiler and run the package consumer test when modifying a generator-facing declaration.
