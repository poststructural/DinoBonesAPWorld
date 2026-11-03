"""Archipelago client for Dino Bones.

The BepInEx plugin owns the game side and talks to this client through a file bridge in
``~/DinoBonesAP/<seed>_<slot>/`` (the user's home folder, on Windows and macOS):

  current.txt   (in ~/DinoBonesAP) client -> plugin, name of the active folder
  slot.json     client -> plugin, slot data plus seed, slot and player
  items.jsonl   client -> plugin, append-only received items
  checks.jsonl  plugin -> client, append-only {"location": name} or {"type": "goal"}
  checked.json  client -> plugin, names of locations already checked on the server
  status.json   plugin -> client, display only
"""
from __future__ import annotations

import argparse
import asyncio
import contextlib
import json
import re
from pathlib import Path

import colorama

import Utils
from CommonClient import CommonContext, get_base_parser, gui_enabled, handle_url_arg, logger, server_loop
from NetUtils import ClientStatus

ROOT_NAME = "DinoBonesAP"


def default_root() -> Path:
    return Path.home() / ROOT_NAME


def folder_name(seed: str, slot: int) -> str:
    return re.sub(r"[^A-Za-z0-9._-]", "_", f"{seed}_{slot}")


class DinoBonesContext(CommonContext):
    game = "Dino Bones"
    items_handling = 0b111

    def __init__(self, server_address: str | None, password: str | None, root: Path):
        super().__init__(server_address, password)
        self.root = root
        self.bridge: Path | None = None
        self.check_offset = 0
        self.items_sent = 0
        self.goal_sent = False
        self.slot_data: dict = {}
        self.slot_written = False
        self.name_to_id: dict[str, int] = {}
        self.last_checked: frozenset[int] | None = None

    # ---- bridge state -------------------------------------------------
    def _open_bridge(self) -> None:
        seed = self.seed_name or self.server_seed_name or "unknown"
        self.bridge = self.root / folder_name(seed, self.slot or 0)
        self.bridge.mkdir(parents=True, exist_ok=True)
        self.check_offset = self._read_int("client_offset.txt")
        self.items_sent = self._read_int("client_items.txt")
        self.last_checked = None
        self.slot_written = False
        self.goal_sent = False
        (self.root / "current.txt").write_text(self.bridge.name, encoding="utf-8")

    def _read_int(self, name: str) -> int:
        try:
            return int((self.bridge / name).read_text().strip() or 0)
        except (OSError, ValueError):
            return 0

    def _write_offsets(self) -> None:
        try:
            (self.bridge / "client_offset.txt").write_text(str(self.check_offset))
            (self.bridge / "client_items.txt").write_text(str(self.items_sent))
        except OSError:
            pass

    # ---- AP connection ------------------------------------------------
    def on_package(self, cmd: str, args: dict) -> None:
        if cmd == "Connected":
            self.slot_data = args.get("slot_data") or {}
            self.bridge = None  # reopen for this seed/slot in the loop
        super().on_package(cmd, args)

    async def server_auth(self, password_requested: bool = False):
        if password_requested and not self.password:
            await super().server_auth(password_requested)
        await self.get_username()
        await self.send_connect()

    def _build_name_lookup(self) -> None:
        try:
            lookup = self.location_names[self.game]
        except KeyError:
            return
        self.name_to_id = {name: loc_id for loc_id, name in lookup.items()}

    def _write_slot_data(self) -> None:
        if self.slot_written:
            return
        payload = dict(self.slot_data)
        payload["seed"] = self.seed_name or self.server_seed_name
        payload["slot"] = self.slot
        payload["player"] = self.auth
        try:
            (self.bridge / "slot.json").write_text(json.dumps(payload), encoding="utf-8")
            self.slot_written = True
            logger.info(f"Wrote slot data to {self.bridge}")
        except OSError as exc:
            logger.warning(f"Could not write slot.json: {exc}")

    def _write_checked(self) -> None:
        current = frozenset(self.checked_locations)
        if current == self.last_checked or not self.name_to_id:
            return
        id_to_name = {loc_id: name for name, loc_id in self.name_to_id.items()}
        names = sorted(id_to_name[loc] for loc in current if loc in id_to_name)
        try:
            (self.bridge / "checked.json").write_text(json.dumps(names), encoding="utf-8")
            self.last_checked = current
        except OSError as exc:
            logger.warning(f"Could not write checked.json: {exc}")

    # ---- checks -------------------------------------------------------
    async def _poll_checks(self) -> None:
        path = self.bridge / "checks.jsonl"
        if not path.exists():
            return
        found: list[int] = []
        goal = False
        try:
            with path.open("rb") as handle:
                handle.seek(self.check_offset)
                data = handle.read()
        except OSError:
            return
        end = data.rfind(b"\n") + 1  # only consume complete lines
        for raw in data[:end].splitlines():
            try:
                msg = json.loads(raw.decode("utf-8-sig").strip() or "null")
            except (json.JSONDecodeError, UnicodeDecodeError):
                continue
            if not isinstance(msg, dict):
                continue
            if msg.get("type") == "goal":
                goal = True
                continue
            loc_id = self.name_to_id.get(msg.get("location") or "")
            if loc_id is not None and loc_id not in self.checked_locations:
                found.append(loc_id)
        if end:
            self.check_offset += end
            self._write_offsets()
        if found:
            await self.check_locations(found)
        if goal and not self.goal_sent:
            self.goal_sent = True
            await self.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
            logger.info("Goal sent to the server.")

    # ---- items --------------------------------------------------------
    async def _push_items(self) -> None:
        total = len(self.items_received)
        if self.items_sent > total:
            self.items_sent = 0
        if self.items_sent == total:
            return
        lines = []
        for index in range(self.items_sent, total):
            item = self.items_received[index]
            name = self.item_names.lookup_in_slot(item.item, item.player)
            sender = self.player_names.get(item.player, str(item.player))
            lines.append(json.dumps({"index": index, "item": name, "player": sender}))
        try:
            with (self.bridge / "items.jsonl").open("a", encoding="utf-8") as handle:
                handle.write("\n".join(lines) + "\n")
        except OSError as exc:
            logger.warning(f"Could not write items.jsonl: {exc}")
            return
        self.items_sent = total
        self._write_offsets()
        logger.info(f"Queued {len(lines)} item(s) for the game.")

    # ---- loop ---------------------------------------------------------
    async def bridge_loop(self) -> None:
        while not self.exit_event.is_set():
            try:
                if self.slot is not None and (self.seed_name or self.server_seed_name):
                    if not self.name_to_id:
                        self._build_name_lookup()
                    if self.bridge is None:
                        self._open_bridge()
                    self._write_slot_data()
                    self._write_checked()
                    await self._poll_checks()
                    await self._push_items()
            except Exception as exc:  # keep the bridge alive
                logger.exception(f"Dino Bones bridge error: {exc}")
            await asyncio.sleep(0.5)


