from dataclasses import dataclass

from Options import Choice, DefaultOnToggle, OptionGroup, PerGameCommonOptions, Range, Toggle


class Goal(Choice):
    """What finishes the seed.

    endgame: complete all nine skeletons (the meteor ending plays).
    skeletons: complete `skeletons_needed` skeletons."""

    display_name = "Goal"
    option_endgame = 0
    option_skeletons = 1
    default = 0


class SkeletonsNeeded(Range):
    """With the skeletons goal, how many skeletons you must complete."""

    display_name = "Skeletons Needed"
    range_start = 1
    range_end = 9
    default = 9


class Bonesanity(Choice):
    """Whether placing bones on a skeleton gives checks.

    off: no bone checks.
    milestones: every 10% of each skeleton.
    every_bone: every single bone placed (1,940 checks)."""

    display_name = "Bonesanity"
    option_off = 0
    option_milestones = 1
    option_every_bone = 2
    default = 1


class Collectionsanity(DefaultOnToggle):
    """Placing each species' coin, cassette and book is a check (27 checks)."""

    display_name = "Collectionsanity"


class Photosanity(DefaultOnToggle):
    """Photographing each exhibit, the cat and the model with the snow globe is a check (11 checks)."""

    display_name = "Photosanity"


class Gardensanity(DefaultOnToggle):
    """Maxing out each garden bed is a check (7 checks)."""

    display_name = "Gardensanity"


class Kitsanity(DefaultOnToggle):
    """Finding each model kit piece is a check (6 checks)."""

    display_name = "Kitsanity"


class Achievementsanity(DefaultOnToggle):
    """Unlocking each of the 29 achievements is a check."""

    display_name = "Achievementsanity"


class Supportsanity(Toggle):
    """Straightening each skeleton support is a check (17 checks)."""

    display_name = "Supportsanity"


OPTION_GROUPS = [
    OptionGroup("Goal", [Goal, SkeletonsNeeded]),
    OptionGroup("Location Checks", [Bonesanity, Collectionsanity, Photosanity, Gardensanity, Kitsanity,
                                    Achievementsanity, Supportsanity]),
]


@dataclass
class DinoBonesOptions(PerGameCommonOptions):
    goal: Goal
    skeletons_needed: SkeletonsNeeded
    bonesanity: Bonesanity
    collectionsanity: Collectionsanity
    photosanity: Photosanity
    gardensanity: Gardensanity
    kitsanity: Kitsanity
    achievementsanity: Achievementsanity
    supportsanity: Supportsanity
