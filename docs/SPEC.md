# Dino Bones AP: shared contract (world <-> plugin)

Plan: `docs/plans/2026-11-03-dino-bones-ap.md` (approved by Austin 2026-11-03, species permits confirmed,
Mac build confirmed Mono). Game: `game = "Dino Bones"`. Bone data: `research/bones.json`
(species -> {support_parts, hammer_points, bones[]}).

## Species (exact strings, used everywhere)
Trex, Triceratops, Mammoth, Spinosaurus, Kentrosaurus, Pachycephalosaurus, Parasaurolophus, Pteranodon, Brachiosaurus

Bone counts: Brachiosaurus 136, Kentrosaurus 231, Mammoth 191, Pachycephalosaurus 165,
Parasaurolophus 180, Pteranodon 82, Spinosaurus 387, Trex 305, Triceratops 263 (total 1940).
Support parts: 2 each, Pteranodon 1 (total 17).

## Location names (the plugin sends exactly these strings)
| Group | Name pattern | Option |
|---|---|---|
| skeleton | `{S} Skeleton Complete` | always |
| secret | `{S} Secret` | always |
| bone milestone | `{S} Bones {P}%`, P = 10,20,...,100 | bonesanity = milestones |
| every bone | `{S} Bone {N}`, N = 1..count(S), Nth bone placed on that skeleton | bonesanity = every_bone |
| collection | `{S} Coin`, `{S} Cassette`, `{S} Book` | collectionsanity |
| photo | `Photo: {S} Exhibit`, `Photo: Cat`, `Photo: Model and Snow Globe` | photosanity |
| garden | `Garden Bed {N}`, N = 1..7, Nth bed maxed | gardensanity |
| kit | `Model Kit {P}`, P in Body, Head, Arms, Leg1, Leg2, Tail | kitsanity |
| support | `{S} Support {N}`, N = 1..support_parts(S), Nth support fixed on that skeleton | supportsanity (default off until verified) |
| achievement | `Achievement: {id}` for every id in AchievementId.All (29) | achievementsanity |

Plugin rule: when a skeleton completes, also send every bone and milestone check of that species that
was not yet sent (covers pre-placed bones and counting drift). The world never requires a check the
plugin can only reach by luck.

## Item names
- `{S} Exhibit Permit` x9 (progression). The plugin refuses bone placement on a skeleton without its permit.
- `Polaroid Camera`, `Glider`, `Watering Can` (EquipmentKind Polaroid/Glider/WateringCan). Camera and
  Watering Can progression, Glider useful.
- `Crate Hammer` (useful): the Brachiosaurus secret hammer, which unlocks crate breaking (`BreakAbility`).
  Crates open without it, so it gates nothing in logic. Support straightening is never gated.
- `Museum Donation` (filler, toast only).

## Slot data
`{"goal": "endgame"|"skeletons", "skeletons_needed": int, "bonesanity": "off"|"milestones"|"every_bone",
"collectionsanity", "photosanity", "gardensanity", "kitsanity", "supportsanity", "achievementsanity": bool,
"starting_permit": species, "death_link": false}`

## Goal
`endgame`: plugin sends `{"type":"goal"}` when EndgameSequence ends completed.
`skeletons`: plugin sends goal once `skeletons_needed` skeletons are complete.

## Bridge (same protocol as Cozy Cleaner, root `~/DinoBonesAP/`)
current.txt, slot.json, items.jsonl (`{"index","item","player"}`), checks.jsonl (`{"location": name}` or
`{"type":"goal"}`), checked.json, status.json. See `CozyCleanerAPWorld/world/cozy_cleaner/client.py`.
