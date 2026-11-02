#!/usr/bin/env python3
"""Assemble v5 VM semantic fixtures; single source of truth for fixture bytes."""
import argparse
import json
from pathlib import Path

GLOBAL, BIT, LOCAL = 0x0000, 0x8000, 0x4000


def address(kind, number):
    return kind | number


def word(value):
    return bytes((value & 0xFF, (value >> 8) & 0xFF))


class Asm:
    def __init__(self):
        self.code = bytearray()
        self.labels = {}
        self.fixups = []

    def label(self, name):
        if name in self.labels:
            raise ValueError(f"Duplicate label {name}")
        self.labels[name] = len(self.code)

    def emit(self, *items):
        for item in items:
            self.code += item if isinstance(item, bytes) else bytes((item,))

    def _reserve(self, label):
        position = len(self.code)
        self.code += b"\x00\x00"
        self.fixups.append((position, label))

    def jump0(self, label):
        """Unconditional 0x18 jumpRelative: opcode + displacement only."""
        self.emit(0x18)
        self._reserve(label)

    def jump1(self, opcode, address, label):
        """equalZero/notEqualZero: opcode + var address + displacement."""
        self.emit(opcode)
        self.code += word(address)
        self._reserve(label)

    def jump2(self, opcode, address, operand, label):
        """Comparisons: opcode + var address + word operand + displacement."""
        self.emit(opcode)
        self.code += word(address) + word(operand)
        self._reserve(label)

    def finish(self):
        for position, label in self.fixups:
            if label not in self.labels:
                raise ValueError(f"Unknown label {label}")
            displacement = self.labels[label] - (position + 2)
            if not -32768 <= displacement <= 32767:
                raise ValueError(f"Jump to {label} out of range")
            self.code[position:position + 2] = word(displacement)
        return bytes(self.code)


def move(a, value):
    return bytes((0x1A,)) + word(a) + word(value)


def move_var(a, source):
    return bytes((0x9A,)) + word(a) + word(source)


def move_indirect(base, index_word, value):
    """Destination 'base + [index]': result-pos 0x2000 word, index word, value."""
    return bytes((0x1A,)) + word(0x2000 | base) + word(index_word) + word(value)


def binary(opcode, a, value):
    return bytes((opcode,)) + word(a) + word(value)


def binary_var(opcode, a, source):
    return bytes((opcode | 0x80,)) + word(a) + word(source)


def fixture_a():
    asm = Asm()
    asm.emit(move(address(GLOBAL, 10), 1000))
    asm.emit(move(address(GLOBAL, 11), -5))
    asm.emit(binary_var(0x5A, address(GLOBAL, 10), address(GLOBAL, 11)))
    asm.emit(move(address(LOCAL, 0), 7))
    asm.emit(move_var(address(GLOBAL, 12), address(LOCAL, 0)))
    asm.emit(move(address(GLOBAL, 100), 2))
    asm.emit(move_indirect(20, 0x2000 | 100, 42))
    asm.emit(move_var(address(GLOBAL, 13), address(GLOBAL, 22)))
    asm.emit(move(address(BIT, 300), 1))
    asm.emit(move_var(address(GLOBAL, 14), address(BIT, 300)))
    asm.emit(move(address(GLOBAL, 15), 32767))
    asm.emit(binary(0x1B, address(GLOBAL, 15), 32767))
    asm.emit(move(address(GLOBAL, 16), 4))
    asm.emit(binary_var(0x1B, address(GLOBAL, 15), address(GLOBAL, 16)))
    asm.emit(move(address(GLOBAL, 17), 100))
    asm.emit(binary(0x5B, address(GLOBAL, 17), 3))
    asm.emit(binary(0x17, address(GLOBAL, 17), 15))
    asm.emit(binary(0x57, address(GLOBAL, 17), 48))
    asm.jump1(0x28, address(GLOBAL, 10), "take10")
    asm.emit(move(address(GLOBAL, 19), 1))
    asm.label("take10")
    asm.emit(move(address(GLOBAL, 19), 5))
    asm.jump1(0x28, address(GLOBAL, 18), "fall18")
    asm.emit(move(address(GLOBAL, 20), 6))
    asm.label("fall18")
    asm.jump2(0x48, address(GLOBAL, 10), 995, "eqTrue")
    asm.emit(move(address(GLOBAL, 21), 1))
    asm.jump0("done21")
    asm.label("eqTrue")
    asm.emit(move(address(GLOBAL, 21), 9))
    asm.label("done21")
    asm.emit(bytes((0x46,)) + word(address(GLOBAL, 23)))
    asm.emit(bytes((0x46,)) + word(address(GLOBAL, 23)))
    asm.emit(bytes((0xC6,)) + word(address(GLOBAL, 23)))
    asm.emit(bytes((0x68,)) + word(address(GLOBAL, 24)) + bytes((10,)))
    asm.jump2(0x78, address(GLOBAL, 10), 100, "greaterSkip")
    asm.emit(move(address(GLOBAL, 25), 1))
    asm.label("greaterSkip")
    asm.emit(move(address(GLOBAL, 25), 11))
    asm.emit(bytes((0x80, 0x00)))
    return asm.finish()


