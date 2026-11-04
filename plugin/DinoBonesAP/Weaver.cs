using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace DinoBonesAP
{
    /// <summary>
    /// Marks a public static class as a hook on Type.Method (by name; give Args to pick an overload). The class holds
    /// a public static Prefix and/or Postfix with Harmony conventions: __instance, __result (ref in a prefix to set
    /// it), original arguments by parameter name (ref to write them), and a bool Prefix returning false skips the
    /// original (postfixes still run, and the method returns __result). Property getters are "get_Name".
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HookAttribute : Attribute
    {
        public readonly Type Type;
        public readonly string Method;
        public readonly Type[] Args;

        public HookAttribute(Type type, string method, params Type[] args)
        {
            Type = type;
            Method = method;
            Args = args == null || args.Length == 0 ? null : args;
        }
    }

    internal sealed class HookInfo
    {
        public Type Class;
        public HookAttribute Attr;
        public MethodInfo Prefix, Postfix;
        public string AssemblyName { get { return Attr.Type.Assembly.GetName().Name; } }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Class.FullName).Append('>').Append(Attr.Type.FullName).Append('.').Append(Attr.Method);
            if (Attr.Args != null) sb.Append('(').Append(string.Join(",", Attr.Args.Select(a => a.FullName).ToArray())).Append(')');
            sb.Append("|pre:").Append(Sig(Prefix)).Append("|post:").Append(Sig(Postfix));
            return sb.ToString();
        }

        static string Sig(MethodInfo m)
        {
            if (m == null) return "-";
            return m.ReturnType.FullName + " (" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name).ToArray()) + ")";
        }
    }

    /// <summary>
    /// Decides whether the woven hooks are live and, when not, weaves them in.
    ///
    /// Weaving is in place, because Unity binds the scripts in scenes to their assembly by FILE name: a woven copy
    /// under another name (tried: DinoMuseum.AP.dll listed in ScriptingAssemblies.json) loads, but every DinoMuseum
    /// MonoBehaviour in the scenes then reports "The referenced script ... is missing". Unity reads assemblies into
    /// memory, so the file of a loaded assembly is not locked and can be replaced while the game runs.
    ///
    /// Files the plugin writes, for each &lt;Asm&gt; that holds hook targets (DinoMuseum, IndieSouls.Runtime), all in Managed:
    ///   &lt;Asm&gt;.dll.orig   copy of the game's own &lt;Asm&gt;.dll, refreshed whenever &lt;Asm&gt;.dll is found unwoven
    ///                    (first run, game update, Steam "verify files")
    ///   &lt;Asm&gt;.dll        replaced by the woven build of &lt;Asm&gt;.dll.orig (assembly name and identity unchanged)
    /// The plugin edits no manifest. Copying our DLLs and the ScriptingAssemblies.json / RuntimeInitializeOnLoads.json
    /// entries (with backups) are the installer's job; its uninstall puts &lt;Asm&gt;.dll.orig back.
    /// </summary>
    internal static class Loader
    {
        /// <summary>Bump to force a re-weave even when the hook signatures did not change.</summary>
        public const string HookSetVersion = "1";
        public const string MarkerType = "DinoBonesAP.Woven.Marker";
        public const string MarkerField = "Stamp";
        public static string Summary = "";
        static List<HookInfo> hooks;
        static string managed;

        public static List<HookInfo> Hooks()
        {
            if (hooks != null) return hooks;
            var list = new List<HookInfo>();
            foreach (var t in typeof(Plugin).Assembly.GetTypes())
            {
                var a = (HookAttribute)t.GetCustomAttributes(typeof(HookAttribute), false).FirstOrDefault();
                if (a == null) continue;
                const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                list.Add(new HookInfo { Class = t, Attr = a, Prefix = t.GetMethod("Prefix", f), Postfix = t.GetMethod("Postfix", f) });
            }
            list.Sort((x, y) => string.CompareOrdinal(x.Class.FullName, y.Class.FullName));
            return hooks = list;
        }

        public static List<string> TargetAssemblies()
        {
            var names = Hooks().Select(h => h.AssemblyName).Distinct().ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public static string ManagedDir()
        {
            if (managed != null) return managed;
            var tries = new List<string>();
            try { tries.Add(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)); } catch { }
            try { tries.Add(Path.Combine(Application.dataPath, "Managed")); } catch { }
            try { tries.Add(Path.Combine(Path.Combine(Path.Combine(Application.dataPath, "Resources"), "Data"), "Managed")); } catch { }
            foreach (var d in tries)
                if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "DinoMuseum.dll"))) return managed = d;
            throw new DirectoryNotFoundException("game Managed folder not found (tried: " + string.Join("; ", tries.ToArray()) + ")");
        }

        public static string GamePath(string asm) { return Path.Combine(ManagedDir(), asm + ".dll"); }
        public static string OrigPath(string asm) { return GamePath(asm) + ".orig"; }
        /// <summary>The unwoven game file: the .orig backup once we have woven, else the game's own file.</summary>
        public static string SourcePath(string asm) { string o = OrigPath(asm); return File.Exists(o) ? o : GamePath(asm); }

        /// <summary>Hook-set version + hash of this assembly's hook signatures + hash of the unwoven game DLL.</summary>
        public static string Stamp(string asm)
        {
            var text = string.Join("\n", Hooks().Where(h => h.AssemblyName == asm).Select(h => h.Describe()).ToArray());
            return "v" + HookSetVersion + "-" + Fnv(Encoding.UTF8.GetBytes(text)) + "-" + Fnv(File.ReadAllBytes(SourcePath(asm)));
        }

        static string Fnv(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        static string LoadedStamp(Assembly a)
        {
            try
            {
                var t = a.GetType(MarkerType, false);
                if (t == null) return null;
                var f = t.GetField(MarkerField, BindingFlags.Public | BindingFlags.Static);
                return f == null ? null : f.GetValue(null) as string;
            }
            catch { return null; }
        }

        public static bool HooksActive(out string why)
        {
            var asms = TargetAssemblies();
            foreach (var asm in asms)
            {
                var loaded = Hooks().First(h => h.AssemblyName == asm).Attr.Type.Assembly;
                string have = LoadedStamp(loaded);
                if (have == null) { why = asm + " is the unwoven original"; return false; }
                string want = Stamp(asm);
                if (have != want) { why = asm + " is woven but stale (" + have + " != " + want + ")"; return false; }
            }
            Summary = Hooks().Count + " hooks in " + string.Join(", ", asms.ToArray());
            why = null;
            return true;
        }

        /// <summary>Weave what is missing or stale into the game files. Returns the message for the player.
        /// Never throws; a game file is only replaced by a woven build that was written and read back in full.</summary>
        public static string Prepare()
        {
            try
            {
                bool wrote = false;
                foreach (var asm in TargetAssemblies())
                {
                    string game = GamePath(asm), orig = OrigPath(asm);
                    string onDisk = Weaver.ReadStamp(game);
                    if (onDisk == null)
                    {
                        // The game's own file (first run, update, or Steam verify): it becomes the source.
                        Weaver.CopyAtomic(game, orig);
                        Plugin.Log.LogInfo("[DinoAP] saved the original " + asm + ".dll as " + orig);
                    }
                    else if (!File.Exists(orig))
                        throw new FileNotFoundException(game + " is woven but " + orig + " is missing; use Steam's \"Verify integrity of game files\", then start the game again");
                    string want = Stamp(asm);
                    if (onDisk == want)
                    {
                        Plugin.Log.LogInfo("[DinoAP] " + game + " is already woven (" + want + ")");
                        continue;
                    }
                    var hs = Hooks().Where(h => h.AssemblyName == asm).ToList();
                    int n = Weaver.Weave(orig, game, want, hs, ManagedDir());
                    Plugin.Log.LogInfo("[DinoAP] wove " + n + " methods into " + game + " (stamp " + want + ")");
                    wrote = true;
                }
                if (!wrote)
                    return "Dino Bones Archipelago: the game files are already set up, but this session started before that.\nQuit the game and start it again.";
                return "Dino Bones Archipelago finished setting up.\nQuit the game and start it again to play with Archipelago.";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[DinoAP] setup failed; the game files were not replaced: " + e);
                return "Dino Bones Archipelago could not set up its hooks, so Archipelago is off.\nSee plugin.log in the DinoBonesAP folder.";
            }
        }
    }

    /// <summary>Mono.Cecil IL weaver. Only touched when weaving, so normal starts never load Mono.Cecil.</summary>
    internal static class Weaver
    {
        sealed class DirResolver : IAssemblyResolver
        {
            readonly string dir;
            readonly Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>();
            public DirResolver(string dir) { this.dir = dir; }
            public AssemblyDefinition Resolve(AssemblyNameReference name) { return Resolve(name, null); }
            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters p)
            {
                AssemblyDefinition a;
                if (cache.TryGetValue(name.Name, out a)) return a;
                string f = Path.Combine(dir, name.Name + ".dll");
                if (!File.Exists(f)) throw new AssemblyResolutionException(name);
                a = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(f)), new ReaderParameters { AssemblyResolver = this });
                cache[name.Name] = a;
                return a;
            }
            public void Dispose() { foreach (var a in cache.Values) a.Dispose(); cache.Clear(); }
        }

        public static string ReadStamp(string path)
        {
            try
            {
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (var asm = AssemblyDefinition.ReadAssembly(ms))
                {
                    var t = asm.MainModule.GetType(Loader.MarkerType);
                    var f = t == null ? null : t.Fields.FirstOrDefault(x => x.Name == Loader.MarkerField);
                    return f == null ? null : f.Constant as string;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[DinoAP] could not read " + path + ": " + e.Message); return null; }
        }

        /// <summary>Weave hooks into a copy of origPath written to outPath. Returns the number of woven methods.
        /// Throws (leaving outPath untouched) on any problem.</summary>
        public static int Weave(string origPath, string outPath, string stamp, List<HookInfo> hooks, string managedDir)
        {
            string tmp = outPath + ".tmp";
            int count = 0;
            using (var resolver = new DirResolver(managedDir))
            using (var ms = new MemoryStream(File.ReadAllBytes(origPath)))
            using (var asm = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate }))
            {
                var mod = asm.MainModule;
                if (mod.GetType(Loader.MarkerType) != null) throw new InvalidOperationException(origPath + " is already woven; expected the original");
                var errors = new List<string>();
                var targets = new Dictionary<MethodDefinition, List<HookInfo>>();
                foreach (var h in hooks)
                {
                    try
                    {
                        Validate(h);
                        var m = FindTarget(mod, h.Attr);
                        List<HookInfo> l;
                        if (!targets.TryGetValue(m, out l)) targets[m] = l = new List<HookInfo>();
                        l.Add(h);
                    }
                    catch (Exception e) { errors.Add(h.Class.Name + ": " + e.Message); }
                }
                foreach (var kv in targets)
                {
                    try { WeaveMethod(mod, kv.Key, kv.Value); count++; }
                    catch (Exception e) { errors.Add(kv.Key.FullName + ": " + e.Message); }
                }
                if (errors.Count > 0) throw new InvalidOperationException("weave errors:\n  " + string.Join("\n  ", errors.ToArray()));

                var marker = new TypeDefinition("DinoBonesAP.Woven", "Marker",
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class, mod.TypeSystem.Object);
                marker.Fields.Add(new FieldDefinition(Loader.MarkerField,
                    FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, mod.TypeSystem.String) { Constant = stamp });
                mod.Types.Add(marker);
                if (File.Exists(tmp)) File.Delete(tmp);
                asm.Write(tmp);
            }
            // Read the result back before it replaces anything.
            if (ReadStamp(tmp) != stamp) { TryDelete(tmp); throw new InvalidDataException("woven file failed to read back: " + tmp); }
            Replace(tmp, outPath);
            return count;
        }

        /// <summary>Copy src to dest through a temp file, so dest is never half written.</summary>
        public static void CopyAtomic(string src, string dest)
        {
            string tmp = dest + ".tmp";
            TryDelete(tmp);
            File.Copy(src, tmp);
            Replace(tmp, dest);
        }

        // File.Replace is ReplaceFile on Windows and rename(2) on Mono/Unix: dest is swapped in one step.
        static void Replace(string tmp, string dest)
        {
            if (!File.Exists(dest)) { File.Move(tmp, dest); return; }
            try { File.Replace(tmp, dest, null); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[DinoAP] File.Replace(" + dest + ") failed (" + e.Message + "); deleting and moving instead");
                File.Delete(dest);
                File.Move(tmp, dest);
            }
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        static void Validate(HookInfo h)
        {
            if (h.Prefix == null && h.Postfix == null) throw new InvalidOperationException("no Prefix or Postfix");
            if (!h.Class.IsPublic) throw new InvalidOperationException("hook class must be public");
            if (h.Prefix != null)
            {
                if (!h.Prefix.IsPublic) throw new InvalidOperationException("Prefix must be public");
                if (h.Prefix.ReturnType != typeof(void) && h.Prefix.ReturnType != typeof(bool)) throw new InvalidOperationException("Prefix must return void or bool");
            }
            if (h.Postfix != null)
            {
                if (!h.Postfix.IsPublic) throw new InvalidOperationException("Postfix must be public");
                if (h.Postfix.ReturnType != typeof(void)) throw new InvalidOperationException("Postfix must return void");
            }
        }

        // ------------------------------------------------------------------ names and types
        static string CecilName(Type t)
        {
            if (t.IsByRef) return CecilName(t.GetElementType()) + "&";
            if (t.IsArray) return CecilName(t.GetElementType()) + "[]";
            if (t.IsNested) return CecilName(t.DeclaringType) + "/" + t.Name;
            return t.FullName;
        }

        static TypeReference MapType(ModuleDefinition mod, Type t)
        {
            if (t.IsByRef) return new ByReferenceType(MapType(mod, t.GetElementType()));
            if (t.IsArray) return new ArrayType(MapType(mod, t.GetElementType()), t.GetArrayRank());
            if (t.Assembly.GetName().Name == mod.Assembly.Name.Name)
            {
                if (t.IsGenericType) throw new NotSupportedException("generic type from the woven assembly: " + t);
                var td = mod.GetType(CecilName(t));
                if (td == null) throw new InvalidOperationException("type not found in " + mod.Name + ": " + t.FullName);
                return td;
            }
            return mod.ImportReference(t);
        }

        static MethodReference ImportHook(ModuleDefinition mod, MethodInfo mi)
        {
            var mr = new MethodReference(mi.Name, MapType(mod, mi.ReturnType), mod.ImportReference(mi.DeclaringType)) { HasThis = false };
            foreach (var p in mi.GetParameters()) mr.Parameters.Add(new ParameterDefinition(MapType(mod, p.ParameterType)));
            return mr;
        }

        static MethodDefinition FindTarget(ModuleDefinition mod, HookAttribute a)
        {
            var td = mod.GetType(CecilName(a.Type));
            if (td == null) throw new InvalidOperationException("type " + a.Type.FullName + " not found");
            var c = td.Methods.Where(m => m.Name == a.Method).ToList();
            if (a.Args != null)
                c = c.Where(m => m.Parameters.Count == a.Args.Length &&
                                 m.Parameters.Select((p, i) => p.ParameterType.FullName == CecilName(a.Args[i])).All(x => x)).ToList();
            if (c.Count == 0) throw new InvalidOperationException("method " + a.Method + " not found on " + td.FullName);
            if (c.Count > 1) throw new InvalidOperationException("method " + a.Method + " is ambiguous on " + td.FullName + "; give the argument types");
            if (!c[0].HasBody) throw new InvalidOperationException(c[0].FullName + " has no IL body");
            return c[0];
        }

        // ------------------------------------------------------------------ IL
        static void ExpandShortBranches(MethodBody body)
        {
            foreach (var i in body.Instructions)
            {
                switch (i.OpCode.Code)
                {
                    case Code.Br_S: i.OpCode = OpCodes.Br; break;
                    case Code.Brfalse_S: i.OpCode = OpCodes.Brfalse; break;
                    case Code.Brtrue_S: i.OpCode = OpCodes.Brtrue; break;
                    case Code.Beq_S: i.OpCode = OpCodes.Beq; break;
                    case Code.Bge_S: i.OpCode = OpCodes.Bge; break;
                    case Code.Bgt_S: i.OpCode = OpCodes.Bgt; break;
                    case Code.Ble_S: i.OpCode = OpCodes.Ble; break;
                    case Code.Blt_S: i.OpCode = OpCodes.Blt; break;
                    case Code.Bne_Un_S: i.OpCode = OpCodes.Bne_Un; break;
                    case Code.Bge_Un_S: i.OpCode = OpCodes.Bge_Un; break;
                    case Code.Bgt_Un_S: i.OpCode = OpCodes.Bgt_Un; break;
                    case Code.Ble_Un_S: i.OpCode = OpCodes.Ble_Un; break;
                    case Code.Blt_Un_S: i.OpCode = OpCodes.Blt_Un; break;
                    case Code.Leave_S: i.OpCode = OpCodes.Leave; break;
                }
            }
        }

        static List<Instruction> LoadArgs(ModuleDefinition mod, MethodDefinition m, MethodInfo hook, VariableDefinition result)
        {
            var list = new List<Instruction>();
            foreach (var hp in hook.GetParameters())
            {
                Type ht = hp.ParameterType;
                bool hRef = ht.IsByRef;
                Type hElem = hRef ? ht.GetElementType() : ht;
                if (hp.Name == "__instance")
                {
                    if (m.IsStatic) throw new InvalidOperationException(hook.Name + ": __instance on a static method");
                    if (m.DeclaringType.IsValueType || hRef) throw new NotSupportedException(hook.Name + ": __instance must be a by-value reference type");
                    CheckType(hook, hp.Name, hElem, m.DeclaringType);
                    list.Add(Instruction.Create(OpCodes.Ldarg_0));
                    continue;
                }
                if (hp.Name == "__result")
                {
                    if (result == null) throw new InvalidOperationException(hook.Name + ": __result on a void method");
                    CheckType(hook, hp.Name, hElem, m.ReturnType);
                    list.Add(Instruction.Create(hRef ? OpCodes.Ldloca : OpCodes.Ldloc, result));
                    continue;
                }
                if (hp.Name.StartsWith("__")) throw new NotSupportedException(hook.Name + ": special argument " + hp.Name + " is not supported");
                var tp = m.Parameters.FirstOrDefault(p => p.Name == hp.Name);
                if (tp == null) throw new InvalidOperationException(hook.Name + ": " + m.Name + " has no parameter named " + hp.Name);
                bool tRef = tp.ParameterType.IsByReference;
                TypeReference tElem = tRef ? ((ByReferenceType)tp.ParameterType).ElementType : tp.ParameterType;
                CheckType(hook, hp.Name, hElem, tElem);
                if (hRef == tRef) list.Add(Instruction.Create(OpCodes.Ldarg, tp));
                else if (hRef) list.Add(Instruction.Create(OpCodes.Ldarga, tp));
                else
                {
                    list.Add(Instruction.Create(OpCodes.Ldarg, tp));
                    list.Add(Instruction.Create(OpCodes.Ldobj, mod.ImportReference(tElem)));
                }
            }
            return list;
        }

        static void CheckType(MethodInfo hook, string name, Type hookType, TypeReference target)
        {
            if (hookType.IsGenericType || target.IsGenericInstance) return; // names are formatted differently; trust the compiler
            if (hookType == typeof(object) && !target.IsValueType) return;
            string tn = target.FullName;
            if (CecilName(hookType) != tn) throw new InvalidOperationException(hook.DeclaringType.Name + "." + hook.Name + ": " + name + " is " + hookType.FullName + " but the target has " + tn);
        }

        /// <summary>
        /// Layout after weaving:
        ///   [run = true; run &amp;= Prefix(...)...; if (!run) goto epilogue]
        ///   original body, every ret replaced in place by (stloc result; br epilogue) or (br epilogue)
        ///   epilogue: Postfix(...)...; [ldloc result]; ret
        /// Rets are rewritten in place (OpCode/Operand swapped on the same Instruction object), so branches and
        /// exception-handler bounds that pointed at a ret still point at the right place.
        /// </summary>
        static void WeaveMethod(ModuleDefinition mod, MethodDefinition m, List<HookInfo> hooks)
        {
            var body = m.Body;
            var il = body.GetILProcessor();
            ExpandShortBranches(body);
            body.InitLocals = true;

            bool hasResult = m.ReturnType.MetadataType != MetadataType.Void;
            VariableDefinition result = null;
            if (hasResult) { result = new VariableDefinition(m.ReturnType); body.Variables.Add(result); }

            var prefixes = hooks.Where(h => h.Prefix != null).Select(h => h.Prefix).ToList();
            var postfixes = hooks.Where(h => h.Postfix != null).Select(h => h.Postfix).ToList();

            // epilogue
            var epi = new List<Instruction>();
            foreach (var post in postfixes)
            {
                epi.AddRange(LoadArgs(mod, m, post, result));
                epi.Add(Instruction.Create(OpCodes.Call, ImportHook(mod, post)));
            }
            if (hasResult) epi.Add(Instruction.Create(OpCodes.Ldloc, result));
            epi.Add(Instruction.Create(OpCodes.Ret));
            Instruction epiStart = epi[0];

            // rets -> epilogue
            var rets = body.Instructions.Where(i => i.OpCode.Code == Code.Ret).ToList();
            foreach (var r in rets)
            {
                if (hasResult)
                {
                    r.OpCode = OpCodes.Stloc;
                    r.Operand = result;
                    il.InsertAfter(r, Instruction.Create(OpCodes.Br, epiStart));
                }
                else
                {
                    r.OpCode = OpCodes.Br;
                    r.Operand = epiStart;
                }
            }
            // A protected region that runs to the end of the method must not swallow the epilogue.
            foreach (var eh in body.ExceptionHandlers)
            {
                if (eh.TryEnd == null) eh.TryEnd = epiStart;
                if (eh.HandlerEnd == null) eh.HandlerEnd = epiStart;
            }
            foreach (var i in epi) il.Append(i);

            // prefix
            if (prefixes.Count > 0)
            {
                Instruction first = body.Instructions[0];
                var pre = new List<Instruction>();
                VariableDefinition run = null;
                if (prefixes.Any(p => p.ReturnType == typeof(bool)))
                {
                    run = new VariableDefinition(mod.TypeSystem.Boolean);
                    body.Variables.Add(run);
                    pre.Add(Instruction.Create(OpCodes.Ldc_I4_1));
                    pre.Add(Instruction.Create(OpCodes.Stloc, run));
                }
                foreach (var p in prefixes)
                {
                    pre.AddRange(LoadArgs(mod, m, p, result));
                    pre.Add(Instruction.Create(OpCodes.Call, ImportHook(mod, p)));
                    if (p.ReturnType == typeof(bool))
                    {
                        pre.Add(Instruction.Create(OpCodes.Ldloc, run));
                        pre.Add(Instruction.Create(OpCodes.And));
                        pre.Add(Instruction.Create(OpCodes.Stloc, run));
                    }
                }
                if (run != null)
                {
                    pre.Add(Instruction.Create(OpCodes.Ldloc, run));
                    pre.Add(Instruction.Create(OpCodes.Brfalse, epiStart));
                }
                foreach (var i in pre) il.InsertBefore(first, i);
            }
        }
    }
}
