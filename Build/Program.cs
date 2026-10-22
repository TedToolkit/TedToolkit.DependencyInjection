using Sourcy.DotNet;

using TedToolkit.ModularPipelines;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var pipeline = new TedPipeline(
    new()
    {
        BuildFiles = [Solutions.TedToolkit_DependencyInjection],
        Solution = Solutions.TedToolkit_DependencyInjection,
        TestFiles = [Projects.TedToolkit_DependencyInjection_Tests],
    },
    new FileInfo(Path.Combine(Projects.Build.Directory!.FullName, "appsettings.json")));

await pipeline.ExecuteAsync().ConfigureAwait(false);
