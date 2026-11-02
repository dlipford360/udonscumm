// Wall-clock pacing for the 60Hz game tick. Unity frames arrive at display
// rate (60/90/120Hz+); jiffies accumulate and whole 4-jiffy ticks are dealt
// out, so per-tick work (walks, costume animation) runs at the same speed
// on every headset. Matches waitForTimer(delta * 4) pacing in the reference
// main loop. Pure logic, covered by host checks.
public static class ScummTickPacer
{
    public const int JiffiesPerTick = 4;
    public const int MaxTicksPerFrame = 4;
    public const int MaxCarry = 24;

    public static int AddCarry(int carry, int units)
    {
        int clamped = units < 1 ? 1 : (units > 15 ? 15 : units);
        int next = carry + clamped;
        return next > MaxCarry ? MaxCarry : next;
    }

    public static int TicksDue(int carry)
    {
        int ticks = carry / JiffiesPerTick;
        return ticks > MaxTicksPerFrame ? MaxTicksPerFrame : ticks;
    }

    public static int ConsumeTicks(int carry, int ticks)
    {
        int rest = carry - ticks * JiffiesPerTick;
        return rest < 0 ? 0 : rest;
    }
}
