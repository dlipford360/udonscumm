#!/usr/bin/env python3
"""Decode an exported v5 ROOM background to index/palette textures for Unity."""
import argparse
import json
from pathlib import Path
import struct
import zlib

from import_monkey import FormatError, chunk, chunks


class Bits:
    def __init__(self, data):
        self.data = data
        self.position = 0

    def read(self, count):
        if self.position + count > len(self.data) * 8:
            raise FormatError("Truncated strip bitstream")
        value = 0
        for bit in range(count):
            position = self.position + bit
            value |= ((self.data[position // 8] >> (position % 8)) & 1) << bit
        self.position += count
        return value


def decode_strip(data, height):
    """Return an eight-pixel-wide strip in row-major order (palette indices)."""
    if height <= 0 or not data:
        raise FormatError("Missing strip or invalid height")
    codec = data[0]
    count = 8 * height
    if codec == 1:
        if len(data) < count + 1:
            raise FormatError("Truncated raw strip")
        return data[1:count + 1]
    width = codec % 10
    family = codec // 10
    if width not in range(4, 9) or family not in (1, 2, 3, 4, 6, 8, 10, 12):
        raise FormatError(f"Unsupported strip codec {codec}")
    if len(data) < 2:
        raise FormatError("Missing initial strip color")
    reader = Bits(data[2:])
    color = data[1]
    direction = -1
    repeat = 0
    pixels = bytearray(count)
    for position in range(count):
        # Vertical strips traverse one column before moving to the next.
        target = (position % height) * 8 + position // height if family in (1, 3) else position
        pixels[target] = color
        if position == count - 1:
            break
        if repeat:
            repeat -= 1
            continue
        if reader.read(1) == 0:
            continue
        if reader.read(1) == 0:
            color = reader.read(width)
            direction = -1
        elif family in (1, 2, 3, 4):
            if reader.read(1):
                direction = -direction
            color = (color + direction) & 255
        else:
            delta = reader.read(3) - 4
            if delta:
                color = (color + delta) & 255
            else:
                length = reader.read(8)
                if length < 2:
                    raise FormatError("Invalid major/minor repeat length")
                repeat = length - 1
    return bytes(pixels)


def child(data, parent, tag):
    _, start, size = parent
    matches = [item for item in chunks(data, start + 8, start + size) if item[0] == tag]
    if len(matches) != 1:
        raise FormatError(f"Expected one {tag} in {parent[0]}, got {len(matches)}")
    return matches[0]


def decode_room(data):
    room = chunk(data, 0)
    if room[0] != "ROOM" or room[2] != len(data):
        raise FormatError("Expected one complete ROOM resource")
    header = child(data, room, "RMHD")
    if header[2] != 14:
        raise FormatError("Expected v5 RMHD")
    width, height, _ = struct.unpack_from("<3H", data, header[1] + 8)
    if width == 0 or width % 8 or height == 0 or width * height > 16 * 1024 * 1024:
        raise FormatError("Invalid or excessive room dimensions")
    palette = child(data, room, "CLUT")
    if palette[2] != 776:
        raise FormatError("Expected a 256-color CLUT")
    palette_rgb = data[palette[1] + 8:palette[1] + palette[2]]
    rmim = child(data, room, "RMIM")
    image = child(data, rmim, "IM00")
    smap = child(data, image, "SMAP")
    strips = width // 8
    if smap[2] < 8 + strips * 4:
        raise FormatError("Truncated SMAP offset table")
    offsets = struct.unpack_from(f"<{strips}I", data, smap[1] + 8)
    minimum = 8 + strips * 4
    if any(offset < minimum or offset >= smap[2] for offset in offsets):
        raise FormatError("SMAP offset outside pixel data")
    # Shared offsets are valid; bound each stream by the next distinct start.
    starts = sorted(set(offsets) | {smap[2]})
    ends = dict(zip(starts, starts[1:]))
    pixels = bytearray(width * height)
    codecs = set()
    for column, offset in enumerate(offsets):
        stream = data[smap[1] + offset:smap[1] + ends[offset]]
        codecs.add(stream[0])
        strip = decode_strip(stream, height)
        for row in range(height):
            start = row * width + column * 8
            pixels[start:start + 8] = strip[row * 8:row * 8 + 8]
    return width, height, bytes(pixels), palette_rgb, sorted(codecs)


def decode_mask_strip(data, height):
    """One mask byte per scanline; each bit covers one pixel in an 8px strip."""
    rows = bytearray()
    pos = 0
    while len(rows) < height:
        if pos >= len(data):
            raise FormatError("Truncated mask run")
        control = data[pos]
        pos += 1
        count = control & 127
        if count == 0:
            raise FormatError("Zero-length mask run")
        count = min(count, height - len(rows))
        if control & 128:
            if pos >= len(data):
                raise FormatError("Truncated repeated mask byte")
            rows.extend([data[pos]] * count)
            pos += 1
        else:
            if pos + count > len(data):
                raise FormatError("Truncated literal mask run")
            rows.extend(data[pos:pos + count])
            pos += count
    return bytes(rows)


def decode_room_masks(data, width, height):
    image = child(data, child(data, chunk(data, 0), "RMIM"), "IM00")
    rgb = bytearray(width * height * 3)
    planes = []
    strips = width // 8
    for tag, start, size in chunks(data, image[1] + 8, image[1] + image[2]):
        if not tag.startswith("ZP"):
            continue
        plane = int(tag[2:])
        if plane < 1 or plane > 3:
            raise FormatError(f"Unsupported mask plane {tag}")
        if size < 8 + strips * 2:
            raise FormatError("Truncated mask offset table")
        offsets = struct.unpack_from(f"<{strips}H", data, start + 8)
        if any(o != 0 and not 8 + strips * 2 <= o < size for o in offsets):
            raise FormatError("Mask offset outside chunk")
        # Streams can share suffixes, so bound reads to the chunk, not the
        # next offset in the table.
        for column, offset in enumerate(offsets):
            if not offset:
                continue
            rows = decode_mask_strip(data[start + offset:start + size], height)
            for y, bits in enumerate(rows):
                for bit in range(8):
                    if bits & (128 >> bit):
                        rgb[(y * width + column * 8 + bit) * 3 + plane - 1] = 255
        planes.append(plane)
    return bytes(rgb), sorted(planes)


def png_rgb(width, height, pixels):
    if len(pixels) != width * height * 3:
        raise ValueError("RGB buffer size mismatch")

    def png_chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload))

    rows = b"".join(b"\0" + pixels[y * width * 3:(y + 1) * width * 3] for y in range(height))
    return (b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + png_chunk(b"IDAT", zlib.compress(rows)) + png_chunk(b"IEND", b""))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("room", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Choose a fresh output directory; existing assets will not be overwritten")
    try:
        width, height, indices, palette, codecs = decode_room(args.room.read_bytes())
        index_rgb = bytes(value for index in indices for value in (index, index, index))
        preview_rgb = b"".join(palette[index * 3:index * 3 + 3] for index in indices)
        args.output.mkdir(parents=True)
        for name, w, h, pixels in (("indices", width, height, index_rgb),
                                   ("palette", 256, 1, palette), ("preview", width, height, preview_rgb)):
            (args.output / f"{name}.png").write_bytes(png_rgb(w, h, pixels))
        metadata = {"schemaVersion": 1, "source": args.room.name, "width": width, "height": height,
                    "codecs": codecs, "limitations": ["Background only; no objects, actors, or foreground masks.",
                                                      "Initial CLUT only; game scripts may change palette entries."]}
        (args.output / "room.json").write_text(json.dumps(metadata, indent=2) + "\n")
    except (FormatError, OSError) as error:
        parser.exit(1, f"Room decode failed: {error}\n")
    print(f"Decoded {width}x{height} background using codecs {codecs}: {args.output}")


if __name__ == "__main__":
    main()
