# Contributor guidance

- Implementation language: C#.
- All documentation, comments, diagnostics, exception messages, and test descriptions must be English.
- Use the TedToolkit name for project names, namespaces, packages, metadata, and diagnostics.
- Keep runtime code independent of Roslyn and application-specific frameworks.
- Preserve the target framework matrix in `externals/TedToolkit/props/AlmostAllFrameworks.props`, plus .NET Framework 4.6.2 when dependencies permit it.
- Keep generated C# structural through `TedToolkit.RoslynHelper` and compile generated consumers in tests.
- Place TUnit behavior tests under the corresponding production type's test directory. Await assertions and provide behavior-focused XML summaries.
- Build with `dotnet build TedToolkit.DependencyInjection.slnx -c Release` and run tests with `dotnet run --project tests/TedToolkit.DependencyInjection.Tests -c Release`.
- Verify package loading and legacy runtimes with `Build/Verify-Package.ps1`; pass `-NativeAot` on Windows with the native toolchain installed.
- The package consumer fixture intentionally stays outside the main solution because it consumes the built NuGet package rather than source projects.
