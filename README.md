# Valheim Mod Syncer

A Valheim mod that lets **the server decide which mods everyone runs**. When you connect:

1. Your game sends the server the list of mods you have installed.
2. The server compares it with its own list and either lets you in or refuses you.
3. If refused, the mod downloads the exact versions the server wants from Thunderstore,
   and asks you to restart Valheim. Restart, reconnect, play.

Nobody has to chase mod updates by hand any more. The host updates the server; everyone
else gets fixed up the next time they connect.

## Words you will meet

| Term | Meaning |
| --- | --- |
| **BepInEx** | The mod loader almost every Valheim mod uses. Mods are `.dll` files it loads at game start. |
| **Plugin** | A BepInEx mod. This project's main output, `ModSyncer.dll`, is one. |
| **Preloader patcher** | A special BepInEx add-on that runs *before* any plugin loads. Ours moves downloaded files into place at that moment, because Windows refuses to overwrite a `.dll` the game has already loaded. |
| **Harmony** | A library that lets a mod run code before or after one of the game's own methods. We use it to join in on the connection handshake. |
| **Thunderstore** | The main Valheim mod site. Every mod version has a fixed `Namespace-Name-Version` name and a direct download link. |
| **r2modman** | A mod manager for Thunderstore mods. It installs each mod into its own `Author-Name` folder, which is how this mod recognises what is installed. |
| **Manifest** | Here: the list of mods and versions one side has or demands. Also the name of the `manifest.json` file in every Thunderstore package. |

## How the server decides what to enforce

- **Everything installed on the server** (in `BepInEx/plugins/Author-Name/` folders with a
  `manifest.json`) is required on every client at the same version. Install server mods through
  r2modman, or keep that folder layout, and this just works.
- `BepInEx/config/ModSyncer.extra-mods.txt` adds or overrides entries. Use it for client-only
  mods (UI mods the server never runs), server-only mods (so clients are not asked for them),
  or pinning a version. The file is created with instructions the first time the server runs.
- Extra mods a player has that the server does not list are allowed.

Settings live in `BepInEx/config/com.boogytime.valheim.modsyncer.cfg` after the first run.

## Building it yourself

Requirements: the .NET SDK (the `dotnet` command), Valheim installed, and BepInExPack_Valheim
installed into the game folder so `BepInEx/core/BepInEx.dll` exists. No package downloads are
needed to build.

```bash
dotnet build
```

The build copies the plugin into `<Valheim>/BepInEx/plugins/Boogytime-ModSyncer/` and the
patcher into `<Valheim>/BepInEx/patchers/Boogytime-ModSyncer/`, so launching the game tests
the fresh build. If Valheim lives somewhere else, edit `ValheimDir` in `Directory.Build.props`
or set a `VALHEIM_DIR` environment variable.

## Repo layout

```
Directory.Build.props        shared build settings (game path, version number)
src/ModSyncer/               the plugin: handshake patches, comparison, downloader, popups
src/ModSyncer.Patcher/       the preloader patcher: applies downloads on next launch
src/Shared/StagingPaths.cs   folder layout both projects agree on
```

## Known limits (first version)

- Mods installed as loose `.dll` files (not in an `Author-Name` folder) are invisible to the
  scanner and cannot be enforced. Use a mod manager.
- BepInEx itself is never updated by this mod.
- Updating Mod Syncer's own patcher may need one manual restart if Windows keeps the file locked.
- Players without Mod Syncer see the game's normal "incompatible version" message and nothing
  else, because there is no mod on their side to explain further.
