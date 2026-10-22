# TedToolkit.DependencyInjection

Compile-time service registration for `Microsoft.Extensions.DependencyInjection`. Declare a service contract and lifetime, then call a generated module entry point. The generator emits standard MSDI descriptors and connects dependency modules at compile time.

## Quick start

Add the `TedToolkit.DependencyInjection` package to your project and reference `Microsoft.Extensions.DependencyInjection` when building a service provider. Build with the .NET 10 SDK or a compiler host supporting Roslyn 5, and enable C# 11 or later for generic attributes.

Libraries that only register services need this package alone: its transitive Abstractions dependency
contains all generated registration APIs. Applications using BuildServiceProvider need the MSDI package
unless their host already provides it; they do not need to implement a container themselves.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.DependencyInjection;

var services = new ServiceCollection();
services.AddExampleAppModule(); // For Example.App.csproj.
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var greeting = scope.ServiceProvider.GetRequiredService<IGreeting>();
Console.WriteLine(greeting.Message);

public interface IGreeting
{
    string Message { get; }
}

[Scoped<IGreeting>]
internal sealed class Greeting : IGreeting
{
    public string Message => "Hello from TedToolkit.";
}
```

Registration modes cover Add, TryAdd, TryAddEnumerable, and Replace. Attributes also support keyed services, open generics, static factories, existing singleton members, and removal directives. Local defaults run before dependency modules; local replacements run after them.

See the [package guide](src/TedToolkit.DependencyInjection/README.md) for lifetimes, multiple implementations, module dependencies, configuration hooks, diagnostics, and source project references.

## Compatibility

The runtime targets .NET Standard 2.0/2.1, .NET Framework 4.6.2/4.7.2/4.8, and .NET 6/7/8/9/10. Compiler requirements are independent of the application's runtime target. The generator targets .NET Standard 2.0 and ships inside the runtime NuGet package; there is no separate analyzer package or runtime dependency on Roslyn.

Older target frameworks are provided for compatibility. This does not extend their platform vendor's support lifecycle. Native AOT analysis is enabled for .NET 8 and later. Package consumers are published and executed as Windows x64 Native AOT applications for .NET 8/9/10. Standard MSDI AOT limits still apply; see the package guide's [Native AOT boundaries](src/TedToolkit.DependencyInjection/README.md#native-aot-boundaries).

## Development

Clone with submodules, or initialize them after cloning:

```sh
git submodule update --init --recursive
dotnet build TedToolkit.DependencyInjection.slnx -c Release
dotnet run --project tests/TedToolkit.DependencyInjection.Tests -c Release
dotnet pack src/TedToolkit.DependencyInjection -c Release -o artifacts/packages
```

The solution contains the runtime, generator, and one TUnit test project. Tests compile and execute generated consumers against the real DI container. `Build/` integrates the shared TedToolkit pipeline; copy `Build/appsettings.example.json` to `Build/appsettings.json` before running `dotnet run --project Build -c Release`.

The package verification also builds a .NET Standard 2.0 consumer that references only this package and
checks that Microsoft.Extensions.DependencyInjection itself is absent from that consumer's dependency graph.

On Windows, run `./Build/Verify-Package.ps1` to pack the library, compile a package-only consumer for every target, and execute the .NET Framework and .NET consumers. Add `-NativeAot` to publish and execute .NET 8/9/10 native executables; this requires the Windows C++ build tools. To select one native target, use `-NativeAot -NativeAotFrameworks net10.0`. Consumers targeting .NET 6 and 7 allow runtime roll-forward when those runtimes are unavailable. Standard targets are compiled as libraries. The package fixture stays outside the main solution so it can validate the packed assets independently of source project references. A Windows CI job runs the complete package and Native AOT verification on every push.

See [contributor guidance](CLAUDE.md) for language and source conventions.

## License

[LGPL-3.0-only](COPYING.LESSER), with the accompanying [GPL terms](COPYING).
