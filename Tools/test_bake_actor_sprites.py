"""Regression cases for classic costume decoding, independent of game assets."""
import json
import struct
import tempfile
import unittest
from pathlib import Path

from bake_actor_sprites import Costume, _decode_byle, bake_costume, find_costume_resource
from import_monkey import FormatError


class CostumeTests(unittest.TestCase):
    def test_run_crosses_columns(self):
        # Column-major stream: five pixels of 1, then one of 2.
        self.assertEqual(_decode_byle(bytes([0x15, 0x21]), 2, 3),
                         bytes([1, 1, 1, 1, 1, 2]))

    def test_extended_run_crosses_columns(self):
        self.assertEqual(_decode_byle(bytes([0x30, 6]), 3, 2), bytes([3] * 6))

    def test_32_colors(self):
        self.assertEqual(_decode_byle(bytes([0xF9, 0x81]), 2, 1, 32),
                         bytes([31, 16]))

    def test_bad_runs_fail_instead_of_partial_sprite(self):
        for data in (b"", b"\x10", b"\x10\x00", b"\x11"):
            with self.subTest(data=data), self.assertRaises(FormatError):
                _decode_byle(data, 2, 2)

    def test_last_animation_entry_is_inclusive(self):
        resource = bytearray(128)
        resource[:4] = b"COST"
        struct.pack_into(">I", resource, 4, len(resource))
        resource[8:10] = bytes([7, 0x58])
        # baseptr = resource + 2; final animation is frame 1, facing 3.
        struct.pack_into("<H", resource, 60 + 7 * 2, 90)
        struct.pack_into("<H", resource, 26, 100)
        struct.pack_into("<HHB", resource, 92, 0x8000, 0, 0)
        resource[102] = 1
        cost = Costume(resource)
        self.assertEqual(cost.decode_pose(3, 1), {0: 0})

    def test_chore_sequence_steps_limbs_until_loop(self):
        # One limb ranging over commands 0..1; states must be 0, 1, then stop.
        resource = bytearray(256)
        resource[:4] = b"COST"
        struct.pack_into(">I", resource, 4, len(resource))
        resource[8:10] = bytes([7, 0x58])
        struct.pack_into("<H", resource, 60 + 7 * 2, 100)
        struct.pack_into("<H", resource, 26, 120)
        struct.pack_into("<H", resource, 102, 0x8000)
        struct.pack_into("<H", resource, 104, 0)
        resource[106] = 2
        resource[122] = 0x00
        resource[123] = 0x01
        cost = Costume(bytes(resource))
        self.assertEqual(cost.chore_sequence(3, 1), [{0: 0}, {0: 1}])

    def test_chore_sequence_empty_without_chore(self):
        resource = bytearray(256)
        resource[:4] = b"COST"
        struct.pack_into(">I", resource, 4, len(resource))
        resource[8:10] = bytes([7, 0x58])
        struct.pack_into("<H", resource, 26, 120)
        cost = Costume(bytes(resource))
        # anim_table entry 7 is zero: no chore, no sequence.
        self.assertEqual(cost.chore_sequence(3, 1), [])
        # anim beyond the table is also empty.
        self.assertEqual(cost.chore_sequence(3, 5), [])

    def test_fire_stand_chore_bakes_three_step_loop(self):
        game = Path(__file__).resolve().parent.parent.parent / "monkeyisland"
        try:
            resource = find_costume_resource(str(game), 48)
        except (FileNotFoundError, KeyError, FormatError) as e:
            self.skipTest(f"Local game data not available: {e}")
        cost = Costume(resource)
        steps = cost.chore_sequence(2, 3)
        self.assertEqual(len(steps), 3)
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "048"
            bake_costume(48, resource, out)
            seq = json.loads((out / "poses.json").read_text())["seq"]
            self.assertEqual(seq["2:3"], [160, 3])
            table = (out / "table.txt").read_text().splitlines()
            self.assertTrue(any(line.startswith("2 160 ") for line in table))
            self.assertTrue(any(line.startswith("2 162 ") for line in table))

    def test_cloud_cels_bake_as_static_chore_poses(self):
        game = Path(__file__).resolve().parent.parent.parent / "monkeyisland"
        try:
            resource = find_costume_resource(str(game), 59)
        except (FileNotFoundError, KeyError, FormatError) as e:
            self.skipTest(f"Local game data not available: {e}")
        cost = Costume(resource)
        # Ambient cloud chores are single static cels, not loops.
        self.assertEqual(len(cost.chore_sequence(2, 4)), 1)
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "059"
            meta = bake_costume(59, resource, out)
            self.assertIn("2:4", meta)
            self.assertIn("2:5", meta)
            self.assertIn("2:6", meta)
            self.assertEqual(json.loads((out / "poses.json").read_text())["seq"], {})


if __name__ == "__main__":
    unittest.main()
