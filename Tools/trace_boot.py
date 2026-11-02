#!/usr/bin/env python3
"""Run the real MI1 boot script in the reference sim; report coverage and stops.

Safe: executes only integer VM ops on local arrays. Exits 0 when the machine
goes idle, 1 on fault/timeout. Prints the opcode histogram and stop location.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from test_vm_fixtures import ReferenceSim


def load_scripts(directory):
    programs = {}
    for path in Path(directory).glob("global-*.bytes"):
        script_id = int(path.stem.split("-")[1])
        programs[script_id] = path.read_bytes()
    return programs


def main():
    root = Path(__file__).resolve().parent.parent
    programs = load_scripts(root / "Assets/Scumm/Generated/MonkeyIsland/scripts")
    print(f"Loaded {len(programs)} global scripts")
    sim = ReferenceSim(programs)
    sim.load_tables(root / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
    sim.load_room_blobs(root / "Assets/Scumm/Generated/RoomBlobs")
    # The opcode trace was captured with --debugflags, which forces
    # ScummVM's _debugMode=1, i.e. VAR_DEBUGMODE (v39) = 1. Mirror that here
    # so the prefix comparison follows the same branch at script 1:1983.
    # Release (and the Udon VM) default v39 to 0.
    sim.globals[39] = 1
    sim.start(1, [0])
    launches = [(1, 0)]
    orig_launch = sim.launch
    def logged_launch(script, args, caller, where=0, offs=0):
        launches.append((script, caller))
        return orig_launch(script, args, caller, where, offs)
    sim.launch = logged_launch
    max_ticks = int(sys.argv[1]) if len(sys.argv) > 1 else 600
    stopped_early = False
    for _ in range(max_ticks):
        sim.tick(4)
        if sim.fault or not sim.slots:
            stopped_early = True
            break
    print(f"Ticks: {sim.ticks} instructions: {sim.instructions}")
    print("Live slots:", {k: (v["script"], v["pc"], v["status"]) for k, v in sim.slots.items()})
    print("Rooms entered:", sim.rooms_entered if hasattr(sim, "rooms_entered") else "?")
    print("Script launches (script, caller depth):", launches)
    print("Globals of interest:",
          {k: sim.globals[k] for k in (1, 3, 4, 14, 17, 18, 19, 22, 52, 53) if k < len(sim.globals)})
    if sim.fault:
        print(f"STOPPED: {sim.fault} script {sim.fault_script} offset {sim.fault_offset} "
              f"opcode {sim.fault_opcode}")
    elif stopped_early:
        print("Machine idle: all scripts stopped")
    else:
        print("Still running after tick budget")
    print("Opcode histogram:")
    for opcode in sorted(sim.op_histogram):
        print(f"  0x{opcode:02x} ({opcode:3}): {sim.op_histogram[opcode]}")
    print(f"v52/v53 cursor/userput: {sim.globals[52]}, {sim.globals[53]}")
    return 0 if not sim.fault and not sim.slots else 1


if __name__ == "__main__":
    sys.exit(main())
