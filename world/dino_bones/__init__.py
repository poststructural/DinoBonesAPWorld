from worlds.LauncherComponents import Component, Type, components, launch as launch_component

from .world import DinoBonesWorld as DinoBonesWorld


def launch_client(*args: str) -> None:
    from .client import launch
    launch_component(launch, name="DinoBonesClient", args=args)


components.append(Component("Dino Bones Client", func=launch_client, component_type=Type.CLIENT,
                            game_name="Dino Bones", supports_uri=True,
                            description="Connects Dino Bones (BepInEx plugin bridge) to a multiworld."))
