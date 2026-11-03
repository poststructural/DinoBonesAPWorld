using System;
using System.IO;
using DinoMuseum.Achievements;
using DinoMuseum.Blueprint;
using DinoMuseum.Coins;
using DinoMuseum.Crates;
using DinoMuseum.Endgame;
using DinoMuseum.Equipment;
using DinoMuseum.ModelKit;
using DinoMuseum.Photo;
using DinoMuseum.Supports;
using HarmonyLib;
using IndieSouls.Save;
using IndieSouls.UI.Seams;
using UnityEngine;

namespace DinoBonesAP
{
    // ===================================================================== save isolation
    // IndieSouls.Save.SavePaths.SavesDirFor(accountId) is the single source of the save folder: SavesDir,
    // MuseumSaveFiles, SaveSlots, OfflineSaves, the achievement ledger and the achievement recovery scan all
    // build their paths from it. Redirecting it moves every one of them into ~/DinoBonesAP/<folder>/save/.
    static class SaveIso
    {
        static bool decided, vanilla;
        public static string Root()
        {
            Bridge.SaveBooted = true;
            if (!decided)
            {
                decided = true;
                if (!Bridge.Connected || Bridge.Dir == null)
                {
                    vanilla = true;
                    Plugin.Log.LogWarning("[DinoAP] Archipelago client was not running when the game started: using the NORMAL save. Start the client before the game for an isolated AP save.");
                }
                else
                {
                    Bridge.FrozenSaveDir = Bridge.SaveDir;
                    try { Directory.CreateDirectory(Bridge.FrozenSaveDir); } catch { }
                    Plugin.Log.LogInfo("[DinoAP] save redirected to " + Bridge.FrozenSaveDir);
                }
            }
            if (vanilla) return null;
            Bridge.SaveIsolated = true;
            return Bridge.FrozenSaveDir;
        }
    }

    [HarmonyPatch(typeof(SavePaths), nameof(SavePaths.SavesDirFor))]
    static class P_SavesDirFor
    {
        static bool Prefix(string accountId, ref string __result)
        {
            try
            {
                string root = SaveIso.Root();
                if (root == null) return true;
                __result = Path.Combine(Path.Combine(root, "Saves"), accountId ?? "local");
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("[DinoAP] SavesDirFor: " + e); return true; }
        }
    }

    [HarmonyPatch(typeof(SavePaths), nameof(SavePaths.PhotosDirFor))]
    static class P_PhotosDirFor
    {
        static bool Prefix(int slot, ref string __result)
        {
            try
            {
                string root = SaveIso.Root();
                if (root == null) return true;
                __result = Path.Combine(root, "Photos");
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("[DinoAP] PhotosDirFor: " + e); return true; }
        }
    }


    // Same redirect on the property getters. Mono may inline these tiny methods into callers, so each layer is patched.
    [HarmonyPatch(typeof(SavePaths), nameof(SavePaths.SavesDir), MethodType.Getter)]
    static class P_SavesDirGet
    {
        static bool Prefix(ref string __result)
        {
            try
            {
                string root = SaveIso.Root();
                if (root == null) return true;
                __result = Path.Combine(Path.Combine(root, "Saves"), SavePaths.AccountId ?? "local");
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("[DinoAP] SavesDir: " + e); return true; }
        }
    }

    [HarmonyPatch(typeof(SavePaths), nameof(SavePaths.PhotosDir), MethodType.Getter)]
    static class P_PhotosDirGet
    {
        static bool Prefix(ref string __result)
        {
            try
            {
                string root = SaveIso.Root();
                if (root == null) return true;
                __result = Path.Combine(root, "Photos");
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("[DinoAP] PhotosDir: " + e); return true; }
        }
    }

    // Account settings (volume, keybinds, ...) stay in the normal location so the player's options are kept.
    [HarmonyPatch(typeof(AccountSettingsFile), nameof(AccountSettingsFile.Path), MethodType.Getter)]
    static class P_AccountSettingsPath
    {
        static bool Prefix(ref string __result)
        {
            try
            {
                if (SaveIso.Root() == null) return true;
                string dir = Path.Combine(Path.Combine(SavePaths.Root, "Saves"), SavePaths.AccountId);
                try { Directory.CreateDirectory(dir); } catch { }
                __result = Path.Combine(dir, AccountSettingsFile.FileName);
                return false;
            }
            catch { return true; }
        }
    }

    // ===================================================================== bones: permit gate
    // BonePlacer.StartFlight is the one private funnel behind TryPlace (hand placement) and TryPlaceInto
    // (auto-fill flight); it is where rig.MarkPlaced happens, so refusing here covers both.
    [HarmonyPatch(typeof(BonePlacer), "StartFlight")]
    static class P_StartFlight
    {
        static bool Prefix(BlueprintRig rig, ref bool __result)
        {
            try
            {
                if (!Bridge.Connected || rig == null || Tools.HasPermit(rig.Species)) return true;
                Tools.Refuse("Need the " + Spec.PermitItem(rig.Species) + " to place bones here");
                __result = false;
                return false;
            }
            catch { return true; }
        }
    }

    // The U-key auto-fill cheat restores bones directly (RestorePlaced), bypassing StartFlight.
    [HarmonyPatch(typeof(BoneGrabber), "StartAutoFillRun")]
    static class P_AutoFill
    {
        static bool Prefix(BlueprintRig rig, out int missing, ref int __result)
        {
            missing = 0;
            try
            {
                if (!Bridge.Connected || rig == null || Tools.HasPermit(rig.Species)) return true;
                Tools.Refuse("Need the " + Spec.PermitItem(rig.Species));
                __result = 0;
                return false;
            }
            catch { return true; }
        }
    }

