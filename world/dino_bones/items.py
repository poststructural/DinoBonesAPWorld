from __future__ import annotations

from BaseClasses import Item, ItemClassification

from . import data


class DinoBonesItem(Item):
    game = data.GAME_NAME


CAMERA = "Polaroid Camera"
GLIDER = "Glider"
CAN = "Watering Can"
HAMMER = "Crate Hammer"
FILLER = "Museum Donation"


def permit_name(species: str) -> str:
    return f"{species} Exhibit Permit"


PERMIT_ITEMS = [permit_name(s) for s in data.SPECIES]
EQUIPMENT_ITEMS = [CAMERA, GLIDER, CAN, HAMMER]

_ORDER = PERMIT_ITEMS + EQUIPMENT_ITEMS + [FILLER]
ITEM_NAME_TO_ID: dict[str, int] = {name: data.BASE_ID + index for index, name in enumerate(_ORDER)}

CLASSIFICATIONS: dict[str, ItemClassification] = {name: ItemClassification.progression for name in PERMIT_ITEMS}
CLASSIFICATIONS.update({CAMERA: ItemClassification.progression, CAN: ItemClassification.progression,
                        GLIDER: ItemClassification.useful, HAMMER: ItemClassification.useful,
                        FILLER: ItemClassification.filler})
