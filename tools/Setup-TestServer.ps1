<#
.SYNOPSIS
    Prepares a Valheim Dedicated Server folder for testing Mod Syncer.

.DESCRIPTION
    Does three things so the server runs with mods:
      1. Installs BepInExPack_Valheim (the mod loader) into the server folder.
         Either downloads it from Thunderstore or unpacks a zip you already have.
      2. Copies the freshly built Mod Syncer plugin and patcher into the server's BepInEx folders,
         using the same Author-Name folder layout mod managers use.
      3. Writes start_test_server.bat, which launches the server with a test world, a password
         and a separate save folder so nothing touches your real worlds.

    Run it from a PowerShell window:
        .\tools\Setup-TestServer.ps1
    Re-running is safe; it only overwrites the files it owns.

.PARAMETER ServerDir
    Where Steam installed "Valheim Dedicated Server".

.PARAMETER BepInExZip
    A BepInExPack_Valheim zip already on disk. If omitted the script downloads the version below.

.PARAMETER BepInExVersion
    Thunderstore version of the pack to download when BepInExZip is not given.
#>
param(
    [string]$ServerDir = "D:\Games\Steam\steamapps\common\Valheim dedicated server",
    [string]$BepInExZip = "",
    [string]$BepInExVersion = "5.4.2333",
    [string]$Namespace = "Boogytime",
    [string]$ModName = "ModSyncer",
    [string]$ServerName = "ModSyncTest",
    [string]$WorldName = "ModSyncTest",
    [string]$Password = "test1234",
    [int]$Port = 2456
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path (Join-Path $ServerDir "valheim_server.exe"))) {
    throw "valheim_server.exe not found in '$ServerDir'. Install 'Valheim Dedicated Server' from Steam (Library > Tools) or pass -ServerDir."
}

# ---------- 1. BepInEx ----------
$tempDir = Join-Path $env:TEMP "ModSyncer-bepinex"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir | Out-Null

if ($BepInExZip -eq "") {
    $BepInExZip = Join-Path $tempDir "BepInExPack_Valheim.zip"
    $url = "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$BepInExVersion/"
    Write-Host "Downloading BepInExPack_Valheim $BepInExVersion from $url"
    Invoke-WebRequest -Uri $url -OutFile $BepInExZip -UseBasicParsing
}

Write-Host "Unpacking $BepInExZip"
Expand-Archive -Path $BepInExZip -DestinationPath (Join-Path $tempDir "unpacked") -Force
$packRoot = Join-Path $tempDir "unpacked\BepInExPack_Valheim"
if (-not (Test-Path $packRoot)) { throw "Zip did not contain a BepInExPack_Valheim folder; is this the right package?" }

Write-Host "Installing BepInEx into $ServerDir"
Copy-Item -Path (Join-Path $packRoot "*") -Destination $ServerDir -Recurse -Force

# IMPORTANT (found the hard way, 2026-09-03): BepInEx gets into the game through a loader stub
# that pretends to be a Windows system DLL. The pack ships it as winhttp.dll, but the dedicated
# server's Steam networking also uses the real WinHTTP library and dies silently a second after
# start when it finds the stub instead. The stub works under the name version.dll too, which
# nothing in the server needs, so rename it. The client is unaffected either way.
$stub = Join-Path $ServerDir "winhttp.dll"
if (Test-Path $stub) {
    Move-Item $stub (Join-Path $ServerDir "version.dll") -Force
    Write-Host "Renamed loader stub winhttp.dll -> version.dll (required for dedicated servers)"
}

# ---------- 2. Mod Syncer ----------
$pluginBuild = Join-Path $repoRoot "src\ModSyncer\bin\Debug\net472"
$patcherBuild = Join-Path $repoRoot "src\ModSyncer.Patcher\bin\Debug\net472"
if (-not (Test-Path (Join-Path $pluginBuild "ModSyncer.dll"))) {
    throw "Build output not found at $pluginBuild. Run 'dotnet build' in the repo first."
}

$pluginDest = Join-Path $ServerDir "BepInEx\plugins\$Namespace-$ModName"
$patcherDest = Join-Path $ServerDir "BepInEx\patchers\$Namespace-$ModName"
New-Item -ItemType Directory -Path $pluginDest -Force | Out-Null
New-Item -ItemType Directory -Path $patcherDest -Force | Out-Null
Copy-Item (Join-Path $pluginBuild "ModSyncer.dll"), (Join-Path $pluginBuild "ModSyncer.pdb"), (Join-Path $pluginBuild "manifest.json") -Destination $pluginDest -Force
Copy-Item (Join-Path $patcherBuild "ModSyncer.Patcher.dll"), (Join-Path $patcherBuild "ModSyncer.Patcher.pdb") -Destination $patcherDest -Force
Write-Host "Mod Syncer copied to $pluginDest and $patcherDest"

# ---------- 3. start script ----------
$saveDir = Join-Path $ServerDir "ModSyncTest-saves"
$bat = @"
@echo off
REM Test server for Mod Syncer. BepInEx loads automatically because winhttp.dll sits next to the exe.
REM -nographics -batchmode : headless (no window rendering)
REM -savedir               : keep test worlds separate from your real ones
REM -public 0              : do not list this server publicly
REM -crossplay is NOT used : Steam networking only, which is what "Join IP" in the client needs.
REM %~dp0 is the folder this .bat lives in, so it works from any working directory.
pushd "%~dp0"
set SteamAppId=892970
"%~dp0valheim_server.exe" -nographics -batchmode -name "$ServerName" -port $Port -world "$WorldName" -password "$Password" -public 0 -savedir "$saveDir" -logFile "%~dp0server_unity.log"
popd
"@
$batPath = Join-Path $ServerDir "start_test_server.bat"
Set-Content -Path $batPath -Value $bat -Encoding ASCII
Write-Host "Wrote $batPath"
Write-Host ""
Write-Host "Done. Start the server by double-clicking start_test_server.bat (or run it from a terminal to watch the log)."
Write-Host "In Valheim: Start Game > Join Game > Join IP > 127.0.0.1:$Port, password '$Password'."
