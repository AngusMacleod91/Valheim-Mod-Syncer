<#
.SYNOPSIS
    Builds a Release version of Mod Syncer and packs it into a Thunderstore-style zip.

.DESCRIPTION
    Thunderstore packages are plain zip files with a fixed layout:
        manifest.json     name, version, description, dependencies
        README.md         shown on the mod page
        CHANGELOG.md      optional
        icon.png          256x256
        plugins/...       goes to BepInEx/plugins/Author-Name/
        patchers/...      goes to BepInEx/patchers/Author-Name/

    r2modman understands this layout ("Import local mod"), Thunderstore accepts it for
    publishing, and Mod Syncer's own downloader unpacks it the same way. The result lands in
    dist\<Namespace>-<Name>-<Version>.zip.

    Run from a PowerShell window in the repo:
        .\tools\Pack-Thunderstore.ps1
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Read the shared settings so the zip name and manifest match the build exactly.
[xml]$props = Get-Content "Directory.Build.props"
$pg = $props.Project.PropertyGroup
$version = $pg.Version
$namespace = $pg.ThunderstoreNamespace
$name = $pg.ThunderstoreName

Write-Host "Building $Configuration..."
dotnet build ValheimModSyncer.slnx -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$pluginOut = "src\ModSyncer\bin\$Configuration\net472"
$patcherOut = "src\ModSyncer.Patcher\bin\$Configuration\net472"

$stage = Join-Path $env:TEMP "ModSyncer-pack"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path "$stage\plugins", "$stage\patchers" | Out-Null

Copy-Item "$pluginOut\ModSyncer.dll" "$stage\plugins\"
Copy-Item "$patcherOut\ModSyncer.Patcher.dll" "$stage\patchers\"
Copy-Item "$pluginOut\manifest.json" "$stage\"
Copy-Item "README.md" "$stage\"
Copy-Item "CHANGELOG.md" "$stage\"
Copy-Item "thunderstore\icon.png" "$stage\"

New-Item -ItemType Directory -Path "dist" -Force | Out-Null
$zip = Join-Path $repoRoot "dist\$namespace-$name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Not Compress-Archive: on Windows PowerShell 5.1 it writes folder separators as backslashes,
# which Thunderstore rejects. The .NET zip API writes proper forward slashes.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        # Entry names inside a zip must use forward slashes regardless of operating system.
        $entryName = $file.FullName.Substring($stage.Length).TrimStart('\').Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Packed: $zip"
Write-Host "Contents:"
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
$archive.Entries | ForEach-Object { "  " + $_.FullName + "  (" + $_.Length + " bytes)" }
$archive.Dispose()
