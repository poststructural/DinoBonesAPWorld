using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DinoBonesAP
{
    // NOTE: structured JSON goes through UnityEngine.JsonUtility (the game ships a stripped Newtonsoft.Json).
    [Serializable]
    public class SlotData
    {
        public string goal = "endgame";          // "endgame" | "skeletons"
        public int skeletons_needed = 9;
        public string bonesanity = "off";        // "off" | "milestones" | "every_bone"
        public bool collectionsanity;
        public bool photosanity;
        public bool gardensanity;
        public bool kitsanity;
        public bool supportsanity;
        public bool achievementsanity;
        public string starting_permit = "";
        public bool death_link;
        public string seed = "";
        public string player = "";
    }

    [Serializable] class ItemLine { public int index = -1; public string item = ""; public string player = ""; }
    [Serializable] class StringList { public List<string> v = new List<string>(); }
    [Serializable]
    class StateDto
    {
        public int applied = -1;
        public bool goal_sent;
        public List<string> sent = new List<string>();
    }

    public class ItemRec
    {
        public int Index;
        public string Name;
        public string Player;
    }

    public class PluginState
    {
        public int applied = -1;
        public HashSet<string> sent = new HashSet<string>();
        public bool goal_sent;
    }

    public enum ToastMode { Each, Summary, None }

    /// <summary>File bridge between the Python client and this plugin. All calls are main-thread.</summary>
    public static class Bridge
    {
        public static string Root;
        public static string Dir;            // active bridge folder (may be null before the client ran)
        public static SlotData Slot;         // null = not connected (vanilla behaviour)
        public static PluginState State = new PluginState();
        public static HashSet<string> Checked = new HashSet<string>();
        public static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public static readonly List<ItemRec> Received = new List<ItemRec>();
        public static readonly Queue<ItemRec> NewItems = new Queue<ItemRec>();   // items not yet announced (index > State.applied)

        static readonly HashSet<int> seenIdx = new HashSet<int>();
        static long itemOffset;
        static string slotStamp = "", checkedStamp = "";
        static bool stateDirty;
        static string lastStatus = "";
        public static string FrozenSaveDir;  // chosen the first time the game asks for a save path (cannot change mid-run)
        public static bool SaveBooted, SaveIsolated;

        public static bool Connected { get { return Slot != null; } }
        public static string SaveDir { get { return Dir == null ? null : Path.Combine(Dir, "save"); } }
        public static string CurrentTxt { get { return Path.Combine(Root, "current.txt"); } }

        // ---------------------------------------------------------------- init
        public static void Init()
        {
            string home = null;
            try { home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            if (string.IsNullOrEmpty(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE") ?? ".";
            // DINOBONES_AP_ROOT matches the client's --bridge-root (testing, or a non-default home).
            string over = Environment.GetEnvironmentVariable("DINOBONES_AP_ROOT");
            Root = string.IsNullOrEmpty(over) ? Path.Combine(home, "DinoBonesAP") : over;
            try { Directory.CreateDirectory(Root); } catch { }
            string cur = ReadCurrent();
            if (cur != null) SwitchDir(Path.Combine(Root, cur));
            else Plugin.Log.LogWarning("[DinoAP] current.txt not found in " + Root + " - start the Archipelago client before the game. Running vanilla.");
        }

        static string ReadCurrent()
        {
            try
            {
                string t = ReadShared(CurrentTxt);
                if (t == null) return null;
                t = t.Trim();
                return t.Length == 0 ? null : t;
            }
            catch { return null; }
        }

        static void SwitchDir(string dir)
        {
            Dir = dir;
            Slot = null;
            State = new PluginState();
            Checked = new HashSet<string>();
            Counts.Clear(); Received.Clear(); NewItems.Clear(); seenIdx.Clear();
            itemOffset = 0; slotStamp = ""; checkedStamp = "";
            stateDirty = false; lastStatus = "";
            Locs.ResetMemo();
            try { Directory.CreateDirectory(Dir); } catch { }
            LoadState();
            ReloadSlot();
            ReloadChecked();
            ReadItems();
            Plugin.Log.LogInfo("[DinoAP] bridge=" + Dir + " connected=" + Connected + " items=" + Received.Count + " sent=" + State.sent.Count);
        }

        static string P(string f) { return Path.Combine(Dir, f); }

        // ---------------------------------------------------------------- polling
        public static void Poll()
        {
            try
            {
                string cur = ReadCurrent();
                if (cur != null)
                {
                    string want = Path.Combine(Root, cur);
                    if (Dir == null || !string.Equals(want, Dir, StringComparison.Ordinal))
                    {
                        bool bootedVanilla = SaveBooted && !SaveIsolated;
                        SwitchDir(want);
                        Toasts.Add("AP: bridge folder changed -> " + cur);
                        if (Connected && FrozenSaveDir != null && !string.Equals(Path.Combine(Dir, "save"), FrozenSaveDir))
                            Plugin.Log.LogWarning("[DinoAP] Bridge changed while the game runs; the save folder stays " + FrozenSaveDir + ". Restart the game.");
                        if (Connected && bootedVanilla)
                            Plugin.Log.LogWarning("[DinoAP] The game already loaded the normal save. Restart the game after starting the client.");
                    }
                }
                if (Dir == null) return;
                bool wasConnected = Connected;
                ReloadSlot();
                if (!wasConnected && Connected)
                {
                    Toasts.Add("AP: connected to " + Slot.seed);
                    if (SaveBooted && !SaveIsolated)
                    {
                        Plugin.Log.LogWarning("[DinoAP] Connected after the game resolved its normal save path - save isolation is NOT active. Restart the game.");
                        Toasts.Add("AP: restart the game for a separate save!");
                    }
                }
                ReloadChecked();
                ReadItems();
                FlushDirty();
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] Poll: " + e.Message); }
        }

        static bool warnedVanilla;

        static void ReloadSlot()
        {
            try
            {
                string f = P("slot.json");
                if (!File.Exists(f)) return;
                string txt = ReadShared(f);
                if (string.IsNullOrEmpty(txt)) return;
                if (txt == slotStamp && Slot != null) return;
                var s = JsonUtility.FromJson<SlotData>(txt);
                if (s == null) return;
                // The game already resolved its normal save path: activating AP now would autosave AP progress
                // into the player's real save. Stay disconnected until the game is restarted with the client running.
                if (SaveBooted && !SaveIsolated)
                {
                    if (!warnedVanilla)
                    {
                        warnedVanilla = true;
                        Plugin.Log.LogWarning("[DinoAP] slot.json appeared after the normal save loaded - AP stays off. Restart the game.");
                        Toasts.Add("AP: restart the game to start the Archipelago save");
                    }
                    return;
                }
                Slot = s;
                slotStamp = txt;
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] slot.json: " + e.Message); }
        }

        static void ReloadChecked()
        {
            try
            {
                string f = P("checked.json");
                if (!File.Exists(f)) return;
                string txt = ReadShared(f);
                if (string.IsNullOrWhiteSpace(txt)) return;
                if (txt == checkedStamp) return;
                var list = JsonUtility.FromJson<StringList>("{\"v\":" + txt.Trim() + "}");
                if (list != null && list.v != null) { Checked = new HashSet<string>(list.v); checkedStamp = txt; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] checked.json: " + e.Message); }
        }

        static void ReadItems()
        {
            try
            {
                string f = P("items.jsonl");
                if (!File.Exists(f)) return;
                byte[] buf;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < itemOffset) itemOffset = 0;
                    if (fs.Length == itemOffset) return;
                    fs.Seek(itemOffset, SeekOrigin.Begin);
                    buf = new byte[fs.Length - itemOffset];
                    int n = 0;
                    while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
                    if (n < buf.Length) Array.Resize(ref buf, n);
                }
                int last = Array.LastIndexOf(buf, (byte)'\n');
                if (last < 0) return; // partial line, wait for more
                string text = Encoding.UTF8.GetString(buf, 0, last + 1);
                itemOffset += last + 1;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    try
                    {
                        var o = JsonUtility.FromJson<ItemLine>(line);
                        if (o == null) continue;
                        var rec = new ItemRec { Index = o.index, Name = o.item, Player = o.player ?? "" };
                        if (string.IsNullOrEmpty(rec.Name) || rec.Index < 0) continue;
                        if (!seenIdx.Add(rec.Index)) continue;
                        Received.Add(rec);                       // every item is replayed into Counts (state is idempotent)
                        int c; Counts.TryGetValue(rec.Name, out c); Counts[rec.Name] = c + 1;
                        if (rec.Index > State.applied) NewItems.Enqueue(rec); // only these get a toast
                    }
                    catch { }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] items.jsonl: " + e.Message); }
        }

        public static bool HasItem(string name) { int c; return Counts.TryGetValue(name, out c) && c > 0; }

        // ---------------------------------------------------------------- checks
        public static bool Has(string loc) { return State.sent.Contains(loc) || Checked.Contains(loc); }

        public static void Send(string loc, ToastMode mode = ToastMode.Each)
        {
            SendMany(new[] { loc }, mode);
        }

        public static void SendMany(IEnumerable<string> locs, ToastMode mode = ToastMode.Each)
        {
            if (!Connected || Dir == null) return;
            var sb = new StringBuilder();
            var fresh = new List<string>();
            foreach (var loc in locs)
            {
                if (string.IsNullOrEmpty(loc) || Has(loc)) continue;
                State.sent.Add(loc);
                fresh.Add(loc);
                sb.Append("{\"location\": \"").Append(Esc(loc)).Append("\"}\n");
            }
            if (fresh.Count == 0) return;
            Append(P("checks.jsonl"), sb.ToString());
            stateDirty = true;
            if (mode == ToastMode.Each && fresh.Count <= 3)
                foreach (var l in fresh) Toasts.Add("Check: " + l);
            else if (mode != ToastMode.None)
                Toasts.Add("Checks sent: " + fresh.Count);
        }

        public static void SendGoal()
        {
            if (!Connected || State.goal_sent) return;
            State.goal_sent = true;
            Append(P("checks.jsonl"), "{\"type\": \"goal\"}\n");
            stateDirty = true;
            Toasts.Add("Goal complete!");
        }

        public static void MarkApplied(int idx) { if (idx > State.applied) { State.applied = idx; stateDirty = true; } }

        // ---------------------------------------------------------------- state files
        static void LoadState()
        {
            try
            {
                string txt = File.Exists(P("plugin_state.json")) ? ReadShared(P("plugin_state.json")) : null;
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    var d = JsonUtility.FromJson<StateDto>(txt);
                    if (d != null)
                    {
                        var s = new PluginState { applied = d.applied, goal_sent = d.goal_sent };
                        if (d.sent != null) s.sent = new HashSet<string>(d.sent);
                        State = s;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] plugin_state.json: " + e.Message); }
        }

        public static void FlushDirty()
        {
            if (Dir == null) return;
            try
            {
                if (stateDirty)
                {
                    var dto = new StateDto { applied = State.applied, goal_sent = State.goal_sent, sent = State.sent.ToList() };
                    WriteAtomic(P("plugin_state.json"), JsonUtility.ToJson(dto, true));
                    stateDirty = false;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] flush: " + e.Message); }
        }

        public static void WriteStatus()
        {
            if (Dir == null) return;
            try
            {
                string s = "{\"scene\": \"" + Esc(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                    + "\", \"connected_seed\": \"" + Esc(Connected ? Slot.seed : "") + "\"}";
                if (s == lastStatus) return;
                lastStatus = s;
                WriteAtomic(P("status.json"), s);
            }
            catch { }
        }

        // ---------------------------------------------------------------- io helpers
        public static string ReadShared(string path)
        {
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static void Append(string path, string text)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] b = new UTF8Encoding(false).GetBytes(text);
                    fs.Write(b, 0, b.Length);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] append " + Path.GetFileName(path) + ": " + e.Message); }
        }

        static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
        }

        static string Esc(string v)
        {
            var sb = new StringBuilder();
            foreach (char c in v ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public static class Toasts
    {
        class T { public string Text; public float Expire; }
        static readonly List<T> list = new List<T>();
        public static void Add(string text)
        {
            list.Add(new T { Text = text, Expire = Time.realtimeSinceStartup + 4f });
            if (list.Count > 8) list.RemoveAt(0);
        }
        public static void Draw(GUIStyle style, float x, float y)
        {
            float now = Time.realtimeSinceStartup;
            list.RemoveAll(t => t.Expire < now);
            foreach (var t in list)
            {
                float a = Mathf.Clamp01((t.Expire - now) / 1f);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                GUI.Label(new Rect(x, y, 900, 24), t.Text, style);
                GUI.color = old;
                y += 22;
            }
        }
    }
}
