"""Static data shared by the world."""
from __future__ import annotations

GAME_NAME = "Dino Bones"
BASE_ID = 0xD1B00000

# Fixed order: location and item ids derive from it, so never reorder.
SPECIES = ["Trex", "Triceratops", "Mammoth", "Spinosaurus", "Kentrosaurus", "Pachycephalosaurus",
           "Parasaurolophus", "Pteranodon", "Brachiosaurus"]
BONES = {"Trex": 305, "Triceratops": 263, "Mammoth": 191, "Spinosaurus": 387, "Kentrosaurus": 231,
         "Pachycephalosaurus": 165, "Parasaurolophus": 180, "Pteranodon": 82, "Brachiosaurus": 136}
SUPPORT_PARTS = {"Trex": 2, "Triceratops": 2, "Mammoth": 2, "Spinosaurus": 2, "Kentrosaurus": 2,
                 "Pachycephalosaurus": 2, "Parasaurolophus": 2, "Pteranodon": 1, "Brachiosaurus": 2}

MILESTONE_PERCENTS = tuple(range(10, 101, 10))
GARDEN_BEDS = 7
KIT_PIECES = ("Body", "Head", "Arms", "Leg1", "Leg2", "Tail")

# AchievementId.All, in game order.
ACHIEVEMENTS = [
    "supports_all", "bone_first", "bones_100", "bones_500", "bones_1000", "exhibits_started_all", "skeletons_3",
    "skeletons_6", "skeletons_all", "brachiosaurus_complete", "secret_camera", "secret_coin_press",
    "secret_cassettes", "secret_cat", "secret_glider", "secret_watering_can", "secret_snowglobe",
    "secret_model_first_piece", "secret_hammer", "photos_exhibits_all", "photo_cat", "coin_album_complete",
    "cassettes_all", "photo_model_snowglobe", "gardens_all_complete", "snowglobe_mammoth", "model_assembled",
    "species_collection_complete", "curator_collection_complete",
]