async def _main(args: argparse.Namespace) -> None:
    root = Path(args.bridge_root) if args.bridge_root else default_root()
    ctx = DinoBonesContext(args.connect, args.password, root)
    ctx.auth = args.name
    ctx.server_task = asyncio.create_task(server_loop(ctx), name="server loop")
    bridge_task = asyncio.create_task(ctx.bridge_loop(), name="Dino Bones bridge loop")
    if gui_enabled and not getattr(args, "nogui", False):
        ctx.run_gui()
    ctx.run_cli()
    try:
        await ctx.exit_event.wait()
    finally:
        bridge_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await bridge_task
        await ctx.shutdown()


def launch(*launch_args: str) -> None:
    parser = get_base_parser(description="Dino Bones Archipelago Client")
    parser.add_argument("--name", default=None, help="Slot name to connect as.")
    parser.add_argument("--bridge-root", default=None, help="Bridge root (default: ~/DinoBonesAP).")
    parser.add_argument("url", nargs="?", help="archipelago:// connection URL")
    args = handle_url_arg(parser.parse_args(launch_args or None), parser)
    Utils.init_logging("DinoBonesClient", exception_logger="Client")
    colorama.just_fix_windows_console()
    try:
        asyncio.run(_main(args))
    finally:
        colorama.deinit()


if __name__ == "__main__":
    launch()
