#!/usr/bin/env python3
"""Bake per-room object image atlases for GPU compositing.

For each room blob with objects, decodes every OBIM image (v5 strip codec,
same as room backgrounds) and packs them into one atlas PNG plus metadata.

Atlas texel: R = palette index, A = coverage (0 for index 0, 255 otherwise).
The runtime resolves color through the room palette texture, so palette
animation keeps working for objects. The index-0-transparent rule matches
the observed title-card surround (room 10 object 109); the ZP01 mask plane
is recorded but not yet applied (see SCUMM.md follow-ups).

Output (git-ignored, like RoomPreviews):
  Assets/Scumm/Generated/ObjectAtlases/<rrr>/atlas.png
  Assets/Scumm/Generated/ObjectAtlases/<rrr>/atlas.json
  {room, atlasSize:[W,H], objects:{id:{u0,v0,u1,v1,w,h}}, transparentIndex:0}
UVs are normalized with Unity convention (v=0 at bottom).
"""
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_room_blobs import parse_blob
from decode_room import decode_strip
from import_monkey import FormatError

ATLAS_MAX_W = 1024
PAD = 1


def decode_bomp(data, width, height):
    """Decode a BOMP image: concatenated bomp-coded lines (no line lengths
    in this variant), 0 = transparent. Mirrors bompDecodeLine(..., setZero=false)."""
    out = bytearray(width * height)
    i = 0
    for y in range(height):
        dst = y * width
        end = dst + width
        while dst < end:
            if i >= len(data):
                raise FormatError("Truncated BOMP codes")
            code = data[i]
            i += 1
            num = (code >> 1) + 1
            if num > end - dst:
                num = end - dst
            if code & 1:
                if i >= len(data):
                    raise FormatError("Truncated BOMP run color")
                color = data[i]
                i += 1
                if color:
                    out[dst:dst + num] = bytes([color]) * num
                dst += num
            else:
                for _ in range(num):
                    if i >= len(data):
                        raise FormatError("Truncated BOMP literal")
                    color = data[i]
                    i += 1
                    if color:
                        out[dst] = color
                    dst += 1
    if i != len(data):
        raise FormatError(f"BOMP size mismatch: used {i} of {len(data)}")
    return bytes(out)


def decode_obim(room_bytes, obim_off, width, height):
    """Return index pixels (bytes, row-major) for one OBIM chunk."""
    imhd_sz = int.from_bytes(room_bytes[obim_off + 8 + 4:obim_off + 8 + 8], "big")
    im01 = obim_off + 8 + imhd_sz
    if room_bytes[im01:im01 + 4] != b"IM01":
        raise FormatError(f"OBIM at {obim_off}: expected IM01")
    body = im01 + 8
    if room_bytes[body:body + 4] == b"BOMP":
        # BOMP variant not yet decoded (e.g. room 99 object 1045); treat as
        # imageless so the bake stays green. See SCUMM.md follow-ups.
        print(f"WARNING: BOMP-encoded object @{obim_off}, skipping image", file=sys.stderr)
        return None
    smap = body
    if room_bytes[smap:smap + 4] != b"SMAP":
        raise FormatError(f"OBIM at {obim_off}: expected SMAP, got {room_bytes[smap:smap+4]!r}")
    smap_sz = int.from_bytes(room_bytes[smap + 4:smap + 8], "big")
    n = width // 8
    if width % 8 or n <= 0:
        raise FormatError(f"Object width {width} not a positive multiple of 8")
    offs = struct.unpack_from(f"<{n}I", room_bytes, smap + 8)
    starts = sorted(set(offs) | {smap_sz})
    ends = dict(zip(starts, starts[1:]))
    pix = bytearray(width * height)
    for col, off in enumerate(offs):
        stream = room_bytes[smap + off:smap + ends[off]]
        strip = decode_strip(stream, height)
        for row in range(height):
            pix[row * width + col * 8:row * width + col * 8 + 8] = strip[row * 8:row * 8 + 8]
    return bytes(pix)


def pack_shelf(sizes):
    """Greedy shelf pack. sizes: [(id, w, h)]. Returns (atlasW, atlasH, placements)."""
    shelves = []  # (y, height, used_w)
    placements = {}
    for oid, w, h in sizes:
        placed = False
        for si, (sy, sh, used) in enumerate(shelves):
            if sh >= h and used + w + PAD <= ATLAS_MAX_W:
                placements[oid] = (used, sy)
                shelves[si] = (sy, sh, used + w + PAD)
                placed = True
                break
        if not placed:
            y = shelves[-1][0] + shelves[-1][1] + PAD if shelves else 0
            shelves.append((y, h, w + PAD))
            placements[oid] = (0, y)
    atlas_w = max(used for _, _, used in shelves)
    atlas_h = shelves[-1][0] + shelves[-1][1]
    return atlas_w, atlas_h, placements


