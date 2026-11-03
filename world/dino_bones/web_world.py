from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld

from . import options as dino_options


class DinoBonesWebWorld(WebWorld):
    theme = "stone"
    option_groups = dino_options.OPTION_GROUPS
    tutorials = [
        Tutorial(
            "Multiworld Setup Guide",
            "A guide to setting up Dino Bones for Archipelago.",
            "English",
            "setup_en.md",
            "setup/en",
            ["Jerrod"],
        )
    ]
