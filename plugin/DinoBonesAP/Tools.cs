using System;
using System.Collections.Generic;
using System.Linq;
using DinoMuseum.Achievements;
using DinoMuseum.Blueprint;
using DinoMuseum.Books;
using DinoMuseum.Cassettes;
using DinoMuseum.Coins;
using DinoMuseum.Crates;
using DinoMuseum.Equipment;
using DinoMuseum.Garden;
using DinoMuseum.ModelKit;
using DinoMuseum.Photo;
using DinoMuseum.Secrets;
using DinoMuseum.Supports;
using HarmonyLib;
using IndieSouls.UI.Seams;
using UnityEngine;

namespace DinoBonesAP
{
    /// <summary>Static world data from SPEC.md (must match the apworld).</summary>
    public static class Spec
    {
        public static readonly string[] Species =
        {
            "Trex", "Triceratops", "Mammoth", "Spinosaurus", "Kentrosaurus",
            "Pachycephalosaurus", "Parasaurolophus", "Pteranodon", "Brachiosaurus"
        };

        public static readonly Dictionary<string, int> Bones = new Dictionary<string, int>
        {
            { "Brachiosaurus", 136 }, { "Kentrosaurus", 231 }, { "Mammoth", 191 }, { "Pachycephalosaurus", 165 },
            { "Parasaurolophus", 180 }, { "Pteranodon", 82 }, { "Spinosaurus", 387 }, { "Trex", 305 }, { "Triceratops", 263 }
        };

        public static int SupportParts(string species) { return species == "Pteranodon" ? 1 : 2; }
        public const int GardenBeds = 7;
        public static readonly string[] KitPieces = { "Body", "Head", "Arms", "Leg1", "Leg2", "Tail" };

        public static string PermitItem(string species) { return species + " Exhibit Permit"; }

        public static string EquipItem(EquipmentKind k)
        {
            switch (k)
            {
                case EquipmentKind.Glider: return "Glider";
                case EquipmentKind.WateringCan: return "Watering Can";
                default: return "Polaroid Camera";
            }
        }

        public const string HammerItem = "Crate Hammer";
    }

    /// <summary>Location sending. Every method is idempotent (Bridge dedupes by name) and respects the slot options.</summary>
    public static class Locs
    {
        static readonly Dictionary<string, int> boneMemo = new Dictionary<string, int>();
        static readonly Dictionary<string, int> mileMemo = new Dictionary<string, int>();
        static readonly HashSet<string> completeDone = new HashSet<string>();

        public static void ResetMemo() { boneMemo.Clear(); mileMemo.Clear(); completeDone.Clear(); }

        static bool On(string mode) { return Bridge.Slot != null && Bridge.Slot.bonesanity == mode; }

        /// <summary>placed = number of bones now on that skeleton; final = skeleton complete (send everything).</summary>
        public static void Bones(string species, int placed, bool final = false)
        {
            if (!Bridge.Connected || string.IsNullOrEmpty(species)) return;
            int count;
            if (!Spec.Bones.TryGetValue(species, out count)) return;
            int n = final ? count : Math.Min(placed, count);
            if (n <= 0) return;
            if (On("every_bone"))
            {
                int from; boneMemo.TryGetValue(species, out from);
                if (n > from)
                {
                    var names = new List<string>();
                    for (int k = from + 1; k <= n; k++) names.Add(species + " Bone " + k);
                    Bridge.SendMany(names, ToastMode.None);
                    boneMemo[species] = n;
                }
            }
            else if (On("milestones"))
            {
                int from; mileMemo.TryGetValue(species, out from);
                var names = new List<string>();
                int top = from;
                for (int p = 10; p <= 100; p += 10)
                {
                    if (p <= from) continue;
                    int needed = (p * count + 99) / 100;
                    if (final || placed >= needed) { names.Add(species + " Bones " + p + "%"); top = p; }
                }
                if (names.Count > 0) { Bridge.SendMany(names, ToastMode.Each); mileMemo[species] = top; }
            }
        }

        public static void SkeletonComplete(string species)
        {
            if (!Bridge.Connected || string.IsNullOrEmpty(species) || !Spec.Bones.ContainsKey(species)) return;
            if (!completeDone.Add(species)) return;
            Bridge.Send(species + " Skeleton Complete");
            Bones(species, Spec.Bones[species], final: true);   // covers pre-placed bones and counting drift
        }

        public static void Secret(string species)
        {
            if (!Bridge.Connected || string.IsNullOrEmpty(species) || !Spec.Bones.ContainsKey(species)) return;
            Bridge.Send(species + " Secret");
        }

        public static void Collection(string species, string kind)
        {
            if (!Bridge.Connected || !Bridge.Slot.collectionsanity || string.IsNullOrEmpty(species) || !Spec.Bones.ContainsKey(species)) return;
            Bridge.Send(species + " " + kind);
        }

        public static void KitPieceFound(KitPiece piece)
        {
            if (!Bridge.Connected || !Bridge.Slot.kitsanity) return;
            Bridge.Send("Model Kit " + piece);
        }

        public static void Garden(int count)
        {
            if (!Bridge.Connected || !Bridge.Slot.gardensanity) return;
            var names = new List<string>();
            for (int k = 1; k <= Math.Min(count, Spec.GardenBeds); k++) names.Add("Garden Bed " + k);
            Bridge.SendMany(names);
        }

        public static void ExhibitPhoto(string species)
        {
            if (!Bridge.Connected || !Bridge.Slot.photosanity || !Spec.Bones.ContainsKey(species ?? "")) return;
            Bridge.Send("Photo: " + species + " Exhibit");
        }

        public static void Achievement(string id)
        {
            if (!Bridge.Connected || string.IsNullOrEmpty(id)) return;
            var s = Bridge.Slot;
            if (s.photosanity)
            {
                if (id == "photo_cat") Bridge.Send("Photo: Cat");
                else if (id == "photo_model_snowglobe") Bridge.Send("Photo: Model and Snow Globe");
            }
            if (s.achievementsanity && Array.IndexOf(AchievementId.All, id) >= 0)
                Bridge.Send("Achievement: " + id, ToastMode.Summary);
        }
    }

