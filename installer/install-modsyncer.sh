#!/usr/bin/env bash
#
# One-step installer for Valheim Mod Syncer on Linux (and BepInEx if missing).
#
#   bash install-modsyncer.sh              # game client: finds Valheim, asks to confirm
#   bash install-modsyncer.sh --server     # dedicated server folder instead
#   bash install-modsyncer.sh --dir PATH   # skip detection, use this folder
#   bash install-modsyncer.sh --yes        # never prompt (fails instead of asking)
#
# What it does:
#   1. Finds the Valheim folder (Steam native, Flatpak Steam, extra library drives) or asks.
#   2. Installs BepInExPack_Valheim from Thunderstore if BepInEx/core/BepInEx.dll is missing.
#   3. Downloads the latest Mod Syncer release from GitHub and puts it in place.
# Safe to run again: it simply updates to the newest release.
#
# Needs: bash, curl, unzip. (Debian/Ubuntu: sudo apt install curl unzip)
#
# Linux note: BepInEx on Linux is started by a wrapper script, not by a DLL next to the exe.
#   - Game: in Steam, set the launch options for Valheim to:   ./start_game_bepinex.sh %command%
#   - Server: start it with ./start_server_bepinex.sh (edit the arguments inside it).
# The installer reminds you of this at the end.

set -euo pipefail

GITHUB_REPO="AngusMacleod91/Valheim-Mod-Syncer"
MOD_FOLDER="Boogytime-ModSyncer"
BEPINEX_API="https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/"

SERVER=0
YES=0
VALHEIM_DIR=""
while [ $# -gt 0 ]; do
    case "$1" in
        --server) SERVER=1 ;;
        --yes|-y) YES=1 ;;
        --dir) shift; VALHEIM_DIR="${1:-}" ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
    shift
done

if [ "$SERVER" = 1 ]; then
    EXE_NAME="valheim_server.x86_64"; FOLDER_NAME="Valheim dedicated server"; WHAT="Valheim Dedicated Server"
else
    EXE_NAME="valheim.x86_64"; FOLDER_NAME="Valheim"; WHAT="Valheim"
fi

step() { printf '\n==> %s\n' "$*"; }
fail() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

for tool in curl unzip; do
    command -v "$tool" >/dev/null 2>&1 || fail "'$tool' is not installed. On Debian/Ubuntu: sudo apt install curl unzip"
done

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ---------------------------------------------------------------- 1. find Valheim

find_candidates() {
    local roots=(
        "$HOME/.steam/steam"
        "$HOME/.local/share/Steam"
        "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"   # Flatpak Steam
        "$HOME/.steam/debian-installation"
    )
    local root vdf lib
    for root in "${roots[@]}"; do
        [ -d "$root" ] || continue
        local libs=("$root")
        vdf="$root/steamapps/libraryfolders.vdf"
        if [ -f "$vdf" ]; then
            # Every extra Steam library drive is listed as   "path"  "/mnt/games/SteamLibrary"
            while IFS= read -r lib; do libs+=("$lib"); done < <(sed -n 's/.*"path"[[:space:]]*"\([^"]*\)".*/\1/p' "$vdf")
        fi
        for lib in "${libs[@]}"; do
            if [ -f "$lib/steamapps/common/$FOLDER_NAME/$EXE_NAME" ]; then
                echo "$lib/steamapps/common/$FOLDER_NAME"
            fi
        done
    done | awk '!seen[$0]++'
}

step "Locating $WHAT"
if [ -z "$VALHEIM_DIR" ]; then
    mapfile -t CANDIDATES < <(find_candidates)
    if [ "${#CANDIDATES[@]}" -gt 0 ]; then
        echo "Found: ${CANDIDATES[0]}"
        if [ "$YES" = 1 ]; then
            VALHEIM_DIR="${CANDIDATES[0]}"
        else
            read -r -p "Use this folder? [Y/n] " answer
            case "${answer:-Y}" in [Yy]*) VALHEIM_DIR="${CANDIDATES[0]}" ;; esac
        fi
    fi
    if [ -z "$VALHEIM_DIR" ]; then
        [ "$YES" = 1 ] && fail "Could not find $WHAT automatically. Pass --dir PATH."
        echo "Type the full path of the folder that contains $EXE_NAME"
        read -r -p "Folder: " VALHEIM_DIR
        VALHEIM_DIR="${VALHEIM_DIR%/}"
    fi
