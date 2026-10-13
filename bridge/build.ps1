param([string]$PrismData = "$env:APPDATA\PrismLauncher", [string]$Jdk = 'C:\Program Files\Java\jdk-25.0.4', [switch]$CompileOnly, [switch]$PackageOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $taskRoot 'artifacts\bridge-26.2'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$libraries = Join-Path $PrismData 'libraries'
$game = Join-Path $libraries 'com\mojang\minecraft\26.2\minecraft-26.2-client.jar'
if (!(Test-Path -LiteralPath $game)) { throw 'Launch Minecraft 26.2 in Prism first to obtain the official local game jar.' }
# Compile against the installed, unobfuscated game; no game classes or dependencies are bundled.
$classpath = (@($game) + @(Get-ChildItem -LiteralPath $libraries -Filter '*.jar' -Recurse | Where-Object { $_.FullName -ne $game -and $_.FullName -notmatch '\\com\\mojang\\minecraft\\' } | ForEach-Object FullName)) -join ';'
$sources = @(Get-ChildItem -LiteralPath "$PSScriptRoot\26.2\src" -Filter '*.java' -Recurse | ForEach-Object FullName)
$argumentsFile = Join-Path $taskRoot 'artifacts\bridge-javac.args'
$arguments = @('-proc:none', '--release', '25', '-encoding', 'UTF-8', '-cp', ('"' + $classpath.Replace('\','/') + '"'), '-d', ('"' + $output.Replace('\','/') + '"')) + @($sources | ForEach-Object { '"' + $_.Replace('\','/') + '"' })
[IO.File]::WriteAllLines($argumentsFile, $arguments, [Text.UTF8Encoding]::new($false))
if (!$PackageOnly) {
    & "$Jdk\bin\javac.exe" "@$argumentsFile"
    if ($LASTEXITCODE -ne 0) { throw 'Bridge compilation failed.' }
}
Copy-Item -LiteralPath "$PSScriptRoot\26.2\resources\fabric.mod.json","$PSScriptRoot\26.2\resources\universal-editor.mixins.json" -Destination $output
if ($CompileOnly) { Write-Output 'Bridge classes compiled.'; return }
$jarPath = Join-Path $taskRoot 'artifacts\universal-editor-bridge-26.2.jar'
# ZipArchive uses literal paths and avoids the JDK jar tool's Windows path normalization issue.
$buffer = [IO.MemoryStream]::new()
$zip = [IO.Compression.ZipArchive]::new($buffer, [IO.Compression.ZipArchiveMode]::Create, $true)
try {
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        $entryName = [IO.Path]::GetRelativePath($output, $file.FullName).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName) | Out-Null
    }
} finally { $zip.Dispose() }
# A security scanner on the project drive can hold a freshly written archive with "Access denied" for minutes.
# Write and verify the jar in %TEMP% first, then copy the verified file over the project artifact.
function Test-Bridge([string]$path, [int]$seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ($true) {
        try {
            $verification = [IO.Compression.ZipFile]::OpenRead($path)
            try { if (!$verification.GetEntry('fabric.mod.json')) { throw 'Bridge manifest is missing.' } } finally { $verification.Dispose() }
            return
        } catch [System.Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [UnauthorizedAccessException] -or [DateTime]::UtcNow -gt $deadline) { throw }
            Write-Host 'Waiting for the archive to become readable (security scan)...'; Start-Sleep -Seconds 2
        }
    }
}
$staging = Join-Path ([IO.Path]::GetTempPath()) ("command-forge-bridge-" + [guid]::NewGuid().ToString('N') + ".jar")
try {
    try { [IO.File]::WriteAllBytes($staging, $buffer.ToArray()) } finally { $buffer.Dispose() }
    Test-Bridge $staging 120
    Copy-Item -LiteralPath $staging -Destination $jarPath -Force
} finally { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -ErrorAction SilentlyContinue } }
# Fail the build here if a locked/unreadable archive would otherwise be deployed to Fabric.
Test-Bridge $jarPath 120
Write-Output "Built artifacts/universal-editor-bridge-26.2.jar"
