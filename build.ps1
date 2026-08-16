# build.ps1 — buduje Duble (Core + App + Cli + Tests) i opcjonalnie publikuje jeden plik Duble.exe.
#
#   .\build.ps1                 zaleznosci (CodeWalker), build Release, testy
#   .\build.ps1 -BezTestow      tylko build
#   .\build.ps1 -Publish        + publish\Duble.exe (self-contained, win-x64, jeden plik)
#   .\build.ps1 -Uruchom        + start aplikacji w trybie deweloperskim (UI z folderu ui\, DevTools)
#
# CodeWalker.Core (dexyfex, MIT) nie jest czescia repo: klonujemy je do ..\CodeWalker (rodzenstwo folderu repo),
# w przypietym commicie — Duble.Core\Duble.Core.csproj wskazuje ..\..\CodeWalker\CodeWalker.Core\CodeWalker.Core.csproj.
[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$Uruchom,
    [switch]$BezTestow,
    [string]$CodeWalkerCommit = '485d56b'
)
$ErrorActionPreference = 'Stop'
$tu = $PSScriptRoot
$sln = Join-Path $tu 'Duble.sln'
$codewalker = Join-Path (Split-Path $tu -Parent) 'CodeWalker'

function Krok($t) { Write-Host "== $t" -ForegroundColor Cyan }

Krok 'sprawdzam narzedzia'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Brak .NET SDK — zainstaluj .NET 10 SDK: https://dotnet.microsoft.com/download' }
$sdk = (dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] } | Where-Object { $_ -like '10.*' } | Select-Object -First 1)
if (-not $sdk) { throw "Potrzebny .NET 10 SDK (znalezione: $((dotnet --list-sdks) -join ', '))" }
Write-Host "   .NET SDK $sdk"

Krok "CodeWalker w $codewalker"
if (-not (Test-Path (Join-Path $codewalker 'CodeWalker.Core\CodeWalker.Core.csproj'))) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Brak git — potrzebny do pobrania CodeWalker (https://github.com/dexyfex/CodeWalker)' }
    Write-Host "   klonuje https://github.com/dexyfex/CodeWalker (commit $CodeWalkerCommit)"
    git clone --quiet https://github.com/dexyfex/CodeWalker $codewalker
    if ($LASTEXITCODE -ne 0) { throw 'git clone CodeWalker nie powiodl sie' }
    git -C $codewalker checkout --quiet $CodeWalkerCommit
    if ($LASTEXITCODE -ne 0) { throw "git checkout $CodeWalkerCommit nie powiodl sie" }
} else {
    $c = (git -C $codewalker rev-parse --short HEAD 2>$null)
    Write-Host "   jest (commit $c)"
    if ($c -and -not $c.StartsWith($CodeWalkerCommit)) { Write-Warning "CodeWalker jest na innym commicie niz przypiety ($CodeWalkerCommit) — jesli cos nie buduje, zrob: git -C `"$codewalker`" checkout $CodeWalkerCommit" }
}

Krok 'build Release'
dotnet build $sln -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build nie powiodl sie' }

if (-not $BezTestow) {
    Krok 'testy'
    dotnet test (Join-Path $tu 'Duble.Tests') -c Release --no-build --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'testy nie przeszly' }
}

if ($Publish) {
    $out = Join-Path $tu 'publish'
    Krok "publish -> $out\Duble.exe"
    # IncludeAllContentForSelfExtract: CodeWalker.Core czyta ShadersGen9Conversion.xml i strings.txt z folderu SWOJEJ dll
    # (Assembly.Location) — w zwyklym single-file to pusta sciezka i pliki Enhanced (gen9) traca geometrie; z pelna
    # ekstrakcja bundle rozpakowuje sie do %TEMP%\.net\Duble\ i wszystko dziala. Kompresja: ~132 MB -> mniej.
    dotnet publish (Join-Path $tu 'Duble.App') -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'publish nie powiodl sie' }
    $exe = Get-Item (Join-Path $out 'Duble.exe')
    Write-Host ("   {0}  {1:N1} MB  wersja {2}" -f $exe.FullName, ($exe.Length / 1MB), $exe.VersionInfo.ProductVersion) -ForegroundColor Green
}

if ($Uruchom) {
    $exe = Join-Path $tu 'Duble.App\bin\Release\net10.0-windows\Duble.exe'
    Krok "uruchamiam $exe --dev"
    Start-Process -FilePath $exe -ArgumentList '--dev' | Out-Null
}
Write-Host 'gotowe' -ForegroundColor Green
