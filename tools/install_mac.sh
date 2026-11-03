#!/bin/sh
# Dino Bones Archipelago: macOS installer.
#
#   sh install_mac.sh                 find the game in your Steam libraries
#   sh install_mac.sh /path/to/DinoBones.app
#
# It installs BepInEx 5.4.23.3 (macOS x64) next to the game app, adds the DinoBonesAP plugin, writes a
# launcher that runs the game under Rosetta on Apple Silicon (BepInEx's injector is Intel-only), and prints
# the Steam launch option to use. Run it again to update the plugin.
set -eu

BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_macos_x64_5.4.23.3.zip"
BEPINEX_SHA256="22a872efae6ee658019bd7c25fdb05b361aad876453a4d7b988790e7259a9088"
PLUGIN_URL="https://github.com/poststructural/DinoBonesAPWorld/releases/latest/download/DinoBonesAP.dll"
LAUNCHER="run_dinobones_ap.sh"

say() { printf '%s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

# ---- find the game -----------------------------------------------------------------------------
find_app() {
    steam="$HOME/Library/Application Support/Steam"
    libs="$steam"
    vdf="$steam/steamapps/libraryfolders.vdf"
    if [ -f "$vdf" ]; then
        # Lines look like:  "path"   "/Volumes/Games/SteamLibrary"
        extra=$(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf")
        libs=$(printf '%s\n%s\n' "$libs" "$extra")
    fi
    printf '%s\n' "$libs" | while IFS= read -r lib; do
        [ -d "$lib/steamapps/common" ] || continue
        find "$lib/steamapps/common" -maxdepth 2 -type d -iname 'DinoBones*.app' 2>/dev/null
    done | head -n 1
}

APP="${1:-}"
if [ -z "$APP" ]; then
    APP=$(find_app)
    [ -n "$APP" ] || die "Could not find DinoBones.app in your Steam libraries. Run: sh install_mac.sh /path/to/DinoBones.app"
fi
APP="${APP%/}"
[ -d "$APP/Contents/MacOS" ] || die "$APP is not a macOS app bundle."
GAME_DIR=$(cd "$(dirname "$APP")" && pwd -P)
APP_NAME=$(basename "$APP")
EXE_NAME=$(defaults read "$GAME_DIR/$APP_NAME/Contents/Info" CFBundleExecutable 2>/dev/null || true)
[ -n "$EXE_NAME" ] || die "Could not read the game's executable name from Info.plist."
EXE="$GAME_DIR/$APP_NAME/Contents/MacOS/$EXE_NAME"
say "Game:    $GAME_DIR/$APP_NAME"

# BepInEx 5 only injects into the Intel build, so the game must have an x86_64 slice.
case "$(file -b "$EXE")" in
    *x86_64*) ;;
    *) die "This game build has no Intel (x86_64) code, so BepInEx 5 cannot load into it. Please report this." ;;
esac
# Apple Silicon runs the game through Rosetta, which must be installed.
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = "1" ] && ! /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then
    say "Rosetta is needed on Apple Silicon. Installing it (you may be asked for your password)..."
    softwareupdate --install-rosetta --agree-to-license || die "Rosetta install failed. Run: softwareupdate --install-rosetta"
fi
[ -d "$GAME_DIR/$APP_NAME/Contents/Resources/Data/Managed" ] || say "Warning: no Managed folder found; the plugin needs the Mono build of the game."

# ---- download ----------------------------------------------------------------------------------
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

say "Downloading BepInEx 5.4.23.3..."
curl -fsSL -o "$TMP/bepinex.zip" "$BEPINEX_URL" || die "BepInEx download failed."
got=$(shasum -a 256 "$TMP/bepinex.zip" | cut -d' ' -f1)
[ "$got" = "$BEPINEX_SHA256" ] || die "BepInEx download checksum mismatch ($got)."

HERE=$(cd "$(dirname "$0")" && pwd -P)
if [ -f "$HERE/DinoBonesAP.dll" ]; then
    say "Using DinoBonesAP.dll next to this script."
    cp "$HERE/DinoBonesAP.dll" "$TMP/DinoBonesAP.dll"
else
    say "Downloading the latest DinoBonesAP.dll..."
    curl -fsSL -o "$TMP/DinoBonesAP.dll" "$PLUGIN_URL" || die "Plugin download failed."
fi

# ---- install -----------------------------------------------------------------------------------
say "Installing BepInEx into $GAME_DIR"
# -n keeps an existing BepInEx config; core files and the launcher script are refreshed.
unzip -q -n "$TMP/bepinex.zip" -d "$GAME_DIR"
unzip -q -o "$TMP/bepinex.zip" 'BepInEx/core/*' run_bepinex.sh libdoorstop.dylib -d "$GAME_DIR"
mkdir -p "$GAME_DIR/BepInEx/plugins"
cp "$TMP/DinoBonesAP.dll" "$GAME_DIR/BepInEx/plugins/DinoBonesAP.dll"

# Our launcher: on Apple Silicon start BepInEx's script as an Intel process, so the game it launches
# runs under Rosetta and the Intel-only injector (libdoorstop.dylib) can load.
cat > "$GAME_DIR/$LAUNCHER" <<'EOF'
#!/bin/sh
# Dino Bones Archipelago launcher (written by install_mac.sh). Steam launch option:
#   "/path/to/run_dinobones_ap.sh" %command%
DIR=$(cd "$(dirname "$0")" && pwd -P)
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = "1" ]; then
    exec arch -x86_64 /bin/sh "$DIR/run_bepinex.sh" "$@"
fi
exec /bin/sh "$DIR/run_bepinex.sh" "$@"
EOF
chmod +x "$GAME_DIR/$LAUNCHER" "$GAME_DIR/run_bepinex.sh"
xattr -dr com.apple.quarantine "$GAME_DIR/BepInEx" "$GAME_DIR/libdoorstop.dylib" "$GAME_DIR/run_bepinex.sh" "$GAME_DIR/$LAUNCHER" 2>/dev/null || true

# Apps signed with the hardened runtime ignore injected libraries. An ad-hoc signature lifts that.
if codesign -dv "$GAME_DIR/$APP_NAME" 2>&1 | grep -q 'flags=.*runtime'; then
    say ""
    say "The game is signed with the hardened runtime, which blocks BepInEx."
    say "Re-signing it locally (ad-hoc) fixes this. Steam's 'Verify integrity of game files' undoes it;"
    say "if that happens, run this installer again."
    answer=y
    if [ -t 0 ]; then
        printf 'Re-sign the game now? [Y/n] '
        read -r answer || answer=y
    fi
    case "$answer" in
        n|N|no|NO) say "Skipped. BepInEx will probably not load until the game is re-signed." ;;
        *)
            codesign --force --deep --sign - "$GAME_DIR/$APP_NAME" || die "Re-signing failed; BepInEx will be blocked."
            say "Re-signed."
            ;;
    esac
fi

OPTION="\"$GAME_DIR/$LAUNCHER\" %command%"
say ""
say "Done. Last step, in Steam:"
say "  Library > DinoBones > right-click > Properties > General > Launch Options, and paste:"
say ""
say "  $OPTION"
say ""
if command -v pbcopy >/dev/null 2>&1; then
    printf '%s' "$OPTION" | pbcopy && say "(The launch option is already on your clipboard.)"
fi
say "Start the Dino Bones Client in Archipelago and connect before starting the game."
say "BepInEx log: $GAME_DIR/BepInEx/LogOutput.log"
