# Dino Bones setup guide

## Requirements

- DinoBones: Tidy Up the Museum on Steam (Windows, or macOS including Apple Silicon).
- From the [latest release](https://github.com/poststructural/DinoBonesAPWorld/releases/latest):
  `DinoBonesAP.dll`, `Mono.Cecil.dll`, and `install_windows.ps1` or `install_mac.sh`.
- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) with `dino_bones.apworld` installed.

BepInEx is not needed (from plugin 0.2.0). If you installed BepInEx for an older version, the installer removes
the old plugin from `BepInEx/plugins`; you can leave BepInEx itself or delete it.

## Install on Windows

1. Put `install_windows.ps1`, `DinoBonesAP.dll` and `Mono.Cecil.dll` in one folder.
2. Right-click `install_windows.ps1` > **Run with PowerShell**, or run
   `powershell -ExecutionPolicy Bypass -File install_windows.ps1`. It finds the game in your Steam libraries;
   add `-GamePath "D:\path\to\DinoBones Tidy Up the Museum"` if it does not.
3. Start the game once. It shows "finished setting up": quit and start it again. From then on it just works.

## Install on macOS

1. Download `install_mac.sh` from the release (put `DinoBonesAP.dll` and `Mono.Cecil.dll` next to it, or
   let it download them) and open Terminal.
2. Run `sh ~/Downloads/install_mac.sh`. It finds the game in your Steam libraries, or pass the app's path:
   `sh install_mac.sh "/path/to/DinoBones.app"`. If macOS says Terminal may not change the app, allow it
   under System Settings > Privacy & Security > App Management and run the installer again.
3. Start the game from Steam as usual (no launch option, no Rosetta). It shows "finished setting up": quit and
   start it again. If you set the launch option from version 0.1.1, you can clear it.

## How it works, updates and uninstalling

The installer copies the two DLLs into the game's `Managed` folder and adds the plugin to
`ScriptingAssemblies.json` and `RuntimeInitializeOnLoads.json` (backups: `*.dinoap.bak`). On the first start the
plugin keeps `DinoMuseum.dll` and `IndieSouls.Runtime.dll` as `*.dll.orig` and replaces them with copies that
contain its hooks, which is why one restart is needed. After a game update or Steam's "Verify integrity of game
files" it does this again by itself (one more restart). If the plugin stops loading at all after a game update,
run the installer again.

To uninstall, run the installer with `-Uninstall` (Windows) or `--uninstall` (macOS). It restores the game files.

## Play

1. Install the `.apworld` by double-clicking it, or put it in the Archipelago `custom_worlds` folder. Then
   generate a seed with `Dino Bones` as the game. The Archipelago Launcher's "Generate Template Options"
   gives a template yaml.
2. Open the **Archipelago Launcher**, start **Dino Bones Client**, and connect with your slot name.
3. **Start the game only after the client has connected.** The plugin then keeps the Archipelago run in its
   own save folder (`~/DinoBonesAP/<seed>_<slot>/save/`), and your normal save is untouched. If the game was
   already running, the plugin stays off and asks you to restart the game.
4. You start with one Exhibit Permit. A skeleton accepts bones only once its permit has arrived. The
   Polaroid Camera, Glider, Watering Can and Crate Hammer are items too.

## Bridge folder

The client and plugin exchange files in `~/DinoBonesAP/<seed>_<slot>/`. `current.txt` in `~/DinoBonesAP/`
names the active folder. The files are `slot.json`, `items.jsonl`, `checks.jsonl`, `checked.json` and
`status.json`. The plugin also reads the `DINOBONES_AP_ROOT` environment variable to use another root,
matching the client's `--bridge-root`.
