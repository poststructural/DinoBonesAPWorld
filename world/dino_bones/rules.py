"""Logic helpers. Every rule is a closure over item names so state.has stays cheap."""
from __future__ import annotations

from typing import Callable

from BaseClasses import CollectionState

from . import data, items

Rule = Callable[[CollectionState], bool]

ALL_PERMITS = tuple(items.permit_name(s) for s in data.SPECIES)


def has_all(player: int, names) -> Rule:
    names = tuple(names)

    def rule(state: CollectionState) -> bool:
        return state.has_all(names, player)
    return rule


def permit_count_rule(player: int, needed: int) -> Rule:
    def rule(state: CollectionState) -> bool:
        return sum(1 for p in ALL_PERMITS if state.has(p, player)) >= needed
    return rule


def bone_count_rule(player: int, threshold: int) -> Rule:
    """Held permits' species bone counts sum to at least `threshold`."""
    def rule(state: CollectionState) -> bool:
        total = 0
        for s in data.SPECIES:
            if state.has(items.permit_name(s), player):
                total += data.BONES[s]
                if total >= threshold:
                    return True
        return False
    return rule


def achievement_rule(player: int, ach: str) -> Rule | None:
    """Rule for one achievement id, or None when it needs nothing."""
    P = items.permit_name
    simple = {
        "brachiosaurus_complete": [P("Brachiosaurus")],
        "secret_camera": [P("Pachycephalosaurus")],
        "secret_glider": [P("Pteranodon")],
        "secret_watering_can": [P("Triceratops")],
        "secret_model_first_piece": [P("Trex")],
        "secret_hammer": [P("Brachiosaurus")],
        "secret_snowglobe": [P("Mammoth")],
        "secret_cat": [P("Spinosaurus")],
        "secret_cassettes": [P("Parasaurolophus")],
        "secret_coin_press": [P("Kentrosaurus")],
        "snowglobe_mammoth": [P("Mammoth")],
        "model_assembled": [P("Trex")],
        "photo_cat": [items.CAMERA, P("Spinosaurus")],
        "photo_model_snowglobe": [items.CAMERA, P("Trex"), P("Mammoth")],
        "photos_exhibits_all": [items.CAMERA, *ALL_PERMITS],
        "gardens_all_complete": [items.CAN],
        "supports_all": list(ALL_PERMITS),
        "exhibits_started_all": list(ALL_PERMITS),
        "coin_album_complete": list(ALL_PERMITS),
        "cassettes_all": list(ALL_PERMITS),
        "curator_collection_complete": list(ALL_PERMITS),
        "skeletons_all": list(ALL_PERMITS),
    }
    if ach in simple:
        return has_all(player, simple[ach])
    if ach in ("bone_first", "species_collection_complete"):
        return None
    if ach.startswith("bones_"):
        return bone_count_rule(player, int(ach.split("_")[1]))
    if ach in ("skeletons_3", "skeletons_6"):
        return permit_count_rule(player, int(ach.split("_")[1]))
    raise KeyError(ach)
