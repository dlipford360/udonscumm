import struct
import tempfile
import unittest
from pathlib import Path

from import_monkey import FormatError, chunk, chunks, decode, directory, room_offsets, inspect


def block(tag, payload=b""):
    return tag + struct.pack(">I", 8 + len(payload)) + payload


class ParserTests(unittest.TestCase):
    def make_game(self, path, bad_script_offset=False):
        room = block(b"ROOM", block(b"RMHD", struct.pack("<3H", 320, 144, 0))
                     + block(b"ENCD", b"\x00") + block(b"EXCD") + block(b"LSCR", b"\xc8\x80\x00"))
        script = block(b"SCRP", b"\x1a\x00\x00\xff\xff\x00")
        loff = block(b"LOFF", b"\x01" + struct.pack("<BI", 1, 30))
        disk = block(b"LECF", loff + block(b"LFLF", room + script))
        table = lambda offset: struct.pack("<HBBII", 2, 0, 1, 0xFFFFFFFF, offset)
        index = block(b"RNAM") + block(b"MAXS", struct.pack("<9H", 800, 16, 2048, 200, 50, 7, 100, 50, 80))
        index += block(b"DROO", table(0))
        index += block(b"DSCR", table(1 if bad_script_offset else len(room)))
        for tag in (b"DSOU", b"DCOS", b"DCHR"):
            index += block(tag, b"\x00\x00")
        index += block(b"DOBJ", struct.pack("<H", 1) + b"\x0f" + struct.pack("<I", 0))
        (path / "monkey.000").write_bytes(bytes(x ^ 0x69 for x in index))
        (path / "monkey.001").write_bytes(bytes(x ^ 0x69 for x in disk))

    def test_complete_extraction_and_source_offsets(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)
            self.make_game(path)
            manifest, output = inspect(path)
            self.assertEqual(manifest["limits"]["globals"], 800)
            self.assertEqual(len(manifest["rooms"]), 1)
            self.assertEqual(len(manifest["scripts"]), 4)
            self.assertEqual(output["scripts/global-001.bytes"], b"\x1a\x00\x00\xff\xff\x00")
            self.assertEqual(output["scripts/room-001-lscr-200.bytes"], b"\x80\x00")
            disk = decode((path / "monkey.001").read_bytes(), b"LECF")[0]
            for script in manifest["scripts"]:
                start = script["fileOffset"]
                self.assertEqual(disk[start:start + script["size"]], output[script["path"]])

    def test_script_directory_cannot_point_inside_a_resource(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)
            self.make_game(path, bad_script_offset=True)
            with self.assertRaisesRegex(FormatError, "SCRP boundary"):
                inspect(path)

    def test_chunk_boundaries(self):
        data = block(b"ROOM", block(b"RMHD", b"123456")) + block(b"SCRP", b"\x00")
        self.assertEqual(list(chunks(data)), [("ROOM", 0, 22), ("SCRP", 22, 9)])
        self.assertEqual(list(chunks(data, 8, 22)), [("RMHD", 8, 14)])
        with self.assertRaises(FormatError):
            chunk(data, 22, 29)

    def test_bad_sizes_and_trailing_bytes(self):
        for data in (b"ROOM\0\0\0\0", b"ROOM\0\0\0\x20", block(b"ROOM") + b"x"):
            with self.assertRaises(FormatError):
                list(chunks(data))

    def test_encoding(self):
        data = block(b"RNAM", bytes(range(256)))
        encrypted = bytes(x ^ 0x69 for x in data)
        self.assertEqual(decode(encrypted, b"RNAM"), (data, 0x69))
        self.assertEqual(decode(data, b"RNAM"), (data, 0))
        with self.assertRaises(FormatError):
            decode(b"wrong", b"RNAM")

    def test_directory_uses_separate_room_and_offset_arrays(self):
        data = struct.pack("<H", 3) + bytes([1, 10, 0]) + struct.pack("<3I", 0, 0x123456, 0xFFFFFFFF)
        self.assertEqual(directory(data), [(1, 0), (10, 0x123456), (0, 0xFFFFFFFF)])
        with self.assertRaises(FormatError):
            directory(data[:-1])

    def test_loff_layout_and_duplicate_rejection(self):
        self.assertEqual(room_offsets(b"\x02" + struct.pack("<BIBI", 1, 455, 2, 46530)),
                         {1: 455, 2: 46530})
        with self.assertRaises(FormatError):
            room_offsets(b"\x02" + struct.pack("<BIBI", 1, 455, 1, 46530))
        with self.assertRaises(FormatError):
            room_offsets(b"\x01\x01")


if __name__ == "__main__":
    unittest.main()
