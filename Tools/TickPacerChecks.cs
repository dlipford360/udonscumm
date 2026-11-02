// Host-only checks of the wall-clock tick pacer: the same tick count must
// come out of 60, 120 and 30 fps frame streams, with clamps holding.
using System;

class TickPacerChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    static int RunFrames(int frames, int units)
    {
        int carry = 0, ticks = 0;
        for (int f = 0; f < frames; f++)
        {
            carry = ScummTickPacer.AddCarry(carry, units);
            int due = ScummTickPacer.TicksDue(carry);
            for (int t = 0; t < due; t++)
            {
                ticks++;
                carry = ScummTickPacer.ConsumeTicks(carry, 1);
            }
        }
        return ticks;
    }

    static void Main()
    {
        // One wall-clock second at three display rates deals 60 ticks.
        Check(RunFrames(60, 4) == 60, "60fps must deal 60 ticks/sec");
        Check(RunFrames(120, 2) == 60, "120fps must deal 60 ticks/sec");
        Check(RunFrames(30, 8) == 60, "30fps must deal 60 ticks/sec");
        // Remainder carries forward: 120fps ticks every second frame.
        int carry = 0, ticks = 0;
        for (int f = 0; f < 4; f++)
        {
            carry = ScummTickPacer.AddCarry(carry, 2);
            int due = ScummTickPacer.TicksDue(carry);
            ticks += due;
            carry = ScummTickPacer.ConsumeTicks(carry, due);
        }
        Check(ticks == 2 && carry == 0, "120fps must tick on alternate frames");
        // Clamps: per-frame units 1..15, backlog capped, max 4 ticks/frame.
        Check(ScummTickPacer.AddCarry(0, 0) == 1, "units floor at 1");
        Check(ScummTickPacer.AddCarry(0, 100) == 15, "units cap at 15");
        Check(ScummTickPacer.AddCarry(20, 15) == 24, "carry caps at 24");
        Check(ScummTickPacer.TicksDue(3) == 0, "no partial ticks");
        Check(ScummTickPacer.TicksDue(24) == 4, "ticks cap at 4/frame");
        Check(ScummTickPacer.ConsumeTicks(3, 1) == 0, "carry never negative");
        Console.WriteLine("PASS tick pacer is frame-rate independent with clamps holding");
    }
}
