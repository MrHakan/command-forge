# Builds a standalone Windows x64 folder: dist\CommandForge\CommandForge.exe (single file) plus the
# release catalog and the 26.2 bridge it loads at runtime.
#   ./publish.ps1                       self-contained exe (~110 MB, no .NET install needed)
#   ./publish.ps1 -FrameworkDependent   small exe that needs the .NET 9 Desktop Runtime
#   ./publish.ps1 -Zip                  also create dist\CommandForge-win-x64.zip
#   ./publish.ps1 -Verify               run the windowless tests against a temporary copy of the published exe
param([switch]$FrameworkDependent, [switch]$Zip, [switch]$Verify)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\CommandForge'
$project = Join-Path $root 'src\UniversalEditor\UniversalEditor.csproj'
# Bundle in a fresh temp folder first; the finished exe is then copied into dist.
$stage = Join-Path ([IO.Path]::GetTempPath()) ('CommandForge-publish-' + [guid]::NewGuid().ToString('N').Substring(0, 8))

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
# Compressed single-file bundles failed with "Access denied" while the bundler re-opened the exe on this machine,
# so the bundle is left uncompressed.
dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -o $stage --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $stage '*') $out -Recurse
Remove-Item -Recurse -Force $stage

$research = Join-Path $out 'docs\research'
New-Item -ItemType Directory -Force $research | Out-Null
foreach ($file in 'mojang-version-manifest.json', 'release-coverage.json') { Copy-Item (Join-Path $root "docs\research\$file") $research }
$bridge = Join-Path $root 'artifacts\universal-editor-bridge-26.2.jar'
New-Item -ItemType Directory -Force (Join-Path $out 'artifacts') | Out-Null
if (Test-Path $bridge) { Copy-Item $bridge (Join-Path $out 'artifacts') }
else { Write-Warning 'artifacts\universal-editor-bridge-26.2.jar is missing; run ./bridge/build.ps1 first. 26.2 will launch vanilla without live editing.' }
Copy-Item (Join-Path $root 'docs\UI-GUIDE.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $out
if (Test-Path (Join-Path $root 'LICENSE')) { Copy-Item (Join-Path $root 'LICENSE') $out }
$runtime = if ($FrameworkDependent) { 'Requires the .NET 9 Desktop Runtime (x64).' } else { 'No .NET installation is required.' }
@"
Command Forge (Windows x64)

Start CommandForge.exe. $runtime
Keep the docs and artifacts folders next to the exe: docs\research holds the release catalog and
artifacts holds the Java 26.2 live bridge. Prism Launcher with a Minecraft account is required to launch the game.

Settings, the autosaved draft and crash.log are stored in %LOCALAPPDATA%\CommandForge.
Live editing works with Java 26.2 private singleplayer only. See UI-GUIDE.md.
"@ | Set-Content -Encoding utf8 (Join-Path $out 'README.txt')

$exe = Join-Path $out 'CommandForge.exe'
Write-Host ("Published {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))

if ($Verify) {
    $copy = Join-Path ([IO.Path]::GetTempPath()) ('CommandForge-verify-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Copy-Item -Recurse $out $copy
    foreach ($mode in '--asset-tests', '--self-test-headless') {
        $p = Start-Process (Join-Path $copy 'CommandForge.exe') -ArgumentList $mode -Wait -WindowStyle Hidden -PassThru -WorkingDirectory $copy
        Write-Host "$mode exit $($p.ExitCode)"
        if ($p.ExitCode -ne 0) { throw "$mode failed; output kept in $copy\artifacts" }
    }
    Remove-Item -Recurse -Force $copy
}
if ($Zip) {
    $archive = Join-Path $root 'dist\CommandForge-win-x64.zip'
    if (Test-Path $archive) { Remove-Item $archive }
    Compress-Archive -Path $out -DestinationPath $archive
    Write-Host "Zip: $archive"
}
