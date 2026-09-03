<#
.SYNOPSIS
    One-click installer for Valheim Mod Syncer (and BepInEx if missing).

.DESCRIPTION
    Run this by double-clicking Install-ModSyncer.bat next to it. It will:
      1. Find your Valheim folder (or ask you for it).
      2. Install BepInEx, the mod loader, if it is not already there.
      3. Download the latest Mod Syncer release from GitHub and put it in place.
    Safe to run again later: it simply updates to the newest release.

    If you use r2modman you do NOT need this; import the Mod Syncer zip there instead.

.PARAMETER ValheimDir
    Skip the folder prompt and use this path.

.PARAMETER Server
    Install into a Valheim Dedicated Server folder instead of the game. Also applies the
    winhttp.dll -> version.dll rename the Windows server needs.

.PARAMETER NonInteractive
    Never prompt (for testing/automation). Fails instead of asking.
#>
param(
    [string]$ValheimDir = "",
    [switch]$Server,
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"
# Older Windows PowerShell defaults to a TLS version GitHub and Thunderstore no longer accept.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GitHubRepo   = "AngusMacleod91/Valheim-Mod-Syncer"
$ModFolder    = "Boogytime-ModSyncer"
$BepInExApi   = "https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/"
$exeName      = if ($Server) { "valheim_server.exe" } else { "valheim.exe" }
$temp         = Join-Path $env:TEMP "ModSyncer-installer"

function Write-Step($text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host ""; Write-Host "ERROR: $text" -ForegroundColor Red; if (-not $NonInteractive) { Read-Host "Press Enter to close" }; exit 1 }

# ---------------------------------------------------------------- 1. find Valheim

function Find-ValheimCandidates {
    $folderName = if ($Server) { "Valheim dedicated server" } else { "Valheim" }
    $found = New-Object System.Collections.Generic.List[string]
    $steamRoots = @()
    foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam") {
        try {
            $p = (Get-ItemProperty $key -ErrorAction Stop)
            if ($p.SteamPath) { $steamRoots += $p.SteamPath }
            if ($p.InstallPath) { $steamRoots += $p.InstallPath }
        } catch { }
    }
    $steamRoots += "C:\Program Files (x86)\Steam"
    foreach ($root in $steamRoots | Select-Object -Unique) {
        # libraryfolders.vdf lists every Steam library drive.
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        $libs = @($root)
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs += $m.Groups[1].Value.Replace('\\', '\')
            }
        }
        foreach ($lib in $libs) {
            $candidate = Join-Path $lib "steamapps\common\$folderName"
            if (Test-Path (Join-Path $candidate $exeName)) { $found.Add($candidate) }
        }
    }
    return $found | Select-Object -Unique
}

Write-Step "Locating $(if ($Server) { 'Valheim Dedicated Server' } else { 'Valheim' })"
if ($ValheimDir -eq "") {
    $candidates = @(Find-ValheimCandidates)
    if ($candidates.Count -gt 0) {
        Write-Host "Found: $($candidates[0])"
        if ($NonInteractive) { $ValheimDir = $candidates[0] }
        else {
            $answer = Read-Host "Use this folder? [Y/n]"
            if ($answer -eq "" -or $answer -match '^[Yy]') { $ValheimDir = $candidates[0] }
        }
    }
    if ($ValheimDir -eq "") {
        if ($NonInteractive) { Fail "Could not find Valheim automatically. Pass -ValheimDir." }
        Write-Host "Type the full path of the folder that contains $exeName"
        Write-Host "(Steam: right-click Valheim > Manage > Browse local files)"
        $ValheimDir = (Read-Host "Folder").Trim('"', ' ')
    }
}
if (-not (Test-Path (Join-Path $ValheimDir $exeName))) { Fail "$exeName not found in '$ValheimDir'." }
Write-Host "Using: $ValheimDir"

New-Item -ItemType Directory -Path $temp -Force | Out-Null

