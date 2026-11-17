[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw "Output directory already exists: $outputRoot" }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$publishRoot = Join-Path $outputRoot 'publish'
$artifactRoot = Join-Path $outputRoot 'artifacts'
$logRoot = Join-Path $outputRoot 'logs'
New-Item -ItemType Directory -Path $logRoot | Out-Null
& $Dotnet publish (Join-Path $sourceRoot 'DeskWidgets.csproj') --configuration Release --runtime win-x64 --self-contained true --artifacts-path $artifactRoot --output $publishRoot "-p:Version=$Version" -p:TargetLatestRuntimePatch=true -p:DebugType=None -p:DebugSymbols=false 2>&1 | Tee-Object -FilePath (Join-Path $logRoot 'publish.log')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
$runtime = Get-Content -LiteralPath (Join-Path $publishRoot 'DeskWidgets.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks -or -not (Test-Path -LiteralPath (Join-Path $publishRoot 'coreclr.dll'))) { throw 'Expected a self-contained Windows runtime' }
$assetFile = Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'obj') -Filter project.assets.json -Recurse -File | Select-Object -First 1
$assets = Get-Content -LiteralPath $assetFile.FullName -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = switch ($framework.name) { 'Microsoft.NETCore.App' { 'microsoft.netcore.app.runtime.win-x64' } 'Microsoft.WindowsDesktop.App' { 'microsoft.windowsdesktop.app.runtime.win-x64' } default { '' } }
    if (-not $package) { continue }
    $packageFolder = $packageRoots | ForEach-Object { Join-Path (Join-Path $_ $package) $framework.version } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($packageFolder) {
        $destination = Join-Path $publishRoot "licenses/$($framework.name)-$($framework.version)"
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $packageFolder -File -Recurse | Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -like '*ThirdPartyNotices*' -or $_.Name -like '*THIRD-PARTY-NOTICES*' } | Copy-Item -Destination $destination -Force
    }
}
$forbidden = Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Where-Object { $_.Name -in @('config.json','config.json.tmp','notes-history.json','notes-history.json.bak','notes-history.json.tmp','input-trace.jsonl','DeskWidgets.log') -or $_.Name -like '*.pdb' -or $_.Name -like '*.runtimeconfig.dev.json' }
if ($forbidden) { throw "Private/development files found in publish output: $($forbidden.Name -join ', ')" }
if ((Get-ChildItem -LiteralPath (Join-Path $publishRoot 'widgets') -Filter widget.json -Recurse -File).Count -ne 15) { throw 'Widget resources incomplete' }
$archiveName = "DeskWidgets-v$Version-win-x64.zip"
$archive = Join-Path $outputRoot $archiveName
Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $archiveName" | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ascii
[ordered]@{ version=$Version; runtime='win-x64'; selfContained=$true; frameworks=$runtime.runtimeOptions.includedFrameworks; sha256=$hash; archive=$archiveName } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'release-manifest.json') -Encoding utf8
Write-Output "Archive: $archive"
Write-Output "SHA256: $hash"
