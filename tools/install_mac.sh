#!/bin/sh
# Dino Bones Archipelago: macOS installer (native loader: no BepInEx, no Rosetta, no Steam launch option).
#
#   sh install_mac.sh                              find the game in your Steam libraries
#   sh install_mac.sh /path/to/DinoBones.app
#   sh install_mac.sh --uninstall [/path/to/DinoBones.app]
#
# Install copies DinoBonesAP.dll and Mono.Cecil.dll into DinoBones.app/Contents/Resources/Data/Managed (from this
# script's folder, or downloaded from the latest GitHub release), backs up ScriptingAssemblies.json and
# RuntimeInitializeOnLoads.json (*.dinoap.bak) and adds the plugin's entries to them.
# On the first game start the plugin saves DinoMuseum.dll and IndieSouls.Runtime.dll as *.dll.orig, replaces them
# with woven builds (its hooks) and asks for one restart. It re-weaves by itself after a game update.
# Uninstall removes our DLLs, puts the *.dll.orig game files back and restores both JSON files.
# Run it again to update the plugin.
set -eu

RELEASE_URL="https://github.com/poststructural/DinoBonesAPWorld/releases/latest/download"
OUR_DLLS="DinoBonesAP.dll Mono.Cecil.dll"
WOVEN_ASMS="DinoMuseum IndieSouls.Runtime"
OLD_LAUNCHER="run_dinobones_ap.sh"

say() { printf '%s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

UNINSTALL=0
APP=""
for arg in "$@"; do
    case "$arg" in
        --uninstall) UNINSTALL=1 ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        -*) die "Unknown option: $arg" ;;
        *) APP="$arg" ;;
    esac
done

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

if [ -z "$APP" ]; then
    APP=$(find_app)
    [ -n "$APP" ] || die "Could not find DinoBones.app in your Steam libraries. Run: sh install_mac.sh /path/to/DinoBones.app"
fi
APP="${APP%/}"
[ -d "$APP/Contents/MacOS" ] || die "$APP is not a macOS app bundle."
APP=$(cd "$APP" && pwd -P)
GAME_DIR=$(dirname "$APP")
DATA="$APP/Contents/Resources/Data"
MANAGED="$DATA/Managed"
SA="$DATA/ScriptingAssemblies.json"
RIO="$DATA/RuntimeInitializeOnLoads.json"
for p in "$MANAGED" "$SA" "$RIO" "$MANAGED/DinoMuseum.dll"; do
    [ -e "$p" ] || die "$p not found. The plugin needs the Mono build of the game."
done
say "Game: $APP"
if pgrep -f "$APP/Contents/MacOS/" >/dev/null 2>&1; then die "DinoBones is running. Quit the game first."; fi

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

