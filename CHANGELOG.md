# Changelog

## 0.1.0 (2026-09-03)

First working version.

- Server and clients exchange mod lists during the connection handshake.
- Server rejects clients whose mods do not match; the rule list is every mod installed on the
  server plus `BepInEx/config/ModSyncer.extra-mods.txt`.
- Client sees the missing/outdated mods on the game's connection-failed panel, downloads the exact
  versions from Thunderstore in the background, and installs them on the next launch through a
  BepInEx preloader patcher.
- Verified end to end on a Windows dedicated server and client on one PC.
