// -----------------------------------------------------------------------
// <copyright file="GeneratorFixture.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using TedToolkit.DependencyInjection.Analyzer;

namespace TedToolkit.DependencyInjection.Tests;

internal static class GeneratorFixture
{
    internal static GeneratorDriverRunResult Run(string name, string source, out Compilation output, string? projectName = null, params MetadataReference[] dependencies)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().Concat(dependencies);
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source),],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ServiceModuleGenerator());
        if (projectName is not null)
        {
            driver = driver.WithUpdatedAnalyzerConfigOptions(new ProjectOptions(projectName));
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out _);
        return driver.GetRunResult();
    }

    internal static CompiledModule Compile(string name, string source, string? projectName = null, params MetadataReference[] dependencies)
    {
        var result = Run(name, source, out var output, projectName, dependencies);
        EnsureClean(result.Diagnostics.Concat(output.GetDiagnostics()).Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        EnsureClean(emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return new(stream.ToArray());
    }

    internal static bool Execute(CompiledModule root, params CompiledModule[] dependencies)
    {
        var context = new AssemblyLoadContext("Generator tests", isCollectible: true);
        try
        {
            foreach (var dependency in dependencies)
            {
                using var stream = new MemoryStream(dependency.Image);
                _ = context.LoadFromStream(stream);
            }

            using var rootStream = new MemoryStream(root.Image);
            var assembly = context.LoadFromStream(rootStream);
            return (bool)assembly.GetType("Probe")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static void EnsureClean(IEnumerable<Diagnostic> diagnostics)
    {
        var messages = diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray();
        if (messages.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException("Generated consumer code failed to compile: " + string.Join(Environment.NewLine, messages));
    }

    internal sealed class CompiledModule(byte[] image)
    {
        internal byte[] Image { get; } = image;

        internal PortableExecutableReference Reference { get; } = MetadataReference.CreateFromImage(image);
    }

    private sealed class ProjectOptions(string projectName) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(projectName);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return GlobalOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return GlobalOptions;
        }
    }

    private sealed class Values(string projectName) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = projectName;
            return key == "build_property.MSBuildProjectName";
        }
    }
}
