// Headless probe: run the REAL ScummV5Vm + REAL ScummActorLayer against real
// game data and real baked actor tables, then print exactly what the layer
// tells the material to draw. Uses host-only Unity type stubs (not Udon).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
namespace UnityEngine
{
    public class TextAsset { public byte[] bytes; public TextAsset(byte[] data) { bytes = data; } }
    public class HideInInspector : Attribute { }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero { get { return new Vector4(0, 0, 0, 0); } }
        public static bool operator ==(Vector4 a, Vector4 b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }
        public static bool operator !=(Vector4 a, Vector4 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector4 v && this == v; }
        public override int GetHashCode() { return (int)(x + y + z + w); }
        public override string ToString() { return "(" + x + "," + y + "," + z + "," + w + ")"; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
    }
    public class Texture2D { public string name = "stub"; }
    public class Material
    {
        public Dictionary<string, float> floats = new Dictionary<string, float>();
        public Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>();
        public Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        public void SetFloat(string name, float v) { floats[name] = v; }
        public void SetVector(string name, Vector4 v) { vectors[name] = v; }
        public void SetTexture(string name, Texture2D t) { textures[name] = t; }
    }
    public class Debug { public static void Log(object o) { } }
}
namespace UdonSharp
{
    public class UdonSharpBehaviour { }
    public enum BehaviourSyncMode { None }
    public class UdonBehaviourSyncModeAttribute : Attribute { public UdonBehaviourSyncModeAttribute(BehaviourSyncMode m) { } }
}
class ActorLayerProbe
{
    static void Set(ScummV5Vm vm, string field, object value)
    {
        typeof(ScummV5Vm).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(vm, value);
    }
    static int GetArr(ScummV5Vm vm, string field, int i)
    {
        return ((int[])typeof(ScummV5Vm).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vm))[i];
    }
    static void Main(string[] args)
    {
        string generated = args[0];
        var vm = new ScummV5Vm();
        vm.globalScripts = new UnityEngine.TextAsset[200];
        foreach (string file in Directory.GetFiles(Path.Combine(generated, "MonkeyIsland", "scripts"), "*.bytes"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            int id;
            if (name.StartsWith("global-") && int.TryParse(name.Substring(7), out id) && id < 200)
                vm.globalScripts[id] = new UnityEngine.TextAsset(File.ReadAllBytes(file));
        }
        vm.roomBlobs = new UnityEngine.TextAsset[256];
        foreach (string file in Directory.GetFiles(Path.Combine(generated, "RoomBlobs"), "rt-room-*.bytes"))
        {
            int id = int.Parse(Path.GetFileNameWithoutExtension(file).Substring(8));
            vm.roomBlobs[id] = new UnityEngine.TextAsset(File.ReadAllBytes(file));
        }
        vm.globalTables = new UnityEngine.TextAsset(File.ReadAllBytes(Path.Combine(generated, "MonkeyIsland", "object-tables.bytes")));
        vm.ResetMachine();
        vm.StartEntry(1, 0);
        for (int f = 0; f < 200000 && vm.completedTicks < 1500 && !vm.finished && vm.fault == ""; f++)
            vm.Advance(1, 4);
        if (vm.fault != "") { Console.WriteLine("FAULT boot: " + vm.fault); return; }
        // Install room 33 exactly like the engine does on transition.
        vm.EnterRoom(33);
        for (int f = 0; f < 200000 && vm.completedTicks < 1700 && !vm.finished && vm.fault == ""; f++)
            vm.Advance(1, 4);
        Console.WriteLine("room=" + vm.ReadRoom() + " fault=" + vm.fault + " scroll=" + vm.GetCameraScrollX());
        // Place ego the way loadRoomWithEgo does on the 96->33 transition.
        int ego = vm.ReadGlobal(1);
        Console.WriteLine("ego=" + ego);
        // Drive the real placement path: put ego via reflection like the engine would.
        int[] ar = new int[48]; for (int i = 0; i < 48; i++) ar[i] = -1;
        int[] ax = new int[48]; int[] ay = new int[48]; int[] av = new int[48];
        int[] ac = new int[48]; int[] af = new int[48];
        ar[1] = 33; ax[1] = 346; ay[1] = 133; av[1] = 1; ac[1] = 1; af[1] = 180;
        Set(vm, "actorRoom", ar); Set(vm, "actorX", ax); Set(vm, "actorY", ay);
        Set(vm, "actorVisible", av); Set(vm, "actorCostume", ac); Set(vm, "actorFacing", af);
        Console.WriteLine("ego room=" + vm.GetActorRoom(1) + " vis=" + vm.GetActorVisible(1)
            + " pos=" + vm.GetActorX(1) + "," + vm.GetActorY(1)
            + " costume=" + vm.GetActorCostume(1) + " facing=" + vm.GetActorFacing(1)
            + " moving=" + vm.GetActorMoving(1));
        Console.WriteLine("clip=" + vm.GetActorClipPlane(1)
            + " scale=" + vm.GetActorRenderScaleX(1) + "," + vm.GetActorRenderScaleY(1));
        // Wire the layer from the real baked tables (mirrors WireActorSprites).
        string actorDir = Path.Combine(generated, "ActorSprites");
        var atlases = new List<UnityEngine.Texture2D>();
        var tableCostume = new List<int>(); var tableKey = new List<int>();
        var tableAtlas = new List<int>(); var tableUV = new List<UnityEngine.Vector4>();
        var tableBox = new List<UnityEngine.Vector4>();
        var seqCostume = new List<int>(); var seqKey = new List<int>();
        var seqBase = new List<int>(); var seqLen = new List<int>();
        foreach (string dir in Directory.GetDirectories(actorDir))
        {
            int id;
            if (!int.TryParse(Path.GetFileName(dir), out id)) continue;
            string tablePath = Path.Combine(dir, "table.txt");
            if (!File.Exists(tablePath)) continue;
            int atlasIndex = atlases.Count;
            atlases.Add(new UnityEngine.Texture2D());
            string seqPath = Path.Combine(dir, "seq.txt");
            if (File.Exists(seqPath))
            {
                foreach (string line in File.ReadAllLines(seqPath))
                {
                    string[] parts = line.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int sf, sfr, ss, sl;
                    if (parts.Length != 4 || !int.TryParse(parts[0], out sf) || !int.TryParse(parts[1], out sfr)
                        || !int.TryParse(parts[2], out ss) || !int.TryParse(parts[3], out sl) || sl < 2) continue;
                    seqCostume.Add(id); seqKey.Add(sf * 8 + sfr); seqBase.Add(ss); seqLen.Add(sl);
                }
            }
            foreach (string line in File.ReadAllLines(tablePath))
            {
                string[] parts = line.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int facing, frame, w, h, anx, any;
                float u0, v0, u1, v1;
                if (parts.Length != 10 || !int.TryParse(parts[0], out facing) || !int.TryParse(parts[1], out frame)) continue;
                if (!float.TryParse(parts[2], out u0) || !float.TryParse(parts[3], out v0)
                    || !float.TryParse(parts[4], out u1) || !float.TryParse(parts[5], out v1)) continue;
                if (!int.TryParse(parts[6], out w) || !int.TryParse(parts[7], out h)
                    || !int.TryParse(parts[8], out anx) || !int.TryParse(parts[9], out any)) continue;
                tableCostume.Add(id); tableKey.Add(facing * 8 + frame); tableAtlas.Add(atlasIndex);
                tableUV.Add(new UnityEngine.Vector4(u0, v0, u1, v1));
                tableBox.Add(new UnityEngine.Vector4(w, h, anx, any));
            }
        }
        Console.WriteLine("poses=" + tableCostume.Count + " seqs=" + seqCostume.Count);
        var layer = new ScummActorLayer();
        layer.vm = vm;
        layer.screenMaterial = new UnityEngine.Material();
        layer.atlases = atlases.ToArray();
        layer.tableCostume = tableCostume.ToArray();
        layer.tableKey = tableKey.ToArray();
        layer.tableAtlas = tableAtlas.ToArray();
        layer.tableUV = tableUV.ToArray();
        layer.tableBox = tableBox.ToArray();
        layer.seqCostume = seqCostume.ToArray();
        layer.seqKey = seqKey.ToArray();
        layer.seqBase = seqBase.ToArray();
        layer.seqLen = seqLen.ToArray();
        var update = typeof(ScummActorLayer).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        update.Invoke(layer, null);
        var mat = layer.screenMaterial;
        // Explain the decision path for ego.
        var findPose = typeof(ScummActorLayer).GetMethod("FindPose", BindingFlags.NonPublic | BindingFlags.Instance);
        var findSeq = typeof(ScummActorLayer).GetMethod("FindSeq", BindingFlags.NonPublic | BindingFlags.Instance);
        int pfacing = vm.GetActorFacing(1);
        int pa = pfacing % 360; if (pa < 0) pa += 360;
        int pod = (pa >= 71 && pa <= 109) ? 1 : (pa > 109 && pa <= 251) ? 2 : (pa > 251 && pa <= 289) ? 0 : 3;
        int prw = vm.GetActorWalkFrame(1), prs = vm.GetActorStandFrame(1);
        int pwf = (prw < 1 || prw > 7) ? 2 : prw, psf = (prs < 1 || prs > 7) ? 3 : prs;
        int ptalkActor = vm.ReadGlobal(25);
        int pframe = vm.GetActorMoving(1) != 0 ? pwf : (ptalkActor == 1 ? 4 : psf);
        int panim = vm.GetActorAnim(1), pstep = vm.GetActorAnimStep(1);
        Console.WriteLine("ego facing=" + pfacing + " olddir=" + pod + " rawWalk=" + prw + " rawStand=" + prs
            + " anim=" + panim + " step=" + pstep + " talkActor=" + ptalkActor + " frame=" + pframe);
        int pkey = pod * 8 + pframe;
        int pseq = (int)findSeq.Invoke(layer, new object[] { 1, pkey });
        Console.WriteLine("key=" + pkey + " seq=" + pseq);
        if (pseq >= 0)
        {
            int[] bases = (int[])typeof(ScummActorLayer).GetField("seqBase").GetValue(layer);
            int[] lens = (int[])typeof(ScummActorLayer).GetField("seqLen").GetValue(layer);
            pframe = bases[pseq] + pstep % lens[pseq];
            pkey = pod * 8 + pframe;
            Console.WriteLine("seqFrame=" + pframe + " seqKey=" + pkey);
        }
        Console.WriteLine("pose=" + findPose.Invoke(layer, new object[] { 1, pkey })
            + " poseStand=" + findPose.Invoke(layer, new object[] { 1, pod * 8 + psf }));
        Console.WriteLine("ActCount=" + (mat.floats.ContainsKey("_ActCount") ? mat.floats["_ActCount"] : -999));
        for (int i = 0; i < 8; i++)
        {
            string r = "_Act" + i, u = "_ActUV" + i, c = "_ActClip" + i, t = "_ActAtlas" + i;
            if (mat.vectors.ContainsKey(r))
                Console.WriteLine(r + "=" + mat.vectors[r] + " " + u + "="
                    + (mat.vectors.ContainsKey(u) ? mat.vectors[u].ToString() : "?")
                    + " " + c + "=" + (mat.floats.ContainsKey(c) ? mat.floats[c].ToString() : "?")
                    + " atlas=" + (mat.textures.ContainsKey(t) && mat.textures[t] != null ? "ok" : "MISSING"));
        }
        Console.WriteLine("PROBE-DONE fault=" + vm.fault);
    }
}
