# Player guide: joining a Mod Syncer server

You install two things once. Everything after that is automatic.

Download the latest Mod Syncer zip from the releases page:
https://github.com/AngusMacleod91/Valheim-Mod-Syncer/releases/latest

## Install once

**Easiest: the installer.** Download `ModSyncer-Installer.zip` from the releases page, unzip it
anywhere, and double-click `Install-ModSyncer.bat`. It finds your Valheim folder (or asks for it),
installs BepInEx if you do not have it, and installs the latest Mod Syncer. Run it again any time
to update. Windows may show a blue "protected your PC" box the first time: click "More info", then
"Run anyway". You can read exactly what it does in `Install-ModSyncer.ps1`, it is plain text.

**On Linux or Steam Deck.** The same zip contains `install-modsyncer.sh`. In a terminal:

```
bash install-modsyncer.sh
```

It needs `curl` and `unzip`. Afterwards, tell Steam to start the game through BepInEx: right-click
Valheim, Properties, Launch Options, and enter `./start_game_bepinex.sh %command%`. On Steam Deck
do this in Desktop Mode.

**With r2modman.** Install r2modman, pick Valheim, create a profile. Install
BepInExPack_Valheim from its mod list. Then use Settings, Import local mod, and choose the
`Boogytime-ModSyncer-x.y.z.zip` your host gave you. Always launch Valheim through r2modman's
"Start modded" button.

**Without a mod manager.** Download BepInExPack_Valheim from Thunderstore and copy the contents of
its `BepInExPack_Valheim` folder into your Valheim folder, next to `valheim.exe`. Unzip Mod
Syncer: put the files from `plugins` into `BepInEx\plugins\Boogytime-ModSyncer\` (including
`manifest.json`) and the files from `patchers` into `BepInEx\patchers\Boogytime-ModSyncer\`.

## Joining

Join the server as normal. One of two things happens:

- **You get in.** Your mods matched. Nothing to do.
- **"This server needs different mods"** appears instead of the password box. Underneath it says
  what is being downloaded. Wait for "Downloaded. Quit Valheim completely, start it again, and
  rejoin", then do exactly that. On the next launch the mods are installed and you get in.

You can keep other mods installed that the server does not list; they are left alone.

## If something goes wrong

- **"Download failed"**: check your internet connection and try joining again.
- **Still refused after restarting**: make sure you quit Valheim fully, not just to the menu. If
  it persists, send your host the file `BepInEx\LogOutput.log` from your Valheim (or r2modman
  profile) folder.
- **Plain "Incompatible version" with no mod list**: either your game version differs from the
  server's, or Mod Syncer is not installed on your side.
