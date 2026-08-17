# build.ps1 - one command for CI and releases: dependencies, Release build, tests, single-file publish.
#
#   .\build.ps1                        submodule, build, tests
#   .\build.ps1 -BezTestow             build only
#   .\build.ps1 -Publish               + publish\Duble.exe (self-contained, win-x64, single file)
#   .\build.ps1 -Publish -Wersja 1.0.1 version stamped into the binaries (used by the release workflow)
#   .\build.ps1 -Uruchom               + start the app in developer mode (interface from ui\, DevTools)
#
# Working in an IDE? You do not need this script: open Duble.sln in Visual Studio or Rider and build.
# CodeWalker.Core lives in the external\CodeWalker submodule, so a clone with submodules is enough.
[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$Uruchom,
    [switch]$BezTestow,
    [string]$Wersja
)
$ErrorActionPreference = 'Stop'
$tu = $PSScriptRoot
$sln = Join-Path $tu 'Duble.sln'

function Krok($t) { Write-Host "== $t" -ForegroundColor Cyan }

Krok 'tools'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'No .NET SDK - install .NET 10 SDK: https://dotnet.microsoft.com/download' }
$sdk = (dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] } | Where-Object { $_ -like '10.*' } | Select-Object -First 1)
if (-not $sdk) { throw "The .NET 10 SDK is required (found: $((dotnet --list-sdks) -join ', '))" }
Write-Host "   .NET SDK $sdk"

Krok 'CodeWalker submodule'
$codewalker = Join-Path $tu 'external\CodeWalker\CodeWalker.Core\CodeWalker.Core.csproj'
if (-not (Test-Path $codewalker)) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'No git - needed to fetch the CodeWalker submodule (https://github.com/dexyfex/CodeWalker)' }
    git -C $tu submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'git submodule update failed' }
}
Write-Host "   $(git -C (Join-Path $tu 'external\CodeWalker') rev-parse --short HEAD)"

$wersjaArg = if ($Wersja) { "-p:Version=$Wersja" } else { $null }
if ($Wersja) { Write-Host "   version $Wersja" }

Krok 'build Release'
dotnet build $sln -c Release --nologo -v q $wersjaArg
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

if (-not $BezTestow) {
    Krok 'tests'
    dotnet test (Join-Path $tu 'Duble.Tests') -c Release --no-build --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}

if ($Publish) {
    $out = Join-Path $tu 'publish'
    Krok "publish -> $out\Duble.exe"
    # IncludeAllContentForSelfExtract: CodeWalker.Core reads ShadersGen9Conversion.xml and strings.txt from the
    # folder of ITS OWN dll (Assembly.Location). In a plain single-file build that path is empty and Enhanced
    # (gen9) files lose their geometry; with full extraction the bundle unpacks to %TEMP%\.net\Duble\ and works.
    dotnet publish (Join-Path $tu 'Duble.App') -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out --nologo -v q $wersjaArg
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
    $exe = Get-Item (Join-Path $out 'Duble.exe')
    Write-Host ("   {0}  {1:N1} MB  version {2}" -f $exe.FullName, ($exe.Length / 1MB), $exe.VersionInfo.ProductVersion) -ForegroundColor Green
}

if ($Uruchom) {
    $exe = Join-Path $tu 'Duble.App\bin\Release\net10.0-windows\Duble.exe'
    Krok "starting $exe --dev"
    Start-Process -FilePath $exe -ArgumentList '--dev' | Out-Null
}
Write-Host 'done' -ForegroundColor Green
