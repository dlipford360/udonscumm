import json
import struct
import tempfile
import unittest
from pathlib import Path

from build_room_blobs import build_blob, parse_blob, parse_room
from import_monkey import FormatError


GENERATED = Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated"


def load_room_blob(blob):
    room = parse_blob(blob)
    return room


class RoomBlobTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        manifest = json.loads((GENERATED / "MonkeyIsland/manifest.json").read_text())
        cls.entries = {entry["id"]: entry for entry in manifest["rooms"]}

    def parse_generated(self, room_id):
        data = (GENERATED / f"MonkeyIsland/rooms/room-{room_id:03}.bytes").read_bytes()
        return data, parse_room(data, room_id)

    def test_lookout_matches_manifest_and_decodes(self):
        data, room = self.parse_generated(38)
        entry = self.entries[38]
        self.assertEqual((room["width"], room["height"]), (entry["width"], entry["height"]))
        self.assertEqual(len(room["palette"]), 768)
        self.assertEqual(len(room["objects"]), entry["objectCount"])
        script_kinds = {}
        for tag, off, size in __import__("import_monkey", fromlist=["chunks"]).chunks(data, 8):
            if tag in ("ENCD", "EXCD", "LSCR"):
                script_kinds.setdefault(tag, 0)
                script_kinds[tag] += 1
        self.assertEqual(len(room["scripts"]), script_kinds.get("LSCR", 0))
        self.assertEqual(room["encd"] != 0xFFFFFFFF, "ENCD" in script_kinds)
        self.assertEqual(room["excd"] != 0xFFFFFFFF, "EXCD" in script_kinds)
        blob = build_blob(room, data)
        back = load_room_blob(blob)
        self.assertEqual(back["roomBytes"], data)
        self.assertEqual(back["width"], room["width"])
        self.assertEqual(len(back["objects"]), len(room["objects"]))
        first = room["objects"][0]
        back_first = back["objects"][0]
        self.assertEqual((back_first["id"], back_first["x"], back_first["y"], back_first["w"],
                          back_first["h"], back_first["flags"], back_first["parent"]),
                         (first["id"], first["x"], first["y"], first["w"], first["h"],
                          first["flags"], first["parent"]))
        self.assertEqual(back["scripts"][0]["id"], room["scripts"][0]["id"])

    def test_all_rooms_parse(self):
        checked, with_palette, with_boxes = 0, 0, 0
        for room_id in sorted(self.entries):
            data = (GENERATED / f"MonkeyIsland/rooms/room-{room_id:03}.bytes").read_bytes()
            room = parse_room(data, room_id)
            self.assertEqual(room["width"], self.entries[room_id]["width"])
            build_blob(room, data)  # must not raise
            checked += 1
            with_palette += 1 if room["palette"] else 0
            with_boxes += 1 if room["boxes"] else 0
        self.assertGreater(checked, 80)
        self.assertGreater(with_palette, 70)
        print(f"\nParsed {checked} rooms ({with_palette} with palettes, {with_boxes} with walkboxes)")

    def test_rejects_bad_blobs(self):
        data, room = self.parse_generated(38)
        build_blob(room, data)
        with tempfile.TemporaryDirectory() as folder:
            bad = Path(folder) / "room-038.bytes"
            bad.write_bytes(data[:100])
            with self.assertRaises(FormatError):
                parse_room(bad.read_bytes(), 38)


if __name__ == "__main__":
    unittest.main()
