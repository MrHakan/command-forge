param([switch]$Background)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\UniversalEditor\UniversalEditor.csproj'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Command Forge build failed.' }
$executable = Join-Path $PSScriptRoot 'src\UniversalEditor\bin\Release\net9.0-windows\CommandForge.exe'
if ($Background) { Start-Process -FilePath $executable -ArgumentList '--background' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden }
else { Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden }
