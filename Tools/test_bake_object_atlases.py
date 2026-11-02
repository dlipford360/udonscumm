"""Baker regression: room 10 objects decode and pack into a usable atlas."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from bake_object_atlases import bake_room


class BakeObjectAtlasesTests(unittest.TestCase):
    def test_room10_title_object(self):
        with tempfile.TemporaryDirectory() as tmp:
            meta = bake_room(
                Path("Assets/Scumm/Generated/RoomBlobs/rt-room-010.bytes"),
                Path(tmp) / "010",
            )
            self.assertIsNotNone(meta)
            self.assertIn("109", meta["objects"])
            entry = meta["objects"]["109"]
            self.assertEqual((entry["w"], entry["h"]), (224, 120))
            self.assertLess(entry["u0"], entry["u1"])
            self.assertLess(entry["v0"], entry["v1"])
            # All six imaged objects packed; no failures.
            self.assertEqual(len(meta["objects"]), 6)
            self.assertTrue((Path(tmp) / "010" / "atlas.png").exists())

    def test_imageless_room_has_no_atlas(self):
        with tempfile.TemporaryDirectory() as tmp:
            meta = bake_room(
                Path("Assets/Scumm/Generated/RoomBlobs/rt-room-038.bytes"),
                Path(tmp) / "038",
            )
        self.assertEqual(meta["objects"], {})
        self.assertTrue(len(meta["imageless"]) > 0)
        self.assertIsNone(meta["atlas"])


if __name__ == "__main__":
    unittest.main()
