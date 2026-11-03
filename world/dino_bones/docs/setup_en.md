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

1. Extract the BepInEx macOS x64 zip into the folder that contains `DinoBones.app`.
2. In a terminal, run `chmod +x run_bepinex.sh` in that folder.
3. In Steam, open the game's Properties and set the launch option to
   `"/path/to/run_bepinex.sh" %command%`, using the real path to the script.
4. Start the game once and close it, so BepInEx creates its folders.
5. Put `DinoBonesAP.dll` in `BepInEx/plugins/`.

The macOS BepInEx build is x64. On Apple Silicon Macs the game must run under Rosetta for BepInEx to load.

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
