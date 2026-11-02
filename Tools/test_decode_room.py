import struct
import unittest
import zlib

from decode_room import Bits, decode_room, decode_strip, decode_mask_strip, decode_room_masks, png_rgb
from import_monkey import FormatError


def pack_bits(fields):
    value, count = 0, 0
    for number, width in fields:
        value |= number << count
        count += width
    return value.to_bytes((count + 7) // 8, "little")


def block(tag, payload):
    return tag + struct.pack(">I", len(payload) + 8) + payload


class DecoderTests(unittest.TestCase):
    def test_mask_runs_and_bounds(self):
        self.assertEqual(decode_mask_strip(bytes([2, 128, 1, 131, 85]), 5), bytes([128, 1, 85, 85, 85]))
        self.assertEqual(decode_mask_strip(bytes([255, 7]), 2), bytes([7, 7]))
        for data in (b"", b"\0", b"\x80", b"\x81", b"\x02\x01"):
            with self.assertRaises(FormatError):
                decode_mask_strip(data, 2)

    def test_mask_offsets_empty_strips_and_msb_order(self):
        plane = block(b"ZP01", struct.pack("<2H", 12, 0) + bytes([2, 128, 1]))
        room = block(b"ROOM", block(b"RMIM", block(b"IM00", plane)))
        rgb, planes = decode_room_masks(room, 16, 2)
        self.assertEqual(planes, [1])
        self.assertEqual([i for i, value in enumerate(rgb) if value], [0, (16 + 7) * 3])
        for offset in (1, 11, 99):
            plane = block(b"ZP01", struct.pack("<2H", offset, 0) + bytes([130, 255]))
            room = block(b"ROOM", block(b"RMIM", block(b"IM00", plane)))
            with self.assertRaises(FormatError):
                decode_room_masks(room, 16, 2)

    def test_actual_lookout_masks_cover_foreground(self):
        from pathlib import Path
        path = Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/rooms/room-038.bytes"
        if not path.exists():
            self.skipTest("Local game data not imported")
        data = path.read_bytes()
        width, height, _, _, _ = decode_room(data)
        rgb, planes = decode_room_masks(data, width, height)
        self.assertEqual(planes, [1])
        self.assertEqual((width, height), (320, 144))
        self.assertEqual(len(rgb), width * height * 3)
        covered = sum(1 for i in range(0, len(rgb), 3) if rgb[i] != 0)
        self.assertGreater(covered, 1000)
        # Only the R channel is used; G/B planes stay empty.
        self.assertEqual(sum(rgb[1::3]) + sum(rgb[2::3]), 0)

    def test_lsb_reader_and_bounds(self):
        bits = Bits(b"\xa5\x03")
        self.assertEqual(bits.read(3), 5)
        self.assertEqual(bits.read(7), 116)
        with self.assertRaises(FormatError):
            bits.read(7)

    def test_raw_row_order(self):
        pixels = bytes(range(16))
        self.assertEqual(decode_strip(b"\x01" + pixels, 2), pixels)

    def test_basic_direction_and_literal(self):
        # 5, same 5, decrement 4, reverse 5, literal 12, same...
        fields = [(0, 1), (3, 3), (7, 3), (1, 2), (12, 4), (0, 3)]
        self.assertEqual(decode_strip(bytes([24, 5]) + pack_bits(fields), 1),
                         bytes([5, 5, 4, 5, 12, 12, 12, 12]))

    def test_vertical_column_order(self):
        # Sixteen sequential colors encoded as absolute updates.
        fields = [(value, width) for color in range(1, 16) for value, width in ((1, 2), (color, 4))]
        self.assertEqual(decode_strip(bytes([14, 0]) + pack_bits(fields), 2),
                         bytes(list(range(0, 16, 2)) + list(range(1, 16, 2))))

    def test_major_minor_repeat_and_wrap(self):
        # First color 0; delta -1 wraps to 255; run of three 255s; absolute 9.
        fields = [(3, 2), (3, 3), (3, 2), (4, 3), (3, 8), (1, 2), (9, 8), (0, 2)]
        self.assertEqual(decode_strip(bytes([68, 0]) + pack_bits(fields), 1),
                         bytes([0, 255, 255, 255, 255, 9, 9, 9]))

    def test_reject_bad_strips(self):
        for data in (b"", b"\x01\x00", b"\x63\x00", b"\x18"):
            with self.assertRaises(FormatError):
                decode_strip(data, 1)

    def test_room_container_and_palette(self):
        pixels = bytes(range(8))
        palette = bytes(range(256)) * 3
        smap = block(b"SMAP", struct.pack("<I", 12) + b"\x01" + pixels)
        room = block(b"ROOM", block(b"RMHD", struct.pack("<3H", 8, 1, 0))
                     + block(b"CLUT", palette) + block(b"RMIM", block(b"IM00", smap)))
        self.assertEqual(decode_room(room), (8, 1, pixels, palette, [1]))
        with self.assertRaises(FormatError):
            decode_room(room[:-1])

    def test_png_crc_and_scanlines(self):
        image = png_rgb(1, 2, bytes([1, 2, 3, 4, 5, 6]))
        self.assertEqual(image[:8], b"\x89PNG\r\n\x1a\n")
        offset = 8
        while offset < len(image):
            length = struct.unpack_from(">I", image, offset)[0]
            tag = image[offset + 4:offset + 8]
            payload = image[offset + 8:offset + 8 + length]
            self.assertEqual(struct.unpack_from(">I", image, offset + 8 + length)[0], zlib.crc32(tag + payload))
            if tag == b"IDAT":
                self.assertEqual(zlib.decompress(payload), b"\0\x01\x02\x03\0\x04\x05\x06")
            offset += length + 12


if __name__ == "__main__":
    unittest.main()
