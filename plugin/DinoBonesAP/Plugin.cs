using System;
using System.Collections;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using DinoMuseum.Blueprint;
using DinoMuseum.Secrets;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DinoBonesAP
{
    [BepInPlugin("dinobones.archipelago", "Dino Bones Archipelago", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dinobones.archipelago";
        internal static ManualLogSource Log;
        internal static Plugin Instance;
        float nextPoll, nextScan;
        bool dumped;
        GUIStyle style;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("[DinoAP] plugin 0.1.0 loading");
            try { Bridge.Init(); } catch (Exception e) { Log.LogError("[DinoAP] Bridge.Init: " + e); }

            var harmony = new Harmony(Guid);
            foreach (var t in typeof(Plugin).Assembly.GetTypes())
            {
                if (!t.GetCustomAttributes(typeof(HarmonyPatch), true).Any()) continue;
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception e) { Log.LogError("[DinoAP] patch " + t.Name + " failed: " + e.Message); }
            }

            HookEvents();
            SceneManager.sceneLoaded += OnSceneLoaded;
            Log.LogInfo("[DinoAP] loaded; connected=" + Bridge.Connected);
        }

        // The game clears BonePlacer.SkeletonCompleted at SubsystemRegistration; re-adding is idempotent.
        static void HookEvents()
        {
            try
            {
                SecretFound.Announced -= OnSecret;
                SecretFound.Announced += OnSecret;
                BonePlacer.SkeletonCompleted -= OnSkeleton;
                BonePlacer.SkeletonCompleted += OnSkeleton;
            }
            catch (Exception e) { Log.LogError("[DinoAP] events: " + e.Message); }
        }

        static void OnSecret(string species) { Locs.Secret(species); }
        static void OnSkeleton(BonePlacer placer, BlueprintRig rig)
        {
            if (rig != null) Locs.SkeletonComplete(rig.Species);
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            try
            {
                HookEvents();
                Tools.InvalidateAll();
                Bridge.WriteStatus();
                StartCoroutine(DumpCounts(s.name));
            }
            catch (Exception e) { Log.LogWarning("[DinoAP] sceneLoaded: " + e.Message); }
        }

        // Debug: log the real counts once per scene that actually has skeletons, to verify the static numbers.
        IEnumerator DumpCounts(string scene)
        {
            yield return new WaitForSeconds(5f);
            if (SceneManager.GetActiveScene().name != scene) yield break;
            if (Tools.Rigs.Get().Length == 0) yield break;
            if (dumped) yield break;
            dumped = true;
            Tools.DumpCounts(scene);
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextPoll)
            {
                nextPoll = now + 0.5f;
                try
                {
                    Bridge.Poll();
                    AnnounceItems();
                    Tools.Enforce();
                    Bridge.WriteStatus();
                }
                catch (Exception e) { Log.LogWarning("[DinoAP] Update: " + e.Message); }
            }
            if (now >= nextScan)
            {
                nextScan = now + 1f;
                try { Tools.Scan(); }
                catch (Exception e) { Log.LogWarning("[DinoAP] Scan: " + e.Message); }
            }
        }

        void OnApplicationQuit() { try { Bridge.FlushDirty(); } catch { } }

        // Item state itself is idempotent and re-applied from Bridge.Counts (Tools.Enforce); this only toasts new items.
        void AnnounceItems()
        {
            if (!Bridge.Connected) return;
            while (Bridge.NewItems.Count > 0)
            {
                var it = Bridge.NewItems.Dequeue();
                Toasts.Add("Received " + it.Name + (string.IsNullOrEmpty(it.Player) ? "" : " from " + it.Player));
                Bridge.MarkApplied(it.Index);
            }
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle { fontSize = 15 };
                style.normal.textColor = Color.white;
            }
            string status;
            Color c;
            if (Bridge.Connected)
            {
                status = "AP: connected to " + Bridge.Slot.seed + " | items " + Bridge.Received.Count;
                c = new Color(0.7f, 1f, 0.7f);
            }
            else if (Bridge.Dir != null)
            {
                status = "AP: bridge folder found, slot not loaded";
                c = new Color(1f, 0.9f, 0.5f);
            }
            else
            {
                status = "AP: no bridge folder (start the client)";
                c = new Color(1f, 0.7f, 0.7f);
            }
            var old = GUI.color;
            GUI.color = c;
            GUI.Label(new Rect(10, 6, 900, 24), status, style);
            GUI.color = old;
            Toasts.Draw(style, 10, 30);
        }
    }
}