def fixture_b_parent():
    asm = Asm()
    asm.emit(move(address(GLOBAL, 30), 1))
    asm.emit(bytes((0x0A, 0x0C, 0x00)) + word(42) + bytes((0xFF,)))
    asm.emit(binary(0x5A, address(GLOBAL, 30), 10))
    asm.emit(bytes((0x00,)))
    return asm.finish()


def fixture_b_child():
    asm = Asm()
    asm.emit(move(address(GLOBAL, 30), 100))
    asm.emit(move_var(address(GLOBAL, 32), address(LOCAL, 0)))
    asm.emit(bytes((0x2B,)) + word(address(GLOBAL, 40)))
    asm.emit(move(address(GLOBAL, 31), 7))
    asm.emit(bytes((0x00,)))
    return asm.finish()


def fixture_c():
    asm = Asm()
    asm.emit(move(address(GLOBAL, 40), 100))
    asm.emit(binary(0x5B, address(GLOBAL, 40), 0))
    asm.emit(bytes((0x00,)))
    return asm.finish()


FIXTURES = [
    {"name": "vars-branches", "file": "vm-fixture-a.bytes", "entry": 10, "build": fixture_a,
     "vars": [10, 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 24, 25, 100],
     "values": [995, -5, 7, 42, 1, -262140, 4, 49, 5, 6, 1, 42, 1, 1, 11, 2],
     "bits": [300], "bitValues": [1], "minTicks": 2,
     "expectFault": False, "faultScript": -1, "faultOpcode": -1},
    {"name": "nesting-delay-args", "file": "vm-fixture-b-parent.bytes", "entry": 11,
     "build": fixture_b_parent, "extra": {"12": fixture_b_child},
     "vars": [30, 31, 32], "values": [110, 7, 42],
     "bits": [], "bitValues": [], "minTicks": 2,
     "expectFault": False, "faultScript": -1, "faultOpcode": -1},
    {"name": "divide-by-zero", "file": "vm-fixture-c.bytes", "entry": 13, "build": fixture_c,
     "vars": [], "values": [], "bits": [], "bitValues": [], "minTicks": 0,
     "expectFault": True, "faultScript": 13, "faultOpcode": 0x5B},
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Choose a fresh output directory; existing assets will not be overwritten")
    args.output.mkdir(parents=True)
    specs = []
    for fixture in FIXTURES:
        data = fixture["build"]()
        (args.output / fixture["file"]).write_bytes(data)
        specs.append({key: (1 if fixture[key] else 0) if key == "expectFault" else fixture[key]
                      for key in (
            "name", "file", "entry", "vars", "values", "bits", "bitValues",
            "minTicks", "expectFault", "faultScript", "faultOpcode")})
        for script_id, build in fixture.get("extra", {}).items():
            (args.output / f"vm-fixture-b-child-{script_id}.bytes").write_bytes(build())
    (args.output / "fixtures.json").write_text(json.dumps({"fixtures": specs}, indent=2) + "\n")
    print(f"Wrote {len(specs)} fixtures to {args.output}")


if __name__ == "__main__":
    main()