# ---- JSON edits (JXA: macOS has no python by default) -------------------------------------------
cat > "$TMP/json.js" <<'EOF'
ObjC.import('Foundation');
function readText(p) {
    var s = $.NSString.stringWithContentsOfFileEncodingError(p, $.NSUTF8StringEncoding, $());
    if (typeof s.js !== 'string') throw new Error('cannot read ' + p);
    return s.js;
}
function readJson(p) { return JSON.parse(readText(p)); }
function writeJson(p, o, like) {
    var s = JSON.stringify(o);
    if (like && /\n$/.test(readText(like))) s += '\n';
    if (!$(s).writeToFileAtomicallyEncodingError(p, true, $.NSUTF8StringEncoding, $())) throw new Error('cannot write ' + p);
}
var ENTRY = {assemblyName: 'DinoBonesAP', nameSpace: 'DinoBonesAP', className: 'Plugin', methodName: 'Boot', loadTypes: 1, isUnityClass: false};
function hasSa(j) { return j.names.indexOf('DinoBonesAP.dll') >= 0; }
function hasRio(j) { return j.root.some(function (e) { return e.assemblyName === 'DinoBonesAP'; }); }
function cleanSa(j) {
    var n = [], t = [];
    for (var i = 0; i < j.names.length; i++) if (j.names[i] !== 'DinoBonesAP.dll') { n.push(j.names[i]); t.push(j.types[i]); }
    j.names = n; j.types = t; return j;
}
function cleanRio(j) { j.root = j.root.filter(function (e) { return e.assemblyName !== 'DinoBonesAP'; }); return j; }
function run(argv) {
    var op = argv[0], f = argv[1], j;
    switch (op) {
        case 'has-sa': return hasSa(readJson(f)) ? 'yes' : 'no';
        case 'has-rio': return hasRio(readJson(f)) ? 'yes' : 'no';
        case 'add-sa':
            j = readJson(f); if (hasSa(j)) return 'present';
            j.names.push('DinoBonesAP.dll'); j.types.push(16); writeJson(f, j, f); return 'added';
        case 'add-rio':
            j = readJson(f); if (hasRio(j)) return 'present';
            j.root.push(ENTRY); writeJson(f, j, f); return 'added';
        case 'clean-sa': writeJson(argv[2], cleanSa(readJson(f)), f); return 'ok';
        case 'clean-rio': writeJson(argv[2], cleanRio(readJson(f)), f); return 'ok';
        case 'same': return JSON.stringify(readJson(f)) === JSON.stringify(readJson(argv[2])) ? 'yes' : 'no';
    }
    throw new Error('unknown op ' + op);
}
EOF
json() { osascript -l JavaScript "$TMP/json.js" "$@"; }

is_woven() { [ -f "$1" ] && LC_ALL=C grep -q 'DinoBonesAP.Woven' "$1"; }
# A real original: a PE file ("MZ") without our marker.
is_original() { [ -f "$1" ] && [ "$(head -c 2 "$1")" = MZ ] && ! is_woven "$1"; }

# macOS 13+ may block Terminal from changing files inside an app ("App Management").
copy_in() {
    cp -f "$1" "$2" 2>/dev/null && return 0
    die "Could not write $2. Allow your terminal app under System Settings > Privacy & Security > App Management, then run this again."
}

remove_old_bepinex_plugin() {
    for p in "$GAME_DIR/BepInEx/plugins/DinoBonesAP.dll" "$GAME_DIR/BepInEx/plugins/DinoBonesAP"; do
        if [ -e "$p" ]; then rm -rf "$p"; say "Removed the old BepInEx plugin: $p"; fi
    done
    # 0.1.1 told players to set a Steam launch option that runs this script under Rosetta. Keep the file so an old
    # launch option still starts the game, but make it start the game directly.
    if [ -f "$GAME_DIR/$OLD_LAUNCHER" ]; then
        cat > "$GAME_DIR/$OLD_LAUNCHER" <<'EOF'
#!/bin/sh
# Dino Bones Archipelago no longer needs a launcher; this only starts the game. You can clear the Steam launch option.
exec "$@"
EOF
        chmod +x "$GAME_DIR/$OLD_LAUNCHER"
        say "Note: if you set the Steam launch option from version 0.1.1, you can clear it now (it is no longer needed)."
    fi
}

# ---- uninstall ---------------------------------------------------------------------------------
if [ "$UNINSTALL" = 1 ]; then
    # Check first, change nothing if a woven game DLL cannot be put back (the game would not start without our plugin).
    for asm in $WOVEN_ASMS; do
        dll="$MANAGED/$asm.dll"
        if is_woven "$dll" && ! is_original "$dll.orig"; then
            die "$asm.dll contains the Archipelago hooks but $asm.dll.orig is missing or not the original. Nothing was changed. Use Steam's 'Verify integrity of game files', then run --uninstall again."
        fi
    done
    for kind in sa rio; do
        if [ "$kind" = sa ]; then f="$SA"; else f="$RIO"; fi
        bak="$f.dinoap.bak"
        json "clean-$kind" "$f" "$TMP/clean.json" >/dev/null
        same=no
        if [ -f "$bak" ]; then same=$(json same "$TMP/clean.json" "$bak") || die "Could not compare $(basename "$f") with its backup."; fi
        if [ "$same" = yes ]; then
            copy_in "$bak" "$f"; say "Restored $(basename "$f") from the backup"
        else
            copy_in "$TMP/clean.json" "$f"
            if [ -f "$bak" ]; then say "Removed our entries from $(basename "$f") (the backup differs, probably a game update; kept the current file)"
            else say "Removed our entries from $(basename "$f")"; fi
        fi
        rm -f "$bak"
    done
    for asm in $WOVEN_ASMS; do
        dll="$MANAGED/$asm.dll"
        if is_woven "$dll"; then
            copy_in "$dll.orig" "$dll"; say "Restored the original $asm.dll"
        fi
        rm -f "$dll.orig" "$dll.orig.tmp" "$dll.tmp"
    done
    for f in $OUR_DLLS; do rm -f "$MANAGED/$f"; done
    remove_old_bepinex_plugin
    say "Uninstalled. The game is vanilla again (AP saves stay in ~/DinoBonesAP)."
    exit 0
