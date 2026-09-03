# Host guide: running a server with Mod Syncer

You run the dedicated server. Everyone else only has to install Mod Syncer once; after that the
server keeps them in line automatically.

## One-time setup

1. **Install the Valheim Dedicated Server** from Steam (Library, filter Tools). Note its folder,
   usually `C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server`.
2. **Install BepInEx** into that folder. Download BepInExPack_Valheim from Thunderstore, open the
   zip, and copy everything inside its `BepInExPack_Valheim` folder next to `valheim_server.exe`.
3. **Rename `winhttp.dll` to `version.dll`** in the server folder. Without this the Windows
   dedicated server exits silently a second after starting. (Linux servers do not need this.)
4. **Install Mod Syncer**: unzip `Boogytime-ModSyncer-x.y.z.zip`. Put the contents of its
   `plugins` folder into `BepInEx\plugins\Boogytime-ModSyncer\` and the contents of `patchers`
   into `BepInEx\patchers\Boogytime-ModSyncer\`. Copy `manifest.json` into the plugins folder too.
5. **Install the mods you want the server to run**, each in its own `BepInEx\plugins\Author-Name\`
   folder containing that mod's `manifest.json`. The easiest way is to build the mod list in
   r2modman on your PC and copy its `BepInEx\plugins` folder across.
6. Start the server once with your usual start script. Mod Syncer creates two files in
   `BepInEx\config\`:
   - `com.boogytime.valheim.modsyncer.cfg` with the settings below.
   - `ModSyncer.extra-mods.txt` for mods the automatic rule cannot express.

If you prefer a script, `tools\Setup-TestServer.ps1` in the repo does steps 2 to 4 and writes a
start script. Edit its parameters for a real server (name, world, password, save folder).

## How the enforced list is built

- Every mod in `BepInEx\plugins\Author-Name\` with a `manifest.json` is required on all clients
  at the same version. Update a mod on the server and everyone updates on their next join.
- `ModSyncer.extra-mods.txt`, one mod per line, `Namespace-Name-Version [both|client|server]`:
  - `client`: players need it, the server does not run it. UI and camera mods go here.
  - `server`: only the server runs it; players are never asked for it.
  - `both`: same as an installed mod; useful for pinning a version.
- The list is logged at startup as "Server is enforcing N mod(s)". Check it there.

## Settings (`com.boogytime.valheim.modsyncer.cfg`)

| Setting | Default | Meaning |
| --- | --- | --- |
| Server.RequireSyncerOnClients | true | Reject players who do not have Mod Syncer at all. |
| Server.EnforceInstalledMods | true | Every installed server mod is required on clients. |
| Server.IgnoreMods | empty | Comma-separated `Namespace-Name` list to exempt from enforcement. |
| Server.RescanEveryConnection | false | Rebuild the list on every join instead of at startup. Handy while setting up. |

## Day to day

- **Adding or updating a mod:** install it on the server, restart the server. Players are refused
  once, download, restart, and join. Warn them in chat so they are not surprised.
- **Client-only mods:** add a `client` line to `ModSyncer.extra-mods.txt`, restart the server.
- **Someone cannot connect:** the server log (`BepInEx\LogOutput.log`) has a "Verdict" line per
  connection attempt listing exactly what was missing.

## Limits to know about

- Mods installed as loose `.dll` files without an `Author-Name` folder are invisible to Mod Syncer.
- BepInEx itself is never updated by Mod Syncer.
- Players need Mod Syncer installed once by hand. After that it updates itself like any other mod.