    // ===================================================================== bones: counting (state based, so loads count too)
    [HarmonyPatch(typeof(BlueprintRig), "MarkPlaced")]
    static class P_MarkPlaced
    {
        static void Postfix(BlueprintRig __instance, bool __result)
        {
            if (__result && Bridge.Connected) Locs.Bones(__instance.Species, __instance.PlacedCount);
        }
    }

    [HarmonyPatch(typeof(BlueprintRig), "MarkArrived")]
    static class P_MarkArrived
    {
        static void Postfix(BlueprintRig __instance, bool __result)
        {
            if (__result && Bridge.Connected) Locs.Bones(__instance.Species, __instance.PlacedCount);
        }
    }

    // ===================================================================== coins (immediate; the scan also covers loads)
    [HarmonyPatch(typeof(CoinAlbum), nameof(CoinAlbum.TryPlace))]
    static class P_CoinPlace
    {
        static void Postfix(Coin coin, bool __result)
        {
            if (__result && coin != null) Locs.Collection(coin.Species, "Coin");
        }
    }

    // ===================================================================== achievements / photos / gardens
    // Private AchievementLog.Report(id, live) is the funnel for Report, ReportRecovered and the tracker; it returns
    // true only for ids new this session, and a loaded save replays earned ids through it.
    [HarmonyPatch(typeof(AchievementLog), "Report", new[] { typeof(string), typeof(bool) })]
    static class P_Report
    {
        static void Postfix(string id, bool __result)
        {
            if (__result && Bridge.Connected) Locs.Achievement(id);
        }
    }

    [HarmonyPatch(typeof(AchievementLog), nameof(AchievementLog.MarkExhibitPhotographed))]
    static class P_ExhibitPhoto
    {
        static void Postfix(string species, bool __result)
        {
            if (__result) Locs.ExhibitPhoto(species);
        }
    }

    [HarmonyPatch(typeof(AchievementLog), nameof(AchievementLog.MarkGardenMaxed))]
    static class P_GardenMaxed
    {
        static void Postfix(bool __result)
        {
            if (__result) Locs.Garden(AchievementLog.GardensMaxedCount);
        }
    }

    // ===================================================================== model kit
    [HarmonyPatch(typeof(ModelKitHole), "Collect", new[] { typeof(ModelKitPieceBody), typeof(bool) })]
    static class P_KitCollect
    {
        static void Postfix(ModelKitPieceBody body, bool __result)
        {
            if (__result && body != null) Locs.KitPieceFound(body.Piece);
        }
    }

    // ===================================================================== goal
    [HarmonyPatch(typeof(EndgameSequence), "End")]
    static class P_EndgameEnd
    {
        static void Postfix(bool completed)
        {
            if (completed && Bridge.Connected && Bridge.Slot.goal == "endgame") Bridge.SendGoal();
        }
    }

    // A loaded save that already played the ending.
    [HarmonyPatch(typeof(EndgameSequence), nameof(EndgameSequence.RestorePlayed))]
    static class P_EndgameRestore
    {
        static void Postfix()
        {
            if (Bridge.Connected && Bridge.Slot.goal == "endgame") Bridge.SendGoal();
        }
    }

    // ===================================================================== equipment gating
    // The secret spawns the tool as a HandItem in the world; the player owns it once PlayerHand.Take runs
    // (NoteEquipmentTaken -> EquipmentInventory.Set(InHand)). Refuse the pickup until the AP item arrived.
    [HarmonyPatch(typeof(PlayerHand), "Take")]
    static class P_Take
    {
        static bool Prefix(PlayerHand __instance, HandItem item)
        {
            try
            {
                if (!Bridge.Connected || Tools.Allow || item == null) return true;
                EquipmentKind kind;
                if (!EquipmentKinds.TryFromHandItem(item.Kind, out kind)) return true;
                if (__instance.Equipment.StateOf(kind) != EquipmentState.Locked) return true;
                if (Tools.HasEquipment(kind)) return true;
                Tools.Refuse("The " + Spec.EquipItem(kind) + " is an Archipelago item");
                return false;
            }
            catch { return true; }
        }
    }

    // EquipmentInventory.Unlock is only reached by the debug cheat (PlayerHand.CheatUnlock); keep it closed too.
    [HarmonyPatch(typeof(EquipmentInventory), nameof(EquipmentInventory.Unlock))]
    static class P_EquipUnlock
    {
        static bool Prefix(EquipmentKind kind, ref bool __result)
        {
            if (!Bridge.Connected || Tools.Allow || Tools.HasEquipment(kind)) return true;
            __result = false;
            return false;
        }
    }

    // The Brachiosaurus secret hammer: HammerPickup.Take / HammerSecret -> BreakAbility.Unlock() -> private Set(true).
    [HarmonyPatch(typeof(BreakAbility), "Set")]
    static class P_BreakSet
    {
        static bool Prefix(bool unlocked, ref bool __result)
        {
            if (!unlocked || !Bridge.Connected || Tools.Allow || Bridge.HasItem(Spec.HammerItem)) return true;
            Tools.Refuse("The Crate Hammer is an Archipelago item");
            __result = false;
            return false;
        }
    }
}