fi
[ -f "$VALHEIM_DIR/$EXE_NAME" ] || fail "$EXE_NAME not found in '$VALHEIM_DIR'."
echo "Using: $VALHEIM_DIR"

# ---------------------------------------------------------------- 2. BepInEx

step "Checking BepInEx (the mod loader)"
if [ -f "$VALHEIM_DIR/BepInEx/core/BepInEx.dll" ]; then
    echo "Already installed."
else
    echo "Not found. Downloading BepInExPack_Valheim from Thunderstore..."
    info="$(curl -fsSL "$BEPINEX_API")"
    # Pull the two fields we need out of the JSON without depending on jq.
    ver="$(printf '%s' "$info" | sed -n 's/.*"latest":{[^}]*"version_number":"\([^"]*\)".*/\1/p')"
    url="$(printf '%s' "$info" | sed -n 's/.*"latest":{[^}]*"download_url":"\([^"]*\)".*/\1/p')"
    [ -n "$url" ] || fail "Could not read the BepInEx download link from Thunderstore."
    curl -fsSL "$url" -o "$TMP/bepinex.zip"
    unzip -q "$TMP/bepinex.zip" -d "$TMP/bepinex"
    [ -d "$TMP/bepinex/BepInExPack_Valheim" ] || fail "Unexpected BepInEx zip layout."
    cp -r "$TMP/bepinex/BepInExPack_Valheim/." "$VALHEIM_DIR/"
    chmod +x "$VALHEIM_DIR"/start_game_bepinex.sh "$VALHEIM_DIR"/start_server_bepinex.sh 2>/dev/null || true
    echo "Installed BepInExPack_Valheim $ver."
fi

# ---------------------------------------------------------------- 3. Mod Syncer

step "Downloading the latest Mod Syncer release from GitHub"
release="$(curl -fsSL -H "User-Agent: ModSyncer-installer" "https://api.github.com/repos/$GITHUB_REPO/releases/latest")"
tag="$(printf '%s' "$release" | sed -n 's/.*"tag_name":[[:space:]]*"\([^"]*\)".*/\1/p' | head -n1)"
asset_url="$(printf '%s' "$release" | grep -o '"browser_download_url":[[:space:]]*"[^"]*ModSyncer[^"]*\.zip"' | head -n1 | sed 's/.*"\(https[^"]*\)"/\1/')"
[ -n "$asset_url" ] || fail "The latest release ($tag) has no Mod Syncer zip attached."
echo "Release $tag: ${asset_url##*/}"
curl -fsSL -H "User-Agent: ModSyncer-installer" "$asset_url" -o "$TMP/modsyncer.zip"
unzip -q "$TMP/modsyncer.zip" -d "$TMP/modsyncer"

plugin_dest="$VALHEIM_DIR/BepInEx/plugins/$MOD_FOLDER"
patcher_dest="$VALHEIM_DIR/BepInEx/patchers/$MOD_FOLDER"
rm -rf "$plugin_dest" "$patcher_dest"
mkdir -p "$plugin_dest" "$patcher_dest"
cp -r "$TMP/modsyncer/plugins/." "$plugin_dest/"
cp -r "$TMP/modsyncer/patchers/." "$patcher_dest/"
cp "$TMP/modsyncer/manifest.json" "$plugin_dest/"   # tells Mod Syncer which version this folder holds
echo "Installed to $plugin_dest"
echo "         and $patcher_dest"

# ---------------------------------------------------------------- done

printf '\nAll done.\n'
if [ "$SERVER" = 1 ]; then
    cat <<EOF
Start the server with the BepInEx wrapper so the mod loader is active:
    cd "$VALHEIM_DIR" && ./start_server_bepinex.sh
Edit the server name, world, password and port inside that script first.
The log will show 'Server is enforcing N mod(s)'.
EOF
else
    cat <<EOF
One more step, once only: tell Steam to start Valheim through the BepInEx wrapper.
    Steam > right-click Valheim > Properties > Launch Options, and enter:
    ./start_game_bepinex.sh %command%
Then launch Valheim normally and join the server. If it needs mods you do not have,
you will see a message, the download happens on its own, and you restart Valheim once.
EOF
fi
