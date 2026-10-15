param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$projectPath = [System.IO.Path]::GetFullPath($Project)
$publishPath = [System.IO.Path]::GetFullPath($Publish)
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$artifactPrefix = $artifactRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (-not $publishPath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Lizenz-Paketierung darf nur in den Projektordner artifacts schreiben.'
}
if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw "Publish-Ausgabe fehlt: $publishPath"
}

$projectDirectory = Split-Path $projectPath -Parent
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$assemblyName = [string]$projectXml.Project.PropertyGroup.AssemblyName
if (-not $assemblyName) { throw 'AssemblyName fehlt im Projekt.' }
$depsPath = Join-Path $projectDirectory "obj\Release\net9.0-windows\win-x64\$assemblyName.deps.json"
$assetsPath = Join-Path $projectDirectory 'obj\project.assets.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$packageRoot = [string]$assets.project.restore.packagesPath
if (-not $packageRoot -or -not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw 'NuGet-Paketordner aus project.assets.json fehlt.'
}
$runtimeTarget = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
if (-not $runtimeTarget) { throw 'Win-x64-Laufzeitziel fehlt in der Publish-Metadatei.' }

function Get-RuntimePackageDirectory([string]$name) {
    $prefix = "runtimepack.$name/"
    $matches = @($runtimeTarget.PSObject.Properties | Where-Object { $_.Name.StartsWith($prefix, [System.StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) { throw "Laufzeitpaket nicht eindeutig: $name" }
    $version = $matches[0].Name.Substring($prefix.Length)
    if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Unerwartete Laufzeitversion: $version" }
    return Join-Path $packageRoot ($name.ToLowerInvariant() + '\' + $version)
}

$coreRuntime = Get-RuntimePackageDirectory 'Microsoft.NETCore.App.Runtime.win-x64'
$desktopRuntime = Get-RuntimePackageDirectory 'Microsoft.WindowsDesktop.App.Runtime.win-x64'
$licenses = Join-Path $publishPath 'Licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null

$copies = @(
    @{ Source = (Join-Path $root 'LICENSE'); Target = (Join-Path $publishPath 'LICENSE.txt') },
    @{ Source = (Join-Path $root 'docs\THIRD_PARTY_LICENSES.md'); Target = (Join-Path $publishPath 'THIRD_PARTY_LICENSES.md') },
    @{ Source = (Join-Path $coreRuntime 'LICENSE.TXT'); Target = (Join-Path $licenses 'dotnet-runtime-LICENSE.txt') },
    @{ Source = (Join-Path $coreRuntime 'THIRD-PARTY-NOTICES.TXT'); Target = (Join-Path $licenses 'dotnet-runtime-THIRD-PARTY-NOTICES.txt') },
    @{ Source = (Join-Path $desktopRuntime 'LICENSE'); Target = (Join-Path $licenses 'windowsdesktop-runtime-LICENSE.txt') }
)
foreach ($copy in $copies) {
    if (-not (Test-Path -LiteralPath $copy.Source -PathType Leaf)) {
        throw "Erforderlicher Lizenztext fehlt: $($copy.Source)"
    }
    Copy-Item -LiteralPath $copy.Source -Destination $copy.Target -Force
    if ((Get-FileHash -LiteralPath $copy.Source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $copy.Target -Algorithm SHA256).Hash) {
        throw "Lizenztext wurde nicht unverändert übernommen: $($copy.Target)"
    }
}
Write-Output "Lizenztexte paketiert: $($copies.Count) Dateien."
