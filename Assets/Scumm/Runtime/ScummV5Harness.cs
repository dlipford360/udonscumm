using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Drives one ScummV5Vm: boot diagnostic (mode 0) or fixture verification (mode 1).
// Timing approximates VAR_TIMER_NEXT=4: one 60Hz Unity frame advances 4 delay units.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummV5Harness : UdonSharpBehaviour
{
    public ScummV5Vm vm;
    public TextAsset[] scripts;
    public int entryScript = 1;
    public int bootArgument;
    public int instructionsPerFrame = 500;
    public int maxTicks = 3600;
    public int mode;
    public int[] checkVars;
    public int[] checkValues;
    public int[] checkBitIndices;
    public int[] checkBitValues;
    public int minTicks;
    public int expectFault;
    public int expectedFaultScript = -1;
    public int expectedFaultOpcode = -1;
    public Text statusText;
    public ScummScreenInput screenInput;
    public int autoEnterRoom = -1;
    public int autoEnterTicks;
    public bool lookoutWalkTest;
    private bool walkTestStarted;
    private bool walkTestReported;
    private int jiffyCarry;
    private int clicksAtAutoEnter;

    private bool running;
    private int frames;
    private string outcome = "";
    private int lastClickSequence;
    private bool autoEntered;

    private void Start()
    {
        Restart();
    }

    public override void Interact()
    {
        Restart();
    }

    public void Restart()
    {
        running = false;
        outcome = "";
        frames = 0;
        jiffyCarry = 0;
        lastClickSequence = screenInput != null ? screenInput.clickSequence : 0;
        lastSeenPrint = 0;
        autoEntered = false;
        walkTestStarted = false;
        walkTestReported = false;
        clicksAtAutoEnter = -1;
        Debug.Log("SCUMM boot-r9 harness start (mode " + mode + ", entry " + entryScript
            + ", autoEnter=" + autoEnterRoom + "@" + autoEnterTicks + ", maxTicks=" + maxTicks + ")");
        if (vm == null)
        {
            if (statusText != null) statusText.text = "SCUMM harness: assign a ScummV5Vm";
            Debug.Log("SCUMM harness: vm is null");
            return;
        }
        if (scripts == null)
        {
            if (statusText != null) statusText.text = "SCUMM harness: no scripts wired";
            Debug.Log("SCUMM harness: scripts array is null");
            return;
        }
        vm.globalScripts = scripts;
        vm.ResetMachine();
        if (mode == 0 && screenInput == null)
            Debug.Log("SCUMM boot-r9: no ScummScreenInput wired; screen clicks disabled");
        if (mode == 0 && screenInput != null)
            Debug.Log("SCUMM boot-r9: screen input wired, pointer feeds VARs 20/21");
        if (mode == 0 && vm.roomBlobs == null)
            Debug.Log("SCUMM harness: WARNING roomBlobs not wired; rooms will fault at loadRoom");
        if (mode == 0 && vm.globalTables == null)
            Debug.Log("SCUMM harness: WARNING object tables not wired; owners/states default to 0");
        vm.StartEntry(entryScript, bootArgument);
        if (vm.fault != "")
        {
            Finish();
            return;
        }
        running = true;
        if (statusText != null) statusText.text = "SCUMM " + (mode == 0 ? "boot" : "fixture") + " running...";
    }

    private void Update()
    {
        if (!running) return;
        FeedScreenInput();
        // Display-rate frames feed wall-clock jiffies; whole 60Hz ticks run
        // from the accumulator so game speed never depends on headset fps.
        jiffyCarry = ScummTickPacer.AddCarry(jiffyCarry, (int)(Time.deltaTime * 240f + 0.5f));
        int ticks = ScummTickPacer.TicksDue(jiffyCarry);
        for (int t = 0; t < ticks; t++)
        {
            vm.Advance(instructionsPerFrame, ScummTickPacer.JiffiesPerTick);
            jiffyCarry = ScummTickPacer.ConsumeTicks(jiffyCarry, 1);
            if (vm.printSequence != lastSeenPrint && vm.fault == "")
            {
                lastSeenPrint = vm.printSequence;
                Debug.Log("SCUMM boot-r9 says (actor " + vm.lastPrintActor + "): " + vm.lastPrintText);
            }
            if (vm.fault != "" || vm.finished || vm.completedTicks >= maxTicks)
            {
                Finish();
                running = false;
                break;
            }
        }
        if (!running) return;
        frames++;
        if (mode == 0 && (frames == 1 || frames % 300 == 0))
            Debug.Log("SCUMM boot-r9 progress: frames=" + frames + " ticks=" + vm.completedTicks
                + " instructions=" + vm.instructionsExecuted + "\n" + vm.DescribeState());
        if (autoEnterRoom >= 0 && !autoEntered && vm.completedTicks >= autoEnterTicks && vm.fault == "")
        {
            autoEntered = true;
            clicksAtAutoEnter = screenInput != null ? screenInput.clickSequence : lastClickSequence;
            vm.EnterRoom(autoEnterRoom);
            Debug.Log("SCUMM boot-r9 auto-enter room " + autoEnterRoom + "\n" + vm.DescribeState());
        }
        if (lookoutWalkTest && autoEntered && !walkTestStarted && vm.ReadRoom() == 38
            && vm.completedTicks >= autoEnterTicks + 200 && vm.fault == "")
        {
            walkTestStarted = true;
            int clicksNow = screenInput != null ? screenInput.clickSequence : lastClickSequence;
            if (clicksNow != clicksAtAutoEnter)
            {
                walkTestReported = true;
                Debug.Log("SCUMM lookout-walk diagnostic skipped: player already took control.");
            }
            else
            {
                vm.BeginLookoutWalkTest();
                int ego = vm.ReadGlobal(1);
                Debug.Log("SCUMM lookout-walk diagnostic: ego=" + ego + " room=" + vm.GetActorRoom(ego)
                    + " position=" + vm.GetActorX(ego) + "," + vm.GetActorY(ego)
                    + " scale=" + vm.GetActorRenderScaleX(ego) + " moving=" + vm.GetActorMoving(ego));
            }
        }
        if (walkTestStarted && !walkTestReported && vm.GetActorMoving(vm.ReadGlobal(1)) == 0)
        {
            walkTestReported = true;
            int ego = vm.ReadGlobal(1);
            Debug.Log("SCUMM lookout-walk stopped: room=" + vm.GetActorRoom(ego)
                + " position=" + vm.GetActorX(ego) + "," + vm.GetActorY(ego)
                + " scale=" + vm.GetActorRenderScaleX(ego));
        }
        if (vm.fault != "" || vm.finished || vm.completedTicks >= maxTicks)
        {
            Finish();
            return;
        }
        if (statusText != null && frames % 15 == 0)
            statusText.text = "SCUMM running: tick " + vm.completedTicks + ", instr " + vm.instructionsExecuted;
    }

    private void Finish()
    {
        running = false;
        if (mode == 0) outcome = DescribeBoot();
        else outcome = VerifyFixture();
        if (statusText != null) statusText.text = outcome;
        Debug.Log(outcome);
        if (mode == 0) Debug.Log(vm.DescribeState());
    }

    private int lastSeenPrint;

    // Screen pointer into VARs 20/21/44/45; clicks run the verb script
    // (area 2 = scene, button 1 = left) when userput allows it.
    // Only the displayed gameplay VM receives input. Click coordinates are
    // captured on press rather than sampled later from a moving pointer.
    private void FeedScreenInput()
    {
        if (vm == null || screenInput == null) return;
        if (mode != 0 || screenInput.ReadTargetVm() != vm)
        {
            lastClickSequence = screenInput.clickSequence;
            return;
        }
        if (screenInput.pointerOverScreen)
        {
            int hoverX = screenInput.pixelX + vm.GetCameraScrollX();
            vm.SetGlobal(20, hoverX);
            vm.SetGlobal(21, screenInput.pixelY);
            vm.SetGlobal(44, hoverX);
            vm.SetGlobal(45, screenInput.pixelY);
        }
        if (screenInput.clickSequence != lastClickSequence)
        {
            lastClickSequence = screenInput.clickSequence;
            if (screenInput.clickVm != vm) return;
            int x = screenInput.clickX;
            int y = screenInput.clickY;
            int worldX = x + vm.GetCameraScrollX();
            vm.SetGlobal(20, worldX);
            vm.SetGlobal(21, y);
            vm.SetGlobal(44, worldX);
            vm.SetGlobal(45, y);
            int userput = vm.ReadGlobal(53);
            int verbSlot = vm.FindVerbAt(x, y);
            if (verbSlot != 0 && userput > 0)
            {
                int verbId = vm.GetVerbId(verbSlot);
                Debug.Log("SCUMM boot-r9 verb click: verb=" + verbId
                    + " (" + vm.GetVerbName(verbSlot) + ") pixels=("
                    + x + "," + y + ")");
                vm.RunInputScript(1, verbId, 1);
                Debug.Log("SCUMM boot-r9 click dispatched\n" + vm.DescribeState());
            }
            else if (verbSlot != 0)
            {
                Debug.Log("SCUMM boot-r9 verb click ignored: userput off");
            }
            else
            {
                Debug.Log("SCUMM boot-r9 click: pixels=(" + x + "," + y
                    + ") userput=" + userput + " verbScript=" + vm.ReadGlobal(32));
                if (userput > 0)
                {
                    vm.RunInputScript(2, 0, 1);
                    Debug.Log("SCUMM boot-r9 click dispatched\n" + vm.DescribeState());
                }
                else
                {
                    Debug.Log("SCUMM boot-r9 click ignored: userput off");
                }
            }
        }
    }

    private string DescribeBoot()
    {
        string result = "SCUMM v5 boot diagnostic boot-r9"
            + "\nEntry script: " + entryScript
            + "\nTicks: " + vm.completedTicks + " / frames: " + frames
            + "\nInstructions: " + vm.instructionsExecuted;
        if (vm.fault != "")
            result += "\nStopped: " + vm.fault
                + "\nScript: " + vm.faultScript + " offset: " + vm.faultOffset + " opcode: " + vm.faultOpcode;
        else if (vm.finished)
            result += "\nMachine idle: all scripts stopped (unexpected for a boot script)";
        else
            result += "\nTIMEOUT: scripts still alive after " + maxTicks + " ticks";
        return result + "\nInteract to repeat.";
    }

    private string VerifyFixture()
    {
        if (expectFault != 0)
        {
            bool ok = vm.fault != "" && vm.faultScript == expectedFaultScript && vm.faultOpcode == expectedFaultOpcode;
            return "SCUMM v5 fixture: " + (ok ? "PASS" : "FAIL")
                + "\nExpected fault in script " + expectedFaultScript + " opcode " + expectedFaultOpcode
                + "\nActual: " + vm.fault + " script " + vm.faultScript + " offset " + vm.faultOffset
                + "\nTicks: " + vm.completedTicks + " instr: " + vm.instructionsExecuted
                + "\nInteract to repeat.";
        }
        if (vm.fault != "")
            return "SCUMM v5 fixture: FAIL\nUnexpected fault: " + vm.fault
                + " script " + vm.faultScript + " offset " + vm.faultOffset
                + "\nInteract to repeat.";
        if (!vm.finished)
            return "SCUMM v5 fixture: FAIL\nTIMEOUT after " + maxTicks + " ticks, instr " + vm.instructionsExecuted
                + "\nInteract to repeat.";
        if (vm.completedTicks < minTicks)
            return "SCUMM v5 fixture: FAIL\nOnly " + vm.completedTicks + " ticks, expected at least " + minTicks
                + "\nInteract to repeat.";
        string mismatches = "";
        if (checkVars != null && checkValues != null && checkVars.Length == checkValues.Length)
        {
            for (int i = 0; i < checkVars.Length; i++)
            {
                int actual = vm.ReadGlobal(checkVars[i]);
                if (actual != checkValues[i])
                    mismatches += "\nv" + checkVars[i] + ": expected " + checkValues[i] + ", got " + actual;
            }
        }
        if (checkBitIndices != null && checkBitValues != null && checkBitIndices.Length == checkBitValues.Length)
        {
            for (int i = 0; i < checkBitIndices.Length; i++)
            {
                int actual = vm.ReadBit(checkBitIndices[i]);
                if (actual != checkBitValues[i])
                    mismatches += "\nbit" + checkBitIndices[i] + ": expected " + checkBitValues[i] + ", got " + actual;
            }
        }
        bool passed = mismatches == "";
        return "SCUMM v5 fixture: " + (passed ? "PASS" : "FAIL") + mismatches
            + "\nTicks: " + vm.completedTicks + " instr: " + vm.instructionsExecuted
            + "\nInteract to repeat.";
    }
}
