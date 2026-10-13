param([int]$ThrottleLimit = 6)
$ErrorActionPreference = 'Stop'
$researchDir = $PSScriptRoot
$commitSha = (Get-Content -LiteralPath (Join-Path $researchDir 'minecraft-data-commit.txt') -Raw).Trim()
if ($commitSha -notmatch '^[a-f0-9]{40}$') { throw 'Expected a pinned minecraft-data commit.' }
$paths = Get-Content -LiteralPath (Join-Path $researchDir 'prismarine-data-paths.json') -Raw | ConvertFrom-Json -AsHashtable
$protocolVersions = Get-Content -LiteralPath (Join-Path $researchDir 'prismarine-protocol-versions.json') -Raw | ConvertFrom-Json -AsHashtable
$releases = @(Import-Csv -LiteralPath (Join-Path $researchDir 'java-release-inventory.csv'))
$datasets = @('protocol','commands','blocks','items','entities','effects','enchantments','particles','attributes','biomes','sounds')
$requestedFiles = @{}
foreach ($release in $releases) {
    if (-not $paths.pc.Contains($release.id)) { continue }
    foreach ($dataset in $datasets) {
        $dataPath = $paths.pc[$release.id][$dataset]
        if ($dataPath) { $requestedFiles[($dataPath + '/' + $dataset + '.json')] = $true }
    }
}
$downloadResults = @($requestedFiles.Keys | ForEach-Object -Parallel {
    $relativePath = $_
    $targetPath = Join-Path (Join-Path $using:researchDir 'minecraft-data') $relativePath
    $url = 'https://raw.githubusercontent.com/PrismarineJS/minecraft-data/' + $using:commitSha + '/data/' + $relativePath
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        if (-not (Test-Path -LiteralPath $targetPath)) { Invoke-WebRequest -Uri $url -OutFile $targetPath }
        $parsed = Get-Content -LiteralPath $targetPath -Raw | ConvertFrom-Json -AsHashtable
        [pscustomobject]@{path=$relativePath;url=$url;status='downloaded-and-json-parsed';sha256=(Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $targetPath).Length;error=$null}
    } catch {
        [pscustomobject]@{path=$relativePath;url=$url;status='failed';sha256=$null;bytes=$null;error=$_.Exception.Message}
    }
} -ThrottleLimit $ThrottleLimit)
$downloadResults | Sort-Object path | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $researchDir 'source-files.json') -Encoding utf8
$resultsByPath = @{}
foreach ($result in $downloadResults) { $resultsByPath[$result.path] = $result }
$schemaSummaries = @{}
$profiles = foreach ($release in $releases) {
    $protocolMatch = @($protocolVersions | Where-Object { $_.minecraftVersion -ceq $release.id })
    if ($protocolMatch.Count -gt 1) { throw ('Ambiguous protocol ID for ' + $release.id) }
    $profile = [ordered]@{id=$release.id;releaseTime=$release.releaseTime;javaMajor=[int]$release.javaMajor;javaSource='Mojang launcher metadata, not a tested minimum';protocolId=$null;dataVersion=$null;protocolIdSource=$null;hasExactDataPathEntry=$paths.pc.Contains($release.id);datasets=[ordered]@{};launchStatus='not-tested';embeddingStatus='not-tested';editingStatus='not-tested'}
    if ($protocolMatch.Count -eq 1) {
        $profile.protocolId = $protocolMatch[0].version
        $profile.dataVersion = $protocolMatch[0].dataVersion
        $profile.protocolIdSource = 'PrismarineJS protocolVersions at pinned commit; not packet-capture verified'
    }
    foreach ($dataset in $datasets) {
        $dataPath = if ($profile.hasExactDataPathEntry) { $paths.pc[$release.id][$dataset] } else { $null }
        if (-not $dataPath) { $profile.datasets[$dataset] = @{status='missing-exact-entry';path=$null}; continue }
        $relativePath = $dataPath + '/' + $dataset + '.json'
        if ($resultsByPath[$relativePath].status -ne 'downloaded-and-json-parsed') { $profile.datasets[$dataset] = @{status='download-failed';path=$relativePath}; continue }
        $data = Get-Content -LiteralPath (Join-Path (Join-Path $researchDir 'minecraft-data') $relativePath) -Raw | ConvertFrom-Json -AsHashtable
        $profile.datasets[$dataset] = @{status='source-data-parsed-not-runtime-tested';path=$relativePath;topLevelCount=$data.Count}
        if ($dataset -eq 'commands') {
            $profile.datasets[$dataset].rootCommandNames = @($data.root.children | ForEach-Object { $_.name })
            $profile.datasets[$dataset].rootCommandCount = $profile.datasets[$dataset].rootCommandNames.Count
        }
        if ($dataset -eq 'protocol' -and -not $schemaSummaries.Contains($relativePath)) {
            $states = [ordered]@{}
            foreach ($state in $data.Keys | Where-Object { $_ -ne 'types' }) {
                $directions = [ordered]@{}
                foreach ($direction in @('toClient','toServer')) {
                    $packetTypes = $data[$state][$direction]['types']
                    $names = @($packetTypes.Keys | Where-Object { $_ -like 'packet_*' } | Sort-Object)
                    $directions[$direction] = @{packetTypeCount=$names.Count;packetTypes=$names}
                }
                $states[$state] = $directions
            }
            $schemaSummaries[$relativePath] = @{states=$states;globalTypeCount=$data.types.Count;status='structural-summary-only'}
        }
    }
    $profile
}
$profiles | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $researchDir 'release-coverage.json') -Encoding utf8
$schemaSummaries | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $researchDir 'protocol-schema-index.json') -Encoding utf8
$profiles | ForEach-Object {
    [pscustomobject]@{id=$_.id;releaseTime=$_.releaseTime;javaMajor=$_.javaMajor;protocolId=$_.protocolId;dataVersion=$_.dataVersion;hasExactDataPathEntry=$_.hasExactDataPathEntry;protocolPath=$_.datasets.protocol.path;protocolStatus=$_.datasets.protocol.status;commandsPath=$_.datasets.commands.path;commandsStatus=$_.datasets.commands.status;launchStatus=$_.launchStatus;embeddingStatus=$_.embeddingStatus;editingStatus=$_.editingStatus}
} | Export-Csv -LiteralPath (Join-Path $researchDir 'java-release-inventory.csv') -NoTypeInformation -Encoding utf8
$summary = [ordered]@{researchDate='2026-10-12';minecraftDataCommit=$commitSha;releaseCount=$releases.Count;exactDataPathEntries=@($profiles | Where-Object hasExactDataPathEntry).Count;protocolIdMatches=@($profiles | Where-Object { $null -ne $_.protocolId }).Count;uniqueProtocolSchemas=$schemaSummaries.Count;requestedSourceFiles=$requestedFiles.Count;downloadedSourceFiles=@($downloadResults | Where-Object status -eq 'downloaded-and-json-parsed').Count;failedSourceFiles=@($downloadResults | Where-Object status -eq 'failed').Count;runtimeTestedReleases=0}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $researchDir 'coverage-summary.json') -Encoding utf8
$summary | ConvertTo-Json