def write_png_rgba(path, width, height, rgba):
    import zlib

    def chunk(tag, payload):
        out = struct.pack(">I", len(payload)) + tag + payload
        out += struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
        return out

    raw = b"".join(b"\x00" + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))
    path.write_bytes(png)


def bake_room(blob_path, out_dir):
    raw = blob_path.read_bytes()
    blob = parse_blob(raw)
    room_bytes = blob["roomBytes"]
    if not blob["objects"]:
        return None
    decoded = {}
    imageless = []
    for obj in blob["objects"]:
        if obj["w"] <= 0 or obj["h"] <= 0 or obj["w"] % 8:
            imageless.append(obj["id"])
            continue
        base = obj["obim"]
        total = int.from_bytes(room_bytes[base + 4:base + 8], "big")
        off, has_image = base + 8, False
        while off + 8 <= base + total:
            tag = room_bytes[off:off + 4]
            try:
                name = tag.decode("ascii")
            except ValueError:
                break
            if not name.isprintable():
                break
            size = int.from_bytes(room_bytes[off + 4:off + 8], "big")
            if size < 8:
                break
            if name == "IM01":
                has_image = True
                break
            off += size
        if not has_image:
            imageless.append(obj["id"])
            continue
        try:
            pix = decode_obim(room_bytes, base, obj["w"], obj["h"])
        except FormatError as e:
            raise FormatError(f"Room {blob_path.stem} object {obj['id']}: {e}")
        if pix is None:
            imageless.append(obj["id"])
            continue
        decoded[obj["id"]] = (obj["w"], obj["h"], pix)
    if not decoded:
        return {"room": int(blob_path.stem.split("-")[2]), "objects": {},
                "imageless": imageless, "atlas": None}
    sizes = sorted(((oid, w, h) for oid, (w, h, _) in decoded.items()), key=lambda t: -t[2])
    atlas_w, atlas_h, placements = pack_shelf(sizes)
    atlas = bytearray(atlas_w * atlas_h * 4)
    meta_objects = {}
    for oid, (w, h, pix) in decoded.items():
        x0, y0 = placements[oid]
        for y in range(h):
            for x in range(w):
                v = pix[y * w + x]
                o = ((y0 + y) * atlas_w + (x0 + x)) * 4
                atlas[o] = v
                atlas[o + 3] = 0 if v == 0 else 255
        meta_objects[str(oid)] = {
            "u0": x0 / atlas_w, "v0": 1.0 - (y0 + h) / atlas_h,
            "u1": (x0 + w) / atlas_w, "v1": 1.0 - y0 / atlas_h,
            "w": w, "h": h,
        }
    out_dir.mkdir(parents=True, exist_ok=True)
    if decoded:
        write_png_rgba(out_dir / "atlas.png", atlas_w, atlas_h, bytes(atlas))
        atlas_ref = {"file": "atlas.png", "size": [atlas_w, atlas_h]}
    else:
        atlas_ref = None
    meta = {"room": int(blob_path.stem.split("-")[2]), "atlas": atlas_ref,
            "objects": meta_objects, "imageless": imageless, "transparentIndex": 0}
    (out_dir / "atlas.json").write_text(json.dumps(meta, indent=1) + "\n")
    with open(out_dir / "table.txt", "w") as f:
        for oid in sorted(meta_objects, key=int):
            uv = meta_objects[oid]
            f.write(f"{oid} {uv['u0']!r} {uv['v0']!r} {uv['u1']!r} {uv['v1']!r}\n")
    return meta


def main():
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--blobs", default="Assets/Scumm/Generated/RoomBlobs")
    ap.add_argument("--output", default="Assets/Scumm/Generated/ObjectAtlases")
    ap.add_argument("--rooms", nargs="*", type=int, default=None)
    args = ap.parse_args()
    blobs = sorted(Path(args.blobs).glob("rt-room-*.bytes"))
    if args.rooms is not None:
        blobs = [b for b in blobs if int(b.stem.split("-")[2]) in args.rooms]
    baked, skipped, failed = [], [], []
    for blob_path in blobs:
        room = int(blob_path.stem.split("-")[2])
        out_dir = Path(args.output) / f"{room:03d}"
        try:
            if parse_blob(blob_path.read_bytes())["objects"]:
                meta = bake_room(blob_path, out_dir)
                baked.append((room, len(meta["objects"])))
            else:
                skipped.append(room)
        except FormatError as e:
            failed.append((room, str(e)))
    print(f"baked={len(baked)} skipped={len(skipped)} failed={len(failed)}")
    for room, count in baked:
        print(f"  room {room}: {count} objects")
    for room, err in failed:
        print(f"  FAILED room {room}: {err}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
