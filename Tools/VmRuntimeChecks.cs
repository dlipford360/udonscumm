// Host-only checks: compile the actual VM with minimal Unity/Udon type stubs.
using System;
using System.IO;
using System.Reflection;
namespace UnityEngine
{
    public class TextAsset { public byte[] bytes; public TextAsset(byte[] data) { bytes = data; } }
    public class HideInInspector : Attribute { }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
    }
}
namespace UdonSharp
{
    public class UdonSharpBehaviour { }
    public enum BehaviourSyncMode { None }
    public class UdonBehaviourSyncMode : Attribute { public UdonBehaviourSyncMode(BehaviourSyncMode mode) { } }
}
class VmRuntimeChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Set(ScummV5Vm vm, string field, object value)
    {
        typeof(ScummV5Vm).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(vm, value);
    }
    static ScummV5Vm Machine()
    {
        var vm = new ScummV5Vm();
        vm.globalScripts = new UnityEngine.TextAsset[200];
        return vm;
    }
    static void Script(ScummV5Vm vm, int id, params byte[] bytes)
    {
        vm.globalScripts[id] = new UnityEngine.TextAsset(bytes);
    }
    static void Run(ScummV5Vm vm, int budget)
    {
        vm.StartEntry(1, 0);
        for (int frame = 0; frame < 10000 && !vm.finished && vm.fault == ""; frame++) vm.Advance(budget, 4);
        Check(vm.fault == "", vm.fault + "\n" + vm.DescribeState());
        Check(vm.finished, "Did not finish\n" + vm.DescribeState());
    }
    static int GetArr(ScummV5Vm vm, string field, int i)
    {
        return ((int[])typeof(ScummV5Vm).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(vm))[i];
    }
    static int CountMode1Verbs(ScummV5Vm vm)
    {
        int count = 0;
        for (int i = 1; i < 100; i++)
            if (vm.GetVerbMode(i) == 1 && vm.GetVerbId(i) != 0) count++;
        return count;
    }
    static void Regression(string generated, int budget)
    {
        var vm = Machine();
        // Enable override skips its jump; disabling consumes only the flag.
        Script(vm, 1, 0x58, 1, 0x18, 0, 0, 0x58, 0, 0x1a, 100, 0, 42, 0, 0);
        vm.ResetMachine();
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 42, "Override operand decoding");

        // Short controls may directly precede NUL; word controls may contain NUL.
        foreach (byte control in new byte[] { 1, 2, 3, 8 })
        {
            vm = Machine();
            Script(vm, 1, 0x14, 255, 15, 255, 7, 34, 0, 255, control, 0,
                0x1a, 100, 0, 42, 0, 0);
            vm.ResetMachine();
            Run(vm, budget);
            Check(vm.ReadGlobal(100) == 42, "Text control alignment " + control);
        }

        vm = Machine();
        Script(vm, 1, 0x14, 7, 0x01, 3, 0x00, 0xA0, 0, 0xA0, 0, 0x0F,
            72, 105, 0, 0x00);
        vm.ResetMachine();
        Run(vm, budget);
        Check(vm.lastPrintText == "Hi" && vm.lastPrintActor == 7 && vm.printSequence == 1,
            "Print capture must decode actor text, got '" + vm.lastPrintText + "'");

        // Sentence runner: doSentence queues the sentence script with args.
        vm = Machine();
        Script(vm, 1, 0x19, 5, 10, 0, 0, 0, 0x00);
        Script(vm, 2, 0x9A, 100, 0, 0x00, 0x40, 0x00);
        vm.ResetMachine();
        vm.globals[33] = 2;
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 5, "Sentence script must run with verb arg");

        // Chain: B dies, C runs with the arg, A resumes.
        vm = Machine();
        Script(vm, 1, 0x0A, 0x02, 0xFF, 0x1A, 91, 0, 1, 0, 0x00);
        Script(vm, 2, 0x42, 0x03, 0x00, 0x07, 0x00, 0xFF, 0x1A, 92, 0, 1, 0, 0x00);
        Script(vm, 3, 0x9A, 90, 0, 0x00, 0x40, 0x00);
        vm.ResetMachine();
        Run(vm, budget);
        Check(vm.fault == "", "Chain must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(90) == 7, "Chained script must receive the arg");
        Check(vm.ReadGlobal(91) == 1, "Chain caller must resume");
        Check(vm.ReadGlobal(92) == 0, "Chained-from script must die");

        // Walkbox query: actor standing in box 1.
        vm = Machine();
        Script(vm, 1, 0x7B, 100, 0, 1, 0x00);
        vm.ResetMachine();
        int[] wb = new int[64 * 11];
        wb[11] = 0; wb[12] = 0; wb[13] = 100; wb[14] = 0;
        wb[15] = 100; wb[16] = 100; wb[17] = 0; wb[18] = 100;
        wb[19] = 0; wb[20] = 0; wb[21] = 255;
        Set(vm, "walkboxData", wb);
        Set(vm, "numWalkboxes", 2);
        Set(vm, "currentRoom", 10);
        int[] ar = new int[48]; ar[1] = 10;
        int[] ax = new int[48]; ax[1] = 50;
        int[] ay = new int[48]; ay[1] = 50;
        Set(vm, "actorRoom", ar);
        Set(vm, "actorX", ax);
        Set(vm, "actorY", ay);
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 1, "Walkbox query must return box 1");

        // Distance: Chebyshev between two actors.
        vm = Machine();
        Script(vm, 1, 0x34, 100, 0, 1, 0, 2, 0, 0x00);
        vm.ResetMachine();
        Set(vm, "currentRoom", 10);
        ar = new int[48]; ar[1] = 10; ar[2] = 10;
        ax = new int[48]; ax[1] = 100; ax[2] = 140;
        ay = new int[48]; ay[1] = 100; ay[2] = 115;
        Set(vm, "actorRoom", ar);
        Set(vm, "actorX", ax);
        Set(vm, "actorY", ay);
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 40, "getDist must be Chebyshev, got " + vm.ReadGlobal(100));

        // Place actor at an object's walk point, with (240,120) fallback.
        vm = Machine();
        Script(vm, 1, 0x0E, 1, 50, 0, 0x00);
        vm.ResetMachine();
        Set(vm, "currentRoom", 10);
        int[] pids = new int[200]; pids[1] = 50;
        int[] pwx = new int[200]; pwx[1] = 200;
        int[] pwy = new int[200]; pwy[1] = 100;
        Set(vm, "objSlotId", pids);
        Set(vm, "objSlotWalkX", pwx);
        Set(vm, "objSlotWalkY", pwy);
        int[] powners = new int[1024]; powners[50] = 15;
        Set(vm, "objectOwner", powners);
        Run(vm, budget);
        Check(GetArr(vm, "actorX", 1) == 200 && GetArr(vm, "actorY", 1) == 100,
            "putActorAtObject must use the walk point");

        // setState operand masks: 0x87 takes a direct state byte.
        vm = Machine();
        Script(vm, 1, 0x87, 134, 0, 7, 0x00);
        vm.ResetMachine();
        vm.globals[134] = 60;
        Run(vm, budget);
        Check(GetArr(vm, "objectState", 60) == 7, "0x87 state must be a direct byte");

        // Matrix ops: set flags/scale, then rebuild the itinerary.
        vm = Machine();
        Script(vm, 1, 0x30, 0x01, 0x01, 0x80, 0x30, 0x02, 0x00, 0x90, 0x30, 0x04, 0x00);
        vm.ResetMachine();
        int[] wb2 = new int[64 * 11];
        wb2[11] = 0; wb2[12] = 0; wb2[13] = 100; wb2[14] = 0;
        wb2[15] = 100; wb2[16] = 100; wb2[17] = 0; wb2[18] = 100;
        wb2[19] = 0; wb2[20] = 0; wb2[21] = 255;
        wb2[22] = 200; wb2[23] = 200; wb2[24] = 300; wb2[25] = 200;
        wb2[26] = 300; wb2[27] = 300; wb2[28] = 200; wb2[29] = 300;
        wb2[30] = 0; wb2[31] = 0; wb2[32] = 255;
        Set(vm, "walkboxData", wb2);
        Set(vm, "numWalkboxes", 2);
        Run(vm, budget);
        Check(vm.fault == "", "Matrix ops must not fault, got " + vm.fault);
        Check(GetArr(vm, "walkboxData", 20) == 0x80, "Box flags must stick");
        Check(GetArr(vm, "walkboxData", 10) == 0x90, "Box scale must stick");

        // String width: 8px per printable char.
        vm = Machine();
        Script(vm, 1, 0x67, 100, 0, 5, 0x00);
        vm.ResetMachine();
        byte[][] strings = new byte[64][];
        strings[5] = new byte[] { 72, 105, 33, 0 };
        Set(vm, "stringTable", strings);
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 24, "String width must be 3*8, got " + vm.ReadGlobal(100));

        // Camera: 0xD2 snaps past half a screen, then follows only while walking.
        vm = Machine();
        Script(vm, 1, 0xD2, 1, 0, 0x00);
        vm.ResetMachine();
        vm.globals[1] = 1;
        Set(vm, "currentRoom", 10);
        Set(vm, "roomWidth", 640);
        ar = new int[48]; ar[1] = 10;
        ax = new int[48]; ax[1] = 500;
        ay = new int[48]; ay[1] = 100;
        Set(vm, "actorRoom", ar);
        Set(vm, "actorX", ax);
        Set(vm, "actorY", ay);
        Run(vm, budget);
        Check(vm.GetCameraScrollX() == 320, "Far follow must snap, got " + vm.GetCameraScrollX());
        vm = Machine();
        Script(vm, 1, 0xD2, 1, 0, 0x1E, 1, 244, 1, 100, 0, 0x00);
        vm.ResetMachine();
        vm.globals[1] = 1;
        Set(vm, "currentRoom", 10);
        Set(vm, "roomWidth", 640);
        ar = new int[48]; ar[1] = 10;
        ax = new int[48]; ax[1] = 200;
        ay = new int[48]; ay[1] = 100;
        Set(vm, "actorRoom", ar);
        Set(vm, "actorX", ax);
        Set(vm, "actorY", ay);
        vm.StartEntry(1, 0);
        for (int f = 0; f < 30 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "", "Follow walk must not fault, got " + vm.fault);
        Check(vm.GetCameraScrollX() > 0, "Camera must track the walking actor");

        // Pickup: object leaves the room, gains owner/state/class, inventory
        // script runs with arg 1.
        vm = Machine();
        Script(vm, 1, 0x25, 109, 0, 0, 0x00);
        Script(vm, 4, 0x9A, 100, 0, 0x00, 0x40, 0x00);
        vm.ResetMachine();
        vm.globals[1] = 1;
        vm.globals[33] = 0;
        vm.globals[34] = 4;
        int[] ids = new int[200]; ids[1] = 109;
        int[] xs = new int[200]; int[] ys = new int[200];
        int[] ws = new int[200]; int[] hs = new int[200];
        ws[1] = 8; hs[1] = 8;
        Set(vm, "objSlotId", ids);
        Set(vm, "objSlotX", xs);
        Set(vm, "objSlotY", ys);
        Set(vm, "objSlotW", ws);
        Set(vm, "objSlotH", hs);
        int[] owners = new int[1024]; owners[109] = 15;
        Set(vm, "objectOwner", owners);
        Run(vm, budget);
        Check(vm.fault == "", "Pickup must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(100) == 1, "Inventory script must run with arg 1");

        // Wait-for-message yields (pc rewinds) while HAVE_MSG set, then stops.
        vm = Machine();
        Script(vm, 1, 0xAE, 0x02, 0x00);
        vm.ResetMachine();
        vm.globals[3] = 255;
        vm.StartEntry(1, 0);
        for (int f = 0; f < 4; f++) vm.Advance(10, 4);
        Check(vm.fault == "", "Wait must not fault");
        vm.globals[3] = 0;
        Run(vm, budget);
        Check(vm.finished, "Wait must fall through once HAVE_MSG clears");

        // Walk-to-object moves the actor to the object's walk point.
        vm = Machine();
        Script(vm, 1, 0x36, 1, 109, 0, 0x00);
        vm.ResetMachine();
        int[] wids = new int[200]; wids[1] = 109;
        int[] wxs = new int[200]; int[] wys = new int[200]; int[] wds = new int[200];
        Set(vm, "objSlotId", wids);
        Set(vm, "objSlotX", wxs);
        Set(vm, "objSlotY", wys);
        Set(vm, "objSlotW", new int[200]);
        Set(vm, "objSlotH", new int[200]);
        int[] wwx = new int[200]; wwx[1] = 50;
        int[] wwy = new int[200]; wwy[1] = 60;
        wds[1] = 2;
        Set(vm, "objSlotWalkX", wwx);
        Set(vm, "objSlotWalkY", wwy);
        Set(vm, "objSlotDir", wds);
        Run(vm, budget);
        Check(vm.fault == "", "Walk-to-object must not fault, got " + vm.fault);
        Check(GetArr(vm, "actorX", 1) == 50 && GetArr(vm, "actorY", 1) == 60,
            "Actor must reach the object walk point");

        // Inventory count counts carried objects owned by the given owner.
        vm = Machine();
        Script(vm, 1, 0xB1, 100, 0, 1, 0, 0x00);
        vm.ResetMachine();
        vm.globals[1] = 1;
        int[] cinv = new int[80]; cinv[0] = 109; cinv[1] = 110;
        Set(vm, "inventoryIds", cinv);
        int[] cown = new int[1024]; cown[109] = 1; cown[110] = 2;
        Set(vm, "objectOwner", cown);
        Run(vm, budget);
        Check(vm.fault == "", "Inventory count must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(100) == 1, "Inventory count must be 1");

        // Restarting the machine must clear verbs instead of doubling them.
        // (SaveRestoreVerbs tags the verb first, so a stale table would miss
        // on re-creation exactly like the real boot did.)
        vm = Machine();
        Script(vm, 1, 0x7A, 5, 0x09, 0x06, 0xFF, 0xAB, 0x01, 5, 5, 9, 0x00);
        vm.ResetMachine();
        Run(vm, budget);
        Check(CountMode1Verbs(vm) == 1, "One verb expected after setup");
        vm.ResetMachine();
        Run(vm, budget);
        Check(vm.fault == "", "Re-run must not fault, got " + vm.fault);
        Check(CountMode1Verbs(vm) == 1, "Restart must not duplicate verbs");

        // Object accessors expose live slot geometry and visibility for the GPU layer.
        vm = Machine();
        int[] oids = new int[200]; oids[1] = 109;
        int[] oxs = new int[200]; int[] oys = new int[200];
        int[] ows = new int[200]; int[] ohs = new int[200];
        oxs[1] = 48; oys[1] = 24; ows[1] = 224; ohs[1] = 120;
        int[] ostates = new int[1024]; ostates[109] = 1;
        Set(vm, "objSlotId", oids);
        Set(vm, "objSlotX", oxs);
        Set(vm, "objSlotY", oys);
        Set(vm, "objSlotW", ows);
        Set(vm, "objSlotH", ohs);
        Set(vm, "numObjectSlots", 1);
        Set(vm, "objectState", ostates);
        Check(vm.GetObjectSlotCount() == 1, "One object slot expected");
        Check(vm.GetObjectSlotId(1) == 109, "Object slot id");
        Check(vm.GetObjectSlotX(1) == 48 && vm.GetObjectSlotY(1) == 24, "Object slot pos");
        Check(vm.GetObjectSlotW(1) == 224 && vm.GetObjectSlotH(1) == 120, "Object slot size");
        Check(vm.GetObjectState(109) == 1, "Object state visible");
        Check(vm.GetObjectState(110) == 0, "Object state hidden");
        Check(vm.GetObjectSlotId(0) == 0 && vm.GetObjectSlotId(200) == 0
            && vm.GetObjectSlotX(200) == 0 && vm.GetObjectSlotY(200) == 0
            && vm.GetObjectSlotW(200) == 0 && vm.GetObjectSlotH(200) == 0
            && vm.GetObjectSlotId(201) == 0, "Slot bounds");
        Check(vm.GetObjectState(-1) == 0 && vm.GetObjectState(1024) == 0, "State bounds");

        // Actor accessors expose room/position/visibility/costume/facing for the GPU layer.
        vm = Machine();
        int[] arooms = new int[48]; int[] axs = new int[48]; int[] ays = new int[48];
        int[] avis = new int[48]; int[] acost = new int[48]; int[] aface = new int[48];
        arooms[2] = 38; axs[2] = 107; ays[2] = 106; avis[2] = 1; acost[2] = 33; aface[2] = 0;
        Set(vm, "actorRoom", arooms);
        Set(vm, "actorX", axs);
        Set(vm, "actorY", ays);
        Set(vm, "actorVisible", avis);
        Set(vm, "actorCostume", acost);
        Set(vm, "actorFacing", aface);
        Check(vm.GetActorRoom(2) == 38, "Actor room");
        Check(vm.GetActorX(2) == 107 && vm.GetActorY(2) == 106, "Actor pos");
        Check(vm.GetActorVisible(2) == 1, "Actor visible");
        Check(vm.GetActorCostume(2) == 33, "Actor costume");
        Check(vm.GetActorFacing(2) == 0, "Actor facing");
        Check(vm.GetActorRoom(0) == -1 && vm.GetActorRoom(48) == -1, "Actor bounds");

        // Find-inventory returns the idx-th carried object of the owner.
        vm = Machine();
        Script(vm, 1, 0xFD, 100, 0, 1, 0, 2, 0, 0x00);
        vm.ResetMachine();
        vm.globals[1] = 1;
        vm.globals[2] = 2;
        int[] finv = new int[80]; finv[0] = 109; finv[1] = 110; finv[2] = 111;
        Set(vm, "inventoryIds", finv);
        int[] fown = new int[1024]; fown[109] = 1; fown[110] = 1; fown[111] = 2;
        Set(vm, "objectOwner", fown);
        Run(vm, budget);
        Check(vm.fault == "", "Find-inventory must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(100) == 110, "Find-inventory must return 2nd owned item");

        // Verb hit-testing follows the engine order (topmost wins).
        vm = Machine();
        Script(vm, 1, 0x00);
        vm.ResetMachine();
        int[] vid = new int[100]; vid[1] = 4; vid[2] = 9;
        int[] vmode = new int[100]; vmode[1] = 1; vmode[2] = 1;
        int[] vsave = new int[100];
        int[] vtype = new int[100];
        int[] vleft = new int[100]; vleft[1] = 22; vleft[2] = 72;
        int[] vtop = new int[100]; vtop[1] = 155; vtop[2] = 155;
        int[] vcenter = new int[100]; vcenter[1] = 1; vcenter[2] = 1;
        byte[][] vnames = new byte[100][];
        vnames[1] = new byte[64]; vnames[1][0] = 71; vnames[1][1] = 105; vnames[1][2] = 118; vnames[1][3] = 101;
        vnames[2] = new byte[64];
        byte[] pickup = new byte[] { 80, 105, 99, 107, 32, 117, 112 };
        for (int i = 0; i < pickup.Length; i++) vnames[2][i] = pickup[i];
        Set(vm, "verbId", vid);
        Set(vm, "verbMode", vmode);
        Set(vm, "verbSaveId", vsave);
        Set(vm, "verbType", vtype);
        Set(vm, "verbLeft", vleft);
        Set(vm, "verbTop", vtop);
        Set(vm, "verbCenter", vcenter);
        Set(vm, "verbNames", vnames);
        int[] vlen = new int[100]; vlen[1] = 4; vlen[2] = 7;
        Set(vm, "verbNameLen", vlen);
        Check(vm.FindVerbAt(30, 160) == 1, "Click on Give must hit verb slot 1");
        Check(vm.FindVerbAt(60, 160) == 2, "Click on Pick up must hit verb slot 2");
        Check(vm.FindVerbAt(200, 160) == 0, "Click on empty area must miss");
        // Dialogue choices (120+) sit over the bottom bar and must win
        // hit-testing despite lower slot numbers (room 81/28 overlap).
        {
            var vm2 = Machine();
            Script(vm2, 1, 0x00);
            vm2.ResetMachine();
            int[] cids = new int[100]; cids[1] = 122; cids[10] = 4;
            int[] cmodes = new int[100]; cmodes[1] = 1; cmodes[10] = 1;
            int[] csave = new int[100];
            int[] ctype = new int[100];
            int[] cleft = new int[100]; cleft[1] = 2; cleft[10] = 22;
            int[] ctop = new int[100]; ctop[1] = 153; ctop[10] = 155;
            int[] ccenter = new int[100];
            byte[][] cnames = new byte[100][];
            cnames[1] = new byte[64];
            byte[] pirate = new byte[] { 73, 32, 119, 97, 110, 116 };
            for (int i = 0; i < pirate.Length; i++) cnames[1][i] = pirate[i];
            cnames[10] = new byte[64];
            byte[] give = new byte[] { 71, 105, 118, 101 };
            for (int i = 0; i < give.Length; i++) cnames[10][i] = give[i];
            int[] clen = new int[100]; clen[1] = 6; clen[10] = 4;
            Set(vm2, "verbId", cids);
            Set(vm2, "verbMode", cmodes);
            Set(vm2, "verbSaveId", csave);
            Set(vm2, "verbType", ctype);
            Set(vm2, "verbLeft", cleft);
            Set(vm2, "verbTop", ctop);
            Set(vm2, "verbCenter", ccenter);
            Set(vm2, "verbNames", cnames);
            Set(vm2, "verbNameLen", clen);
            Check(vm2.FindVerbAt(30, 157) == 1, "Overlapped choice must beat Give despite lower slot");
        }

        // Hover sentence helpers: active verb, verb names, object hit + OBNA.
        vm = Machine();
        Script(vm, 1, 0x00);
        vm.ResetMachine();
        vm.globals[107] = 7;
        Check(vm.GetActiveVerbId() == 7, "Active verb must read game var 107");
        Check(vm.GetVerbNameById(7) == "Use", "Known verb id needs English fallback without slots");
        int[] hvids = new int[100]; hvids[1] = 7;
        int[] hvmode = new int[100]; hvmode[1] = 1;
        int[] hvsave = new int[100];
        int[] hvtype = new int[100];
        int[] hvleft = new int[100]; int[] hvtop = new int[100]; int[] hvcenter = new int[100];
        byte[][] hvnames = new byte[100][];
        hvnames[1] = new byte[64]; hvnames[1][0] = 85; hvnames[1][1] = 115; hvnames[1][2] = 101;
        int[] hvlen = new int[100]; hvlen[1] = 3;
        Set(vm, "verbId", hvids);
        Set(vm, "verbMode", hvmode);
        Set(vm, "verbSaveId", hvsave);
        Set(vm, "verbType", hvtype);
        Set(vm, "verbLeft", hvleft);
        Set(vm, "verbTop", hvtop);
        Set(vm, "verbCenter", hvcenter);
        Set(vm, "verbNames", hvnames);
        Set(vm, "verbNameLen", hvlen);
        Check(vm.GetVerbNameById(7) == "Use", "Verb name lookup must hit visible slot");
        Check(vm.GetVerbNameById(11) == "Walk to", "Walk-to needs English fallback");
        int[] hoids = new int[200]; hoids[1] = 448;
        int[] hoxs = new int[200]; hoxs[1] = 376;
        int[] hoys = new int[200]; hoys[1] = 80;
        int[] hows = new int[200]; hows[1] = 16;
        int[] hohs = new int[200]; hohs[1] = 32;
        int[] hobcd = new int[200]; hobcd[1] = 0;
        int[] hown = new int[1024]; hown[448] = 15;
        Set(vm, "objSlotId", hoids);
        Set(vm, "objSlotX", hoxs);
        Set(vm, "objSlotY", hoys);
        Set(vm, "objSlotW", hows);
        Set(vm, "objSlotH", hohs);
        Set(vm, "objSlotFlags", new int[200]);
        Set(vm, "objSlotParent", new int[200]);
        Set(vm, "numObjectSlots", 1);
        Set(vm, "objSlotObcd", hobcd);
        Set(vm, "objectOwner", hown);
        Set(vm, "objectState", new int[1024]);
        Check(vm.GetObjectAt(384, 96) == 448, "Hover hit must find door rect");
        Check(vm.GetObjectAt(10, 10) == 0, "Hover miss must return 0");
        Check(vm.GetObjectName(448) == "", "Missing OBNA must be empty, not fault");
        byte[] roomBytes = new byte[] { 79, 66, 67, 68, 0, 0, 0, 21, 79, 66, 78, 65, 0, 0, 0, 13, 100, 111, 111, 114, 0 };
        Set(vm, "roomBytes", roomBytes);
        Check(vm.GetObjectName(448) == "door", "OBNA door name must decode, got " + vm.GetObjectName(448));
        vm.globals[107] = 0;
        Check(vm.GetActiveVerbId() == 11, "Active verb must fall back to Walk-to");

        // Face-actor turns toward the object; missing objects are silent.
        vm = Machine();
        Script(vm, 1, 0x09, 1, 109, 0, 0x00);
        vm.ResetMachine();
        int[] fids = new int[200]; fids[1] = 109;
        int[] fxs = new int[200]; fxs[1] = 100;
        Set(vm, "objSlotId", fids);
        Set(vm, "objSlotX", fxs);
        Set(vm, "objSlotY", new int[200]);
        Set(vm, "objSlotW", new int[200]);
        Set(vm, "objSlotH", new int[200]);
        Run(vm, budget);
        Check(vm.fault == "", "Face-actor must not fault, got " + vm.fault);
        Check(GetArr(vm, "actorFacing", 1) == 90, "Actor must face right toward object");

        vm = Machine();
        Script(vm, 1, 0x14, 255, 15, 255, 7, 34);
        vm.ResetMachine();
        vm.StartEntry(1, 0);
        vm.Advance(500, 4);
        Check(vm.fault == "String operand outside script", "Truncated text operand must fault");

        vm = Machine();
        Script(vm, 1, 0x72, 0, 0x9a, 102, 0, 100, 0, 0);
        Script(vm, 2, 0x1a, 100, 0, 1, 0, 0);
        Script(vm, 3, 0x9a, 101, 0, 100, 0, 0x1a, 100, 0, 3, 0, 0);
        vm.ResetMachine();
        vm.globals[30] = 2;
        vm.globals[31] = 3;
        Set(vm, "roomBytes", new byte[] { 0x1a, 100, 0, 2, 0, 0 });
        Set(vm, "roomExcdOffs", 0);
        Run(vm, budget);
        Check(vm.ReadGlobal(101) == 2 && vm.ReadGlobal(102) == 3, "Exit hooks/caller order");

        // Input script launches the verb script with click args when idle.
        vm = Machine();
        Script(vm, 1, 0x00);
        Script(vm, 32, 0x1a, 100, 0, 1, 0, 0);
        vm.ResetMachine();
        vm.globals[32] = 32;
        vm.StartEntry(1, 0);
        vm.Advance(10, 4);
        vm.RunInputScript(2, 0, 1);
        Check(vm.fault == "", "Input script launch must not fault");
        for (int f = 0; f < 1000 && !vm.finished && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.ReadGlobal(100) == 1, "Verb script must run with click args");
        // panCameraTo sets the destination and drops the followed actor.
        vm = Machine();
        Script(vm, 1, 0x12, 0x00, 0x01, 0x00);
        vm.ResetMachine();
        Set(vm, "cameraFollowActor", 5);
        Run(vm, budget);
        Check(vm.fault == "", "panCameraTo must not fault, got " + vm.fault);
        Check((int)typeof(ScummV5Vm).GetField("cameraDestX", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vm) == 256,
            "panCameraTo must set dest 256");
        Check((int)typeof(ScummV5Vm).GetField("cameraFollowActor", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vm) == 0,
            "panCameraTo must drop follow");

        // Object tables: states live in their own array; classes are LE u32.
        vm = Machine();
        vm.globalTables = new UnityEngine.TextAsset(File.ReadAllBytes(Path.Combine(generated, "MonkeyIsland", "object-tables.bytes")));
        vm.ResetMachine();
        Check(vm.fault == "", "Tables must load, got " + vm.fault);
        Check(GetArr(vm, "objectState", 730) == 1, "obj 730 state must be 1");
        Check(GetArr(vm, "objectState", 428) == 0, "obj 428 state must be 0");
        Check(GetArr(vm, "objectClass", 442) == 0x20, "obj 442 class must be 0x20");
        Check(GetArr(vm, "objectClass", 285) == 0x4020, "obj 285 class must be 0x4020");

        // ifClassOfIs jumps when the class MATCHES (plain: present, 0x80: absent).
        vm = Machine();
        Script(vm, 1, 0x9D, 10, 0, 0x01, 0x06, 0x00, 0xFF, 0x08, 0x00,
            0x1A, 100, 0, 7, 0, 0x18, 0x05, 0x00,
            0x1A, 100, 0, 9, 0, 0x00);
        vm.ResetMachine();
        vm.globals[10] = 10;
        ((int[])typeof(ScummV5Vm).GetField("objectClass", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vm))[10] = 0x20;
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 9, "ifClassOfIs must jump when class present");
        vm = Machine();
        Script(vm, 1, 0x9D, 10, 0, 0x01, 0x06, 0x00, 0xFF, 0x08, 0x00,
            0x1A, 100, 0, 7, 0, 0x18, 0x05, 0x00,
            0x1A, 100, 0, 9, 0, 0x00);
        vm.ResetMachine();
        vm.globals[10] = 10;
        Run(vm, budget);
        Check(vm.ReadGlobal(100) == 7, "ifClassOfIs must fall through when class absent");

        // Object 0 is valid: hover with no object runs ifClassOfIs(0, ...)
        // and must branch, not desync into Invalid actor 141 (room-28 23:168).
        vm = Machine();
        Script(vm, 1, 0x1D, 0, 0, 0x01, 0x06, 0x00, 0xFF, 0x08, 0x00,
            0x1A, 100, 0, 7, 0, 0x18, 0x05, 0x00,
            0x1A, 100, 0, 9, 0, 0x00);
        vm.ResetMachine();
        Run(vm, budget);
        Check(vm.fault == "", "ifClassOfIs(0) must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(100) == 7, "ifClassOfIs(0) absent must fall through, got " + vm.ReadGlobal(100));
        vm = Machine();
        Script(vm, 1, 0x1D, 0, 0, 0x01, 0x06, 0x00, 0xFF, 0x08, 0x00,
            0x1A, 100, 0, 7, 0, 0x18, 0x05, 0x00,
            0x1A, 100, 0, 9, 0, 0x00);
        vm.ResetMachine();
        ((int[])typeof(ScummV5Vm).GetField("objectClass", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vm))[0] = 0x20;
        Run(vm, budget);
        Check(vm.fault == "", "ifClassOfIs(0) present must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(100) == 9, "ifClassOfIs(0) present must jump, got " + vm.ReadGlobal(100));

        // Abort: a click during an armed cutscene override resumes instead
        // of dispatching the verb script, and reports VAR_OVERRIDE.
        vm = Machine();
        Script(vm, 1, 0x40, 0xFF, 0x58, 0x01, 0x18, 0x00, 0x00, 0x1A, 100, 0, 7, 0, 0x00);
        Script(vm, 4, 0x1A, 101, 0, 42, 0, 0x00);
        Script(vm, 23, 0x2E, 0xFF, 0xFF, 0x7F, 0x00);
        vm.ResetMachine();
        vm.globals[35] = 0;
        vm.globals[32] = 4;
        Run(vm, budget);
        Check(vm.fault == "", "Cutscene setup must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(100) == 7, "Setup script must run to completion");
        vm.RunInputScript(2, 0, 1);
        Check(vm.fault == "", "Abort click must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(5) == 1, "Abort must set VAR_OVERRIDE");
        Check(vm.ReadGlobal(101) == 0, "Abort must consume the click (verb script must not run)");

        // Verb clicks must not abort an armed override: dialogue choices
        // (area 1, e.g. room 81 verbs 120+) dispatch to the verb script
        // instead of skipping. Scene clicks (area 2) still abort (room-32).
        vm = Machine();
        Script(vm, 1, 0x40, 0xFF, 0x58, 0x01, 0x18, 0x00, 0x00, 0x1A, 100, 0, 7, 0, 0x00);
        Script(vm, 4, 0x1A, 101, 0, 42, 0, 0x00);
        Script(vm, 23, 0x2E, 0xFF, 0xFF, 0x7F, 0x00);
        vm.ResetMachine();
        vm.globals[35] = 0;
        vm.globals[32] = 4;
        Run(vm, budget);
        Check(vm.fault == "", "Cutscene setup must not fault, got " + vm.fault);
        vm.RunInputScript(1, 120, 1);
        Check(vm.fault == "", "Verb click must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(5) == 0, "Verb click must not abort (VAR_OVERRIDE stays 0)");
        Check(vm.ReadGlobal(101) == 0, "Verb click with input off must not dispatch yet");
        vm.globals[53] = 1;
        vm.RunInputScript(1, 120, 1);
        for (int f = 0; f < 100 && vm.fault == "" && vm.ReadGlobal(101) == 0; f++) vm.Advance(budget, 4);
        Check(vm.ReadGlobal(101) == 42, "Verb click must dispatch to verb script when input is on");

        // Heal must preserve 14 while an override is armed (dialogue menus
        // use 14 with input on), but restore 14->4 once consumed (room-32).
        // Dialogue choices also report via g194 so late menus (g32=4) unblock.
        vm = Machine();
        Script(vm, 1, 0x40, 0xFF, 0x58, 0x01, 0x18, 0x00, 0x00, 0x00);
        Script(vm, 4, 0x1A, 101, 0, 42, 0, 0x00);
        vm.ResetMachine();
        vm.globals[35] = 0;
        vm.globals[32] = 14;
        vm.globals[53] = 1;
        Run(vm, budget);
        vm.RunInputScript(1, 120, 1);
        Check(vm.ReadGlobal(32) == 14, "Heal must preserve 14 while override is armed, got " + vm.ReadGlobal(32));
        Check(vm.ReadGlobal(194) == 120, "Choice click must report via g194, got " + vm.ReadGlobal(194));

        // Leaving chat hides dialogue choices (120-199) so they don't linger
        // over the bottom bar; choice clicks keep the rest for the next menu.
        vm = Machine();
        Script(vm, 4, 0x00);
        vm.ResetMachine();
        vm.globals[32] = 4;
        int[] hv = new int[100]; hv[1] = 120; hv[2] = 121;
        Set(vm, "verbId", hv);
        int[] hm = new int[100]; hm[1] = 1; hm[2] = 1;
        Set(vm, "verbMode", hm);
        vm.RunInputScript(1, 122, 1);
        Check(vm.ReadGlobal(194) == 122, "Choice must report via g194");
        Check(GetArr(vm, "verbMode", 1) == 1 && GetArr(vm, "verbMode", 2) == 1, "Choice click must keep other choices");
        vm.RunInputScript(2, 0, 1);
        Check(vm.fault == "", "Scene click must not fault, got " + vm.fault);
        Check(GetArr(vm, "verbMode", 1) == 0 && GetArr(vm, "verbMode", 2) == 0, "Walk-away must hide dialogue choices");

        // EndCutscene restores the verb script slot and the hover tracker.
        vm = Machine();
        Script(vm, 1, 0xC0, 0x00);
        Script(vm, 23, 0x2E, 0xFF, 0xFF, 0x7F, 0x00);
        vm.ResetMachine();
        vm.globals[32] = 14;
        vm.globals[36] = 0;
        vm.StartEntry(1, 0);
        for (int f = 0; f < 100 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "", "EndCutscene must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(32) == 4, "EndCutscene must restore the verb script");
        bool has23 = false;
        for (int i = 1; i < 80; i++)
            if (GetArr(vm, "states", i) != 0 && GetArr(vm, "scriptIds", i) == 23) has23 = true;
        Check(has23, "EndCutscene must relaunch the missing hover tracker");

        // Stale cutscene flags must not survive slot reuse: a script that
        // opened a cutscene then died must not fault a later StopScript.
        // (Room-32's entry spiel dies with its cutscene open; the slot later
        // hosts the hover tracker, and stopping it faulted the pirate talk.)
        vm = Machine();
        Script(vm, 1, 0x40, 0xFF, 0x00);
        Script(vm, 23, 0x2E, 0xFF, 0xFF, 0x7F, 0x00);
        Script(vm, 2, 0x62, 23, 0x00);
        vm.ResetMachine();
        vm.StartEntry(1, 0);
        for (int f = 0; f < 100 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "", "Cutscene setup must not fault, got " + vm.fault);
        typeof(ScummV5Vm).GetMethod("LaunchInner", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(vm, new object[] { 23, false, false, 0, 0 });
        for (int f = 0; f < 10 && vm.fault == ""; f++) vm.Advance(budget, 4);
        typeof(ScummV5Vm).GetMethod("LaunchInner", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(vm, new object[] { 2, false, false, 0, 0 });
        for (int f = 0; f < 100 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "", "StopScript after slot reuse must not fault, got " + vm.fault);

        // o5_lights sets VAR_CURRENT_LIGHTS (9); reset defaults it on.
        vm = Machine();
        Script(vm, 1, 0x70, 0x05, 0x00, 0x00, 0x00);
        vm.ResetMachine();
        Check(vm.ReadGlobal(9) == 11, "Reset must default lights on");
        Run(vm, budget);
        Check(vm.fault == "", "Lights must not fault, got " + vm.fault);
        Check(vm.ReadGlobal(9) == 5, "Lights c==0 must set VAR_CURRENT_LIGHTS");
        Console.WriteLine("PASS runtime regressions budget=" + budget);
    }
    static void WalkScale(string generated, int budget)
    {
        var vm = Machine();
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
            vm.Advance(budget, 4);
        Check(vm.fault == "", "WalkScale boot must not fault, got " + vm.fault);
        vm.EnterRoom(38);
        // Let entry scripts and room hooks settle before touching actors.
        for (int f = 0; f < 200000 && vm.completedTicks < 1700 && !vm.finished && vm.fault == ""; f++)
            vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.ReadRoom() == 38, "Lookout entry must not fault, got " + vm.fault);
        // Boot pre-places ego in the lookout (putActorInRoom at script 1:5859);
        // forced entry must not move it (putActor keeps _room).
        Check(vm.GetActorRoom(1) == 38, "Boot leaves ego pre-placed in lookout, got " + vm.GetActorRoom(1));
        vm.BeginLookoutWalkTest();
        Check(vm.GetActorRoom(1) == 38 && vm.GetActorVisible(1) == 1, "Diagnostic must place visible ego in displayed room");
        Check(vm.GetActorX(1) == 312 && vm.GetActorY(1) == 73 && vm.GetActorMoving(1) == 1,
            "Diagnostic must start on distant path with a pending walk");
        Check(vm.GetActorRenderScaleX(1) == 216, "Distant scale uses signed truncation, not floor");
        int routeTicks = vm.completedTicks;
        bool sawIntermediate = false;
        for (int f = 0; f < 100000 && vm.GetActorMoving(1) != 0 && vm.fault == ""
            && vm.completedTicks < routeTicks + 500; f++)
        {
            vm.Advance(budget, 4);
            if (vm.GetActorX(1) < 312 && vm.GetActorX(1) > 154) sawIntermediate = true;
        }
        Check(vm.fault == "" && sawIntermediate && vm.GetActorMoving(1) == 0, "Full Lookout route must progress and finish");
        Check(vm.GetActorX(1) == 154 && vm.GetActorY(1) == 106 && vm.GetActorRenderScaleX(1) == 241,
            "Ego must arrive at lookout at its box scale");
        Console.WriteLine("PASS full Lookout ego route budget=" + budget + " ticks=" + (vm.completedTicks - routeTicks));
        int[] frames = new int[48];
        frames[2] = 2;
        Set(vm, "actorWalkFrame", frames);
        frames = new int[48];
        frames[2] = 3;
        Set(vm, "actorStandFrame", frames);
        Check(vm.GetActorWalkFrame(2) == 2 && vm.GetActorStandFrame(2) == 3, "Frame getters must track engine state");

        // Room 38 scale slot 1: y=124 -> 255, y=106 -> 241.
        int[] rooms = new int[48]; int[] ixs = new int[48]; int[] iys = new int[48];
        int[] ivis = new int[48]; int[] ign = new int[48];
        rooms[2] = 38; ixs[2] = 154; iys[2] = 124; ivis[2] = 1; ign[2] = 0;
        Set(vm, "actorRoom", rooms);
        Set(vm, "actorX", ixs);
        Set(vm, "actorY", iys);
        Set(vm, "actorVisible", ivis);
        Set(vm, "actorIgnoreBoxes", ign);
        Check(vm.GetActorRenderScaleX(2) == 255, "Scale at y=124 must be 255, got " + vm.GetActorRenderScaleX(2));
        iys[2] = 106;
        Set(vm, "actorY", iys);
        Check(vm.GetActorRenderScaleX(2) == 241, "Scale at y=106 must be 241, got " + vm.GetActorRenderScaleX(2));
        Check(vm.GetActorRenderScaleX(2) == vm.GetActorRenderScaleY(2), "Box scale must apply to both axes");

        // Clip plane: walkbox mask wins; force-clip overrides; NeverClip/ignore suppress.
        rooms[2] = 38; ixs[2] = 154; iys[2] = 106; ivis[2] = 1; ign[2] = 0;
        Set(vm, "actorRoom", rooms);
        Set(vm, "actorX", ixs);
        Set(vm, "actorY", iys);
        Set(vm, "actorVisible", ivis);
        Set(vm, "actorIgnoreBoxes", ign);
        Check(vm.GetActorClipPlane(2) == 1, "Walkbox mask must select plane 1, got " + vm.GetActorClipPlane(2));
        int[] force = new int[48]; force[2] = 2;
        Set(vm, "actorForceClip", force);
        Check(vm.GetActorClipPlane(2) == 2, "Force clip must override walkbox mask");
        force = new int[48];
        Set(vm, "actorForceClip", force);
        ign[2] = 1;
        Set(vm, "actorIgnoreBoxes", ign);
        Check(vm.GetActorClipPlane(2) == 0, "Ignore-boxes actors must not clip");

        // Overhead talk sits above the head with the scaled -80 offset.
        ign[2] = 0;
        Set(vm, "actorIgnoreBoxes", ign);
        ixs[2] = 312; iys[2] = 73;
        Set(vm, "actorX", ixs);
        Set(vm, "actorY", iys);
        Check(vm.GetTalkOverhead() == 1, "Dialogue keeps the armed overhead slot");
        Check(vm.GetTalkX(2) == 240 && vm.GetTalkY(2) == 1,
            "Distant talk clamps to screen, got " + vm.GetTalkX(2) + "," + vm.GetTalkY(2));
        ixs[2] = 154; iys[2] = 106;
        Set(vm, "actorX", ixs);
        Set(vm, "actorY", iys);
        Check(vm.GetTalkX(2) == 154 && vm.GetTalkY(2) == 29,
            "Lookout talk sits above head, got " + vm.GetTalkX(2) + "," + vm.GetTalkY(2));

        // Positioned print reports fixed coordinates; the bare follow-up
        // inherits the whole slot (overhead, position, color).
        vm = Machine();
        Script(vm, 1, 0x14, 7, 0x01, 3, 0x00, 0xA0, 0, 0xA0, 0, 0x0F, 72, 105, 0,
            0x14, 7, 0x0F, 89, 111, 0, 0x2E, 0x60, 0xEA, 0x00, 0x00);
        vm.ResetMachine();
        vm.StartEntry(1, 0);
        for (int f = 0; f < 1000 && vm.printSequence < 2 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.lastPrintText == "Yo", "Both prints must run, got '" + vm.lastPrintText + "'");
        Check(vm.GetTalkActor() == 7 && vm.GetTalkTimer() == 68, "Second print refreshes a live timer");
        Check(vm.GetTalkOverhead() == 0 && vm.GetTalkX(9) == 160 && vm.GetTalkY(9) == 160,
            "Bare print must inherit positioned slot");
        byte[] palette = new byte[768];
        palette[9] = 255;
        Set(vm, "roomPalette", palette);
        Set(vm, "actorTalkColor", new int[48]);
        UnityEngine.Color color = vm.GetTalkColor(7);
        Check(color.r == 1 && color.g == 0 && color.b == 0, "Print color override must persist via room palette");
        for (int f = 0; f < 10000 && vm.GetTalkTimer() > 0 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.GetTalkTimer() == 0 && vm.GetTalkActor() == -1 && vm.ReadGlobal(3) == 0,
            "Talk timer must expire and clear HAVE_MSG");
        // Overhead arming sticks: bare follow-ups stay overhead.
        vm = Machine();
        Script(vm, 1, 0x14, 7, 0x07, 0x0F, 72, 105, 0, 0x14, 7, 0x0F, 89, 111, 0, 0x00);
        vm.ResetMachine();
        Run(vm, budget);
        Check(vm.GetTalkOverhead() == 1, "Armed overhead must persist to bare prints");

        // Costume anims step once per tick for in-room actors, gated by speed.
        vm = Machine();
        Script(vm, 1, 0x2E, 0x60, 0xEA, 0x00, 0x00);
        vm.ResetMachine();
        Set(vm, "currentRoom", 38);
        int[] aro = new int[48]; int[] acost = new int[48]; int[] aspd = new int[48];
        aro[3] = 38; acost[3] = 48; aspd[3] = 0;
        Set(vm, "actorRoom", aro);
        Set(vm, "actorCostume", acost);
        Set(vm, "actorAnimSpeed", aspd);
        int[] aani = new int[48]; aani[3] = 4;
        Set(vm, "actorAnim", aani);
        Check(vm.GetActorAnim(3) == 4, "Actor anim must report the running costume command");
        vm.StartEntry(1, 0);
        for (int f = 0; f < 100 && vm.GetActorAnimStep(3) < 3 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.GetActorAnimStep(3) == 3,
            "Speed-0 anims must step every tick, got " + vm.GetActorAnimStep(3));
        aspd[3] = 2;
        Set(vm, "actorAnimSpeed", aspd);
        Set(vm, "actorAnimProgress", new int[48]);
        Set(vm, "actorAnimStep", new int[48]);
        for (int f = 0; f < 100 && vm.GetActorAnimStep(3) < 2 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.GetActorAnimStep(3) == 2,
            "Speed-2 anims must step every second tick, got " + vm.GetActorAnimStep(3));
        aro[4] = 0; acost[4] = 48;
        Set(vm, "actorRoom", aro);
        Set(vm, "actorCostume", acost);
        for (int f = 0; f < 10 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.GetActorAnimStep(4) == 0, "Off-room actors must not step");

        // Straight walk (ignore boxes): gradual 8px/tick steps, never a teleport.
        // (Restore the room table the anim checks above replaced.)
        Set(vm, "actorRoom", rooms);
        ign[2] = 1;
        Set(vm, "actorIgnoreBoxes", ign);
        ixs[2] = 100; iys[2] = 100;
        Set(vm, "actorX", ixs);
        Set(vm, "actorY", iys);
        int[] scx = new int[48]; int[] scy = new int[48];
        for (int i = 0; i < 48; i++) { scx[i] = 255; scy[i] = 255; }
        Set(vm, "actorScaleX", scx);
        Set(vm, "actorScaleY", scy);
        vm.StartActorWalk(2, 140, 100, -1);
        Check(vm.GetActorMoving(2) == 1, "Walk must schedule movement");
        Check(vm.GetActorX(2) == 100 && vm.GetActorY(2) == 100, "Walk must not teleport");
        int tick0 = vm.completedTicks;
        for (int f = 0; f < 1000 && vm.completedTicks <= tick0 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.completedTicks > tick0, "Tick must complete");
        Check(vm.GetActorX(2) == 108 && vm.GetActorY(2) == 100, "Walk must step 8px/tick, got " + vm.GetActorX(2));
        Check(vm.GetActorMoving(2) == 1, "Walk must still be moving");
        tick0 = vm.completedTicks;
        for (int f = 0; f < 10000 && vm.GetActorMoving(2) == 1 && vm.fault == "" && vm.completedTicks < tick0 + 50; f++) vm.Advance(budget, 4);
        Check(vm.GetActorX(2) == 140 && vm.GetActorY(2) == 100 && vm.GetActorMoving(2) == 0, "Walk must arrive");

        // Walkbox walk: follow the room 38 itinerary to the destination.
        ign[2] = 0;
        Set(vm, "actorIgnoreBoxes", ign);
        ixs[2] = 154; iys[2] = 106;
        Set(vm, "actorX", ixs);
        Set(vm, "actorY", iys);
        vm.StartActorWalk(2, 244, 106, -1);
        Check(vm.GetActorMoving(2) == 1, "Walkbox walk must schedule movement");
        tick0 = vm.completedTicks;
        for (int f = 0; f < 100000 && vm.GetActorMoving(2) == 1 && vm.fault == "" && vm.completedTicks < tick0 + 500; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.GetActorMoving(2) == 0, "Walkbox walk must finish, got " + vm.fault);
        Check(vm.GetActorX(2) == 244 && vm.GetActorY(2) == 106, "Walkbox walk must arrive, got " + vm.GetActorX(2) + "," + vm.GetActorY(2));

        // Camera pans 8px/tick toward its destination and reports scrolled offsets.
        // SO_ROOM_SCREEN sets vertical bounds, never the 320x200 hardware size:
        // after it runs, a full pan must still scroll exactly 320, not 480.
        vm = Machine();
        Script(vm, 1, 0x33, 0x03, 0, 0, 0xC8, 0, 0x32, 0xE0, 0x01, 0x2E, 0x60, 0xEA, 0x00, 0x00);
        vm.ResetMachine();
        Set(vm, "currentRoom", 10);
        Set(vm, "roomWidth", 640);
        vm.globals[27] = 9;
        vm.StartEntry(1, 0);
        for (int f = 0; f < 200 && vm.ReadGlobal(2) != 480 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.ReadGlobal(2) == 480, "Camera must pan to 480, got " + vm.ReadGlobal(2));
        Check(vm.GetCameraScrollX() == 320, "Room 10 right half scrolls 320, got " + vm.GetCameraScrollX());
        Set(vm, "cameraDestX", 160);
        int cam0 = vm.completedTicks;
        for (int f = 0; f < 1000 && vm.ReadGlobal(2) != 160 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "" && vm.completedTicks - cam0 == 40, "Pan back must take 40 ticks");
        Check(vm.GetCameraScrollX() == 0, "Vista half must not scroll");

        // RoomOps scale slot programs slot 2 (index 1) for later box lookups.
        vm = Machine();
        Script(vm, 1, 0x33, 0x07, 200, 100, 0x00, 150, 80, 0x00, 3, 0x00);
        vm.ResetMachine();
        Run(vm, budget);
        Check(GetArr(vm, "scaleSlots", 4) == 200 && GetArr(vm, "scaleSlots", 5) == 100
            && GetArr(vm, "scaleSlots", 6) == 150 && GetArr(vm, "scaleSlots", 7) == 80, "RoomOps scale slot");
        Console.WriteLine("PASS walk and scale budget=" + budget);
    }
    static string Boot(string generated, int budget)
    {
        var vm = Machine();
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
        for (int frame = 0; frame < 200000 && vm.completedTicks < 1500 && !vm.finished && vm.fault == ""; frame++)
            vm.Advance(budget, 4);
        string result = "ticks=" + vm.completedTicks + " instructions=" + vm.instructionsExecuted + " fault=" + vm.fault
            + " prints=" + vm.printSequence + " lastPrint=[" + vm.lastPrintActor + "] " + vm.lastPrintText + "\n" + vm.DescribeState();
        Console.WriteLine("Boot budget=" + budget + " " + result);
        Check(vm.fault == "", result);
        return result;
    }
    static string Gameplay(string generated, int budget)
    {
        var vm = Machine();
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
        for (int frame = 0; frame < 200000 && vm.completedTicks < 1500 && !vm.finished && vm.fault == ""; frame++)
            vm.Advance(budget, 4);
        Check(vm.fault == "", "Boot to wait must not fault");
        int printsBefore = vm.printSequence;
        vm.EnterRoom(38);
        for (int frame = 0; frame < 200000 && vm.completedTicks < 1700 && !vm.finished && vm.fault == ""; frame++)
            vm.Advance(budget, 4);
        Check(vm.fault == "", "Lookout entry must not fault, got " + vm.fault);
        vm.SetGlobal(20, 272);
        vm.SetGlobal(21, 128);
        vm.RunInputScript(2, 0, 1);
        for (int frame = 0; frame < 200000 && vm.completedTicks < 2400 && !vm.finished && vm.fault == ""; frame++)
            vm.Advance(budget, 4);
        string result = "ticks=" + vm.completedTicks + " instructions=" + vm.instructionsExecuted + " fault=" + vm.fault
            + " prints=" + vm.printSequence + " lastPrint=[" + vm.lastPrintActor + "] " + vm.lastPrintText + "\n" + vm.DescribeState();
        Console.WriteLine("Gameplay budget=" + budget + " " + result);
        Check(vm.fault == "", result);
        Check(vm.printSequence > printsBefore, "Lookout click must produce dialogue");
        return result;
    }
    static void Main(string[] args)
    {
        Regression(args.Length > 0 ? args[0] : "Assets/Scumm/Generated", 1);
        Regression(args.Length > 0 ? args[0] : "Assets/Scumm/Generated", 500);
        if (args.Length > 0) Check(Boot(args[0], 1) == Boot(args[0], 500), "Boot differs across budgets");
        if (args.Length > 0) Check(Gameplay(args[0], 1) == Gameplay(args[0], 500), "Gameplay differs across budgets");
        if (args.Length > 0) WalkScale(args[0], 1);
        if (args.Length > 0) WalkScale(args[0], 500);
        if (args.Length > 0) RoomTransition(args[0], 1);
        if (args.Length > 0) RoomTransition(args[0], 500);
    }
    static void RoomTransition(string generated, int budget)
    {
        // loadRoomWithEgo enters at the object's walk point (room 33's west
        // door), holds the entry view, then walks to the target with the
        // camera following. Placement uses the destination room even though
        // the install runs in a later scheduler phase.
        var vm = Machine();
        Script(vm, 1, 0x24, 0xAA, 0x01, 33, 0x5A, 0x01, 0x85, 0x00, 0x00);
        vm.roomBlobs = new UnityEngine.TextAsset[256];
        foreach (string file in Directory.GetFiles(Path.Combine(generated, "RoomBlobs"), "rt-room-*.bytes"))
        {
            int id = int.Parse(Path.GetFileNameWithoutExtension(file).Substring(8));
            if (id < 256) vm.roomBlobs[id] = new UnityEngine.TextAsset(File.ReadAllBytes(file));
        }
        vm.globalTables = new UnityEngine.TextAsset(File.ReadAllBytes(Path.Combine(generated, "MonkeyIsland", "object-tables.bytes")));
        vm.ResetMachine();
        vm.globals[1] = 1;
        vm.StartEntry(1, 0);
        for (int f = 0; f < 100000 && vm.ReadRoom() != 33 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "", "Room transition must not fault, got " + vm.fault);
        Check(vm.ReadRoom() == 33, "Must arrive in room 33");
        Check(vm.GetActorRoom(1) == 33, "Ego must land in the new room, got " + vm.GetActorRoom(1));
        Check(vm.GetCameraScrollX() == 0, "Entry view must hold, got scroll " + vm.GetCameraScrollX());
        Check(vm.GetActorMoving(1) != 0, "Ego must walk in from the door");
        for (int f = 0; f < 100000 && vm.GetActorMoving(1) != 0 && vm.fault == ""; f++) vm.Advance(budget, 4);
        Check(vm.fault == "", "Arrival walk must not fault, got " + vm.fault);
        Check(vm.GetActorX(1) == 346 && vm.GetActorY(1) == 133, "Ego must reach the target");
        Check(vm.GetCameraScrollX() > 100, "Camera must follow east, got " + vm.GetCameraScrollX());
        Console.WriteLine("PASS room transition budget=" + budget);
    }
}