fi

# ---- install -----------------------------------------------------------------------------------
HERE=$(cd "$(dirname "$0")" && pwd -P)
SRC=""
for d in "$HERE" "$HERE/../release"; do
    ok=1
    for f in $OUR_DLLS; do [ -f "$d/$f" ] || ok=0; done
    # A DinoBonesAP.dll that references Harmony is the old BepInEx build (0.1.x): ignore it.
    if [ "$ok" = 1 ] && grep -q 0Harmony "$d/DinoBonesAP.dll" 2>/dev/null; then
        say "Ignoring the old BepInEx DinoBonesAP.dll in $d (0.1.x)."
        ok=0
    fi
    if [ "$ok" = 1 ]; then SRC=$(cd "$d" && pwd -P); break; fi
done
if [ -n "$SRC" ]; then
    say "Using the DLLs in $SRC"
else
    say "Downloading the latest DinoBonesAP.dll and Mono.Cecil.dll..."
    for f in $OUR_DLLS; do
        curl -fsSL -o "$TMP/$f" "$RELEASE_URL/$f" || die "Download of $f failed."
    done
    SRC="$TMP"
fi

# Back up the manifests while they are vanilla (refreshed after a game update rewrote them).
has=$(json has-sa "$SA") || die "Could not read ScriptingAssemblies.json."
if [ "$has" = no ]; then cp -f "$SA" "$SA.dinoap.bak"; say "Backed up ScriptingAssemblies.json"; fi
has=$(json has-rio "$RIO") || die "Could not read RuntimeInitializeOnLoads.json."
if [ "$has" = no ]; then cp -f "$RIO" "$RIO.dinoap.bak"; say "Backed up RuntimeInitializeOnLoads.json"; fi

for f in $OUR_DLLS; do
    copy_in "$SRC/$f" "$MANAGED/$f"
    xattr -d com.apple.quarantine "$MANAGED/$f" 2>/dev/null || true
done
say "Copied $OUR_DLLS into $MANAGED"

result=$(json add-sa "$SA") || die "Could not edit ScriptingAssemblies.json."
case "$result" in
    added) say "Added DinoBonesAP.dll to ScriptingAssemblies.json" ;;
    present) say "ScriptingAssemblies.json already lists DinoBonesAP.dll" ;;
    *) die "Unexpected result editing ScriptingAssemblies.json: $result" ;;
esac
result=$(json add-rio "$RIO") || die "Could not edit RuntimeInitializeOnLoads.json."
case "$result" in
    added) say "Added the DinoBonesAP boot entry to RuntimeInitializeOnLoads.json" ;;
    present) say "RuntimeInitializeOnLoads.json already has the DinoBonesAP entry" ;;
    *) die "Unexpected result editing RuntimeInitializeOnLoads.json: $result" ;;
esac

remove_old_bepinex_plugin

say ""
say "Done. Start the Dino Bones Client in the Archipelago Launcher, then start the game from Steam as usual."
say "The first start sets up the hooks and asks you to quit and start the game once more."
say "Log: ~/DinoBonesAP/plugin.log (and ~/Library/Logs/Indie Souls Studio/DinoBones/Player.log)"
