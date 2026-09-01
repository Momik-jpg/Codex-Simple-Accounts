$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$solution = Join-Path $root 'CodexAccountTray.sln'
$project = Join-Path $root 'src\CodexAccountTray\CodexAccountTray.csproj'
$publish = Join-Path $root 'artifacts\publish'
$installer = Join-Path $root 'installer\CodexAccountTray.iss'

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'Test-Wiederherstellung fehlgeschlagen.' }

dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish-Wiederherstellung fehlgeschlagen.' }

dotnet test $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests fehlgeschlagen.' }

dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore `
    -p:PublishSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish fehlgeschlagen.' }

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 wurde nicht gefunden.' }

& $iscc $installer
if ($LASTEXITCODE -ne 0) { throw 'Installer-Build fehlgeschlagen.' }
