# Dino Bones setup guide

## Requirements

- DinoBones: Tidy Up the Museum on Steam (Windows or macOS).
- [BepInEx 5.4.23.3](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.3): `BepInEx_win_x64_5.4.23.3.zip`
  on Windows, `BepInEx_macos_x64_5.4.23.3.zip` on macOS.
- `DinoBonesAP.dll` (the Dino Bones Archipelago plugin).
- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) with `dino_bones.apworld` installed.

## Install on Windows

1. Extract the BepInEx zip into the game folder (next to `DinoBones.exe`).
2. **Rename `winhttp.dll` to `version.dll`.** This game does not load BepInEx through `winhttp.dll`.
3. Start the game once and close it, so BepInEx creates its folders. `BepInEx\LogOutput.log` should now exist.
4. Put `DinoBonesAP.dll` in `BepInEx\plugins\`.

## Install on macOS

1. Download `install_mac.sh` from the release and open Terminal.
2. Run `sh ~/Downloads/install_mac.sh`. It finds the game in your Steam libraries (or pass the app's path:
   `sh install_mac.sh "/path/to/DinoBones.app"`), installs BepInEx and the plugin, and puts the Steam launch
   option on your clipboard.
3. In Steam, open the game's Properties > General > Launch Options and paste it. It looks like
   `"/path/to/run_dinobones_ap.sh" %command%`.

BepInEx's macOS injector is Intel-only, so on Apple Silicon the launcher starts the game under Rosetta
(install Rosetta if macOS asks). If the game is signed with the hardened runtime, the installer offers to
re-sign it locally so BepInEx can load; Steam's "Verify integrity of game files" undoes that, so run the
installer again afterwards. Running it again also updates the plugin.

To install by hand instead: extract `BepInEx_macos_x64_5.4.23.3.zip` next to `DinoBones.app`, put
`DinoBonesAP.dll` in `BepInEx/plugins/`, and launch through `run_bepinex.sh` as an Intel process
(`arch -x86_64 /bin/sh run_bepinex.sh %command%`).

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
