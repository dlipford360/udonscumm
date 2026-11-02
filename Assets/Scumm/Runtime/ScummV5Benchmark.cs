using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// A single-script throughput probe, not the game VM or its scheduler.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummV5Benchmark : UdonSharpBehaviour
{
    public TextAsset program;
    public Text statusText;
    [Range(1, 2048)] public int instructionsPerFrame = 100;
    public int expectedInstructions = 3004;
    public int expectedVariableZero = 1000;
    public int expectedVariableOne = 0;

    private byte[] code;
    private int[] variables;
    private int pc;
    private int instructionOffset;
    private int opcode;
    private int instructions;
    private int samples;
    private float totalMilliseconds;
    private float peakMilliseconds;
    private bool running;
    private bool yielded;
    private string fault = "";

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
        if (program == null)
        {
            if (statusText != null) statusText.text = "SCUMM probe: assign a benchmark program";
            return;
        }
        code = program.bytes;
        variables = new int[800];
        pc = 0;
        instructions = 0;
        samples = 0;
        totalMilliseconds = 0;
        peakMilliseconds = 0;
        fault = "";
        running = true;
        if (statusText != null) statusText.text = "SCUMM v5 synthetic probe running...";
    }

    private void Update()
    {
        if (!running) return;
        yielded = false;
        int budget = Mathf.Clamp(instructionsPerFrame, 1, 2048);
        float start = Time.realtimeSinceStartup;
        for (int i = 0; i < budget && running && !yielded; i++)
        {
            instructionOffset = pc;
            opcode = ReadByte();
            if (!running) break;
            ExecuteInstruction();
            instructions++;
        }
        float elapsed = (Time.realtimeSinceStartup - start) * 1000f;
        samples++;
        totalMilliseconds += elapsed;
        peakMilliseconds = Mathf.Max(peakMilliseconds, elapsed);
        if (!running) Report();
    }

    private int ReadByte()
    {
        if (!running) return 0;
        if (pc < 0 || pc >= code.Length)
        {
            Fail("Read outside script");
            return 0;
        }
        return code[pc++];
    }

    private int ReadWord()
    {
        int low = ReadByte();
        return low | (ReadByte() << 8);
    }

    private int SignedWord()
    {
        int value = ReadWord();
        return value >= 32768 ? value - 65536 : value;
    }

    private int VariableAddress()
    {
        int address = ReadWord();
        // Deliberately reject locals, bits and indirection in this first probe.
        if (address < 0 || address >= variables.Length)
        {
            Fail("Unsupported variable address " + address);
            return 0;
        }
        return address;
    }

    private int Operand()
    {
        if ((opcode & 128) == 0) return SignedWord();
        return variables[VariableAddress()];
    }

    private void Jump(int displacement)
    {
        int target = pc + displacement;
        if (target < 0 || target >= code.Length)
        {
            Fail("Jump outside script");
            return;
        }
        pc = target;
    }

    private void ExecuteInstruction()
    {
        if (opcode == 0 || opcode == 160)
        {
            running = false;
            return;
        }
        if (opcode == 128)
        {
            yielded = true;
            return;
        }
        if (opcode == 24)
        {
            int displacement = SignedWord();
            if (running) Jump(displacement);
            return;
        }
        if (opcode == 40)
        {
            int address = VariableAddress();
            int displacement = SignedWord();
            if (running && variables[address] != 0) Jump(displacement);
            return;
        }
        int operation = opcode & 127;
        if (operation == 26 || operation == 90 || operation == 58)
        {
            int address = VariableAddress();
            int value = Operand();
            if (!running) return;
            if (operation == 26) variables[address] = value;
            else if (operation == 90) variables[address] += value;
            else variables[address] -= value;
            return;
        }
        Fail("Unsupported opcode " + opcode);
    }

    private void Fail(string message)
    {
        if (fault == "") fault = message + " at byte " + instructionOffset;
        running = false;
    }

    private void Report()
    {
        bool passed = fault == "" && instructions == expectedInstructions
            && variables[0] == expectedVariableZero && variables[1] == expectedVariableOne;
        string result = "SCUMM v5 synthetic probe: " + (passed ? "PASS" : "FAIL")
            + "\nInstructions: " + instructions + " / frames: " + samples
            + "\nCPU total ms: " + totalMilliseconds + " / peak ms: " + peakMilliseconds
            + "\nBudget: " + instructionsPerFrame + " / vars: " + variables[0] + ", " + variables[1]
            + "\n" + fault + "\nInteract with cube to repeat. Not a game compatibility test.";
        if (statusText != null) statusText.text = result;
        Debug.Log(result);
    }
}
