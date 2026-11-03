from __future__ import annotations

from collections.abc import Mapping
from typing import Any, ClassVar

from BaseClasses import ItemClassification, Region
from worlds.AutoWorld import World

from . import data, items, locations, rules, web_world
from . import options as dino_options


class DinoBonesWorld(World):
    """Dino Bones: Tidy Up the Museum is a cozy museum game: sort crates of fossil bones, rebuild nine
    skeletons, photograph exhibits, tend the garden and collect coins, cassettes and books.

    Exhibit permits, the camera, the watering can and the crate hammer are shuffled across the multiworld."""

    game = data.GAME_NAME
    web = web_world.DinoBonesWebWorld()
    options_dataclass = dino_options.DinoBonesOptions
    options: dino_options.DinoBonesOptions

    item_name_to_id: ClassVar[dict[str, int]] = items.ITEM_NAME_TO_ID
    location_name_to_id: ClassVar[dict[str, int]] = locations.LOCATION_NAME_TO_ID
    item_name_groups = {
        "Permits": set(items.PERMIT_ITEMS),
        "Equipment": set(items.EQUIPMENT_ITEMS),
    }

    def generate_early(self) -> None:
        self.starting_permit: str = self.random.choice(data.SPECIES)
        self.active: list[str] = self._active_location_names()
        self.active_set: set[str] = set(self.active)

    # ---- planning -----------------------------------------------------
    def _active_location_names(self) -> list[str]:
        opt = self.options
        names: list[str] = []
        for s in data.SPECIES:
            names += [locations.skeleton_name(s), locations.secret_name(s)]
        if opt.bonesanity.current_key == "milestones":
            names += [locations.milestone_name(s, p) for s in data.SPECIES for p in data.MILESTONE_PERCENTS]
        elif opt.bonesanity.current_key == "every_bone":
            names += [locations.bone_name(s, n) for s in data.SPECIES for n in range(1, data.BONES[s] + 1)]
        if opt.collectionsanity:
            names += [n for s in data.SPECIES for n in locations.collection_names(s)]
        if opt.photosanity:
            names += [locations.photo_name(s) for s in data.SPECIES] + [locations.PHOTO_CAT, locations.PHOTO_MODEL]
        if opt.gardensanity:
            names += [locations.garden_name(n) for n in range(1, data.GARDEN_BEDS + 1)]
        if opt.kitsanity:
            names += [locations.kit_name(p) for p in data.KIT_PIECES]
        if opt.supportsanity:
            names += [locations.support_name(s, n) for s in data.SPECIES
                      for n in range(1, data.SUPPORT_PARTS[s] + 1)]
        if opt.achievementsanity:
            names += [locations.achievement_name(a) for a in data.ACHIEVEMENTS]
        return names

    # ---- generation ---------------------------------------------------
    def create_regions(self) -> None:
        player = self.player
        menu = Region("Menu", player, self.multiworld)
        self.multiworld.regions.append(menu)
        P, ids = items.permit_name, self.location_name_to_id

        def place(name: str, *needs: str, rule=None) -> None:
            if name not in self.active_set:
                return
            loc = locations.DinoBonesLocation(player, name, ids[name], menu)
            if rule is not None:
                loc.access_rule = rule
            elif needs:
                loc.access_rule = rules.has_all(player, needs)
            menu.locations.append(loc)

        for s in data.SPECIES:
            permit = P(s)
            place(locations.skeleton_name(s), permit)
            place(locations.secret_name(s), permit)
            for pct in data.MILESTONE_PERCENTS:
                place(locations.milestone_name(s, pct), permit)
            for n in range(1, data.BONES[s] + 1):
                place(locations.bone_name(s, n), permit)
            for name in locations.collection_names(s):
                place(name, permit)
            place(locations.photo_name(s), items.CAMERA, permit)
            for n in range(1, data.SUPPORT_PARTS[s] + 1):
                place(locations.support_name(s, n), permit)
        place(locations.PHOTO_CAT, items.CAMERA, P("Spinosaurus"))
        place(locations.PHOTO_MODEL, items.CAMERA, P("Trex"), P("Mammoth"))
        for n in range(1, data.GARDEN_BEDS + 1):
            place(locations.garden_name(n), items.CAN)
        for piece in data.KIT_PIECES:
            place(locations.kit_name(piece), P("Trex"))
        for ach in data.ACHIEVEMENTS:
            place(locations.achievement_name(ach), rule=rules.achievement_rule(player, ach))

        goal_loc = locations.DinoBonesLocation(player, locations.GOAL_NAME, None, menu)
        if self.options.goal == dino_options.Goal.option_endgame:
            goal_loc.access_rule = rules.has_all(player, rules.ALL_PERMITS)
        else:
            goal_loc.access_rule = rules.permit_count_rule(player, self.options.skeletons_needed.value)
        goal_loc.place_locked_item(items.DinoBonesItem("Victory", ItemClassification.progression, None, player))
        menu.locations.append(goal_loc)

    def create_item(self, name: str) -> items.DinoBonesItem:
        classification = items.CLASSIFICATIONS[name]
        return items.DinoBonesItem(name, classification, self.item_name_to_id[name], self.player)

    def create_items(self) -> None:
        self.multiworld.push_precollected(self.create_item(items.permit_name(self.starting_permit)))
        pool_names = [items.permit_name(s) for s in data.SPECIES if s != self.starting_permit]
        pool_names += items.EQUIPMENT_ITEMS
        pool = [self.create_item(n) for n in pool_names]
        filler = len(self.active) - len(pool)
        pool += [self.create_item(items.FILLER) for _ in range(max(0, filler))]
        self.multiworld.itempool += pool

    def get_filler_item_name(self) -> str:
        return items.FILLER

    def set_rules(self) -> None:
        self.multiworld.completion_condition[self.player] = lambda state: state.has("Victory", self.player)

    def fill_slot_data(self) -> Mapping[str, Any]:
        opt = self.options
        return {
            "goal": opt.goal.current_key,
            "skeletons_needed": opt.skeletons_needed.value,
            "bonesanity": opt.bonesanity.current_key,
            "collectionsanity": bool(opt.collectionsanity),
            "photosanity": bool(opt.photosanity),
            "gardensanity": bool(opt.gardensanity),
            "kitsanity": bool(opt.kitsanity),
            "supportsanity": bool(opt.supportsanity),
            "achievementsanity": bool(opt.achievementsanity),
            "starting_permit": self.starting_permit,
            "death_link": False,
        }
