from __future__ import annotations

from BaseClasses import Location

from . import data


class DinoBonesLocation(Location):
    game = data.GAME_NAME


GOAL_NAME = "Goal"
PHOTO_CAT = "Photo: Cat"
PHOTO_MODEL = "Photo: Model and Snow Globe"


def skeleton_name(s: str) -> str:
    return f"{s} Skeleton Complete"


def secret_name(s: str) -> str:
    return f"{s} Secret"


def milestone_name(s: str, pct: int) -> str:
    return f"{s} Bones {pct}%"


def bone_name(s: str, n: int) -> str:
    return f"{s} Bone {n}"


def collection_names(s: str) -> list[str]:
    return [f"{s} Coin", f"{s} Cassette", f"{s} Book"]


def photo_name(s: str) -> str:
    return f"Photo: {s} Exhibit"


def garden_name(n: int) -> str:
    return f"Garden Bed {n}"


def kit_name(piece: str) -> str:
    return f"Model Kit {piece}"


def support_name(s: str, n: int) -> str:
    return f"{s} Support {n}"


def achievement_name(ach_id: str) -> str:
    return f"Achievement: {ach_id}"


def _build() -> dict[str, int]:
    """Every location any option combination can create, in a fixed order (never reorder)."""
    table: dict[str, int] = {}

    def add(name: str) -> None:
        table[name] = data.BASE_ID + len(table)

    for s in data.SPECIES:
        add(skeleton_name(s))
        add(secret_name(s))
    for s in data.SPECIES:
        for pct in data.MILESTONE_PERCENTS:
            add(milestone_name(s, pct))
    for s in data.SPECIES:
        for n in range(1, data.BONES[s] + 1):
            add(bone_name(s, n))
    for s in data.SPECIES:
        for name in collection_names(s):
            add(name)
    for s in data.SPECIES:
        add(photo_name(s))
    add(PHOTO_CAT)
    add(PHOTO_MODEL)
    for n in range(1, data.GARDEN_BEDS + 1):
        add(garden_name(n))
    for piece in data.KIT_PIECES:
        add(kit_name(piece))
    for s in data.SPECIES:
        for n in range(1, data.SUPPORT_PARTS[s] + 1):
            add(support_name(s, n))
    for ach in data.ACHIEVEMENTS:
        add(achievement_name(ach))
    return table


LOCATION_NAME_TO_ID: dict[str, int] = _build()
