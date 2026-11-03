using UdonSharp;
using UnityEngine;

// Global-script VM foundation. Engine-service opcodes fail explicitly.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummV5Vm : UdonSharpBehaviour
{
    public TextAsset[] globalScripts;
    public TextAsset[] roomBlobs;
    public TextAsset globalTables;
    public int globalCount = 800;
    public int bitCount = 2048;
    [HideInInspector] public int[] globals;
    [HideInInspector] public bool[] bits;
    [HideInInspector] public int instructionsExecuted;
    [HideInInspector] public int completedTicks;
    [HideInInspector] public bool tickInProgress;
    [HideInInspector] public bool finished;
    [HideInInspector] public string fault = "";
    [HideInInspector] public int faultScript = -1;
    [HideInInspector] public int faultOffset = -1;
    [HideInInspector] public int faultOpcode = -1;

    private byte[][] codeCache;
    private int[] scriptIds;
    private int[] offsets;
    private int[] states; // 0 dead, 1 delayed, 2 runnable
    private int[] delays;
    private int[] generations;
    private bool[] executed;
    private int[] locals;
    private int[] returnSlots;
    private int[] returnGenerations;
    private int[] returnRooms;
    private int[] returnPhases;
    private int[] arguments;
    private int stackDepth;
    private int activeSlot = -1;
    private int scanSlot;
    private int opcode;
    private int instructionOffset;
    private int instructionScript;
    private int cursorState;
    private int userPut;
    private int currentCursor;
    private int currentCharset;
    private int[] charsetColors = new int[16];
    private int lastSoundRequest;
    private int soundRunning;
    private int talkDelay;
    private int talkingActor = -1;
    private int lastPrintLength;
    private int lastPrintOverhead = 1;
    private int lastPrintX;
    private int lastPrintY;
    private int lastPrintColor = -1;
    private int lastStringOp = -1;
    private int lastStringId = -1;
    private byte[][] stringTable;
    private int cameraMinX;
    private int cameraMaxX;
    private int screenWidth = 320;
    private int screenHeight = 200;
    private int screenTop;
    private int screenBottom = 200;
    private int shakeOn;
    private int switchRoomEffect;
    private int saveLoadFlag;
    private int loadStringId = -1;
    private int lastResourceOp;
    private int lastResourceId;
    private int sentenceVerb;
    private int sentenceObjectA;
    private int sentenceObjectB;
    private int sentenceNum;
    private int randomState = 1;
    private int[] palChangeLog = new int[256 * 4];
    private int currentRoom = 0;
    private int roomResource = -1;
    private int roomWidth;
    private int roomHeight;
    private byte[] roomBytes;
    private byte[] roomPalette;
    private int roomTrans = 255;
    private int numWalkboxes;
    private int[] walkboxData = new int[64 * 11];
    private byte[] walkMatrix;
    private int roomEncdOffs = -1;
    private int roomExcdOffs = -1;
    private int[] localScriptIds = new int[64];
    private int[] localScriptOffs = new int[64];
    private int[] localScriptSizes = new int[64];
    private int numLocalScripts;
    private int cameraX = 160;
    private int cameraY = 100;
    private int cameraDestX = 160;
    private int cameraFollowActor;
    private int pendingPlaceEgo = -1;
    private int pendingPlaceObj;
    private int pendingPlaceX;
    private int pendingPlaceY;
    private int[] objectOwner = new int[1024];
    private int[] objectState = new int[1024];
    private int[] objectRoom = new int[1024];
    private int[] objectClass = new int[1024];
    private int[] objectParent = new int[1024];
    private int numObjectsTracked;
    private int[] objSlotId = new int[200];
    private int[] objSlotX = new int[200];
    private int[] objSlotY = new int[200];
    private int[] objSlotW = new int[200];
    private int[] objSlotH = new int[200];
    private int[] objSlotWalkX = new int[200];
    private int[] objSlotWalkY = new int[200];
    private int[] objSlotDir = new int[200];
    private int[] objSlotFlags = new int[200];
    private int[] objSlotParent = new int[200];
    private int[] objSlotObcd = new int[200];
    private int[] inventoryIds = new int[80];
    private int numObjectSlots;
    private int[] actorRoom = new int[48];
    private int[] actorX = new int[48];
    private int[] actorY = new int[48];
    private int[] actorVisible = new int[48];
    private int[] actorCostume = new int[48];
    private int[] actorFacing = new int[48];
    private int[] actorAnim = new int[48];
    private int[] actorElevation = new int[48];
    private int[] actorWidth = new int[48];
    private int[] actorScaleX = new int[48];
    private int[] actorScaleY = new int[48];
    private int[] actorWalkCount = new int[48];
    private int[] actorWalkIndex = new int[48];
    private int[] actorPathX = new int[48 * 64];
    private int[] actorPathY = new int[48 * 64];
    private int[] actorWalkEndDir = new int[48];
    private int[] actorWalkCredit = new int[48];
    private int[] actorMoveRemainderX = new int[48];
    private int[] actorMoveRemainderY = new int[48];
    private int[] scaleSlots = new int[16 * 4];
    private int tickJiffies;
    private int nearestX;
    private int nearestY;
    private int[] actorWalkSpeedX = new int[48];
    private int[] actorWalkSpeedY = new int[48];
    private int[] actorTalkColor = new int[48];
    private int[] actorInitFrame = new int[48];
    private int[] actorWalkFrame = new int[48];
    private int[] actorStandFrame = new int[48];
    private int[] actorTalkStart = new int[48];
    private int[] actorTalkStop = new int[48];
    private int[] actorIgnoreBoxes = new int[48];
    private int[] actorForceClip = new int[48];
    private int[] actorShadow = new int[48];
    private int[] actorAnimSpeed = new int[48];
    private int[] actorAnimProgress = new int[48];
    private int[] actorAnimStep = new int[48];
    private int[] actorSound = new int[48];
    private int[] actorPalette = new int[48 * 32];
    private byte[][] actorNames = new byte[48][];
    private byte[][] slotCode = new byte[80][];
    private int[] slotWhere = new int[80];
    private int[] slotFrozen = new int[80];
    private bool[] slotFreezeResistant = new bool[80];
    private bool[] slotRecursive = new bool[80];
    private int[] verbId = new int[100];
    private int[] verbMode = new int[100];
    private int[] verbColor = new int[100];
    private int[] verbHiColor = new int[100];
    private int[] verbDimColor = new int[100];
    private int[] verbBkColor = new int[100];
    private int[] verbLeft = new int[100];
    private int[] verbTop = new int[100];
    private int[] verbKey = new int[100];
    private int[] verbCenter = new int[100];
    private int[] verbType = new int[100];
    private int[] verbImgIndex = new int[100];
    private int[] verbSaveId = new int[100];
    private int[] verbNameLen = new int[100];
    private byte[][] verbNames = new byte[100][];

    public void ResetMachine()
    {        globals = new int[globalCount > 0 ? globalCount : 800];
        bits = new bool[bitCount > 0 ? bitCount : 2048];
        codeCache = new byte[globalScripts == null ? 0 : globalScripts.Length][];
        scriptIds = new int[80];
        offsets = new int[80];
        states = new int[80];
        delays = new int[80];
        generations = new int[80];
        executed = new bool[80];
        locals = new int[80 * 26];
        returnSlots = new int[15];
        returnGenerations = new int[15];
        returnRooms = new int[15];
        returnPhases = new int[15];
        arguments = new int[25];
        activeSlot = -1;
        stackDepth = 0;
        scanSlot = 0;
        instructionsExecuted = 0;
        completedTicks = 0;
        tickInProgress = false;
        finished = true;
        fault = "";
        faultScript = -1;
        faultOffset = -1;
        faultOpcode = -1;
        instructionScript = -1;
        instructionOffset = -1;
        opcode = -1;
        cursorState = 0;
        userPut = 0;
        currentCursor = 0;
        currentCharset = 0;
        for (int i = 0; i < 16; i++) charsetColors[i] = 0;
        lastSoundRequest = 0;
        soundRunning = 0;
        randomState = 1;
        talkDelay = 0;
        talkingActor = -1;
        lastPrintLength = 0;
        // Zero-initialized slot defaults, like a fresh engine: fixed
        // position until scripts arm overhead/coordinates explicitly.
        lastPrintOverhead = 0;
        lastPrintX = 0;
        lastPrintY = 0;
        lastPrintColor = -1;
        lastPrintText = "";
        printSequence = 0;
        lastPrintActor = -1;
        lastStringOp = -1;
        lastStringId = -1;
        stringTable = new byte[64][];
        // Engine-set reference configuration (VGA render mode + AdLib music,
        // like setupScummVars/setSoundCardVarToCurrentConfig).
        if (globalCount > 48) globals[48] = 3;
        if (globalCount > 49) globals[49] = 19;
        // ScummEngine::resetScummVars: VAR_INPUTMODE = 3 (mouse) for v5.
        if (globalCount > 67) globals[67] = 3;
        // ScummEngine_v5::resetScummVars: _scummVars[74] = 1225 for GID_MONKEY.
        if (globalCount > 74) globals[74] = 1225;
        // ScummEngine::resetScummVars: VAR_CHARINC (text speed) defaults to 4.
        if (globalCount > 37) globals[37] = 4;
        // ScummEngine_v5::resetScummVars: VAR_V5_TALK_STRING_Y = -80 for v4/v5
        // puts overhead dialogue above the speaker's head (see displayDialog).
        if (globalCount > 54) globals[54] = -80;
        // ScummEngine_v5::resetScummVars: lights on (base palette + colors
        // + room lights = 11). Sentence script 2 gates Look on
        // VAR_CURRENT_LIGHTS (9); without this every Look says "too dark".
        if (globalCount > 9) globals[9] = 11;
        currentRoom = 0;
        roomResource = -1;
        roomWidth = 0;
        roomHeight = 0;
        roomBytes = null;
        roomPalette = null;
        roomTrans = 255;
        numWalkboxes = 0;
        for (int i = 0; i < scaleSlots.Length; i++) scaleSlots[i] = 0;
        walkMatrix = null;
        roomEncdOffs = -1;
        roomExcdOffs = -1;
        numLocalScripts = 0;
        cameraX = 160;
        cameraY = 100;
        cameraDestX = 160;
        screenTop = 0;
        screenBottom = 200;
        cameraMinX = 0;
        cameraMaxX = 0;
        numObjectsTracked = 0;
        numObjectSlots = 0;
        for (int i = 0; i < 200; i++)
        {
            objSlotId[i] = 0;
            objSlotX[i] = 0;
            objSlotY[i] = 0;
            objSlotW[i] = 0;
            objSlotH[i] = 0;
            objSlotWalkX[i] = 0;
            objSlotWalkY[i] = 0;
            objSlotDir[i] = 0;
            objSlotFlags[i] = 0;
            objSlotParent[i] = 0;
            objSlotObcd[i] = 0;
        }
        for (int i = 0; i < 80; i++) inventoryIds[i] = 0;
        for (int i = 0; i < 100; i++)
        {
            verbId[i] = 0;
            verbMode[i] = 0;
            verbColor[i] = 0;
            verbHiColor[i] = 0;
            verbDimColor[i] = 0;
            verbBkColor[i] = 0;
            verbLeft[i] = 0;
            verbTop[i] = 0;
            verbKey[i] = 0;
            verbCenter[i] = 0;
            verbType[i] = 0;
            verbImgIndex[i] = 0;
            verbSaveId[i] = 0;
            verbNameLen[i] = 0;
        }
        sentenceVerb = 0;
        sentenceObjectA = 0;
        sentenceObjectB = 0;
        sentenceNum = 0;
        cameraFollowActor = 0;
        pendingPlaceEgo = -1;
        pendingPlaceObj = 0;
        pendingPlaceX = 0;
        pendingPlaceY = 0;
        for (int i = 0; i < 48; i++)
        {
            actorRoom[i] = 0;
            actorX[i] = 0;
            actorY[i] = 0;
            actorVisible[i] = 0;
            actorCostume[i] = 0;
            actorFacing[i] = 0;
            actorAnim[i] = 0;
            actorElevation[i] = 0;
            actorWidth[i] = 0;
            actorScaleX[i] = 255;
            actorScaleY[i] = 255;
            actorWalkCount[i] = 0;
            actorWalkIndex[i] = 0;
            actorWalkCredit[i] = 0;
            actorMoveRemainderX[i] = 0;
            actorMoveRemainderY[i] = 0;
            actorWalkEndDir[i] = -1;
            actorWalkSpeedX[i] = 0;
            actorWalkSpeedY[i] = 0;
            actorTalkColor[i] = 0;
            actorInitFrame[i] = 0;
            actorWalkFrame[i] = 0;
            actorStandFrame[i] = 0;
            actorTalkStart[i] = 0;
            actorTalkStop[i] = 0;
            actorIgnoreBoxes[i] = 0;
            actorForceClip[i] = 0;
            actorShadow[i] = 0;
            actorAnimSpeed[i] = 0;
            actorAnimProgress[i] = 0;
            actorAnimStep[i] = 0;
            actorSound[i] = 0;
            actorNames[i] = null;
            for (int k = 0; k < 32; k++) actorPalette[i * 32 + k] = k;
        }
        for (int i = 0; i < 80; i++)
        {
            slotCode[i] = null;
            slotWhere[i] = 0;
            slotFrozen[i] = 0;
            slotFreezeResistant[i] = false;
            slotRecursive[i] = false;
            slotCutscene[i] = 0;
        }
        cutSceneSlot = -1;
        cutStackPtr = -1;
        LoadGlobalTables();
    }

    public void StartEntry(int script, int bootArgument)    {
        if (states == null) ResetMachine();
        if (!finished || fault != "") return;
        for (int i = 0; i < 25; i++) arguments[i] = 0;
        arguments[0] = bootArgument;
        Launch(script, false);
        // The entry is queued for the next scheduler pass.
        activeSlot = -1;
    }

    // At most one logical tick per call. A budget pause never advances timers.
    public void Advance(int instructionBudget, int elapsedJiffies)
    {
        if (finished || fault != "" || instructionBudget < 1) return;
        if (!tickInProgress)
        {
            int delta = elapsedJiffies < 0 ? 0 : (elapsedJiffies > 15 ? 15 : elapsedJiffies);
            tickJiffies = delta;
            for (int i = 1; i < 80; i++)
            {
                executed[i] = false;
                if (states[i] == 1)
                {
                    delays[i] -= delta;
                    if (delays[i] < 0) states[i] = 2;
                }
            }
            if (talkDelay > 0)
            {
                talkDelay -= delta;
                if (talkDelay <= 0)
                {
                    talkDelay = 0;
                    if (3 < globals.Length) globals[3] = 0;
                }
            }
            // actor.cpp processActors: every in-room costumed actor advances
            // its limb animation once per frame, gated by its anim speed.
            for (int act = 1; act < 48; act++)
            {
                if (actorRoom[act] != currentRoom || actorCostume[act] == 0) continue;
                actorAnimProgress[act]++;
                if (actorAnimProgress[act] >= actorAnimSpeed[act])
                {
                    actorAnimProgress[act] = 0;
                    actorAnimStep[act]++;
                }
            }
            AdvanceCamera();
            scanSlot = 1;
            tickInProgress = true;
        }
        int consumed = 0;
        while (fault == "" && consumed < instructionBudget)
        {
            if (activeSlot < 0)
            {
                ResumeCaller();
                if (fault != "") return;
                if (activeSlot < 0)
                {
                    while (scanSlot < 80)
                    {
                        int candidate = scanSlot++;
                        if (states[candidate] == 2 && slotFrozen[candidate] == 0 && !executed[candidate])
                        {
                            activeSlot = candidate;
                            break;
                        }
                    }
                }
                if (activeSlot < 0)
                {
                    tickInProgress = false;
                    completedTicks++;
                    AdvanceActorWalks();
                    CheckSentence();
                    if (fault != "") return;
                    finished = true;
                    for (int i = 1; i < 80; i++)
                        if (states[i] != 0) finished = false;
                    for (int i = 1; i < 48; i++)
                        if (GetActorMoving(i) != 0) finished = false;
                    return;
                }
            }
            instructionScript = scriptIds[activeSlot];
            instructionOffset = offsets[activeSlot];
            opcode = -1;
            opcode = ReadByte();
            if (fault != "") return;
            executed[activeSlot] = true;
            ExecuteInstruction();
            instructionsExecuted++;
            consumed++;
        }
    }

    public int ReadGlobal(int index)
    {
        if (globals == null || index < 0 || index >= globals.Length) return 0;
        return globals[index];
    }

    public int ReadRoom()
    {
        return currentRoom;
    }

    public void SetGlobal(int index, int value)
    {
        if (globals == null || index < 0 || index >= globals.Length) return;
        globals[index] = value;
    }

    // Test/debug entry: load a room directly, like the debugger's room jump.
    public void EnterRoom(int room)
    {
        if (states == null) ResetMachine();
        if (fault != "") return;
        StartScene(room);
        activeSlot = -1;
    }

    // Mirrors runInputScript: scene/verb click runs VAR_VERB_SCRIPT with
    // [clickArea, val, mode]. Called between ticks from the harness.
    public void RunInputScript(int clickArea, int val, int mode)
    {
        if (states == null) ResetMachine();
        if (fault != "" || tickInProgress) return;
        // Dialogue choices are verb clicks (area 1) during a cutscene with an
        // armed override: they must dispatch to the cutscene verb script
        // (14, which reports the pick via g194), not abort the cutscene.
        // Scene clicks (area 2) still skip via abort (room-32).
        if (clickArea != 1 && AbortCutscene()) { activeSlot = -1; return; }
        if (globals.Length > 53 && globals[53] > 0)
        {
            // Room-32's entry spiel strands verb script 14 with no armed
            // override and no hover tracker; heal that once input is live.
            // Do not touch 14 while an override is still armed (dialogue
            // menus legitimately use 14 with input on, e.g. room 81).
            bool overrideArmed = cutStackPtr >= 0 && cutActive[cutStackPtr] && cutSlot[cutStackPtr] >= 0;
            if (!overrideArmed && globals.Length > 32 && globals[32] == 14) globals[32] = 4;
            RestoreHoverTracker();
        }
        int verbScript = globals.Length > 32 ? globals[32] : 0;
        if (verbScript == 0) return;
        // Dialogue choices (verbs 120+) unblock waiting dialogue scripts via
        // g194 (room 81 global 92, room 28 local 220 poll 194 != 0). The
        // cutscene verb script (14) reports the pick this way; the normal
        // verb script (4) does not, so report it here as well to keep late
        // menus (input on, g32=4) working. Stale picks are harmless: each
        // wait loop clears 194 to 0 before polling.
        if (clickArea == 1 && val >= 120 && 194 < globals.Length) globals[194] = val;
        for (int i = 0; i < 25; i++) arguments[i] = 0;
        arguments[0] = clickArea;
        arguments[1] = val;
        arguments[2] = mode;
        Launch(verbScript, false);
        activeSlot = -1;
    }

    public string DescribeState()
    {
        string result = "VM boot-r9 room=" + currentRoom + " active=" + activeSlot
            + " nesting=" + stackDepth + " last=" + instructionScript + ":" + instructionOffset + " op=" + opcode;
        if (states == null) return result + " (not initialized)";
        for (int i = 1; i < 80; i++)
            if (states[i] != 0)
                result += "\nslot=" + i + " script=" + scriptIds[i] + " offset=" + offsets[i]
                    + " where=" + slotWhere[i] + " state=" + states[i] + " delay=" + delays[i]
                    + " frozen=" + slotFrozen[i];
        return result;
    }

    public int ReadBit(int index)
    {
        if (bits == null || index < 0 || index >= bits.Length) return 0;
        return bits[index] ? 1 : 0;
    }

    private void Launch(int script, bool recursive)
    {
        LaunchInner(script, recursive, false, 0, 0);
    }

    private void LaunchInner(int script, bool recursive, bool freezeResistant, int where, int roomOffs)
    {
        if (script == 0) return;
        byte[] code = null;
        int start = 0;
        if (where == 0 && script >= 200)
        {
            where = 1;
        }
        if (where == 0)
        {
            if (script < 0 || script >= codeCache.Length || globalScripts[script] == null)
            {
                Fail("Missing global script " + script);
                return;
            }
            if (codeCache[script] == null) codeCache[script] = globalScripts[script].bytes;
            code = codeCache[script];
        }
        else if (where == 1)
        {
            int found = -1;
            for (int i = 0; i < numLocalScripts; i++)
                if (localScriptIds[i] == script) { found = i; break; }
            if (found < 0 || roomBytes == null)
            {
                Fail("Missing local script " + script + " in room " + currentRoom);
                return;
            }
            code = roomBytes;
            start = localScriptOffs[found];
        }
        else
        {
            if (roomBytes == null)
            {
                Fail("No room loaded for room script");
                return;
            }
            code = roomBytes;
            start = roomOffs;
        }
        if (!recursive)
        {
            if (where == 2) StopObjectScript(script);
            else StopScript(script);
        }
        if (activeSlot >= 0 && stackDepth >= 14)
        {
            Fail("Script nesting exceeds 15 levels");
            return;
        }
        int slot = -1;
        for (int i = 1; i < 80; i++)
            if (states[i] == 0) { slot = i; break; }
        if (slot < 0) { Fail("No free script slots"); return; }
        if (activeSlot >= 0)
        {
            returnSlots[stackDepth] = activeSlot;
            returnGenerations[stackDepth++] = generations[activeSlot];
        }
        scriptIds[slot] = script;
        offsets[slot] = start;
        states[slot] = 2;
        delays[slot] = 0;
        generations[slot]++;
        executed[slot] = false;
        slotWhere[slot] = where;
        slotCode[slot] = code;
        slotFrozen[slot] = 0;
        slotCutscene[slot] = 0;
        slotFreezeResistant[slot] = freezeResistant;
        slotRecursive[slot] = recursive;
        for (int i = 0; i < 26; i++) locals[slot * 26 + i] = i < 25 ? arguments[i] : 0;
        activeSlot = slot;
        finished = false;
    }

    private void ResumeCaller()
    {
        // Mirrors runScriptNested: resume the caller only if its slot is
        // unchanged (generation match) and unfrozen; otherwise keep unwinding.
        while (stackDepth > 0)
        {
            int frame = --stackDepth;
            int slot = returnSlots[frame];
            if (slot == -1)
            {
                ContinueScene(returnRooms[frame], returnPhases[frame]);
                if (activeSlot >= 0 || fault != "") return;
                continue;
            }
            if (slot == -2) { cutSceneSlot = -1; continue; }
            if (states[slot] != 0 && generations[slot] == returnGenerations[frame]
                && slotFrozen[slot] == 0)
            {
                activeSlot = slot;
                return;
            }
        }
    }

    private void StopScript(int script)
    {
        for (int i = 1; i < 80; i++)
        {
            if (states[i] != 0 && (slotWhere[i] == 0 || slotWhere[i] == 1) && scriptIds[i] == script)
            {
                if (slotCutscene[i] > 0) { Fail("Script " + script + " stopped with active cutscene/override"); return; }
                KillSlot(i);
            }
        }
    }

    private void StopObjectScript(int script)
    {
        for (int i = 1; i < 80; i++)
        {
            if (states[i] != 0 && slotWhere[i] == 2 && scriptIds[i] == script)
            {
                if (slotCutscene[i] > 0) { Fail("Object stopped with active cutscene/override"); return; }
                KillSlot(i);
            }
        }
    }

    private void KillSlot(int slot)
    {
        states[slot] = 0;
        slotFrozen[slot] = 0;
        slotCutscene[slot] = 0;
        slotCode[slot] = null;
        if (activeSlot == slot) activeSlot = -1;
    }

    private void KillRoomSlots()
    {
        for (int i = 1; i < 80; i++)
        {
            if (states[i] != 0 && (slotWhere[i] == 1 || slotWhere[i] == 2))
            {
                states[i] = 0;
                slotFrozen[i] = 0;
                slotCutscene[i] = 0;
                slotCode[i] = null;
                if (activeSlot == i) activeSlot = -1;
            }
        }
    }

    private void FreezeScripts(int flag)
    {
        for (int i = 1; i < 80; i++)
        {
            if (i != activeSlot && states[i] != 0 && (!slotFreezeResistant[i] || flag >= 128))
            {
                slotFrozen[i]++;
            }
        }
        // The cutscene owner is exempt: it keeps running its nested scripts.
        if (cutSceneSlot >= 1 && cutSceneSlot < 80)
        {
            slotFrozen[cutSceneSlot] = 0;
        }
    }

    private void UnfreezeScripts()
    {
        for (int i = 1; i < 80; i++)
        {
            if (slotFrozen[i] > 0) slotFrozen[i]--;
        }
    }

    private int ReadByte()
    {
        if (fault != "") return 0;
        byte[] code = slotCode[activeSlot];
        if (code == null) { Fail("No code for script"); return 0; }
        int offset = offsets[activeSlot];
        if (offset < 0 || offset >= code.Length) { Fail("Read outside script"); return 0; }
        offsets[activeSlot]++;
        return code[offset];
    }

    private int ReadWord()
    {
        int low = ReadByte();
        return low | (ReadByte() << 8);
    }

    private int Signed16(int value)
    {
        value &= 65535;
        return value >= 32768 ? value - 65536 : value;
    }

    private int Wrap32(long value)
    {
        value &= 4294967295L;
        if (value >= 2147483648L) value -= 4294967296L;
        return (int)value;
    }

    private int ReadAddress()
    {
        int address = ReadWord();
        // v5 getResultPos/readVar resolve 0x2000 indirection; allow bounded nesting.
        for (int depth = 0; depth < 4 && (address & 8192) != 0; depth++)
        {
            int index = ReadWord();
            int displacement = (index & 8192) != 0 ? ReadStorage(index & ~8192) : index & 4095;
            if (fault != "") return 0;
            address = (address + displacement) & ~8192;
        }
        if ((address & 8192) != 0) Fail("Too much variable indirection");
        return address;
    }

    private int ReadStorage(int address)
    {
        if (fault != "") return 0;
        if ((address & 61440) == 0 && address >= 0 && address < globals.Length) return globals[address];
        if ((address & 32768) != 0 && (address & 32767) < bits.Length) return bits[address & 32767] ? 1 : 0;
        // v5 fills NUM_SCRIPT_LOCAL (25) locals per slot; accept 0..24.
        if ((address & 32768) == 0 && (address & 16384) != 0 && (address & 4095) < 25)
            return locals[activeSlot * 26 + (address & 4095)];
        Fail("Invalid variable address " + address);
        return 0;
    }

    private void WriteStorage(int address, int value)
    {
        if (fault != "") return;
        if ((address & 61440) == 0 && address >= 0 && address < globals.Length) globals[address] = value;
        else if ((address & 32768) != 0 && (address & 32767) < bits.Length) bits[address & 32767] = value != 0;
        else if ((address & 32768) == 0 && (address & 16384) != 0 && (address & 4095) < 25)
            locals[activeSlot * 26 + (address & 4095)] = value;
        else Fail("Invalid variable address " + address);
    }

    private int WordOperand(int mode)
    {
        return WordOperandMode(mode, 128);
    }

    private int WordOperandMode(int mode, int mask)
    {
        return (mode & mask) != 0 ? ReadStorage(ReadAddress()) : Signed16(ReadWord());
    }

    private int WordOperandBit(int mask)
    {
        return WordOperandMode(opcode, mask);
    }

    // v5 o5_chainScript: word-vararg args like startScript, but the current
    // slot dies and the chained script inherits its freeze/resistant flags.
    // No return frame is pushed, so completion resumes the original caller.
    private void ChainScriptOp()
    {
        int script = ByteOperand(opcode);
        ReadVarargs();
        if (fault != "") return;
        int cur = activeSlot;
        bool fr = cur >= 0 && cur < 80 ? slotFreezeResistant[cur] : false;
        bool rec = cur >= 0 && cur < 80 ? slotRecursive[cur] : false;
        if (script == 0)
        {
            if (cur >= 0) KillSlot(cur);
            else activeSlot = -1;
            return;
        }
        if (cur >= 0)
        {
            states[cur] = 0;
            slotFrozen[cur] = 0;
            slotCode[cur] = null;
        }
        activeSlot = -1;
        LaunchInner(script, rec, fr, 0, 0);
    }

    private int ByteOperand(int mode)
    {
        return ByteOperandMode(mode, 128);
    }

    private int ByteOperandMode(int mode, int mask)
    {
        return (mode & mask) != 0 ? ReadStorage(ReadAddress()) : ReadByte();
    }

    private int ByteOperandBit(int mask)
    {
        return ByteOperandMode(opcode, mask);
    }

    private void Branch(bool condition)
    {
        int displacement = Signed16(ReadWord());
        if (fault != "" || condition) return;
        int target = offsets[activeSlot] + displacement;
        byte[] code = slotCode[activeSlot];
        if (code == null || target < 0 || target >= code.Length)
            Fail("Jump outside script");
        else offsets[activeSlot] = target;
    }

    private int ReadVarargs()
    {
        for (int i = 0; i < 25; i++) arguments[i] = 0;
        int count = 0;
        while (fault == "")
        {
            int mode = ReadByte();
            if (mode == 255 || fault != "") break;
            if (count == 25) { Fail("Too many script arguments"); return count; }
            arguments[count++] = WordOperand(mode);
        }
        return count;
    }

    private void StartScriptOp(int mode)
    {
        int script = ByteOperand(mode);
        ReadVarargs();
        if (fault != "") return;
        LaunchInner(script, (mode & 64) != 0, (mode & 32) != 0, 0, 0);
    }

    private int ReadVararg(int[] dest, int max)
    {
        int count = 0;
        while (fault == "")
        {
            int mode = ReadByte();
            if (mode == 255 || fault != "") break;
            if (count >= max || count >= dest.Length) { Fail("Too many vararg entries"); return count; }
            dest[count++] = WordOperand(mode);
        }
        return count;
    }

    private void LightsOp()
    {
        // v5 o5_lights: a = varOrDirectByte, then two direct bytes.
        // c==0 sets VAR_CURRENT_LIGHTS (9); c==1 is flashlight (ignored).
        int a = ByteOperand(opcode);
        if (fault != "") return;
        int b = ReadByte();
        if (fault != "") return;
        int c = ReadByte();
        if (fault != "") return;
        if (c == 0 && 9 < globals.Length) globals[9] = a;
    }

    private void CursorCommand()
    {
        // v5 o5_cursorCommand. DOS release: no Mac-specific clamps.
        int subop = ReadByte();        if (fault != "") return;
        if (subop == 1) cursorState = 1;
        else if (subop == 2) cursorState = 0;
        else if (subop == 3) userPut = 1;
        else if (subop == 4) userPut = 0;
        else if (subop == 5) cursorState++;
        else if (subop == 6) cursorState--;
        else if (subop == 7) userPut++;
        else if (subop == 8) userPut--;
        else if (subop == 10) { ByteOperand(subop); ByteOperandMode(subop, 64); }
        else if (subop == 11) { ByteOperand(subop); ByteOperandMode(subop, 64); ByteOperandMode(subop, 32); }
        else if (subop == 12) currentCursor = ByteOperand(subop);
        else if (subop == 13) currentCharset = ByteOperand(subop);
        else if (subop == 14)
        {
            int[] table = new int[16];
            int count = ReadVararg(table, 16);
            if (fault != "") return;
            for (int i = 0; i < 16; i++) charsetColors[i] = i < count ? table[i] : 0;
        }
        if (fault != "") return;
        if (52 < globals.Length) globals[52] = cursorState;
        if (53 < globals.Length) globals[53] = userPut;
    }

    // Sound is a stub: requests are recorded for script-visible status, but no
    // audio plays yet, so sounds complete immediately. Real playback comes later.
    private void StartMusicOrSound(bool fromMusic)
    {
        int sound = ByteOperand(opcode);
        if (fault != "") return;
        if (!fromMusic && 14 < globals.Length) globals[14] = 0;
        lastSoundRequest = sound;
        soundRunning = 0;
    }

    private void StopSound()
    {
        int sound = ByteOperand(opcode);
        if (fault != "") return;
        if (sound == lastSoundRequest) soundRunning = 0;
    }

    private void SoundKludge()
    {
        int[] items = new int[25];
        ReadVararg(items, 25);
    }

    private void IsSoundRunning()
    {
        int address = ReadAddress();
        int sound = ByteOperand(opcode);
        if (fault != "") return;
        int running = (sound != 0 && sound == lastSoundRequest) ? soundRunning : 0;
        WriteStorage(address, running);
    }

    // Printable ASCII for dialogue capture (32..126). Substring-only so the
    // lookup stays inside known Udon string operations.
    private string printChars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
    public string lastPrintText = "";
    public int printSequence;
    public int lastPrintActor = -1;

    // Dialogue stub: operands and inline text are consumed exactly, HAVE_MSG is
    // raised with a length-based timer, but nothing is drawn and no speech
    // plays yet. 0x14/0x94 print, 0xD8 printEgo (actor VAR_EGO).
    private void Print(bool ego)
    {
        int actor = ego ? (globals.Length > 1 ? globals[1] : 0) : ByteOperand(opcode);
        if (fault != "") return;
        talkingActor = actor;
        // Slot state persists across prints (loadDefault at entry,
        // saveDefault at exit in decodeParseString): overhead, position and
        // color change only through explicit SO_* ops, never reset here.
        while (fault == "")
        {
            int op = ReadByte();
            if (op == 255 || fault != "") break;
            int kind = op & 15;
            if (kind == 0)
            {
                int x = WordOperand(op);
                int y = WordOperandMode(op, 64);
                if (fault != "") return;
                lastPrintX = x;
                lastPrintY = y;
                lastPrintOverhead = 0;
            }
            else if (kind == 1)
            {
                int color = ByteOperand(op);
                if (fault != "") return;
                lastPrintColor = color;
            }
            else if (kind == 2) { WordOperand(op); }
            else if (kind == 4 || kind == 6) { lastPrintOverhead = 0; }
            else if (kind == 7) { lastPrintOverhead = 1; }
            else if (kind == 8) { Fail("Print voice block unsupported"); return; }
            else if (kind == 15)
            {
                int textStart = offsets[activeSlot];
                SkipInlineText();
                if (fault != "") return;
                lastPrintText = DecodePrintText(textStart, offsets[activeSlot] - 1);
                lastPrintActor = talkingActor;
                printSequence++;
                if (3 < globals.Length) globals[3] = 255;
                // string.cpp displayDialog: base delay plus one CHARINC
                // (engine var 37, default 4) per printed character. Units
                // match the scheduler tick delta exactly.
                int charInc = globals.Length > 37 ? globals[37] : 4;
                talkDelay = 60 + lastPrintText.Length * charInc;
                return;
            }
            else if (kind == 3) { Fail("Print erase unsupported"); return; }
            else { Fail("Print parse op " + op + " unsupported"); return; }
        }
    }

    // Printable subset of an inline string: skips NUL, 0xFF escapes (with
    // word operands except controls 1/2/3/8), and non-ASCII bytes.
    private string DecodePrintText(int start, int end)
    {
        string result = "";
        byte[] code = slotCode[activeSlot];
        if (code == null || start < 0 || end > code.Length || end < start) return result;
        int i = start;
        while (i < end)
        {
            int c = code[i++];
            if (c == 0) break;
            if (c == 255)
            {
                if (i >= end) break;
                int control = code[i++];
                if (control != 1 && control != 2 && control != 3 && control != 8) i += 2;
                continue;
            }
            if (c >= 32 && c < 127 && printChars.Length == 95)
                result += printChars.Substring(c - 32, 1);
        }
        return result;
    }

    private void SkipInlineText()
    {
        int start = offsets[activeSlot];
        int end = FindInlineEnd();
        if (fault != "") return;
        offsets[activeSlot] = end;
        lastPrintLength = end - start - 1;
    }

    private int FindInlineEnd()
    {
        // v5 resStrLen: controls 1/2/3/8 have no word operand.
        byte[] code = slotCode[activeSlot];
        if (code == null) { Fail("No code for script"); return 0; }
        int end = offsets[activeSlot];
        while (true)
        {
            if (end < 0 || end >= code.Length) { Fail("String outside script"); return 0; }
            int c = code[end++];
            if (c == 0) return end;
            if (c == 255)
            {
                if (end >= code.Length) { Fail("String control outside script"); return 0; }
                int control = code[end++];
                if (control != 1 && control != 2 && control != 3 && control != 8)
                {
                    if (end + 2 > code.Length) { Fail("String operand outside script"); return 0; }
                    end += 2;
                }
            }
        }
    }

    // String table: v5 strings live inline in scripts. Case 1 copies the inline
    // NUL-terminated (0xFF-aware) text into the slot and advances past it.
    // Cases 2-5 operate on stored slots. No disk resources involved.
    private void StringOps()
    {
        int subop = ReadByte();
        if (fault != "") return;
        lastStringOp = subop & 31;
        if (lastStringOp == 1)
        {
            int id = ByteOperand(subop);
            if (fault != "") return;
            lastStringId = id;
            if (id < 0 || id >= stringTable.Length) { Fail("String id out of range"); return; }
            stringTable[id] = ReadInlineString();
        }
        else if (lastStringOp == 2)
        {
            int to = ByteOperand(subop);
            int from = ByteOperandMode(subop, 64);
            if (fault != "") return;
            lastStringId = to;
            if (to < 0 || to >= stringTable.Length || from < 0 || from >= stringTable.Length)
            {
                Fail("String id out of range");
                return;
            }
            stringTable[to] = stringTable[from];
        }
        else if (lastStringOp == 3)
        {
            int id = ByteOperand(subop);
            int index = ByteOperandMode(subop, 64);
            int value = ByteOperandMode(subop, 32);
            if (fault != "") return;
            if (id < 0 || id >= stringTable.Length || stringTable[id] == null)
            {
                Fail("String id out of range");
                return;
            }
            if (index >= 0 && index < stringTable[id].Length) stringTable[id][index] = (byte)(value & 255);
        }
        else if (lastStringOp == 4)
        {
            int address = ReadAddress();
            int id = ByteOperand(subop);
            int index = ByteOperandMode(subop, 64);
            if (fault != "") return;
            int result = 0;
            if (id >= 0 && id < stringTable.Length && stringTable[id] != null
                && index >= 0 && index < stringTable[id].Length)
                result = stringTable[id][index];
            WriteStorage(address, result);
        }
        else if (lastStringOp == 5)
        {
            int id = ByteOperand(subop);
            int size = ByteOperandMode(subop, 64);
            if (fault != "") return;
            if (id < 0 || id >= stringTable.Length || size < 0) { Fail("String id out of range"); return; }
            stringTable[id] = new byte[size];
        }
        else { Fail("String op " + subop + " unsupported"); return; }
    }

    // Room ops: operand consumption is exact; visual/palette effects and room
    // transitions are recorded, not applied. Case 2/4 error on v5 resources.
    private void RoomOps()
    {
        int subop = ReadByte();
        if (fault != "") return;
        int kind = subop & 31;
        if (kind == 1)
        {
            int a = WordOperandMode(subop, 128);
            int b = WordOperandMode(subop, 64);
            if (fault != "") return;
            if (17 < globals.Length) globals[17] = a;
            if (18 < globals.Length) globals[18] = b;
            cameraMinX = a;
            cameraMaxX = b;
        }
        else if (kind == 2) { Fail("Room color subop removed in v5"); return; }
        else if (kind == 4)
        {
            int r = WordOperand(subop);
            int g = WordOperand(subop);
            int b = WordOperand(subop);
            int mode = ReadByte();
            int idx = ByteOperandMode(mode, 128);
            if (fault != "") return;
            if (idx >= 0 && idx < 256)
            {
                palChangeLog[idx * 4] = idx;
                palChangeLog[idx * 4 + 1] = r;
                palChangeLog[idx * 4 + 2] = g;
                palChangeLog[idx * 4 + 3] = b;
            }
        }
        else if (kind == 3)
        {
            // SO_ROOM_SCREEN sets the main virtual screen's vertical bounds
            // (rows b..h), not the hardware resolution, which stays 320x200.
            // (Storing the first operand as the width shifted every camera
            // scroll by half a screen.)
            screenTop = WordOperandMode(subop, 128);
            screenBottom = WordOperandMode(subop, 64);
        }
        else if (kind == 5) { shakeOn = 1; }
        else if (kind == 6) { shakeOn = 0; }
        else if (kind == 7)
        {
            int mode = subop;
            int s1 = ByteOperandMode(mode, 128);
            int y1 = ByteOperandMode(mode, 64);
            mode = ReadByte();
            int s2 = ByteOperandMode(mode, 128);
            int y2 = ByteOperandMode(mode, 64);
            mode = ReadByte();
            int slot = ByteOperandMode(mode, 64) - 2;
            if (fault != "") return;
            if (slot < 0 || slot >= 16) { Fail("Invalid room scale slot"); return; }
            scaleSlots[slot * 4] = s1;
            scaleSlots[slot * 4 + 1] = y1;
            scaleSlots[slot * 4 + 2] = s2;
            scaleSlots[slot * 4 + 3] = y2;
        }
        else if (kind == 8)
        {
            ByteOperandMode(subop, 128);
            ByteOperandMode(subop, 64);
            ByteOperandMode(subop, 32);
        }
        else if (kind == 9)
        {
            saveLoadFlag = ByteOperandMode(subop, 128);
            ByteOperandMode(subop, 64);
        }
        else if (kind == 10) { switchRoomEffect = WordOperand(subop); }
        else if (kind == 11)
        {
            int mode = subop;
            WordOperandMode(mode, 128); WordOperandMode(mode, 64); WordOperandMode(mode, 32);
            mode = ReadByte(); ByteOperandMode(mode, 128); ByteOperandMode(mode, 64);
        }
        else if (kind == 12)
        {
            int mode = subop;
            WordOperandMode(mode, 128); WordOperandMode(mode, 64); WordOperandMode(mode, 32);
            mode = ReadByte(); ByteOperandMode(mode, 128); ByteOperandMode(mode, 64);
        }
        else if (kind == 13)
        {
            ByteOperand(subop);
            while (fault == "" && ReadByte() != 0) { }
        }
        else if (kind == 14)
        {
            loadStringId = ByteOperand(subop);
            while (fault == "" && ReadByte() != 0) { }
        }
        else if (kind == 15)
        {
            int mode = subop;
            ByteOperandMode(mode, 128);
            mode = ReadByte(); ByteOperandMode(mode, 128); ByteOperandMode(mode, 64);
            mode = ReadByte(); ByteOperandMode(mode, 128);
        }
        else if (kind == 16)
        {
            ByteOperandMode(subop, 128);
            ByteOperandMode(subop, 64);
        }
        else { Fail("Room op " + subop + " unsupported"); return; }
    }

    // Resource routines: prefetch/lock hints only. All game data is packaged,
    // so loads always succeed; the ids are recorded for later stages.
    private void ResourceRoutines()
    {
        int subop = ReadByte();
        if (fault != "") return;
        int resid = 0;
        if (subop != 17) resid = ByteOperand(subop);
        if (fault != "") return;
        int op = subop & 63;
        lastResourceOp = op;
        lastResourceId = resid;
        if (op >= 1 && op <= 8) { }
        else if (op == 9) { if (resid >= 200) return; }
        else if (op >= 10 && op <= 16) { }
        else if (op == 17) { }
        else if (op == 18) { currentCharset = resid; }
        else if (op == 19) { }
        else if (op == 20) { WordOperandMode(subop, 64); }
        else if (op == 32 || op == 33) { }
        else { Fail("Resource op " + subop + " unsupported"); return; }
    }

    // ---- Rooms, actors, objects ----
    // Room blobs (Tools/build_room_blobs.py) carry dims, palette, walkboxes,
    // objects, local-script directory, entry/exit offsets, then room bytes.

    private int ReadU16(byte[] data, int offset)
    {
        if (data == null || offset < 0 || offset + 1 >= data.Length) { Fail("Room data outside bounds"); return 0; }
        return data[offset] | (data[offset + 1] << 8);
    }

    private int ReadU32(byte[] data, int offset)
    {
        if (data == null || offset < 0 || offset + 3 >= data.Length) { Fail("Room data outside bounds"); return 0; }
        return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
    }

    private void StartScene(int room)
    {
        globals[72] = room;
        if (stackDepth >= 13) { Fail("Room transition nesting overflow"); return; }
        if (activeSlot >= 0)
        {
            returnSlots[stackDepth] = activeSlot;
            returnGenerations[stackDepth++] = generations[activeSlot];
        }
        activeSlot = -1;
        QueueScene(room, 0);
    }

    private void QueueScene(int room, int phase)
    {
        if (stackDepth >= 15) { Fail("Room continuation stack overflow"); return; }
        returnSlots[stackDepth] = -1;
        returnRooms[stackDepth] = room;
        returnPhases[stackDepth++] = phase;
    }

    // A hook runs until stop/yield, just like runScriptNested. Continuations
    // survive frame-budget pauses and execute before the original caller.
    private void ContinueScene(int room, int phase)
    {
        if (phase >= 7) return;
        if (phase == 3)
        {
            InstallRoom(room);
            if (fault == "" && room != 0) QueueScene(room, 4);
            return;
        }
        QueueScene(room, phase + 1);
        if (fault != "") return;
        for (int i = 0; i < 25; i++) arguments[i] = 0;
        if (phase == 0) LaunchInner(globals[30], false, false, 0, 0);
        else if (phase == 1 && roomBytes != null && roomExcdOffs >= 0)
            LaunchInner(10001, false, false, 2, roomExcdOffs);
        else if (phase == 2) LaunchInner(globals[31], false, false, 0, 0);
        else if (phase == 4) LaunchInner(globals[28], false, false, 0, 0);
        else if (phase == 5 && roomBytes != null && roomEncdOffs >= 0)
            LaunchInner(10002, false, false, 2, roomEncdOffs);
        else if (phase == 6) LaunchInner(globals[29], false, false, 0, 0);
    }

    private void InstallRoom(int room)
    {
        KillRoomSlots();
        // startScene resets the camera on every entry: centered, held still
        // until scripts pan or a followed actor walks.
        cameraX = 160;
        cameraDestX = 160;
        talkDelay = 0;
        if (3 < globals.Length) globals[3] = 0;
        currentRoom = room;
        roomResource = room;
        if (room == 0)
        {
            roomBytes = null;
            roomEncdOffs = -1;
            roomExcdOffs = -1;
            numLocalScripts = 0;
            numObjectSlots = 0;
            for (int i = 0; i < 200; i++) { objSlotId[i] = 0; objSlotObcd[i] = 0; }
            if (4 < globals.Length) globals[4] = 0;
            pendingPlaceEgo = -1;
            for (int i = 1; i < 100; i++)
                if (verbId[i] >= 120 && verbId[i] < 200) verbMode[i] = 0;
            return;
        }
        if (roomBlobs == null || room < 0 || room >= roomBlobs.Length || roomBlobs[room] == null)
        {
            Fail("Room " + room + " not converted");
            return;
        }
        byte[] blob = roomBlobs[room].bytes;
        if (blob == null) { Fail("Room " + room + " has no bytes"); return; }
        ParseRoomBlob(blob);
        if (fault != "") return;
        if (4 < globals.Length) globals[4] = room;
        if (22 < globals.Length) globals[22] = roomResource;
        if (17 < globals.Length) globals[17] = screenWidth / 2;
        if (18 < globals.Length) globals[18] = roomWidth - screenWidth / 2;
        // A loadRoomWithEgo transition places ego at the object's walk point
        // (falling back to the target), snaps the camera there, and walks it
        // to the target unless it is (-1, -1). Deferred here because the room
        // (objects, width) only exists after ParseRoomBlob, and InstallRoom
        // runs in a later scheduler phase than the room-changing opcode.
        if (pendingPlaceEgo >= 1 && pendingPlaceEgo < 48)
        {
            int pe = pendingPlaceEgo, po = pendingPlaceObj;
            int px = pendingPlaceX, py = pendingPlaceY;
            pendingPlaceEgo = -1;
            int wx = px, wy = py;
            int wslot = FindObjectSlot(po);
            if (wslot != 0 && (objSlotWalkX[wslot] != 0 || objSlotWalkY[wslot] != 0))
            {
                wx = objSlotWalkX[wslot];
                wy = objSlotWalkY[wslot];
            }
            actorRoom[pe] = currentRoom;
            actorX[pe] = wx;
            actorY[pe] = wy;
            actorVisible[pe] = 1;
            actorWalkCount[pe] = 0;
            int half = screenWidth / 2;
            int lo = half, hi = roomWidth - half;
            if (hi < lo) hi = lo;
            cameraX = wx;
            if (cameraX < lo) cameraX = lo;
            else if (cameraX > hi) cameraX = hi;
            cameraDestX = cameraX;
            if (px != -1) StartActorWalk(pe, px, py, -1);
        }
        // Dialogue choices (verbs 120-199) are per-conversation; a room change
        // ends the conversation, so hide any leftovers (e.g. room 81 Bye
        // exits leaving 124/125 visible over the bar verbs in 28).
        for (int i = 1; i < 100; i++)
            if (verbId[i] >= 120 && verbId[i] < 200) verbMode[i] = 0;
        // Room entries reset the camera (see InstallRoom); explicit pans and
        // actor-follow walk it from there.
        for (int i = 0; i < 256 * 4; i++) palChangeLog[i] = -1;
    }

    private int FindObjectSlot(int obj)
    {
        for (int i = 1; i < 200; i++)
            if (objSlotId[i] == obj) return i;
        return 0;
    }

    // 0 = not found, 1 = room, 2 = inventory.
    private int WhereIsObject(int obj)
    {
        if (obj < 1 || obj >= 1024) return 0;
        if (objectOwner[obj] != 15)
        {
            for (int i = 0; i < 80; i++)
                if (inventoryIds[i] == obj) return 2;
            return 0;
        }
        return FindObjectSlot(obj) != 0 ? 1 : 0;
    }

    private int GetVerbEntrypoint(int obj, int entry)
    {
        if (WhereIsObject(obj) == 0) return 0;
        int slot = FindObjectSlot(obj);
        if (slot == 0 || roomBytes == null) return 0;
        int obcd = objSlotObcd[slot];
        // VERB is a sub-chunk inside the object's OBCD chunk
        // (ScummEngine::findResource): size-walk siblings bounded by the
        // OBCD size instead of hopping across neighboring chunks.
        if (obcd < 0 || obcd + 7 >= roomBytes.Length) return 0;
        int total = (roomBytes[obcd + 4] << 24) | (roomBytes[obcd + 5] << 16)
            | (roomBytes[obcd + 6] << 8) | roomBytes[obcd + 7];
        if (total < 8) return 0;
        int limit = obcd + total;
        if (limit > roomBytes.Length) return 0;
        int end = obcd + 8;
        while (true)
        {
            if (end + 3 >= limit) return 0;
            if (roomBytes[end] == 86 && roomBytes[end + 1] == 69
                && roomBytes[end + 2] == 82 && roomBytes[end + 3] == 66) break;
            if (end + 7 >= limit) return 0;
            int size = (roomBytes[end + 4] << 24) | (roomBytes[end + 5] << 16)
                | (roomBytes[end + 6] << 8) | roomBytes[end + 7];
            if (size < 8) return 0;
            end += size;
            if (end >= limit) return 0;
        }
        int verboffs = end - obcd;
        int p = end + 8;
        while (true)
        {
            if (p >= roomBytes.Length) return 0;
            int id = roomBytes[p];
            if (id == 0) return 0;
            if (id == entry || id == 255) break;
            p += 3;
        }
        if (p + 2 >= roomBytes.Length) return 0;
        return verboffs + (roomBytes[p + 1] | (roomBytes[p + 2] << 8));
    }

    private void GetVerbEntrypointOp()
    {
        int address = ReadAddress();
        int obj = WordOperand(opcode);
        int entry = WordOperandBit(64);
        if (fault != "") return;
        WriteStorage(address, GetVerbEntrypoint(obj, entry));
    }

    // Mirrors o5_drawObject: optional move, overlap clear, then putState.
    private void DrawObjectOp()
    {
        int obj = WordOperand(opcode);
        int subop = ReadByte();
        if (fault != "") return;
        int kind = subop & 31;
        int state = 1;
        int xpos = 255;
        int ypos = 255;
        if (kind == 1)
        {
            xpos = WordOperandMode(subop, 128);
            ypos = WordOperandMode(subop, 64);
        }
        else if (kind == 2)
        {
            state = WordOperandMode(subop, 128);
        }
        else if (kind != 31)
        {
            Fail("DrawObject subop " + kind + " unsupported");
            return;
        }
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        int slot = FindObjectSlot(obj);
        if (slot == 0) return;
        if (xpos != 255)
        {
            objSlotWalkX[slot] += (xpos * 8) - objSlotX[slot];
            objSlotWalkY[slot] += (ypos * 8) - objSlotY[slot];
            objSlotX[slot] = xpos * 8;
            objSlotY[slot] = ypos * 8;
        }
        int x = objSlotX[slot];
        int y = objSlotY[slot];
        int w = objSlotW[slot];
        int h = objSlotH[slot];
        for (int i = 1; i < 200; i++)
        {
            if (objSlotId[i] != 0 && objSlotX[i] == x && objSlotY[i] == y
                && objSlotW[i] == w && objSlotH[i] == h)
                objectState[objSlotId[i]] = 0;
        }
        objectState[obj] = state;
    }

    private void LoadRoomOp()
    {
        int room = ByteOperand(opcode);
        if (fault != "") return;
        StartScene(room);
    }

    // Mirrors o5_getActorWalkBox (0x7B/0xFB): the walkbox the actor stands in.
    private void GetActorWalkBox()
    {
        int address = ReadAddress();
        int act = ByteOperand(opcode);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        int box = 0;
        if (actorRoom[act] == currentRoom)
        {
            for (int b = 1; b < numWalkboxes; b++)
                if (PointInWalkbox(b, actorX[act], actorY[act])) { box = b; break; }
            if (box == 0)
            {
                int near = NearestWalkbox(actorX[act], actorY[act]);
                if (near > 0) box = near;
            }
        }
        WriteStorage(address, box);
    }

    // Mirrors o5_getDist (0x34/0x74/0xB4/0xF4): Chebyshev distance between
    // two object/actor endpoints, 0xFF when either is unresolvable.
    // v5 object ids below 13 are actors; room objects use their walk point.
    private void GetDistOp()
    {
        int address = ReadAddress();
        int o1 = WordOperand(opcode);
        int o2 = WordOperandBit(64);
        if (fault != "") return;
        int x = 0, y = 0, x2 = 0, y2 = 0;
        int acta = 0, actb = 0;
        if (!ObjActXY(o1, ref x, ref y, ref acta) || !ObjActXY(o2, ref x2, ref y2, ref actb))
        {
            WriteStorage(address, 255);
            return;
        }
        if (acta != 0 && actb != 0 && actorRoom[acta] == actorRoom[actb]
            && actorRoom[acta] != 0 && actorRoom[acta] != currentRoom)
        {
            WriteStorage(address, 0);
            return;
        }
        if (acta != 0 && actb == 0)
        {
            int box = 0;
            for (int b = 1; b < numWalkboxes; b++)
                if (PointInWalkbox(b, actorX[acta], actorY[acta])) { box = b; break; }
            if (box > 0)
            {
                ClosestWalkboxPoint(box, x2, y2);
                x2 = nearestX; y2 = nearestY;
            }
        }
        int dx = x - x2; if (dx < 0) dx = -dx;
        int dy = y - y2; if (dy < 0) dy = -dy;
        WriteStorage(address, dx > dy ? dx : dy);
    }

    // Resolves an object-or-actor id to room coordinates. Returns false when
    // the endpoint is unavailable (result 0xFF). actorOut receives the actor
    // number, or 0 for plain objects.
    private bool ObjActXY(int id, ref int x, ref int y, ref int actorOut)
    {
        actorOut = 0;
        if (id >= 1 && id < 13)
        {
            if (actorRoom[id] != currentRoom) return false;
            x = actorX[id]; y = actorY[id]; actorOut = id;
            return true;
        }
        if (id < 0 || id >= 1024) return false;
        if (objectOwner[id] != 15)
        {
            int owner = objectOwner[id];
            if (owner >= 1 && owner < 48 && actorRoom[owner] == currentRoom)
            {
                x = actorX[owner]; y = actorY[owner]; actorOut = owner;
                return true;
            }
            return false;
        }
        int slot = FindObjectSlot(id);
        if (slot == 0) return false;
        x = objSlotWalkX[slot]; y = objSlotWalkY[slot];
        return true;
    }

    private int ValidActor(int act)
    {
        if (act < 1 || act >= 48) { Fail("Invalid actor " + act); return 0; }
        return 1;
    }

    private int ValidObject(int obj)
    {
        if (obj < 0 || obj >= 1024) { Fail("Invalid object " + obj); return 0; }
        return 1;
    }

    private void PutActor()
    {
        int act = ByteOperand(opcode);
        int x = WordOperandBit(64);
        int y = WordOperandBit(32);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        // ScummVM Actor::putActor(x,y) keeps _room; only position changes.
        actorX[act] = x;
        actorY[act] = y;
        actorVisible[act] = 1;
        actorWalkCount[act] = 0;
    }

    private void PutActorInRoom()
    {
        int act = ByteOperand(opcode);
        int room = ByteOperandBit(64);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        actorRoom[act] = room;
        actorWalkCount[act] = 0;
        if (room == 0)
        {
            actorX[act] = 0;
            actorY[act] = 0;
        }
    }

    // Mirrors o5_putActorAtObject: place the actor at the object's walk
    // point, or (240,120) when the object is not around. Room is unchanged
    // (ScummVM putActor keeps _room); only position changes.
    private void PutActorAtObject()
    {
        int act = ByteOperandBit(128);
        int obj = WordOperandBit(64);
        if (fault != "") return;
        if (ValidActor(act) == 0 || ValidObject(obj) == 0) return;
        int x = 240, y = 120;
        int where = WhereIsObject(obj);
        if (where == 1)
        {
            int slot = FindObjectSlot(obj);
            if (slot != 0) { x = objSlotWalkX[slot]; y = objSlotWalkY[slot]; }
        }
        else if (where == 2 && objectOwner[obj] >= 1 && objectOwner[obj] < 48
            && actorRoom[objectOwner[obj]] == currentRoom)
        {
            x = actorX[objectOwner[obj]];
            y = actorY[objectOwner[obj]];
        }
        actorX[act] = x;
        actorY[act] = y;
        actorVisible[act] = 1;
        actorWalkCount[act] = 0;
    }

    private void GetActorRoom()
    {
        int address = ReadAddress();
        int act = ByteOperand(opcode);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        WriteStorage(address, actorRoom[act]);
    }

    private void GetActorXY(bool isX)
    {
        int address = ReadAddress();
        int act = ByteOperand(opcode);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        WriteStorage(address, isX ? actorX[act] : actorY[act]);
    }

    private void GetActorField(int kind)
    {
        int address = ReadAddress();
        int act = ByteOperand(opcode);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        WriteStorage(address, kind == 6 ? actorFacing[act] : actorCostume[act]);
    }

    private void AnimateActor()
    {
        int act = ByteOperand(opcode);
        int anim = ByteOperandBit(64);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        actorAnim[act] = anim;
        if (anim >= 252)
        {
            if (actorRoom[act] == currentRoom) actorWalkCount[act] = 0;
        }
        else if (anim >= 244)
        {
            int dir = anim & 3;
            actorFacing[act] = dir == 0 ? 270 : (dir == 1 ? 90 : (dir == 2 ? 180 : 0));
        }
    }

    // Verbs: full slot bookkeeping; drawing and hit-testing come later.
    private int FindVerbSlot(int verb, int save)
    {
        for (int i = 1; i < 100; i++)
            if (verbId[i] == verb && verbSaveId[i] == save) return i;
        return 0;
    }

    private void VerbOps()
    {
        int verb = ByteOperand(opcode);
        if (fault != "") return;
        int slot = FindVerbSlot(verb, 0);
        verbId[slot] = verb;
        while (fault == "")
        {
            int subop = ReadByte();
            if (subop == 255 || fault != "") break;
            int kind = subop & 31;
            if (kind == 1)
            {
                int img = WordOperand(subop);
                if (fault != "") return;
                if (slot != 0) { verbImgIndex[slot] = img; verbType[slot] = 1; }
            }
            else if (kind == 2)
            {
                byte[] name = ReadInlineString();
                if (fault != "") return;
                if (slot != 0 && name != null)
                {
                    int len = name.Length - 1;
                    if (len < 0) len = 0;
                    if (len > 63) len = 63;
                    if (verbNames[slot] == null) verbNames[slot] = new byte[64];
                    for (int i = 0; i < len; i++) verbNames[slot][i] = name[i];
                    verbNameLen[slot] = len;
                    verbType[slot] = 0;
                    verbImgIndex[slot] = 0;
                }
            }
            else if (kind == 3) { int v = ByteOperand(subop); if (fault != "") return; if (slot != 0) verbColor[slot] = v; }
            else if (kind == 4) { int v = ByteOperand(subop); if (fault != "") return; if (slot != 0) verbHiColor[slot] = v; }
            else if (kind == 5)
            {
                int left = WordOperandMode(subop, 128);
                int top = WordOperandMode(subop, 64);
                if (fault != "") return;
                if (slot != 0) { verbLeft[slot] = left; verbTop[slot] = top; }
            }
            else if (kind == 6) { if (slot != 0) verbMode[slot] = 1; }
            else if (kind == 7) { if (slot != 0) verbMode[slot] = 0; }
            else if (kind == 8)
            {
                if (slot != 0)
                {
                    verbId[slot] = 0;
                    verbMode[slot] = 0;
                    verbNameLen[slot] = 0;
                }
            }
            else if (kind == 9)
            {
                slot = FindVerbSlot(verb, 0);
                if (slot == 0)
                {
                    for (int i = 1; i < 100; i++)
                        if (verbId[i] == 0) { slot = i; break; }
                    if (slot == 0) { Fail("Too many verbs"); return; }
                }
                verbId[slot] = verb;
                verbColor[slot] = 2;
                verbHiColor[slot] = 0;
                verbDimColor[slot] = 8;
                verbType[slot] = 0;
                verbMode[slot] = 0;
                verbSaveId[slot] = 0;
                verbKey[slot] = 0;
                verbCenter[slot] = 0;
                verbImgIndex[slot] = 0;
            }
            else if (kind == 16) { int v = ByteOperand(subop); if (fault != "") return; if (slot != 0) verbDimColor[slot] = v; }
            else if (kind == 17) { if (slot != 0) verbMode[slot] = 2; }
            else if (kind == 18) { int v = ByteOperand(subop); if (fault != "") return; if (slot != 0) verbKey[slot] = v; }
            else if (kind == 19) { if (slot != 0) verbCenter[slot] = 1; }
            else if (kind == 20)
            {
                int id = WordOperand(subop);
                if (fault != "") return;
                if (slot != 0)
                {
                    byte[] source = (id >= 0 && id < stringTable.Length) ? stringTable[id] : null;
                    if (source == null)
                    {
                        verbNameLen[slot] = 0;
                    }
                    else
                    {
                        int len = source.Length - 1;
                        if (len < 0) len = 0;
                        if (len > 63) len = 63;
                        if (verbNames[slot] == null) verbNames[slot] = new byte[64];
                        for (int i = 0; i < len; i++) verbNames[slot][i] = source[i];
                        verbNameLen[slot] = len;
                    }
                    verbType[slot] = 0;
                    verbImgIndex[slot] = 0;
                }
            }
            else if (kind == 22)
            {
                int img = WordOperandMode(subop, 128);
                int obj = ByteOperandMode(subop, 64);
                if (fault != "") return;
                if (slot != 0 && verbImgIndex[slot] != img) { verbImgIndex[slot] = img; verbType[slot] = 1; }
            }
            else if (kind == 23) { int v = ByteOperand(subop); if (fault != "") return; if (slot != 0) verbBkColor[slot] = v; }
            else { Fail("Verb op " + subop + " unsupported"); return; }
        }
    }

    private byte[] ReadInlineString()
    {
        byte[] code = slotCode[activeSlot];
        int start = offsets[activeSlot];
        int end = FindInlineEnd();
        if (fault != "") return null;
        byte[] stored = new byte[end - start];
        for (int i = 0; i < stored.Length; i++) stored[i] = code[start + i];
        offsets[activeSlot] = end;
        return stored;
    }

    private byte[] ReadInlineText()
    {
        return ReadInlineString();
    }

    private void ActorOps()
    {
        int act = ByteOperand(opcode);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        while (fault == "")
        {
            int subop = ReadByte();
            if (subop == 255 || fault != "") break;
            int kind = subop & 31;
            if (kind == 0) { ByteOperand(subop); }
            else if (kind == 1) { int v = ByteOperand(subop); if (fault != "") return; actorCostume[act] = v; }
            else if (kind == 2)
            {
                int x = ByteOperand(subop);
                int y = ByteOperandMode(subop, 64);
                if (fault != "") return;
                actorWalkSpeedX[act] = x;
                actorWalkSpeedY[act] = y;
            }
            else if (kind == 3) { int v = ByteOperand(subop); if (fault != "") return; actorSound[act] = v; }
            else if (kind == 4) { int v = ByteOperand(subop); if (fault != "") return; actorWalkFrame[act] = v; }
            else if (kind == 5)
            {
                int a = ByteOperand(subop);
                int b = ByteOperandMode(subop, 64);
                if (fault != "") return;
                actorTalkStart[act] = a;
                actorTalkStop[act] = b;
            }
            else if (kind == 6) { int v = ByteOperand(subop); if (fault != "") return; actorStandFrame[act] = v; }
            else if (kind == 7) { ByteOperand(subop); ByteOperandMode(subop, 64); ByteOperandMode(subop, 32); }
            else if (kind == 8)
            {
                actorCostume[act] = 0;
                actorWalkSpeedX[act] = 0;
                actorWalkSpeedY[act] = 0;
            }
            else if (kind == 9) { actorElevation[act] = WordOperand(subop); }
            else if (kind == 10)
            {
                actorInitFrame[act] = 1;
                actorWalkFrame[act] = 2;
                actorStandFrame[act] = 3;
                actorTalkStart[act] = 4;
                actorTalkStop[act] = 5;
            }
            else if (kind == 11)
            {
                int i = ByteOperand(subop);
                int j = ByteOperandMode(subop, 64);
                if (fault != "") return;
                if (i < 0 || i >= 32) { Fail("Bad actor palette slot"); return; }
                actorPalette[act * 32 + i] = j;
            }
            else if (kind == 12) { int v = ByteOperand(subop); if (fault != "") return; actorTalkColor[act] = v; }
            else if (kind == 13)
            {
                byte[] name = ReadInlineText();
                if (fault != "") return;
                actorNames[act] = name;
            }
            else if (kind == 14) { int v = ByteOperand(subop); if (fault != "") return; actorInitFrame[act] = v; }
            else if (kind == 16) { int v = ByteOperand(subop); if (fault != "") return; actorWidth[act] = v; }
            else if (kind == 17)
            {
                int x = ByteOperand(subop);
                int y = ByteOperandMode(subop, 64);
                if (fault != "") return;
                actorScaleX[act] = x;
                actorScaleY[act] = y;
            }
            else if (kind == 18) { actorForceClip[act] = 0; }
            else if (kind == 19) { int v = ByteOperand(subop); if (fault != "") return; actorForceClip[act] = v; }
            else if (kind == 20 || kind == 21)
            {
                actorIgnoreBoxes[act] = (subop & 1) == 0 ? 1 : 0;
                actorForceClip[act] = 0;
            }
            else if (kind == 22) { int v = ByteOperand(subop); if (fault != "") return; actorAnimSpeed[act] = v; }
            else if (kind == 23) { int v = ByteOperand(subop); if (fault != "") return; actorShadow[act] = v; }
            else { Fail("Actor op " + subop + " unsupported"); return; }
        }
    }

    private void SetCameraAt()
    {
        // Pan, like panCameraTo: the destination is set and moveCamera walks
        // the current position there 8px per tick. Never snaps.
        cameraDestX = WordOperandBit(128);
    }

    private void PanCameraTo()
    {
        cameraDestX = WordOperandBit(128);
        cameraFollowActor = 0;
    }

    // Mirrors moveCamera for v5: script pans walk 8px per tick toward the
    // destination; a followed actor takes over the destination only while it
    // is moving, so room entries hold their view until the actor walks.
    // Room bounds clamp like cameraMoved.
    private void AdvanceCamera()
    {
        if (cameraFollowActor > 0 && cameraFollowActor < 48
            && actorRoom[cameraFollowActor] == currentRoom
            && GetActorMoving(cameraFollowActor) != 0)
            cameraDestX = actorX[cameraFollowActor];
        if (cameraMaxX > cameraMinX)
        {
            if (cameraDestX < cameraMinX) cameraDestX = cameraMinX;
            if (cameraDestX > cameraMaxX) cameraDestX = cameraMaxX;
        }
        cameraX &= ~7;
        int before = cameraX;
        if (cameraX < cameraDestX) cameraX += 8;
        else if (cameraX > cameraDestX) cameraX -= 8;
        // Snap inside one step: the reference oscillates ±8 around
        // non-aligned destinations (e.g. actor-follow); settling is stable
        // and identical whenever both ends are strip-aligned, as in boot.
        if (cameraX > cameraDestX - 8 && cameraX < cameraDestX + 8) cameraX = cameraDestX;
        int half = screenWidth / 2;
        int lo = half, hi = roomWidth - half;
        if (hi < lo) hi = lo;
        if (cameraX < lo) cameraX = lo;
        else if (cameraX > hi) cameraX = hi;
        // VAR_CAMERA_POS_X is published only while a scroll script is
        // installed (moveCamera); never clobber the slot otherwise.
        if (cameraX != before && globals.Length > 27 && globals[27] != 0
            && 2 < globals.Length) globals[2] = cameraX;
    }

    public int GetCameraScrollX()
    {
        int max = roomWidth - screenWidth;
        if (max < 0) max = 0;
        int scroll = cameraX - screenWidth / 2;
        if (scroll < 0) scroll = 0;
        if (scroll > max) scroll = max;
        return scroll;
    }

    private void PutState()
    {
        int obj = WordOperand(opcode);
        int state = ByteOperandBit(64);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        objectState[obj] = state;
    }

    private void GetState()
    {
        int address = ReadAddress();
        int obj = WordOperand(opcode);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        WriteStorage(address, objectState[obj]);
    }

    private void PutOwner()
    {
        int obj = WordOperand(opcode);
        int owner = ByteOperandBit(64);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        objectOwner[obj] = owner;
    }

    private void GetOwner()
    {
        int address = ReadAddress();
        int obj = WordOperand(opcode);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        WriteStorage(address, objectOwner[obj]);
    }

    private int GetClass(int obj, int cls)
    {
        cls &= 127;
        if (cls < 1 || cls > 32) { Fail("Invalid class"); return 0; }
        return (objectClass[obj] & (1 << (cls - 1))) != 0 ? 1 : 0;
    }

    private void PutClass(int obj, int cls, bool set)
    {
        cls &= 127;
        if (cls < 1 || cls > 32) { Fail("Invalid class"); return; }
        if (set) objectClass[obj] |= 1 << (cls - 1);
        else objectClass[obj] &= ~(1 << (cls - 1));
    }

    private void SetClass()
    {
        int obj = WordOperand(opcode);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        while (fault == "")
        {
            int mode = ReadByte();
            if (mode == 255 || fault != "") break;
            int cls = WordOperand(mode);
            if (fault != "") return;
            if ((cls & 255) == 0) objectClass[obj] = 0;
            else PutClass(obj, cls, (cls & 128) != 0);
        }
    }

    private void IfClassOfIs()
    {
        int obj = WordOperand(opcode);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        bool cond = true;
        while (fault == "")
        {
            int mode = ReadByte();
            if (mode == 255 || fault != "") break;
            int cls = WordOperand(mode);
            if (fault != "") return;
            int b = GetClass(obj, cls);
            if (fault != "") return;
            if (((cls & 128) != 0 && b == 0) || ((cls & 128) == 0 && b != 0)) cond = false;
        }
        if (fault != "") return;
        Branch(cond);
    }

    private int[] exprStack = new int[64];
    private int exprSp;

    // v5 RPN expression evaluator with its own value stack.
    private void ExpressionOp()
    {
        int address = ReadAddress();
        if (fault != "") return;
        exprSp = 0;
        bool done = false;
        while (!done && fault == "")
        {
            int subop = ReadByte();
            if (subop == 255 || fault != "")
            {
                done = true;
                break;
            }
            int kind = subop & 31;
            if (kind == 1) { ExprPush(WordOperand(subop)); }
            else if (kind >= 2 && kind <= 5)
            {
                if (exprSp < 2) { Fail("Expression stack underflow"); return; }
                int divisor = exprStack[--exprSp];
                int left = exprStack[--exprSp];
                long result = 0;
                if (kind == 2) result = (long)left + divisor;
                else if (kind == 3) result = (long)left - divisor;
                else if (kind == 4) result = (long)left * divisor;
                else
                {
                    if (divisor == 0) { Fail("Division by zero"); return; }
                    result = (long)left / divisor;
                }
                ExprPush(Wrap32(result));
            }
            else if (kind == 6)
            {
                int savedOpcode = opcode;
                int savedScript = instructionScript;
                int savedOffset = instructionOffset;
                int nested = ReadByte();
                if (fault != "") return;
                opcode = nested;
                instructionScript = scriptIds[activeSlot];
                instructionOffset = offsets[activeSlot] - 1;
                ExecuteInstruction();
                if (fault != "") return;
                opcode = savedOpcode;
                instructionScript = savedScript;
                instructionOffset = savedOffset;
                if (globals.Length > 0) ExprPush(globals[0]);
                else ExprPush(0);
            }
        }
        if (fault != "") return;
        if (exprSp < 1) { Fail("Empty expression"); return; }
        WriteStorage(address, exprStack[--exprSp]);
    }

    private void ExprPush(int value)
    {
        if (exprSp >= 64) { Fail("Expression stack overflow"); return; }
        exprStack[exprSp++] = value;
    }

    private void FreezeOp()
    {
        int flag = ByteOperand(opcode);
        if (fault != "") return;
        if (flag != 0) FreezeScripts(flag);
        else UnfreezeScripts();
    }

    private void SetVarRange()
    {
        int address = ReadAddress();
        int count = ReadByte();
        if (fault != "") return;
        while (count-- > 0 && fault == "")
        {
            int value = ((opcode & 128) != 0) ? Signed16(ReadWord()) : ReadByte();
            if (fault != "") return;
            WriteStorage(address, value);
            address++;
        }
    }

    // Mirrors o5_getStringWidth (0x67/0xE7): 8px per printable character,
    // matching the hit-testing in VerbNameWidth.
    private void GetStringWidth()
    {
        int address = ReadAddress();
        int id = ByteOperand(opcode);
        if (fault != "") return;
        int width = 0;
        if (id >= 0 && id < stringTable.Length && stringTable[id] != null)
        {
            byte[] text = stringTable[id];
            int i = 0;
            while (i < text.Length)
            {
                int c = text[i++];
                if (c == 0) break;
                if (c == 255)
                {
                    if (i >= text.Length) break;
                    int control = text[i++];
                    if (control != 1 && control != 2 && control != 3 && control != 8) i += 2;
                    continue;
                }
                if (c >= 32 && c < 127) width += 8;
            }
        }
        WriteStorage(address, width);
    }

    private void ActorFollowCamera()
    {
        int act = ByteOperand(opcode);
        if (fault != "") return;
        cameraFollowActor = act;
        // Mirrors actorFollowCamera/setCameraFollows: snap when the actor is
        // over half a screen away (like setCameraAt).
        if (act < 1 || act >= 48 || actorRoom[act] != currentRoom) return;
        int dx = actorX[act] - cameraX;
        if (dx < 0) dx = -dx;
        if (dx <= screenWidth / 2) return;
        int half = screenWidth / 2;
        int lo = half, hi = roomWidth - half;
        if (hi < lo) hi = lo;
        cameraX = actorX[act];
        if (cameraX < lo) cameraX = lo;
        else if (cameraX > hi) cameraX = hi;
        cameraDestX = cameraX;
    }

    // Mirrors o5_matrixOps: walkbox flags/scale plus the itinerary rebuild.
    private void MatrixOps()
    {
        int subop = ReadByte();
        if (fault != "") return;
        int kind = subop & 31;
        if (kind == 1)
        {
            int box = ByteOperand(subop);
            int val = ByteOperandMode(subop, 64);
            if (fault != "") return;
            if (box >= 0 && box < numWalkboxes) walkboxData[box * 11 + 9] = val & 255;
        }
        else if (kind == 2)
        {
            int box = ByteOperand(subop);
            int scale = ByteOperandMode(subop, 64);
            if (fault != "") return;
            if (box >= 0 && box < numWalkboxes) walkboxData[box * 11 + 10] = scale & 65535;
        }
        else if (kind == 3)
        {
            int box = ByteOperand(subop);
            int slot = ByteOperandMode(subop, 64);
            if (fault != "") return;
            if (box >= 0 && box < numWalkboxes) walkboxData[box * 11 + 10] = ((slot - 1) | 32768) & 65535;
        }
        else if (kind == 4) CreateBoxMatrix();
        else { Fail("Matrix op " + subop + " unsupported"); return; }
    }

    private bool SidesTouch(int x1, int y1, int x2, int y2, int x3, int y3, int x4, int y4)
    {
        if (x1 == x2 && x3 == x4 && x1 == x3)
        {
            int lo1 = y1 < y2 ? y1 : y2, hi1 = y1 < y2 ? y2 : y1;
            int lo2 = y3 < y4 ? y3 : y4, hi2 = y3 < y4 ? y4 : y3;
            if (hi1 < lo2 || hi2 < lo1) return false;
            if ((hi1 == lo2 || hi2 == lo1) && hi1 != lo1 && hi2 != lo2) return false;
            return true;
        }
        if (y1 == y2 && y3 == y4 && y1 == y3)
        {
            int lo1 = x1 < x2 ? x1 : x2, hi1 = x1 < x2 ? x2 : x1;
            int lo2 = x3 < x4 ? x3 : x4, hi2 = x3 < x4 ? x4 : x3;
            if (hi1 < lo2 || hi2 < lo1) return false;
            if ((hi1 == lo2 || hi2 == lo1) && hi1 != lo1 && hi2 != lo2) return false;
            return true;
        }
        return false;
    }

    private bool BoxesTouch(int a, int b)
    {
        if (a < 0 || b < 0 || a >= numWalkboxes || b >= numWalkboxes) return false;
        if ((walkboxData[a * 11 + 9] & 128) != 0 || (walkboxData[b * 11 + 9] & 128) != 0) return false;
        for (int k = 0; k < 4; k++)
        {
            int ax = walkboxData[a * 11 + k * 2], ay = walkboxData[a * 11 + k * 2 + 1];
            int bx = walkboxData[a * 11 + ((k + 1) % 4) * 2], by = walkboxData[a * 11 + ((k + 1) % 4) * 2 + 1];
            for (int m = 0; m < 4; m++)
            {
                int cx = walkboxData[b * 11 + m * 2], cy = walkboxData[b * 11 + m * 2 + 1];
                int dx = walkboxData[b * 11 + ((m + 1) % 4) * 2], dy = walkboxData[b * 11 + ((m + 1) % 4) * 2 + 1];
                if (SidesTouch(ax, ay, bx, by, cx, cy, dx, dy)) return true;
            }
        }
        return false;
    }

    // Mirrors createBoxMatrix: Floyd-Warshall over touching boxes,
    // RLE-compressed per row exactly like the room blobs.
    private void CreateBoxMatrix()
    {
        int num = numWalkboxes;
        if (num < 1) { walkMatrix = new byte[0]; return; }
        if (num > 64) num = 64;
        int size = 64;
        int[] adj = new int[size * size];
        int[] nxt = new int[size * size];
        for (int i = 0; i < num; i++)
            for (int j = 0; j < num; j++)
            {
                if (i == j) { adj[i * size + j] = 0; nxt[i * size + j] = j; }
                else if (BoxesTouch(i, j)) { adj[i * size + j] = 1; nxt[i * size + j] = j; }
                else { adj[i * size + j] = 255; nxt[i * size + j] = 255; }
            }
        for (int k = 0; k < num; k++)
            for (int i = 0; i < num; i++)
                for (int j = 0; j < num; j++)
                {
                    if (i == j) continue;
                    int via = adj[i * size + k] + adj[k * size + j];
                    if (via < adj[i * size + j]) { adj[i * size + j] = via; nxt[i * size + j] = nxt[i * size + k]; }
                }
        int[] tmp = new int[4096 + 64];
        int count = 0;
        for (int i = 0; i < num; i++)
        {
            int j = 0;
            while (j < num)
            {
                int n = nxt[i * size + j];
                if (n == 255) { j++; continue; }
                int lo = j;
                while (j + 1 < num && nxt[i * size + j + 1] == n) j++;
                tmp[count++] = lo; tmp[count++] = j; tmp[count++] = n;
                j++;
            }
            tmp[count++] = 255;
        }
        walkMatrix = new byte[count];
        for (int i = 0; i < count; i++) walkMatrix[i] = (byte)(tmp[i] & 255);
    }

    private int FindObjectAt(int x, int y)
    {
        for (int i = 1; i <= numObjectSlots; i++)
        {
            int id = objSlotId[i];
            if (id < 1) continue;
            if ((objectClass[id] & (1 << 31)) != 0) continue;
            int slot = i;
            while (true)
            {
                int flags = objSlotFlags[slot];
                int parentState = flags == 128 ? 1 : (flags & 15);
                int parent = objSlotParent[slot];
                if (parent == 0)
                {
                    if (objSlotX[i] <= x && objSlotX[i] + objSlotW[i] > x
                        && objSlotY[i] <= y && objSlotY[i] + objSlotH[i] > y)
                        return id;
                    break;
                }
                int parentId = (parent >= 1 && parent <= numObjectSlots) ? objSlotId[parent] : 0;
                int parentStateNow = (parentId >= 1 && parentId < 1024) ? (objectState[parentId] & 15) : 0;
                if (parentStateNow != parentState) break;
                slot = parent;
                if (slot < 1 || slot > numObjectSlots) break;
            }
        }
        return 0;
    }

    private void FindObjectOp()
    {
        int address = ReadAddress();
        int x = ByteOperand(opcode);
        int y = ByteOperandBit(64);
        if (fault != "") return;
        WriteStorage(address, FindObjectAt(x, y));
    }

    // Actor picking needs mask-buffer usage bits; boot only needs the
    // no-hit answer in the black room. Record inputs for later work.
    private void ActorFromPosOp()
    {
        int address = ReadAddress();
        int x = WordOperand(opcode);
        int y = WordOperandBit(64);
        if (fault != "") return;
        WriteStorage(address, 0);
    }

    // Mirrors o5_startObject: object verb entry runs as a room object script.
    private void StartObjectOp()
    {
        int obj = WordOperand(opcode);
        int entry = ByteOperandBit(64);
        ReadVarargs();
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        if (WhereIsObject(obj) == 0) return;
        int slot = FindObjectSlot(obj);
        if (slot == 0 || roomBytes == null) return;
        int entryOff = GetVerbEntrypoint(obj, entry);
        if (fault != "" || entryOff == 0) return;
        LaunchInner(obj, false, false, 2, objSlotObcd[slot] + entryOff);
    }

    public int GetActorMoving(int act)
    {
        return act > 0 && act < 48 && actorWalkIndex[act] < actorWalkCount[act] ? 1 : 0;
    }

    // Mirrors o5_getActorMoving (0x56/0xD6): whether the actor is walking.
    private void GetActorMovingOp()
    {
        int address = ReadAddress();
        int act = ByteOperand(opcode);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        WriteStorage(address, GetActorMoving(act));
    }

    public int GetActorWalkFrame(int act)
    {
        return act > 0 && act < 48 ? actorWalkFrame[act] : 2;
    }

    public int GetActorStandFrame(int act)
    {
        return act > 0 && act < 48 ? actorStandFrame[act] : 3;
    }

    private bool PointInWalkbox(int box, int x, int y)
    {
        int p = box * 11;
        int minX = walkboxData[p], maxX = minX;
        int minY = walkboxData[p + 1], maxY = minY;
        bool positive = false, negative = false;
        for (int k = 0; k < 4; k++)
        {
            int a = p + k * 2, b = p + ((k + 1) % 4) * 2;
            int ax = walkboxData[a], ay = walkboxData[a + 1];
            if (ax < minX) minX = ax;
            if (ax > maxX) maxX = ax;
            if (ay < minY) minY = ay;
            if (ay > maxY) maxY = ay;
            long cross = (long)(walkboxData[b] - ax) * (y - ay)
                - (long)(walkboxData[b + 1] - ay) * (x - ax);
            if (cross > 0) positive = true;
            if (cross < 0) negative = true;
        }
        return !(positive && negative) && x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private void ClosestWalkboxPoint(int box, int x, int y)
    {
        nearestX = x; nearestY = y;
        if (PointInWalkbox(box, x, y)) return;
        long best = long.MaxValue;
        int p = box * 11;
        for (int k = 0; k < 4; k++)
        {
            int a = p + k * 2, b = p + ((k + 1) % 4) * 2;
            int ax = walkboxData[a], ay = walkboxData[a + 1];
            int dx = walkboxData[b] - ax, dy = walkboxData[b + 1] - ay;
            long length = (long)dx * dx + (long)dy * dy;
            long dot = (long)(x - ax) * dx + (long)(y - ay) * dy;
            if (dot < 0) dot = 0;
            if (dot > length) dot = length;
            int px = ax + (length == 0 ? 0 : (int)(dx * dot / length));
            int py = ay + (length == 0 ? 0 : (int)(dy * dot / length));
            long distance = (long)(px - x) * (px - x) + (long)(py - y) * (py - y);
            if (distance < best) { best = distance; nearestX = px; nearestY = py; }
        }
    }

    private int NearestWalkbox(int x, int y)
    {
        int result = -1, bestX = x, bestY = y;
        long best = long.MaxValue;
        // v5 box zero is the invalid-box sentinel.
        for (int box = numWalkboxes - 1; box >= 1; box--)
        {
            if ((walkboxData[box * 11 + 9] & 128) != 0) continue;
            ClosestWalkboxPoint(box, x, y);
            long distance = (long)(nearestX - x) * (nearestX - x)
                + (long)(nearestY - y) * (nearestY - y);
            if (distance < best)
            {
                best = distance; result = box; bestX = nearestX; bestY = nearestY;
            }
        }
        nearestX = bestX; nearestY = bestY;
        return result;
    }

    private int NextWalkbox(int from, int to)
    {
        if (from == to) return to;
        if (walkMatrix == null) return -1;
        int pos = 0;
        for (int row = 0; row < from; row++)
        {
            while (pos < walkMatrix.Length && walkMatrix[pos] != 255) pos += 3;
            pos++;
        }
        while (pos + 2 < walkMatrix.Length && walkMatrix[pos] != 255)
        {
            if (walkMatrix[pos] <= to && to <= walkMatrix[pos + 1])
                return walkMatrix[pos + 2] == 255 ? -1 : walkMatrix[pos + 2];
            pos += 3;
        }
        return -1;
    }

    private bool WalkboxPortal(int from, int to)
    {
        long best = long.MaxValue;
        int px = 0, py = 0;
        // Find a shared corner/edge point, including degenerate line boxes.
        for (int side = 0; side < 2; side++)
        {
            int a = side == 0 ? from : to, b = side == 0 ? to : from;
            for (int k = 0; k < 4; k++)
            {
                int x = walkboxData[a * 11 + k * 2], y = walkboxData[a * 11 + k * 2 + 1];
                ClosestWalkboxPoint(b, x, y);
                long distance = (long)(nearestX - x) * (nearestX - x)
                    + (long)(nearestY - y) * (nearestY - y);
                if (distance < best) { best = distance; px = (x + nearestX) / 2; py = (y + nearestY) / 2; }
            }
        }
        nearestX = px; nearestY = py;
        return best <= 4;
    }

    // Engine entry for walk scripts and host tests; putActor stays immediate.
    public void StartActorWalk(int act, int x, int y, int endDir)
    {
        if (ValidActor(act) == 0) return;
        actorWalkCount[act] = 0; actorWalkIndex[act] = 0;
        actorWalkCredit[act] = 0;
        actorMoveRemainderX[act] = 0; actorMoveRemainderY[act] = 0;
        actorWalkEndDir[act] = endDir;
        if (actorRoom[act] != currentRoom)
        {
            actorX[act] = x; actorY[act] = y;
            if (endDir >= 0) actorFacing[act] = endDir;
            return;
        }
        int count = 0, baseIndex = act * 64;
        if (actorIgnoreBoxes[act] == 0 && numWalkboxes > 1)
        {
            int from = NearestWalkbox(actorX[act], actorY[act]);
            int startX = nearestX, startY = nearestY;
            int to = NearestWalkbox(x, y);
            x = nearestX; y = nearestY;
            if (from < 1 || to < 1) return;
            // Approach the nearest legal point before following the itinerary.
            actorPathX[baseIndex + count] = startX;
            actorPathY[baseIndex + count++] = startY;
            for (int hop = 0; from != to && hop < 62; hop++)
            {
                int next = NextWalkbox(from, to);
                if (next < 1 || next >= numWalkboxes || next == from
                    || (walkboxData[next * 11 + 9] & 128) != 0 || !WalkboxPortal(from, next)) return;
                actorPathX[baseIndex + count] = nearestX;
                actorPathY[baseIndex + count++] = nearestY;
                from = next;
            }
            if (from != to) return;
        }
        actorPathX[baseIndex + count] = x;
        actorPathY[baseIndex + count++] = y;
        actorWalkCount[act] = count;
        finished = false;
    }

    // Explicit diagnostic placement after a debugger-style room jump. Normal
    // game scripts still own actor placement through putActor/putActorInRoom.
    public void BeginLookoutWalkTest()
    {
        if (fault != "" || currentRoom != 38) return;
        int ego = ReadGlobal(1);
        if (ValidActor(ego) == 0) return;
        actorRoom[ego] = 38;
        actorVisible[ego] = 1;
        actorX[ego] = 312;
        actorY[ego] = 73;
        actorIgnoreBoxes[ego] = 0;
        StartActorWalk(ego, 154, 106, 270);
    }

    public int GetActorClipPlane(int act)
    {
        if (act < 1 || act >= 48 || actorRoom[act] != currentRoom) return 0;
        if (actorForceClip[act] != 0) return actorForceClip[act];
        if ((objectClass[act] & (1 << 19)) != 0 || actorIgnoreBoxes[act] != 0) return 0;
        int box = NearestWalkbox(actorX[act], actorY[act]);
        return box > 0 ? walkboxData[box * 11 + 8] : 0;
    }

    public int GetActorRenderScaleX(int act)
    {
        if (act < 1 || act >= 48) return 255;
        return ActorBoxScale(act, actorScaleX[act]);
    }

    public int GetActorRenderScaleY(int act)
    {
        if (act < 1 || act >= 48) return 255;
        return ActorBoxScale(act, actorScaleY[act]);
    }

    private int ActorBoxScale(int act, int fallback)
    {
        if (actorRoom[act] != currentRoom || actorIgnoreBoxes[act] != 0) return fallback;
        int box = NearestWalkbox(actorX[act], actorY[act]);
        if (box < 1) return fallback;
        int scale = walkboxData[box * 11 + 10];
        if ((scale & 32768) != 0)
        {
            int slot = (scale & 32767) * 4;
            if (slot + 3 >= scaleSlots.Length) return fallback;
            int s1 = scaleSlots[slot], y1 = scaleSlots[slot + 1];
            int s2 = scaleSlots[slot + 2], y2 = scaleSlots[slot + 3];
            if (y1 == y2) return fallback;
            int y = actorY[act] < 0 ? 0 : actorY[act];
            scale = s1 + (s2 - s1) * (y - y1) / (y2 - y1);
        }
        return scale < 1 ? 1 : (scale > 255 ? 255 : scale);
    }

    public int GetActorElevation(int act)
    {
        return act > 0 && act < 48 ? actorElevation[act] : 0;
    }

    // Overhead dialogue placement mirrors displayDialog for v5: above the
    // speaker's head with the VAR_V5_TALK_STRING_Y offset scaled by the
    // actor's box scale, clamped to the 320-wide screen. Positioned prints
    // (SO_AT) report their fixed coordinates instead.
    public int GetTalkTimer()
    {
        return talkDelay;
    }

    public int GetActorAnimStep(int act)
    {
        return act > 0 && act < 48 ? actorAnimStep[act] : 0;
    }

    public int GetActorAnim(int act)
    {
        return act > 0 && act < 48 ? actorAnim[act] : 0;
    }

    public int GetTalkActor()
    {
        return talkDelay > 0 ? talkingActor : -1;
    }

    public int GetTalkOverhead()
    {
        return lastPrintOverhead;
    }

    public int GetTalkX(int act)
    {
        if (lastPrintOverhead == 0) return lastPrintX;
        if (act < 1 || act >= 48) return -1;
        int x = actorX[act];
        if (x < 80) x = 80;
        if (x > 240) x = 240;
        return x;
    }

    public int GetTalkY(int act)
    {
        if (lastPrintOverhead == 0) return lastPrintY;
        if (act < 1 || act >= 48) return -1;
        int y = actorY[act] - actorElevation[act];
        int v = globals.Length > 54 ? globals[54] : -80;
        if (v < 0)
        {
            int s = GetActorRenderScaleY(act) * v / 255;
            y += (v - s) / 2 + s;
        }
        else y = v;
        return y < 1 ? 1 : y;
    }

    public Color GetTalkColor(int act)
    {
        int index = lastPrintColor >= 0 ? lastPrintColor
            : (act > 0 && act < 48 ? actorTalkColor[act] : 15);
        if (roomPalette != null && index >= 0 && index * 3 + 2 < roomPalette.Length)
            return new Color((roomPalette[index * 3] & 255) / 255f,
                (roomPalette[index * 3 + 1] & 255) / 255f,
                (roomPalette[index * 3 + 2] & 255) / 255f, 1f);
        return Color.white;
    }

    private void AdvanceActorWalks()
    {
        for (int act = 1; act < 48; act++)
        {
            if (GetActorMoving(act) == 0) continue;
            if (actorRoom[act] != currentRoom) { actorWalkCount[act] = 0; continue; }
            actorWalkCredit[act] += tickJiffies;
            // 240 jiffies/sec, one movement update per four jiffies.
            while (actorWalkCredit[act] >= 4 && GetActorMoving(act) != 0)
            {
                actorWalkCredit[act] -= 4;
                int p = act * 64 + actorWalkIndex[act];
                int dx = actorPathX[p] - actorX[act], dy = actorPathY[p] - actorY[act];
                int ax = dx < 0 ? -dx : dx, ay = dy < 0 ? -dy : dy;
                int speedX = actorWalkSpeedX[act] > 0 ? actorWalkSpeedX[act] : 8;
                int speedY = actorWalkSpeedY[act] > 0 ? actorWalkSpeedY[act] : 2;
                int scale = GetActorRenderScaleX(act);
                // Keep fractional pixels so tiny actors still make progress.
                int vx = speedX * scale, vy = speedY * scale;
                if ((long)ax * speedY > (long)ay * speedX)
                    vy = ax == 0 ? 0 : (int)((long)vx * ay / ax);
                else vx = ay == 0 ? 0 : (int)((long)vy * ax / ay);
                actorMoveRemainderX[act] += vx;
                actorMoveRemainderY[act] += vy;
                int sx = actorMoveRemainderX[act] / 255, sy = actorMoveRemainderY[act] / 255;
                actorMoveRemainderX[act] %= 255; actorMoveRemainderY[act] %= 255;
                if (sx > ax) sx = ax;
                if (sy > ay) sy = ay;
                actorX[act] += dx < 0 ? -sx : sx;
                actorY[act] += dy < 0 ? -sy : sy;
                if (ax > ay) actorFacing[act] = dx < 0 ? 270 : 90;
                else if (ay > 0) actorFacing[act] = dy < 0 ? 0 : 180;
                if (actorX[act] == actorPathX[p] && actorY[act] == actorPathY[p])
                {
                    actorWalkIndex[act]++;
                    actorMoveRemainderX[act] = 0; actorMoveRemainderY[act] = 0;
                    if (GetActorMoving(act) == 0 && actorWalkEndDir[act] >= 0)
                        actorFacing[act] = actorWalkEndDir[act];
                }
            }
        }
    }

    // Walk commands schedule movement; putActor remains an immediate placement.
    private void WalkActorToOp()
    {
        int act = ByteOperand(opcode);
        int x = WordOperandBit(64);
        int y = WordOperandBit(32);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        StartActorWalk(act, x, y, -1);
    }

    // Mirrors o5_pickupObject: move a room object into the inventory,
    // retag it owned/untouchable/state 1, then run the inventory script.
    private void PickupObjectOp()
    {
        int obj = WordOperand(opcode);
        int room = ByteOperandBit(64);
        if (fault != "") return;
        if (ValidObject(obj) == 0) return;
        if (room == 0) room = currentRoom;
        int invSlot = -1;
        for (int i = 0; i < 80; i++)
        {
            if (inventoryIds[i] == 0) { invSlot = i; break; }
        }
        if (invSlot < 0) { Fail("Inventory full"); return; }
        inventoryIds[invSlot] = obj;
        int slot = FindObjectSlot(obj);
        if (slot != 0)
        {
            objSlotId[slot] = 0;
            objSlotX[slot] = 0;
            objSlotY[slot] = 0;
            objSlotW[slot] = 0;
            objSlotH[slot] = 0;
        }
        if (1 < globals.Length) objectOwner[obj] = globals[1];
        else objectOwner[obj] = 0;
        objectClass[obj] |= (1 << 31);
        objectState[obj] = 1;
        int inventoryScript = globals.Length > 34 ? globals[34] : 0;
        if (inventoryScript != 0)
        {
            for (int i = 0; i < 25; i++) arguments[i] = 0;
            arguments[0] = 1;
            LaunchInner(inventoryScript, false, false, 0, 0);
        }
    }

    // Mirrors o5_wait: subop kinds wait for actor/message/camera/sentence.
    // Yield rewinds to the opcode (like breakHere); return continues.
    private void WaitOp()
    {
        int subop = ReadByte();
        if (fault != "") return;
        int kind = subop & 31;
        if (kind == 1)
        {
            int act = ByteOperand(subop);
            if (fault != "") return;
            if (ValidActor(act) == 0) return;
            if (actorRoom[act] == currentRoom && GetActorMoving(act) != 0)
            {
                offsets[activeSlot] = instructionOffset;
                activeSlot = -1;
            }
            return;
        }
        else if (kind == 2)
        {
            if (3 < globals.Length && globals[3] != 0)
            {
                offsets[activeSlot] = instructionOffset;
                activeSlot = -1;
            }
            return;
        }
        else if (kind == 3)
        {
            // No smooth camera motion yet; camera is never moving.
            return;
        }
        else if (kind == 4)
        {
            int sentenceScript = globals.Length > 33 ? globals[33] : 0;
            if (sentenceNum > 0 || IsScriptRunning(sentenceScript))
            {
                offsets[activeSlot] = instructionOffset;
                activeSlot = -1;
            }
            return;
        }
        Fail("Wait subop " + kind + " unsupported");
    }

    // Mirrors o5_walkActorToObject: walk the actor to the object's walk
    // box point instantly (no pathfinding yet) and face its direction.
    private void WalkActorToObjectOp()
    {
        int act = ByteOperand(opcode);
        int obj = WordOperandBit(64);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        if (ValidObject(obj) == 0) return;
        int slot = FindObjectSlot(obj);
        if (slot == 0)
        {
            if (WhereIsObject(obj) != 2) return;
            for (int i = 0; i < 80; i++)
                if (inventoryIds[i] == obj) return;
            return;
        }
        int dir = objSlotDir[slot];
        StartActorWalk(act, objSlotWalkX[slot], objSlotWalkY[slot],
            dir == 0 ? 270 : (dir == 1 ? 90 : (dir == 2 ? 180 : 0)));
    }

    // Mirrors o5_faceActor (v5): turn toward the object, 90 (right) or 270.
    // Missing actors/objects are a silent no-op like derefActorSafe.
    private void FaceActorOp()
    {
        int act = ByteOperand(opcode);
        int obj = WordOperandBit(64);
        if (fault != "") return;
        if (ValidActor(act) == 0) return;
        if (ValidObject(obj) == 0) return;
        int slot = FindObjectSlot(obj);
        if (slot == 0) return;
        actorFacing[act] = objSlotX[slot] > actorX[act] ? 90 : 270;
    }

    // Mirrors o5_getInventoryCount: count carried objects owned by owner.
    private void GetInventoryCountOp()
    {
        int address = ReadAddress();
        int owner = ByteOperand(opcode);
        if (fault != "") return;
        int count = 0;
        for (int i = 0; i < 80; i++)
        {
            int obj = inventoryIds[i];
            if (obj != 0 && obj >= 0 && obj < 1024 && objectOwner[obj] == owner) count++;
        }
        WriteStorage(address, count);
    }

    // Mirrors o5_findInventory: idx-th carried object owned by owner.
    private void FindInventoryOp()
    {
        int address = ReadAddress();
        int owner = ByteOperand(opcode);
        int index = ByteOperandBit(64);
        if (fault != "") return;
        int count = 0;
        int found = 0;
        for (int i = 0; i < 80; i++)
        {
            int obj = inventoryIds[i];
            if (obj != 0 && obj >= 0 && obj < 1024 && objectOwner[obj] == owner)
            {
                count++;
                if (count == index) { found = obj; break; }
            }
        }
        WriteStorage(address, found);
    }

    private void LoadRoomWithEgo()
    {
        int obj = WordOperand(opcode);
        int room = ByteOperandBit(64);
        int x = Signed16(ReadWord());
        int y = Signed16(ReadWord());
        if (fault != "") return;
        int ego = globals.Length > 1 ? globals[1] : 0;
        if (ValidActor(ego) == 0) return;
        StartScene(room);
        if (fault != "") return;
        // Placement, camera snap, and arrival walk are deferred to InstallRoom
        // (room objects/width only exist after ParseRoomBlob, which runs in a
        // later scheduler phase). Follow engages immediately.
        pendingPlaceEgo = ego;
        pendingPlaceObj = obj;
        pendingPlaceX = x;
        pendingPlaceY = y;
        cameraFollowActor = ego;
        if (38 < globals.Length) globals[38] = obj;
        if (38 < globals.Length) globals[38] = 0;
    }

    private void LoadGlobalTables()
    {
        for (int i = 0; i < 1024; i++)
        {
            objectOwner[i] = 0;
            objectState[i] = 0;
            objectRoom[i] = 0;
            objectClass[i] = 0;
            objectParent[i] = 0;
        }
        numObjectsTracked = 0;
        if (globalTables == null) return;
        byte[] data = globalTables.bytes;
        if (data == null || data.Length < 2) { Fail("Bad object tables"); return; }
        int count = data[0] | (data[1] << 8);
        if (count <= 0 || count > 1024 || data.Length != 2 + count + count + count * 4)
        {
            Fail("Bad object tables");
            return;
        }
        numObjectsTracked = count;
        for (int i = 0; i < count; i++)
        {
            objectOwner[i] = data[2 + i] & 15;
            objectState[i] = data[2 + count + i] & 15;
            int baseOff = 2 + count + count + i * 4;
            int c = data[baseOff]
                | (data[baseOff + 1] << 8)
                | (data[baseOff + 2] << 16)
                | (data[baseOff + 3] << 24);
            objectClass[i] = c;
        }
    }

    private void ParseRoomBlob(byte[] blob)
    {
        roomWidth = 0;
        roomHeight = 0;
        roomPalette = null;
        numWalkboxes = 0;
        walkMatrix = null;
        roomTrans = 255;
        numObjectSlots = 0;
        numLocalScripts = 0;
        roomEncdOffs = -1;
        roomExcdOffs = -1;
        roomBytes = null;
        if (blob == null || blob.Length < 12) { Fail("Bad room blob"); return; }
        int schema = ReadU16(blob, 0);
        if (fault != "" || (schema != 1 && schema != 2)) { Fail("Unsupported room blob"); return; }
        roomWidth = ReadU16(blob, 2);
        roomHeight = ReadU16(blob, 4);
        int pos = 6;
        if (fault != "") return;
        if (blob[pos++] != 0)
        {
            if (pos + 768 > blob.Length) { Fail("Bad room palette"); return; }
            roomPalette = new byte[768];
            for (int i = 0; i < 768; i++) roomPalette[i] = blob[pos + i];
            pos += 768;
        }
        numWalkboxes = blob[pos++];
        if (numWalkboxes > 64 || pos + numWalkboxes * 20 > blob.Length) { Fail("Bad walkboxes"); return; }
        for (int b = 0; b < numWalkboxes; b++)
        {
            for (int k = 0; k < 8; k++)
            {
                int v = blob[pos] | (blob[pos + 1] << 8);
                walkboxData[b * 11 + k] = v >= 32768 ? v - 65536 : v;
                pos += 2;
            }
            walkboxData[b * 11 + 8] = blob[pos++];
            walkboxData[b * 11 + 9] = blob[pos++];
            walkboxData[b * 11 + 10] = blob[pos] | (blob[pos + 1] << 8);
            pos += 2;
        }
        int matrixLen = ReadU16(blob, pos);
        pos += 2;
        if (fault != "" || matrixLen > 4096 || pos + matrixLen > blob.Length) { Fail("Bad walk matrix"); return; }
        walkMatrix = new byte[matrixLen];
        for (int i = 0; i < matrixLen; i++) walkMatrix[i] = blob[pos + i];
        pos += matrixLen;
        int scalLen = ReadU16(blob, pos);
        pos += 2;
        if (fault != "" || pos + scalLen > blob.Length) { Fail("Bad scale table"); return; }
        for (int i = 0; i < 64; i++) scaleSlots[i] = 0;
        for (int i = 0; i < scalLen / 2 && i < 64; i++)
            scaleSlots[i] = blob[pos + i * 2] | (blob[pos + i * 2 + 1] << 8);
        pos += scalLen;
        roomTrans = blob[pos++];
        int numObj = ReadU16(blob, pos);
        pos += 2;
        if (fault != "" || numObj > 200) { Fail("Too many room objects"); return; }
        numObjectSlots = 0;
        for (int i = 0; i < numObj; i++)
        {
            if (pos + 26 > blob.Length) { Fail("Bad object table"); return; }
            int slot = ++numObjectSlots;
            int id = ReadU16(blob, pos);
            objSlotId[slot] = id;
            objSlotX[slot] = blob[pos + 2] | (blob[pos + 3] << 8);
            if (objSlotX[slot] >= 32768) objSlotX[slot] -= 65536;
            objSlotY[slot] = blob[pos + 4] | (blob[pos + 5] << 8);
            if (objSlotY[slot] >= 32768) objSlotY[slot] -= 65536;
            objSlotW[slot] = blob[pos + 6] | (blob[pos + 7] << 8);
            objSlotH[slot] = blob[pos + 8] | (blob[pos + 9] << 8);
            objSlotFlags[slot] = blob[pos + 10];
            objectParent[id] = blob[pos + 11];
            objSlotParent[slot] = blob[pos + 11];
            objSlotWalkX[slot] = blob[pos + 12] | (blob[pos + 13] << 8);
            if (objSlotWalkX[slot] >= 32768) objSlotWalkX[slot] -= 65536;
            objSlotWalkY[slot] = blob[pos + 14] | (blob[pos + 15] << 8);
            if (objSlotWalkY[slot] >= 32768) objSlotWalkY[slot] -= 65536;
            objSlotDir[slot] = blob[pos + 16];
            objSlotObcd[slot] = ReadU32(blob, pos + 18);
            pos += 26;
            if (id >= 0 && id < 1024) objectRoom[id] = roomResource;
        }
        if (fault != "") return;
        numLocalScripts = blob[pos++];
        if (numLocalScripts > 64) { Fail("Too many local scripts"); return; }
        for (int i = 0; i < numLocalScripts; i++)
        {
            if (pos + 9 > blob.Length) { Fail("Bad script directory"); return; }
            localScriptIds[i] = blob[pos];
            localScriptOffs[i] = ReadU32(blob, pos + 1);
            localScriptSizes[i] = ReadU32(blob, pos + 5);
            pos += 9;
        }
        int encd = ReadU32(blob, pos);
        int excd = ReadU32(blob, pos + 4);
        pos += 8;
        roomEncdOffs = encd == -1 ? -1 : encd;
        roomExcdOffs = excd == -1 ? -1 : excd;
        int roomLen = ReadU32(blob, pos);
        pos += 4;
        if (fault != "" || roomLen < 0 || pos + roomLen != blob.Length) { Fail("Bad room bytes"); return; }
        roomBytes = new byte[roomLen];
        for (int i = 0; i < roomLen; i++) roomBytes[i] = blob[pos + i];
    }

    // Room-number remapping for rooms >= 0x80; this game has none, consume only.
    // Deterministic LCG so the sim and the VM agree exactly. Real-game
    // randomness comes later with audio/input-driven seeding.
    private int NextRandom(int max)
    {
        if (max <= 0) return 0;
        randomState = (int)(((long)randomState * 1103515245 + 12345) & 2147483647L);
        return randomState % (max + 1);
    }

    private void GetRandomNr()
    {
        int address = ReadAddress();
        int max = ByteOperand(opcode);
        if (fault != "") return;
        WriteStorage(address, NextRandom(max));
    }

    private int[] slotCutscene = new int[80];
    private int cutSceneSlot = -1;
    private int cutStackPtr = -1;
    private int[] cutData = new int[5];
    private int[] cutSlot = new int[5];
    private int[] cutPos = new int[5];
    private int[] cutScript = new int[5];
    private int[] cutWhere = new int[5];
    private byte[][] cutCode = new byte[5][];
    private bool[] cutActive = new bool[5];

    private void CutsceneOp()
    {
        ReadVarargs();
        if (fault != "") return;
        slotCutscene[activeSlot]++;
        cutSceneSlot = activeSlot;
        if (cutStackPtr + 1 >= 5) { Fail("Cutscene stack overflow"); return; }
        cutStackPtr++;
        cutData[cutStackPtr] = arguments[0];
        cutSlot[cutStackPtr] = -1;
        cutPos[cutStackPtr] = 0;
        cutActive[cutStackPtr] = true;
        if (globals.Length > 35 && globals[35] != 0)
        {
            if (stackDepth >= 13) { Fail("Cutscene hook nesting overflow"); return; }
            returnSlots[stackDepth] = activeSlot;
            returnGenerations[stackDepth++] = generations[activeSlot];
            returnSlots[stackDepth++] = -2;
            activeSlot = -1;
            LaunchInner(globals[35], false, false, 0, 0);
        }
        else cutSceneSlot = -1;
    }

    private void EndCutsceneOp()
    {
        if (slotCutscene[activeSlot] > 0)
        {
            slotCutscene[activeSlot]--;
            if (cutStackPtr >= 0 && cutActive[cutStackPtr])
            {
                cutActive[cutStackPtr] = false;
                if (slotCutscene[activeSlot] > 0) slotCutscene[activeSlot]--;
            }
            if (cutStackPtr >= 0)
            {
                cutSlot[cutStackPtr] = -1;
                cutPos[cutStackPtr] = 0;
            }
        }
        if (5 < globals.Length) globals[5] = 0;
        if (cutStackPtr > 0) cutStackPtr--;
        else cutStackPtr = -1;
        if (globals.Length > 32 && globals[32] == 14) globals[32] = 4;
        RestoreHoverTracker();
        if (globals.Length > 36 && globals[36] != 0)
        {
            for (int i = 0; i < 25; i++) arguments[i] = 0;
            LaunchInner(globals[36], false, false, 0, 0);
        }
    }

    private void BeginOverrideOp()
    {
        int enabled = ReadByte();
        if (enabled != 0)
        {
            if (cutStackPtr >= 0)
            {
                cutSlot[cutStackPtr] = activeSlot;
                cutPos[cutStackPtr] = offsets[activeSlot];
                cutScript[cutStackPtr] = scriptIds[activeSlot];
                cutWhere[cutStackPtr] = slotWhere[activeSlot];
                cutCode[cutStackPtr] = slotCode[activeSlot];
            }
            ReadByte();
            ReadWord();
        }
        else if (cutStackPtr >= 0)
        {
            cutSlot[cutStackPtr] = -1;
            cutPos[cutStackPtr] = 0;
        }
        if (fault != "") return;
        if (5 < globals.Length) globals[5] = 0;
    }

    // Mirrors ScummEngine::abortCutscene: a click during an armed cutscene
    // override resumes the suspended script and reports VAR_OVERRIDE.
    // Returns true when an override was aborted (click consumed).
    private bool AbortCutscene()
    {
        if (cutStackPtr < 0 || !cutActive[cutStackPtr]) return false;
        int slot = cutSlot[cutStackPtr];
        if (slot < 0) return false;
        int pos = cutPos[cutStackPtr];
        int script = cutScript[cutStackPtr];
        int where = cutWhere[cutStackPtr];
        byte[] code = cutCode[cutStackPtr];
        if (slot >= 0 && slot < 80 && states[slot] != 0 && scriptIds[slot] == script && slotWhere[slot] == where)
        {
            offsets[slot] = pos;
            states[slot] = 2;
            slotFrozen[slot] = 0;
        }
        else if (script != 0 && code != null && pos >= 0 && pos < code.Length)
        {
            int fresh = -1;
            for (int i = 1; i < 80; i++) if (states[i] == 0) { fresh = i; break; }
            if (fresh >= 0)
            {
                scriptIds[fresh] = script;
                slotWhere[fresh] = where;
                slotCode[fresh] = code;
                offsets[fresh] = pos;
                states[fresh] = 2;
                slotFrozen[fresh] = 0;
                slotCutscene[fresh] = 0;
                slotRecursive[fresh] = false;
                for (int i = 0; i < 26; i++) locals[fresh * 26 + i] = 0;
                generations[fresh]++;
            }
        }
        cutSlot[cutStackPtr] = -1;
        cutPos[cutStackPtr] = 0;
        if (5 < globals.Length) globals[5] = 1;
        RestoreHoverTracker();
        return true;
    }

    // The hover tracker (script 23) feeds clicks with the object under the
    // cursor; entry cutscenes stop it without restarting it (room 32), which
    // would fizzle every click. Relaunch it when it is missing.
    private void RestoreHoverTracker()
    {
        for (int i = 1; i < 80; i++)
            if (states[i] != 0 && scriptIds[i] == 23) return;
        if (globalScripts == null || globalScripts.Length <= 23 || globalScripts[23] == null) return;
        LaunchInner(23, false, false, 0, 0);
    }

    // Sentence queue: doSentence records (verb, objectA, objectB); the
    // scheduler runs the sentence script (VAR_SENTENCE_SCRIPT) with those
    // args once per tick, mirroring checkAndRunSentenceScript.
    private void DoSentence()
    {
        int verb = ByteOperand(opcode);
        if (fault != "") return;
        if (verb == 254)
        {
            sentenceNum = 0;
            sentenceVerb = 0;
            sentenceObjectA = 0;
            sentenceObjectB = 0;
            if (33 < globals.Length) StopScript(globals[33]);
            return;
        }
        sentenceVerb = verb;
        sentenceObjectA = WordOperandBit(64);
        sentenceObjectB = WordOperandBit(32);
        if (fault != "") return;
        sentenceNum = 1;
    }

    private bool IsScriptRunning(int script)
    {
        if (script == 0 || states == null) return false;
        for (int i = 1; i < 80; i++)
            if (states[i] != 0 && scriptIds[i] == script) return true;
        return false;
    }

    private void CheckSentence()
    {
        if (fault != "" || sentenceNum <= 0) return;
        int sentenceScript = globals.Length > 33 ? globals[33] : 0;
        int verb = sentenceVerb;
        int objA = sentenceObjectA;
        int objB = sentenceObjectB;
        sentenceNum = 0;
        sentenceVerb = 0;
        sentenceObjectA = 0;
        sentenceObjectB = 0;
        if (sentenceScript == 0 || IsScriptRunning(sentenceScript)) return;
        for (int i = 0; i < 25; i++) arguments[i] = 0;
        arguments[0] = verb;
        arguments[1] = objA;
        arguments[2] = objB;
        LaunchInner(sentenceScript, false, false, 0, 0);
        // Queue for the next scheduler pass like StartEntry does.
        activeSlot = -1;
    }

    private int FindVerbSlotAny(int verb, int save)
    {
        for (int i = 1; i < 100; i++)
            if (verbId[i] == verb && verbSaveId[i] == save) return i;
        return 0;
    }

    // Printable width of a verb name in pixels (8px SCUMM font).
    private int VerbNameWidth(int slot)
    {
        if (slot < 1 || slot >= 100 || verbNames[slot] == null) return 0;
        int width = 0;
        int i = 0;
        while (i < verbNameLen[slot] && i < 64)
        {
            int c = verbNames[slot][i++];
            if (c == 0) break;
            if (c == 255)
            {
                if (i >= verbNameLen[slot]) break;
                int control = verbNames[slot][i++];
                if (control != 1 && control != 2 && control != 3 && control != 8) i += 2;
                continue;
            }
            if (c >= 32 && c < 127) width++;
        }
        return width * 8;
    }

    // Decoded verb name for UI display.
    public string GetVerbName(int slot)
    {
        if (slot < 1 || slot >= 100 || verbNames[slot] == null) return "";
        string result = "";
        int i = 0;
        while (i < verbNameLen[slot] && i < 64)
        {
            int c = verbNames[slot][i++];
            if (c == 0) break;
            if (c == 255)
            {
                if (i >= verbNameLen[slot]) break;
                int control = verbNames[slot][i++];
                if (control != 1 && control != 2 && control != 3 && control != 8) i += 2;
                continue;
            }
            if (c >= 32 && c < 127 && printChars.Length == 95)
                result += printChars.Substring(c - 32, 1);
        }
        return result;
    }

    public int GetVerbId(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbId[slot];
    }

    public int GetVerbMode(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbMode[slot];
    }

    public int GetVerbLeft(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbLeft[slot];
    }

    public int GetVerbTop(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbTop[slot];
    }

    public int GetVerbSaveId(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbSaveId[slot];
    }

    public int GetVerbType(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbType[slot];
    }

    public int GetVerbCenter(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return verbCenter[slot];
    }

    // Pixel width used by both drawing and hit-testing (8px per character).
    public int GetVerbNameWidth(int slot)
    {
        if (slot < 1 || slot >= 100) return 0;
        return VerbNameWidth(slot);
    }

    // Draw color for a text verb: dim when mode 2, highlight on hover,
    // otherwise the normal color — all through the live room palette.
    public Color GetVerbTextColor(int slot, int hover)
    {
        if (slot < 1 || slot >= 100) return Color.white;
        int index = verbColor[slot];
        if (verbMode[slot] == 2) index = verbDimColor[slot];
        else if (hover != 0 && verbHiColor[slot] != 0) index = verbHiColor[slot];
        if (roomPalette != null && index >= 0 && index * 3 + 2 < roomPalette.Length)
            return new Color((roomPalette[index * 3] & 255) / 255f,
                (roomPalette[index * 3 + 1] & 255) / 255f,
                (roomPalette[index * 3 + 2] & 255) / 255f, 1f);
        return Color.white;
    }

    // Mirrors findVerbAtPos: topmost clickable text verb under the cursor.
    public int FindVerbAt(int x, int y)    {
        for (int i = 99; i >= 1; i--)
        {
            if (verbMode[i] != 1 || verbId[i] == 0 || verbSaveId[i] != 0) continue;
            if (verbType[i] != 0) continue;
            int top = verbTop[i];
            if (y < top || y >= top + 8) continue;
            int width = VerbNameWidth(i);
            if (width <= 0) continue;
            if (verbCenter[i] != 0)
            {
                // Centered text spans half the width on either side of left.
                if (x < verbLeft[i] - width / 2 || x >= verbLeft[i] + width / 2) continue;
            }
            else
            {
                if (x < verbLeft[i] || x >= verbLeft[i] + width) continue;
            }
            return i;
        }
        return 0;
    }

    // Hover helper: object under a world pixel, for the sentence line.
    public int GetObjectAt(int x, int y)
    {
        return FindObjectAt(x, y);
    }

    // Active verb id for hover text (game var 107, Walk-to 11 fallback).
    public int GetActiveVerbId()
    {
        if (globals != null && globals.Length > 107 && globals[107] != 0) return globals[107];
        return 11;
    }

    // Verb name by id (first visible slot, else English fallback).
    public string GetVerbNameById(int verb)
    {
        for (int i = 1; i < 100; i++)
        {
            if (verbId[i] != verb || verbSaveId[i] != 0) continue;
            if (verbType[i] != 0) continue;
            string name = GetVerbName(i);
            if (name != "") return name;
        }
        if (verb == 11) return "Walk to";
        if (verb == 2) return "Open";
        if (verb == 3) return "Close";
        if (verb == 9) return "Pick up";
        if (verb == 8) return "Look at";
        if (verb == 10) return "Talk to";
        if (verb == 7) return "Use";
        if (verb == 5) return "Push";
        if (verb == 6) return "Pull";
        if (verb == 4) return "Give";
        return "";
    }

    // Object name from the room OBNA chunk (getObjOrActorName, objects only).
    public string GetObjectName(int obj)
    {
        if (obj >= 1 && obj < 48 && actorNames[obj] != null)
        {
            string actor = "";
            byte[] data = actorNames[obj];
            int i = 0;
            while (i < data.Length)
            {
                int c = data[i++];
                if (c == 0) break;
                if (c == 255)
                {
                    if (i >= data.Length) break;
                    int control = data[i++];
                    if (control != 1 && control != 2 && control != 3 && control != 8) i += 2;
                    continue;
                }
                if (c >= 32 && c < 127 && printChars.Length == 95)
                    actor += printChars.Substring(c - 32, 1);
            }
            if (actor != "") return actor;
        }
        if (WhereIsObject(obj) == 0) return "";
        int slot = FindObjectSlot(obj);
        if (slot == 0 || roomBytes == null) return "";
        int obcd = objSlotObcd[slot];
        if (obcd < 0 || obcd + 7 >= roomBytes.Length) return "";
        int total = (roomBytes[obcd + 4] << 24) | (roomBytes[obcd + 5] << 16)
            | (roomBytes[obcd + 6] << 8) | roomBytes[obcd + 7];
        if (total < 8) return "";
        int limit = obcd + total;
        if (limit > roomBytes.Length) return "";
        int end = obcd + 8;
        while (true)
        {
            if (end + 3 >= limit) return "";
            if (roomBytes[end] == 79 && roomBytes[end + 1] == 66
                && roomBytes[end + 2] == 78 && roomBytes[end + 3] == 65) break;
            if (end + 7 >= limit) return "";
            int size = (roomBytes[end + 4] << 24) | (roomBytes[end + 5] << 16)
                | (roomBytes[end + 6] << 8) | roomBytes[end + 7];
            if (size < 8) return "";
            end += size;
            if (end >= limit) return "";
        }
        string result = "";
        int p = end + 8;
        while (p < roomBytes.Length)
        {
            int c = roomBytes[p++];
            if (c == 0) break;
            if (c == 255)
            {
                if (p >= roomBytes.Length) break;
                int control = roomBytes[p++];
                if (control != 1 && control != 2 && control != 3 && control != 8) p += 2;
                continue;
            }
            if (c >= 32 && c < 127 && printChars.Length == 95)
                result += printChars.Substring(c - 32, 1);
        }
        return result;
    }

    // Object slots for the GPU compositor: live room position/size plus the
    // script-driven visibility state (putState/drawObject/pickup).
    public int GetObjectSlotCount()
    {
        return numObjectSlots < 0 ? 0 : (numObjectSlots > 200 ? 200 : numObjectSlots);
    }

    public int GetObjectSlotId(int slot)
    {
        if (slot < 1 || slot >= objSlotId.Length) return 0;
        return objSlotId[slot];
    }

    public int GetObjectSlotX(int slot)
    {
        if (slot < 1 || slot >= objSlotX.Length) return 0;
        return objSlotX[slot];
    }

    public int GetObjectSlotY(int slot)
    {
        if (slot < 1 || slot >= objSlotY.Length) return 0;
        return objSlotY[slot];
    }

    public int GetObjectSlotW(int slot)
    {
        if (slot < 1 || slot >= objSlotW.Length) return 0;
        return objSlotW[slot];
    }

    public int GetObjectSlotH(int slot)
    {
        if (slot < 1 || slot >= objSlotH.Length) return 0;
        return objSlotH[slot];
    }

    public int GetObjectState(int obj)
    {
        if (obj < 0 || obj >= 1024) return 0;
        return objectState[obj];
    }

    // Actor slots for the GPU actor layer: live room/position/visibility,
    // costume id, and facing. Slots 1..47 mirror the engine actor table.
    public int GetActorRoom(int actor)
    {
        if (actor < 1 || actor >= 48) return -1;
        return actorRoom[actor];
    }

    public int GetActorX(int actor)
    {
        if (actor < 1 || actor >= 48) return 0;
        return actorX[actor];
    }

    public int GetActorY(int actor)
    {
        if (actor < 1 || actor >= 48) return 0;
        return actorY[actor];
    }

    public int GetActorVisible(int actor)
    {
        if (actor < 1 || actor >= 48) return 0;
        return actorVisible[actor];
    }

    public int GetActorCostume(int actor)
    {
        if (actor < 1 || actor >= 48) return 0;
        return actorCostume[actor];
    }

    public int GetActorFacing(int actor)
    {
        if (actor < 1 || actor >= 48) return 0;
        return actorFacing[actor];
    }

    private void KillVerbSlot(int slot)
    {
        if (slot < 1 || slot >= 100) return;
        verbId[slot] = 0;
        verbMode[slot] = 0;
        verbNameLen[slot] = 0;
    }

    private void SaveRestoreVerbs()
    {
        int subop = ReadByte();
        if (fault != "") return;
        int a = ByteOperandMode(subop, 128);
        int b = ByteOperandMode(subop, 64);
        int c = ByteOperandMode(subop, 32);
        if (fault != "") return;
        int kind = subop & 255;
        if (kind == 1)
        {
            while (a <= b && fault == "")
            {
                int slot = FindVerbSlotAny(a, 0);
                if (slot != 0 && verbSaveId[slot] == 0) verbSaveId[slot] = c;
                a++;
            }
        }
        else if (kind == 2)
        {
            while (a <= b && fault == "")
            {
                int slot = FindVerbSlotAny(a, c);
                if (slot != 0)
                {
                    int slot2 = FindVerbSlotAny(a, 0);
                    if (slot2 != 0) KillVerbSlot(slot2);
                    slot = FindVerbSlotAny(a, c);
                    verbSaveId[slot] = 0;
                }
                a++;
            }
        }
        else if (kind == 3)
        {
            while (a <= b && fault == "")
            {
                int slot = FindVerbSlotAny(a, c);
                if (slot != 0) KillVerbSlot(slot);
                a++;
            }
        }
        else { Fail("SaveRestoreVerbs op " + subop + " unsupported"); return; }
    }

    private void PseudoRoom()
    {
        ReadByte();
        if (fault != "") return;
        while (fault == "")
        {
            if (ReadByte() == 0 || fault != "") break;
        }
    }

    private void ExecuteInstruction()
    {
        if (opcode == 44) { CursorCommand(); return; }
        if (opcode == 112 || opcode == 240) { LightsOp(); return; }
        if (opcode == 2 || opcode == 130) { StartMusicOrSound(true); return; }
        if (opcode == 28 || opcode == 156) { StartMusicOrSound(false); return; }
        if (opcode == 32) { lastSoundRequest = 0; soundRunning = 0; return; }
        if (opcode == 60 || opcode == 188) { StopSound(); return; }
        if (opcode == 76) { SoundKludge(); return; }
        if (opcode == 124 || opcode == 252) { IsSoundRunning(); return; }
        if (opcode == 20 || opcode == 148) { Print(false); return; }
        if (opcode == 216) { Print(true); return; }
        if (opcode == 39) { StringOps(); return; }
        if (opcode == 51 || opcode == 179) { RoomOps(); return; }
        if (opcode == 12 || opcode == 140) { ResourceRoutines(); return; }
        if (opcode == 114 || opcode == 242) { LoadRoomOp(); return; }
        if (opcode == 5 || opcode == 133) { DrawObjectOp(); return; }
        if (opcode == 82 || opcode == 210) { ActorFollowCamera(); return; }
        if (opcode == 103 || opcode == 231) { GetStringWidth(); return; }
        if (opcode == 48) { MatrixOps(); return; }
        if (opcode == 86 || opcode == 214) { GetActorMovingOp(); return; }
        if (opcode == 53 || opcode == 117 || opcode == 181 || opcode == 245) { FindObjectOp(); return; }
        if (opcode == 85 || opcode == 213) { ActorFromPosOp(); return; }
        if (opcode == 55 || opcode == 119 || opcode == 183 || opcode == 247) { StartObjectOp(); return; }
        if (opcode == 37 || opcode == 101 || opcode == 165 || opcode == 229) { PickupObjectOp(); return; }
        if (opcode == 125 || opcode == 253) { FindInventoryOp(); return; }
        if (opcode == 49 || opcode == 177) { GetInventoryCountOp(); return; }
        if (opcode == 9 || opcode == 73 || opcode == 137 || opcode == 201) { FaceActorOp(); return; }
        if (opcode == 54 || opcode == 118 || opcode == 182 || opcode == 246) { WalkActorToObjectOp(); return; }
        if (opcode == 174) { WaitOp(); return; }
        if (opcode == 30 || opcode == 62 || opcode == 94 || opcode == 126
            || opcode == 158 || opcode == 190 || opcode == 222 || opcode == 254) { WalkActorToOp(); return; }
        if (opcode == 7 || opcode == 71 || opcode == 135 || opcode == 199) { PutState(); return; }
        if (opcode == 14 || opcode == 78 || opcode == 142 || opcode == 206) { PutActorAtObject(); return; }
        if (opcode == 41 || opcode == 105 || opcode == 169 || opcode == 233) { PutOwner(); return; }
        if (opcode == 109 || opcode == 45 || opcode == 173 || opcode == 237) { PutActorInRoom(); return; }
        if (opcode == 11 || opcode == 75 || opcode == 139 || opcode == 203) { GetVerbEntrypointOp(); return; }
        if (opcode == 51 || opcode == 115 || opcode == 179 || opcode == 243) { RoomOps(); return; }
        if (opcode == 38 || opcode == 166) { SetVarRange(); return; }
        if (opcode == 107 || opcode == 235) { WordOperand(opcode); return; }
        if (opcode == 167) return;
        if (opcode == 96 || opcode == 224) { FreezeOp(); return; }
        if (opcode == 178 || opcode == 50) { SetCameraAt(); return; }
        if (opcode == 18 || opcode == 146) { PanCameraTo(); return; }
        if (opcode == 1 || opcode == 33 || opcode == 65 || opcode == 97 || opcode == 129
            || opcode == 161 || opcode == 193 || opcode == 225) { PutActor(); return; }
        if (opcode == 109 || opcode == 237) { PutActorInRoom(); return; }
        if (opcode == 3 || opcode == 131) { GetActorRoom(); return; }
        if (opcode == 67 || opcode == 195) { GetActorXY(true); return; }
        if (opcode == 35 || opcode == 163) { GetActorXY(false); return; }
        if (opcode == 99 || opcode == 227) { GetActorField(6); return; }
        if (opcode == 113 || opcode == 241) { GetActorField(7); return; }
        if (opcode == 123 || opcode == 251) { GetActorWalkBox(); return; }
        if (opcode == 52 || opcode == 116 || opcode == 180 || opcode == 244) { GetDistOp(); return; }
        if (opcode == 17 || opcode == 81 || opcode == 145 || opcode == 209) { AnimateActor(); return; }
        if (opcode == 7 || opcode == 135) { PutState(); return; }
        if (opcode == 15 || opcode == 143) { GetState(); return; }
        if (opcode == 41 || opcode == 169) { PutOwner(); return; }
        if (opcode == 16 || opcode == 144) { GetOwner(); return; }
        if (opcode == 93 || opcode == 221) { SetClass(); return; }
        if (opcode == 29 || opcode == 157) { IfClassOfIs(); return; }
        if (opcode == 38 || opcode == 166) { SetVarRange(); return; }
        if (opcode == 172) { ExpressionOp(); return; }
        if (opcode == 11 || opcode == 75 || opcode == 139 || opcode == 203) { GetVerbEntrypointOp(); return; }
        if (opcode == 122 || opcode == 250) { VerbOps(); return; }
        if (opcode == 204) { PseudoRoom(); return; }
        if (opcode == 19 || opcode == 83 || opcode == 147 || opcode == 211) { ActorOps(); return; }
        if (opcode == 171) { SaveRestoreVerbs(); return; }
        if (opcode == 22 || opcode == 150) { GetRandomNr(); return; }
        if (opcode == 25 || opcode == 57 || opcode == 89 || opcode == 121
            || opcode == 153 || opcode == 185 || opcode == 217 || opcode == 249) { DoSentence(); return; }
        if (opcode == 64) { CutsceneOp(); return; }
        if (opcode == 88) { BeginOverrideOp(); return; }
        if (opcode == 192) { EndCutsceneOp(); return; }
        if (opcode == 36 || opcode == 100 || opcode == 164 || opcode == 228) { LoadRoomWithEgo(); return; }
        if (opcode == 0 || opcode == 160)
        {
            states[activeSlot] = 0;
            activeSlot = -1;
            return;
        }
        if (opcode == 128) { activeSlot = -1; return; }
        if (opcode == 24) { Branch(false); return; }
        if (opcode == 46 || opcode == 43)
        {
            int delay;
            if (opcode == 43) delay = ReadStorage(ReadAddress());
            else
            {
                delay = ReadByte();
                delay |= ReadByte() << 8;
                delay |= ReadByte() << 16;
            }
            if (fault != "") return;
            delays[activeSlot] = delay;
            states[activeSlot] = 1;
            activeSlot = -1;
            return;
        }
        if (opcode == 40 || opcode == 168)
        {
            int value = ReadStorage(ReadAddress());
            Branch(opcode == 40 ? value == 0 : value != 0);
            return;
        }
        if (opcode == 70 || opcode == 198)
        {
            int address = ReadAddress();
            long value = ReadStorage(address);
            WriteStorage(address, Wrap32(value + (opcode == 70 ? 1 : -1)));
            return;
        }
        if ((opcode & 31) == 10)
        {
            StartScriptOp(opcode);
            return;
        }
        if (opcode == 66)
        {
            ChainScriptOp();
            return;
        }
        int operation = opcode & 127;
        if (operation == 98)
        {
            int script = ByteOperand(opcode);
            if (fault != "") return;
            if (script == 0)
            {
                if (slotCutscene[activeSlot] > 0)
                {
                    Fail("Script stopped with active cutscene/override");
                    return;
                }
                if (slotWhere[activeSlot] == 2) StopObjectScript(scriptIds[activeSlot]);
                else
                {
                    states[activeSlot] = 0;
                    activeSlot = -1;
                }
            }
            else StopScript(script);
            return;
        }
        if (operation == 104)
        {
            int address = ReadAddress();
            int script = ByteOperand(opcode);
            int running = 0;
            for (int i = 1; i < 80; i++) if (states[i] != 0 && scriptIds[i] == script) running = 1;
            WriteStorage(address, running);
            return;
        }
        if (operation == 26 || operation == 90 || operation == 58 || operation == 27
            || operation == 91 || operation == 23 || operation == 87)
        {
            int address = ReadAddress();
            int right = WordOperand(opcode);
            if (operation == 26) { WriteStorage(address, right); return; }
            int left = ReadStorage(address);
            long result = 0;
            if (operation == 90) result = (long)left + right;
            else if (operation == 58) result = (long)left - right;
            else if (operation == 27) result = (long)left * right;
            else if (operation == 91)
            {
                if (right == 0) { Fail("Division by zero"); return; }
                result = (long)left / right;
            }
            else if (operation == 23) result = left & right;
            else if (operation == 87) result = left | right;
            WriteStorage(address, Wrap32(result));
            return;
        }
        if (operation == 4 || operation == 8 || operation == 56 || operation == 68 || operation == 72 || operation == 120)
        {
            int left = Signed16(ReadStorage(ReadAddress()));
            int right = Signed16(WordOperand(opcode));
            bool condition = false;
            if (operation == 4) condition = right >= left;
            else if (operation == 8) condition = right != left;
            else if (operation == 56) condition = right <= left;
            else if (operation == 68) condition = right < left;
            else if (operation == 72) condition = right == left;
            else if (operation == 120) condition = right > left;
            Branch(condition);
            return;
        }
        Fail("Unsupported opcode " + opcode);
    }

    private void Fail(string message)
    {
        if (fault != "") return;
        fault = message;
        faultScript = instructionScript;
        faultOffset = instructionOffset;
        faultOpcode = opcode;
    }
}
