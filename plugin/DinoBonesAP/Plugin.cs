using System;
using System.Collections;
using System.IO;
using DinoMuseum.Blueprint;
using DinoMuseum.Secrets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DinoBonesAP
{
    /// <summary>Debug.Log (Player.log) plus &lt;bridge root&gt;/plugin.log, rewritten each game start.</summary>
    public sealed class PluginLog
    {
        readonly object gate = new object();
        StreamWriter file;
        bool opened;

        void Write(string level, string msg)
        {
            lock (gate)
            {
                try
                {
                    if (!opened)
                    {
                        opened = true;
                        try
                        {
                            string root = Bridge.ComputeRoot();
                            Directory.CreateDirectory(root);
                            var fs = new FileStream(Path.Combine(root, "plugin.log"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                            file = new StreamWriter(fs) { AutoFlush = true };
                        }
                        catch { file = null; }
                    }
                    if (file != null) file.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + msg);
                }
                catch { }
            }
        }

        public void LogInfo(string m) { Write("Info", m); Debug.Log(m); }
        public void LogWarning(string m) { Write("Warning", m); Debug.LogWarning(m); }
        public void LogError(string m) { Write("Error", m); Debug.LogError(m); }
    }

    /// <summary>
    /// Entry point. Unity runs Boot from RuntimeInitializeOnLoads.json (the installer adds the entry). No BepInEx:
    /// the hooks are IL woven into copies of the game assemblies (see Loader/Weaver).
    /// </summary>
    public static class Plugin
    {
        public const string Version = "0.2.0";
        internal static readonly PluginLog Log = new PluginLog();
        static readonly object gate = new object();
        static bool coreDone;

        /// <summary>True when the loaded game assemblies carry the current woven hooks. AP only runs then.</summary>
        internal static bool HooksActive;
        /// <summary>A message the overlay shows instead of the status line (restart needed, weave failed).</summary>
        internal static string Notice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Boot()
        {
            try
            {
                EnsureCore();
                var go = new GameObject("DinoBonesAP");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Runner>();
            }
            catch (Exception e) { Log.LogError("[DinoAP] boot failed: " + e); }
        }

        /// <summary>
        /// Idempotent. Called from Boot and from the save-path hooks, because the game may ask for its save
        /// folder before Boot runs. Decides whether the woven hooks are live; only then does the bridge start.
        /// </summary>
        internal static void EnsureCore()
        {
            lock (gate)
            {
                if (coreDone) return;
                coreDone = true;
                Log.LogInfo("[DinoAP] plugin " + Version + " loading (native loader)");
                string why;
                try { HooksActive = Loader.HooksActive(out why); }
                catch (Exception e) { HooksActive = false; why = "check failed: " + e; }
                if (!HooksActive)
                {
                    Log.LogWarning("[DinoAP] hooks not active: " + why + ". Archipelago stays off this session.");
                    Notice = Loader.Prepare();
                    Log.LogWarning("[DinoAP] " + Notice);
                    return;
                }
                Log.LogInfo("[DinoAP] hooks active (" + Loader.Summary + ")");
                try { Bridge.Init(); } catch (Exception e) { Log.LogError("[DinoAP] Bridge.Init: " + e); }
                Log.LogInfo("[DinoAP] loaded; connected=" + Bridge.Connected);
            }
        }
    }

    /// <summary>The persistent object: poll loop, scans, overlay (was the BepInEx plugin behaviour).</summary>
    public class Runner : MonoBehaviour
    {
        float nextPoll, nextScan;
        bool dumped;
        GUIStyle style, noticeStyle;

        void Awake()
        {
            if (!Plugin.HooksActive) return;
            HookEvents();
            SceneManager.sceneLoaded += OnSceneLoaded;
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
            catch (Exception e) { Plugin.Log.LogError("[DinoAP] events: " + e.Message); }
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
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] sceneLoaded: " + e.Message); }
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
            if (!Plugin.HooksActive) return;
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
                catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] Update: " + e.Message); }
            }
            if (now >= nextScan)
            {
                nextScan = now + 1f;
                try { Tools.Scan(); }
                catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] Scan: " + e.Message); }
            }
        }

        void OnApplicationQuit()
        {
            if (!Plugin.HooksActive) return;
            try { Bridge.FlushDirty(); } catch { }
        }

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
                noticeStyle = new GUIStyle { fontSize = 22, wordWrap = true, alignment = TextAnchor.MiddleCenter };
                noticeStyle.normal.textColor = Color.white;
            }
            if (Plugin.Notice != null)
            {
                var r = new Rect(Screen.width * 0.15f, Screen.height * 0.35f, Screen.width * 0.7f, 140);
                var oldBg = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.9f, 0.5f);
                GUI.Label(new Rect(r.x + 16, r.y + 8, r.width - 32, r.height - 16), Plugin.Notice, noticeStyle);
                GUI.color = oldBg;
                return;
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