    /// <summary>Cached FindObjectsByType (refreshed every few seconds and on scene load).</summary>
    public class Finder<T> where T : UnityEngine.Object
    {
        T[] items = new T[0];
        float next;
        public T[] Get()
        {
            float now = Time.realtimeSinceStartup;
            bool stale = now >= next;
            if (!stale)
                for (int i = 0; i < items.Length; i++) if (items[i] == null) { stale = true; break; }
            if (stale)
            {
                try { items = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include); }
                catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] find " + typeof(T).Name + ": " + e.Message); items = new T[0]; }
                next = now + (items.Length == 0 ? 2f : 5f);
            }
            return items;
        }
        public void Invalidate() { next = 0f; }
    }

    public static class Tools
    {
        /// <summary>True only while the plugin itself grants something (so our own patches let it through).</summary>
        public static bool Allow;

        public static readonly Finder<BlueprintRig> Rigs = new Finder<BlueprintRig>();
        public static readonly Finder<SupportHammer> Hammers = new Finder<SupportHammer>();
        public static readonly Finder<CassetteKiosk> Kiosks = new Finder<CassetteKiosk>();
        public static readonly Finder<BookStand> Books = new Finder<BookStand>();
        public static readonly Finder<ModelKitHole> Kits = new Finder<ModelKitHole>();
        public static readonly Finder<PlayerHand> Hands = new Finder<PlayerHand>();
        public static readonly Finder<PlantBed> Beds = new Finder<PlantBed>();

        static float nextSecret;
        static float lastRefusal;

        public static void InvalidateAll()
        {
            Rigs.Invalidate(); Hammers.Invalidate(); Kiosks.Invalidate(); Books.Invalidate();
            Kits.Invalidate(); Hands.Invalidate(); Beds.Invalidate();
        }

        // ------------------------------------------------------------ permits / gating
        public static bool HasPermit(string species)
        {
            var s = Bridge.Slot;
            if (s == null) return true;                                  // not connected: vanilla
            if (!Spec.Bones.ContainsKey(species ?? "")) return true;
            if (string.Equals(s.starting_permit, species, StringComparison.Ordinal)) return true;
            return Bridge.HasItem(Spec.PermitItem(species));
        }

        public static bool HasEquipment(EquipmentKind k) { return Bridge.HasItem(Spec.EquipItem(k)); }

        public static void Refuse(string message)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastRefusal < 2.5f) return;
            lastRefusal = now;
            Toasts.Add(message);
        }

        // ------------------------------------------------------------ item state (idempotent, re-applied every poll)
        public static void Enforce()
        {
            if (!Bridge.Connected) return;
            try
            {
                var hands = Hands.Get();
                PlayerHand hand = hands.Length > 0 ? hands[0] : null;
                if (hand != null)
                {
                    foreach (var kind in EquipmentKinds.All)
                    {
                        if (HasEquipment(kind) && hand.Equipment.StateOf(kind) == EquipmentState.Locked)
                        {
                            Allow = true;
                            try { hand.Equipment.Unlock(kind); } finally { Allow = false; }
                            Plugin.Log.LogInfo("[DinoAP] granted " + kind);
                        }
                    }
                }
                if (Bridge.HasItem(Spec.HammerItem) && !BreakAbility.Unlocked)
                {
                    Allow = true;
                    try { BreakAbility.Unlock(); } finally { Allow = false; }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] enforce: " + e.Message); }
        }

        // ------------------------------------------------------------ state scans (also cover loading a save)
        public static void Scan()
        {
            if (!Bridge.Connected) return;
            Guard("skeletons", ScanSkeletons);
            Guard("collections", ScanCollections);
            Guard("kit", ScanKit);
            Guard("supports", ScanSupports);
            Guard("achievements", ScanAchievements);
            Guard("secrets", ScanSecrets);
            Guard("isolation", CheckIsolation);
        }

        // If Mono inlined a patched SavePaths method the redirect silently misses; verify files really land in the AP folder.
        static float museumSince = -1f;
        static bool isoChecked;
        static void CheckIsolation()
        {
            if (isoChecked || !Bridge.SaveIsolated || Bridge.FrozenSaveDir == null) return;
            if (Rigs.Get().Length == 0) return;
            float now = Time.realtimeSinceStartup;
            if (museumSince < 0f) museumSince = now;
            if (now - museumSince < 240f) return;
            isoChecked = true;
            bool any = System.IO.Directory.Exists(Bridge.FrozenSaveDir)
                && System.IO.Directory.GetFiles(Bridge.FrozenSaveDir, "*", System.IO.SearchOption.AllDirectories).Length > 0;
            if (any) Plugin.Log.LogInfo("[DinoAP] save isolation verified: files exist under " + Bridge.FrozenSaveDir);
            else
            {
                Plugin.Log.LogError("[DinoAP] SAVE ISOLATION NOT VERIFIED: no files under " + Bridge.FrozenSaveDir + " after 4 min in the museum (a patched SavePaths method may have been inlined). Check the normal save folder.");
                Toasts.Add("AP: save isolation NOT verified - see log");
            }
        }

        static void Guard(string what, Action a)
        {
            try { a(); } catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] scan " + what + ": " + e.Message); }
        }

        static void ScanSkeletons()
        {
            var rigs = Rigs.Get();
            int done = 0;
            foreach (var rig in rigs)
            {
                if (rig == null) continue;
                string sp = rig.Species;
                Locs.Bones(sp, rig.PlacedCount);
                if (rig.AllArrived) { done++; Locs.SkeletonComplete(sp); }
            }
            var s = Bridge.Slot;
            if (s.goal == "skeletons" && done >= Math.Max(1, s.skeletons_needed)) Bridge.SendGoal();
        }

        static void ScanCollections()
        {
            if (!Bridge.Slot.collectionsanity) return;
            var album = CoinCollection.Album;
            if (album != null)
                for (int i = 1; i <= Spec.Species.Length; i++)
                    if (album.IsPlaced(i)) Locs.Collection(Spec.Species[i - 1], "Coin");
            foreach (var k in Kiosks.Get())
                if (k != null && k.HasCassette && !k.Inserting) Locs.Collection(k.Species, "Cassette");
            foreach (var b in Books.Get())
                if (b != null && b.HasBook) Locs.Collection(b.Species, "Book");
        }

        static void ScanKit()
        {
            if (!Bridge.Slot.kitsanity) return;
            foreach (var hole in Kits.Get())
            {
                if (hole == null || hole.Pieces == null) continue;
                for (int i = 0; i < hole.Pieces.Count; i++)
                {
                    var p = hole.Pieces[i];
                    if (p != null && p.State >= KitPieceState.Collected) Locs.KitPieceFound(p.Piece);
                }
            }
        }

        static void ScanSupports()
        {
            if (!Bridge.Slot.supportsanity) return;
            foreach (var hammer in Hammers.Get())
            {
                if (hammer == null) continue;
                foreach (var rig in hammer.Rigs)
                {
                    if (rig == null || rig.Data == null || string.IsNullOrEmpty(rig.Species)) continue;
                    int cap = Spec.SupportParts(rig.Species);
                    int straight = 0;
                    if (rig.AllStraight) straight = cap;
                    else
                    {
                        var withPoints = new HashSet<int>();
                        for (int i = 0; i < rig.Data.PointCount; i++) withPoints.Add(rig.Data.Points[i].PartIndex);
                        foreach (int part in withPoints) if (rig.IsPartStraight(part)) straight++;
                    }
                    var names = new List<string>();
                    for (int k = 1; k <= Math.Min(straight, cap); k++) names.Add(rig.Species + " Support " + k);
                    Bridge.SendMany(names);
                }
            }
        }

        static void ScanAchievements()
        {
            var s = Bridge.Slot;
            if (s.photosanity)
                foreach (var sp in new List<string>(AchievementLog.ExhibitsPhotographed)) Locs.ExhibitPhoto(sp);
            if (s.gardensanity) Locs.Garden(AchievementLog.GardensMaxedCount);
            if (s.achievementsanity || s.photosanity)
                foreach (var id in new List<string>(AchievementLog.Session)) Locs.Achievement(id);
        }

        static void ScanSecrets()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextSecret) return;
            nextSecret = now + 10f;
            foreach (var sp in Spec.Species)
            {
                if (Bridge.Has(sp + " Secret")) continue;
                bool found = false;
                try { found = SecretFound.IsFound(sp); } catch { }
                if (found) Locs.Secret(sp);
            }
        }

        // ------------------------------------------------------------ first-scene debug dump
        public static void DumpCounts(string scene)
        {
            try
            {
                var rigs = Rigs.Get();
                int total = 0;
                foreach (var r in rigs)
                {
                    if (r == null) continue;
                    int spec; Spec.Bones.TryGetValue(r.Species ?? "", out spec);
                    total += r.BoneCount;
                    Plugin.Log.LogInfo(string.Format("[DinoAP] rig {0}: bones={1} (spec {2}){3} placed={4} arrived={5}",
                        r.Species, r.BoneCount, spec, r.BoneCount == spec ? "" : " MISMATCH", r.PlacedCount, r.ArrivedCount));
                }
                Plugin.Log.LogInfo(string.Format("[DinoAP] scene={0} blueprintRigs={1} totalBones={2} (spec 1940)", scene, rigs.Length, total));
                var hammers = Hammers.Get();
                int parts = 0, rigsN = 0;
                foreach (var h in hammers)
                {
                    if (h == null) continue;
                    foreach (var sr in h.Rigs)
                    {
                        if (sr == null || sr.Data == null) continue;
                        rigsN++; parts += sr.Data.Parts.Count;
                        Plugin.Log.LogInfo(string.Format("[DinoAP] support {0}: parts={1} points={2} (spec parts {3})",
                            sr.Species, sr.Data.Parts.Count, sr.Data.PointCount, Spec.SupportParts(sr.Species)));
                    }
                }
                Plugin.Log.LogInfo(string.Format("[DinoAP] supportRigs={0} supportParts={1} (spec 17)", rigsN, parts));
                Plugin.Log.LogInfo(string.Format("[DinoAP] gardenBeds={0} (spec {1}) kitHoles={2} kiosks={3} bookStands={4} playerHands={5}",
                    Beds.Get().Length, Spec.GardenBeds, Kits.Get().Length, Kiosks.Get().Length, Books.Get().Length, Hands.Get().Length));
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] dump: " + e.Message); }
        }
    }
}
