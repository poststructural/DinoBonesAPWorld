# Native loader (no BepInEx) for Windows and Apple Silicon Macs (2026-11-04)

Approved by Austin 2026-11-04 ("Okay").

## Why
Austin's Mac copy of the game is arm64-only. BepInEx 5.4.23.3's Doorstop dylib is x86_64-only, and its
MonoMod.RuntimeDetour 22.1 / HarmonyX 2.9 cannot detour on arm64 macOS. So the BepInEx path cannot work there.

## Proven (2026-11-04, Windows)
A DLL in `DinoBones_Data/Managed/`, listed in `ScriptingAssemblies.json` (names + types 16) and with a
`RuntimeInitializeOnLoads.json` entry (loadTypes 1 = BeforeSceneLoad), is loaded and run by Unity with
BepInEx disabled.

## Design
- `DinoBonesAP.dll` keeps all its logic, but drops the BepInEx and Harmony dependencies. A
  `[RuntimeInitializeOnLoadMethod]` boot creates the overlay GameObject and the poll loop. Logging goes to
  `Debug.Log` (Player.log) plus `~/DinoBonesAP/plugin.log`.
- Hooks are woven into the game's `DinoMuseum` assembly with Mono.Cecil (shipped next to the plugin). The
  plugin's own Mono runtime runs the weaver on first launch, and the plugin asks for one restart. The woven
  copy is re-made when the game's `DinoMuseum.dll` hash or the plugin's hook-set version changes.
  - Preferred: write the woven assembly as `DinoMuseum.AP.dll` and point `ScriptingAssemblies.json` at it,
    so the original file is never modified. Fallback: overwrite `DinoMuseum.dll` in place and keep
    `DinoMuseum.dll.orig`.
- Each existing Harmony patch becomes a hook declared by our own attribute, with the same
  prefix/postfix semantics (skip original, `__instance`, `__result`, by-name arguments).
- Installers:
  - `install_mac.sh` copies the DLLs into `DinoBones.app/Contents/Resources/Data/Managed` and edits the two
    JSON files with JXA (`osascript -l JavaScript`). Its `--uninstall` option restores them.
  - `install_windows.ps1` does the same on Windows.
- BepInEx is no longer needed. The 0.1.2 release stays up as a Windows fallback.

## Unchanged
The APWorld, the location and item names, the client, and the bridge protocol.

## Verify
On Windows with BepInEx disabled:
1. The first launch weaves and asks for a restart.
2. On the second launch the hooks are active, the bridge connects through `DINOBONES_AP_ROOT`, and the save
   is redirected.
3. Uninstall restores the vanilla game, and it launches.
