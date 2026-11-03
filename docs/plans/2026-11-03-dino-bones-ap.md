# Dino Bones: Tidy Up the Museum Archipelago: plan (2026-11-03)

Goal: an APWorld plus game mod/client for DinoBones: Tidy Up the Museum (Indie Souls Studio) with as many
checks as the game allows ("check slop"), exposed through `-sanity` options, running on Windows and Mac.

## What the game is

- Unity **6000.6.4f1, Mono** build (`DinoBones_Data/Managed/DinoMuseum.dll` holds the game code).
  BepInEx 5.4.23.x + Harmony runs on Unity 6 Mono, so one plugin DLL can serve Windows and Mac.
  **Risk to verify:** the Mac build must also be Mono (look for `Contents/Resources/Data/Managed/DinoMuseum.dll`
  in the Mac app). If the Mac build is IL2CPP, Mac needs a BepInEx 6 IL2CPP build of the plugin instead.
- One open museum, no levels, no shop or currency. Progress is reassembling 9 skeletons
  (T-rex, Triceratops, Mammoth, Spinosaurus, Kentrosaurus, Pachycephalosaurus, Parasaurolophus,
  Pteranodon, Brachiosaurus) from bones that come out of crates.
- **1,940 bones**, each with a unique mesh name per species (read from the `D_Atlas_*` assets by
  `research/bones.json`): Brachio 136, Kentro 231, Mammoth 191, Pachy 165, Paras 180, Ptera 82,
  Spino 387, T-rex 305, Trike 263. Supports: 17 parts (2 per species, Pteranodon 1).
- Finishing a species' skeleton reveals that species' **secret** (9): camera (Pachy), glider (Ptera),
  watering can (Trike), model kit (T-rex), support hammer (Brachio), snow globe (Mammoth), cat flap (Spino),
  cassette cabinet (Paras), coin press (Kentro).
- Per-species collectibles: 9 coins (album), 9 cassettes (kiosk), 9 books (stand), gated by
  bone-count "expertise". Plus 6 model-kit pieces, garden beds (at least 7), exhibit photos.
- 29 achievements, all through `AchievementLog.Report(string id)`.
- Win: `EndgameSequence` plays after all 9 skeletons are complete (meteor cutscene), then a post-game tour.
- Save: `ProgressSnapshot` + `WorldSnapshot` (IndieSouls.Save).

## Checks (locations)

| Option | Checks | Count |
|---|---|---|
| always | Complete each skeleton | 9 |
| always | Reveal each species' secret | 9 |
| `bonesanity` | `off` / `milestones` (every 10% of each skeleton) / `every_bone` ("T-rex Bone 17", counted in placement order per species) | 0 / 90 / 1,940 |
| `collectionsanity` | each coin placed in the album, cassette loaded, book placed | 27 |
| `photosanity` | each exhibit photographed, plus the cat and the model+snow globe shots | 11 |
| `gardensanity` | each garden bed maxed | ~7 (exact count from the plugin's first-run dump) |
| `kitsanity` | each model-kit piece found | 6 |
| `supportsanity` | each skeleton support straightened | 17 (verify in game) |
| `achievementsanity` | each achievement (tracked by the mod; Steam not needed) | 29 |

Maximal seed: about **2,050 locations**. Bone checks count "the Nth bone placed on species X", not
specific mesh names, so the order the player places bones in does not matter.

## Items

- **Exhibit Permits (9, progression):** a bone can only be placed on a skeleton once its species permit
  arrives (patch `BonePlacer.TryPlace`/`TryPlaceInto` to refuse, with a HUD note). Start with 1 random
  permit (option `starting_permits`, default 1). Optional `progressive_permits`.
- **Equipment (3, progression/useful):** Polaroid Camera, Glider, Watering Can, granted through
  `EquipmentInventory.Unlock`. The secret pickup becomes a check instead of granting the tool.
  Photo checks need the camera, garden checks need the watering can.
- **Support Hammer** (needed for `supportsanity` and skeleton completion if supports must be straight).
- **Monocle** (the anatomy helper) as useful.
- Filler: "Museum Donation" (cosmetic/no-op toast) and optional traps later (e.g. "Bone Rain" spills a
  crate, "Cat Mischief").
- Goal options: `endgame` (all 9 skeletons, cutscene plays) or `skeletons_completed` N.

## Mod and client architecture (same as Cozy Cleaner / Moon Sweepers)

- **Plugin** `DinoBones.AP` (BepInEx 5, netstandard2.0, Harmony):
  - `BonePlacer.TryPlace`/`TryPlaceInto`: permit gate + bone checks (postfix on success).
  - `BonePlacer.SkeletonCompleted` event: skeleton checks. `SecretFound.Announced`: secret checks.
  - `CoinAlbum.Placed`, `CassetteKiosk.Loaded`, `BookStand.Placed`, `ModelKitHole.Collect`,
    `PhotoSubjects`, `AchievementLog.MarkGardenMaxed`, `AchievementLog.Report`: the other sanities.
  - `EquipmentInventory.Unlock` prefix: block vanilla pickups, grant on AP item.
  - `EndgameSequence.End(true)`: goal.
  - Overlay: connection status and "received X from Y" toasts. First run dumps real counts (gardens,
    supports) to the log so the static numbers get verified.
- **Bridge:** JSON-lines files (`checks.jsonl`, `items.jsonl`, `slot.json`) in a per-seed folder.
- **Client:** Python `CommonContext` client inside the APWorld ("Dino Bones Client"). Windows install is
  the BepInEx winhttp proxy; Mac uses `run_bepinex.sh` as the Steam launch option.
- AP runs use a per-seed save slot so the normal save is untouched.

## Layout

```
DinoBonesAPWorld/
  DinoBones Tidy Up the Museum/   game copy (git-ignored)
  research/                       decomp (git-ignored), bones.json
  tools/                          scan_bones.py, gen_world_data.py
  plugin/DinoBones.AP/            BepInEx 5 plugin (C#)
  world/dino_bones/               the .apworld (options, items, locations, rules, client.py, docs)
  docs/                           plans, HANDOFF.md, INSTALL.md (Windows + Mac)
```

## Work split (after approval)

- Me: plugin hooks and gating, world logic, verification in game.
- agy (Gemini): sweep the decomp for exact garden-bed, support and photo-subject IDs; draft setup docs.
- deepcode / Codex: generate the data tables (`gen_world_data.py`), review the plugin and world diffs.
- Nothing is pushed to GitHub without Austin's OK.

## Open questions for Austin

1. Is the Mac build Mono? (Check for the `Managed` folder inside the app bundle.)
2. Is gating bone placement by species permits the right feel, or keep the museum fully open and make
   only equipment/secrets the items?
