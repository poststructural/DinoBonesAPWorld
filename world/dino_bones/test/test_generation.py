import json

from BaseClasses import ItemClassification

from .. import data, items, locations
from ..options import Goal
from . import DinoBonesTestBase

OFF = {"bonesanity": "off", "collectionsanity": False, "photosanity": False, "gardensanity": False,
       "kitsanity": False, "achievementsanity": False, "supportsanity": False}
MAX = {"bonesanity": "every_bone", "collectionsanity": True, "photosanity": True, "gardensanity": True,
       "kitsanity": True, "achievementsanity": True, "supportsanity": True}


def real_locations(world):
    return [loc for loc in world.multiworld.get_locations(world.player) if loc.address is not None]


def pool_of(test):
    return [i for i in test.multiworld.itempool if i.player == test.player]


class TestDefaults(DinoBonesTestBase):
    def test_slot_data_and_counts(self):
        slot = self.world.fill_slot_data()
        self.assertEqual(slot["goal"], "endgame")
        self.assertEqual(slot["skeletons_needed"], 9)
        self.assertEqual(slot["bonesanity"], "milestones")
        self.assertFalse(slot["supportsanity"])
        self.assertIn(slot["starting_permit"], data.SPECIES)
        self.assertFalse(slot["death_link"])
        json.dumps(slot)
        self.assertEqual(len(pool_of(self)), len(real_locations(self.world)))
        self.assertEqual(len(real_locations(self.world)), 18 + 90 + 27 + 11 + 7 + 6 + 29)

    def test_permits(self):
        start = items.permit_name(self.world.starting_permit)
        pre = [i.name for i in self.multiworld.precollected_items[self.player]]
        self.assertEqual(pre, [start])
        names = [i.name for i in pool_of(self)]
        self.assertNotIn(start, names)
        self.assertEqual(sum(1 for n in names if n in items.PERMIT_ITEMS), 8)

    def test_classification(self):
        # achievementsanity (default on) makes supports_all need the hammer, so it is progression here
        self.assertFalse(self.world.create_item("Crate Hammer").advancement)
        self.assertEqual(self.world.create_item("Glider").classification, ItemClassification.useful)
        self.assertTrue(self.world.create_item("Polaroid Camera").advancement)
        self.assertTrue(self.world.create_item("Watering Can").advancement)


class TestEverythingOff(DinoBonesTestBase):
    options = OFF

    def test_hammer_useful_when_unneeded(self):
        self.assertFalse(self.world.create_item("Crate Hammer").advancement)
        self.assertTrue(self.world.create_item("Crate Hammer").classification & ItemClassification.useful)

    def test_still_fits(self):
        self.assertEqual(len(real_locations(self.world)), 18)
        self.assertEqual(len(pool_of(self)), 18)
        self.assertEqual(len(self.multiworld.precollected_items[self.player]), 1)


class TestEverythingOn(DinoBonesTestBase):
    options = MAX

    def test_counts(self):
        self.assertEqual(len(real_locations(self.world)), 18 + 1940 + 27 + 11 + 7 + 6 + 17 + 29)
        self.assertEqual(len(pool_of(self)), len(real_locations(self.world)))
        self.assertFalse(self.world.create_item("Crate Hammer").advancement)


class TestSkeletonsGoal(DinoBonesTestBase):
    options = {"goal": Goal.option_skeletons, "skeletons_needed": 3, "bonesanity": "milestones"}

    def test_goal_rule(self):
        self.assertEqual(self.world.fill_slot_data()["goal"], "skeletons")
        state = self.multiworld.get_all_state(False)
        state.prog_items[self.player].clear()
        goal = self.multiworld.get_location(locations.GOAL_NAME, self.player)
        permits = list(items.PERMIT_ITEMS)
        for p in permits[:2]:
            state.collect(self.world.create_item(p))
        self.assertFalse(goal.can_reach(state))
        state.collect(self.world.create_item(permits[2]))
        self.assertTrue(goal.can_reach(state))


class TestSkeletonsGoalOff(DinoBonesTestBase):
    options = {**OFF, "goal": Goal.option_skeletons, "skeletons_needed": 1}


class TestNames(DinoBonesTestBase):
    options = MAX

    def test_every_spec_name_exists(self):
        table = locations.LOCATION_NAME_TO_ID
        self.assertEqual(len(set(table.values())), len(table))
        self.assertEqual(len(table), 2145)  # 2055 with every_bone, plus the 90 milestone names
        want = []
        for s in data.SPECIES:
            want += [f"{s} Skeleton Complete", f"{s} Secret", f"{s} Coin", f"{s} Cassette", f"{s} Book",
                     f"Photo: {s} Exhibit"]
            want += [f"{s} Bones {p}%" for p in range(10, 101, 10)]
            want += [f"{s} Bone {n}" for n in range(1, data.BONES[s] + 1)]
            want += [f"{s} Support {n}" for n in range(1, data.SUPPORT_PARTS[s] + 1)]
        want += ["Photo: Cat", "Photo: Model and Snow Globe"]
        want += [f"Garden Bed {n}" for n in range(1, 8)]
        want += [f"Model Kit {p}" for p in ("Body", "Head", "Arms", "Leg1", "Leg2", "Tail")]
        want += [f"Achievement: {a}" for a in data.ACHIEVEMENTS]
        self.assertEqual(len(data.ACHIEVEMENTS), 29)
        for name in want:
            self.assertIn(name, table)
        self.assertEqual(len(want), len(table))

    def test_achievement_rules(self):
        state = self.multiworld.get_all_state(False)
        state.prog_items[self.player].clear()
        loc = lambda a: self.multiworld.get_location(locations.achievement_name(a), self.player)
        self.assertTrue(loc("bone_first").can_reach(state))
        self.assertFalse(loc("bones_100").can_reach(state))
        state.collect(self.world.create_item("Pteranodon Exhibit Permit"))  # 82
        self.assertFalse(loc("bones_100").can_reach(state))
        state.collect(self.world.create_item("Brachiosaurus Exhibit Permit"))  # 218
        self.assertTrue(loc("bones_100").can_reach(state))
        self.assertFalse(loc("bones_500").can_reach(state))
        self.assertTrue(loc("secret_glider").can_reach(state))
        self.assertFalse(loc("photo_cat").can_reach(state))
        self.assertFalse(loc("skeletons_3").can_reach(state))