# ---------------------------------------------------------------- 2. BepInEx

Write-Step "Checking BepInEx (the mod loader)"
$bepCore = Join-Path $ValheimDir "BepInEx\core\BepInEx.dll"
$stubWin = Join-Path $ValheimDir "winhttp.dll"
$stubVer = Join-Path $ValheimDir "version.dll"
if ((Test-Path $bepCore) -and ((Test-Path $stubWin) -or (Test-Path $stubVer))) {
    Write-Host "Already installed."
} else {
    Write-Host "Not found. Downloading BepInExPack_Valheim from Thunderstore..."
    $info = Invoke-RestMethod -Uri $BepInExApi -UseBasicParsing
    $ver = $info.latest.version_number
    $zip = Join-Path $temp "BepInExPack_Valheim-$ver.zip"
    Invoke-WebRequest -Uri $info.latest.download_url -OutFile $zip -UseBasicParsing
    $unpack = Join-Path $temp "bepinex"
    if (Test-Path $unpack) { Remove-Item $unpack -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $unpack -Force
    $packRoot = Join-Path $unpack "BepInExPack_Valheim"
    if (-not (Test-Path $packRoot)) { Fail "Unexpected BepInEx zip layout." }
    Copy-Item -Path (Join-Path $packRoot "*") -Destination $ValheimDir -Recurse -Force
    Write-Host "Installed BepInExPack_Valheim $ver."
}

if ($Server -and (Test-Path $stubWin)) {
    # The Windows dedicated server exits silently with the stub named winhttp.dll (Steam networking clash).
    Move-Item $stubWin $stubVer -Force
    Write-Host "Renamed loader stub winhttp.dll -> version.dll (needed on dedicated servers)."
}

# ---------------------------------------------------------------- 3. Mod Syncer

Write-Step "Downloading the latest Mod Syncer release from GitHub"
$headers = @{ "User-Agent" = "ModSyncer-installer" }   # GitHub's API refuses requests without one
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$GitHubRepo/releases/latest" -Headers $headers -UseBasicParsing
$asset = $release.assets | Where-Object { $_.name -like "*ModSyncer*.zip" } | Select-Object -First 1
if (-not $asset) { Fail "The latest release ($($release.tag_name)) has no Mod Syncer zip attached." }
Write-Host "Release $($release.tag_name): $($asset.name)"
$modZip = Join-Path $temp $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $modZip -UseBasicParsing -Headers $headers

$unpackMod = Join-Path $temp "modsyncer"
if (Test-Path $unpackMod) { Remove-Item $unpackMod -Recurse -Force }
Expand-Archive -Path $modZip -DestinationPath $unpackMod -Force

$pluginDest  = Join-Path $ValheimDir "BepInEx\plugins\$ModFolder"
$patcherDest = Join-Path $ValheimDir "BepInEx\patchers\$ModFolder"
foreach ($d in $pluginDest, $patcherDest) { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Path $d | Out-Null }
Copy-Item (Join-Path $unpackMod "plugins\*")  $pluginDest  -Recurse -Force
Copy-Item (Join-Path $unpackMod "patchers\*") $patcherDest -Recurse -Force
# manifest.json tells Mod Syncer (and mod managers) which version this folder holds.
Copy-Item (Join-Path $unpackMod "manifest.json") $pluginDest -Force
Write-Host "Installed to $pluginDest"
Write-Host "         and $patcherDest"

Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- done

Write-Host ""
Write-Host "All done." -ForegroundColor Green
if ($Server) {
    Write-Host "Start the server as usual. The log will show 'Server is enforcing N mod(s)'."
} else {
    Write-Host "Launch Valheim normally through Steam and join the server."
    Write-Host "If the server needs mods you do not have, you will see a message, the download"
    Write-Host "happens on its own, and then you restart Valheim once."
}
if (-not $NonInteractive) { Write-Host ""; Read-Host "Press Enter to close" }
