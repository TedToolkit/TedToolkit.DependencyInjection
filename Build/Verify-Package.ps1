param(
    [switch]$NativeAot,
    [ValidateSet('net8.0', 'net9.0', 'net10.0')]
    [string[]]$NativeAotFrameworks = @('net8.0', 'net9.0', 'net10.0')
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$runDirectory = Join-Path $repository ('artifacts/verification/' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $runDirectory 'feed'
$cache = Join-Path $runDirectory 'packages'
$fixture = Join-Path $repository 'tests/Fixtures/PackageConsumer/Example.App.csproj'
$registrationFixture = Join-Path $repository 'tests/Fixtures/RegistrationOnly/RegistrationOnly.csproj'
$globalPackages = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$configuration = Join-Path $runDirectory 'NuGet.Config'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

function Assert-PackageIdentity([string]$Project) {
    $assetsPath = Join-Path (Split-Path -Parent $Project) 'obj/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $ownPackage = @($assets.libraries.PSObject.Properties | Where-Object Name -Match '^TedToolkit\.DependencyInjection/')
    if ($ownPackage.Count -ne 1 -or $ownPackage[0].Value.sha512 -ne $expectedPackageHash) {
        throw "The restored package does not match this run's newly packed artifact: $Project"
    }
}

Invoke-DotNet @('pack', (Join-Path $repository 'src/TedToolkit.DependencyInjection'), '-c', 'Release', '-p:GeneratePackageOnBuild=false', '-o', $feed, '-m:1', '--nologo')
$packedPackage = Get-ChildItem -LiteralPath $feed -Filter 'TedToolkit.DependencyInjection.*.nupkg'
$hasher = [System.Security.Cryptography.SHA512]::Create()
try {
    $expectedPackageHash = [Convert]::ToBase64String($hasher.ComputeHash([System.IO.File]::ReadAllBytes($packedPackage.FullName)))
} finally {
    $hasher.Dispose()
}
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
$escapedCache = [System.Security.SecurityElement]::Escape($cache)
$escapedFallback = [System.Security.SecurityElement]::Escape($globalPackages.TrimEnd('\', '/'))
@"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <fallbackPackageFolders>
    <clear />
    <add key="global" value="$escapedFallback" />
  </fallbackPackageFolders>
  <config>
    <add key="globalPackagesFolder" value="$escapedCache" />
  </config>
</configuration>
"@ | Set-Content -LiteralPath $configuration -Encoding utf8
Invoke-DotNet @('restore', $registrationFixture, '--configfile', $configuration, '--packages', $cache, '-m:1', '--nologo')
Assert-PackageIdentity $registrationFixture
Invoke-DotNet @('build', $registrationFixture, '-c', 'Release', '--no-restore', '-m:1', '--nologo')
$registrationAssets = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $registrationFixture) 'obj/project.assets.json') -Raw | ConvertFrom-Json
if ($registrationAssets.libraries.PSObject.Properties.Name -match '^Microsoft\.Extensions\.DependencyInjection/') {
    throw 'Registration-only consumers must not require the default container implementation.'
}
Invoke-DotNet @('restore', $fixture, '--configfile', $configuration, '--packages', $cache, '-m:1', '--nologo')
Assert-PackageIdentity $fixture
Invoke-DotNet @('build', $fixture, '-c', 'Release', '--no-restore', '-m:1', '--nologo')

foreach ($framework in @('net462', 'net472', 'net48', 'net6.0', 'net7.0', 'net8.0', 'net9.0', 'net10.0')) {
    $output = Join-Path (Split-Path -Parent $fixture) "bin/Release/$framework/Consumer.Binary"
    Write-Host "Running package consumer for $framework."
    if ($framework -in @('net462', 'net472', 'net48')) {
        & "$output.exe"
        if ($LASTEXITCODE -ne 0) {
            throw "The $framework package consumer failed."
        }
    } else {
        Invoke-DotNet @("$output.dll")
    }
}

if ($NativeAot) {
    foreach ($framework in $NativeAotFrameworks) {
        $nativeOutput = Join-Path $runDirectory "native/$framework"
        Write-Host "Publishing Native AOT package consumer for $framework."
        Invoke-DotNet @('publish', $fixture, '-c', 'Release', '-f', $framework, "-p:TargetFrameworks=$framework", '-r', 'win-x64', '-p:PublishAot=true', '-p:IlcTreatWarningsAsErrors=true', '-p:ILLinkTreatWarningsAsErrors=true', "-p:RestoreConfigFile=$configuration", "-p:RestorePackagesPath=$cache", '-o', $nativeOutput, '-m:1', '--nologo')
        Assert-PackageIdentity $fixture
        & (Join-Path $nativeOutput 'Consumer.Binary.exe')
        if ($LASTEXITCODE -ne 0) {
            throw "The $framework Native AOT package consumer failed."
        }
    }
}

Write-Host "Package verification completed. Artifacts: $runDirectory"
