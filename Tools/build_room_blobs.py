#!/usr/bin/env python3
"""Build per-room runtime blobs for the Udon VM; never executes game content."""
import argparse
import json
import struct
from pathlib import Path

from import_monkey import FormatError, chunk, chunks


SCHEMA_VERSION = 2
MAX_OBJECTS = 200
MAX_BOXES = 64
MAX_LOCAL_SCRIPTS = 64


def find_children(data, start, size, tag):
    return [item for item in chunks(data, start + 8, start + size) if item[0] == tag]


def one_child(data, parent, tag, optional=False):
    matches = find_children(data, parent[1], parent[2], tag)
    if not matches:
        if optional:
            return None
        raise FormatError(f"Room is missing {tag}")
    if len(matches) != 1:
        raise FormatError(f"Room has {len(matches)} {tag} chunks")
    return matches[0]


def parse_room(data, room_id):
    root = chunk(data, 0)
    if root[0] != "ROOM" or root[2] != len(data):
        raise FormatError(f"Room {room_id}: expected one complete ROOM resource")
    rmhd = one_child(data, root, "RMHD")
    if rmhd[2] != 14:
        raise FormatError(f"Room {room_id}: expected v5 RMHD")
    width, height, object_count = struct.unpack_from("<3H", data, rmhd[1] + 8)
    if width == 0 or height == 0 or object_count > MAX_OBJECTS:
        raise FormatError(f"Room {room_id}: bad dimensions/object count")

    palette = b""
    clut = one_child(data, root, "CLUT", optional=True)
    if clut is not None:
        if clut[2] != 776:
            raise FormatError(f"Room {room_id}: expected a 256-color CLUT")
        palette = data[clut[1] + 8:clut[1] + clut[2]]

    trans = 255
    trns = one_child(data, root, "TRNS", optional=True)
    if trns is not None:
        trans = data[trns[1] + 8]

    boxes, matrix = [], b""
    boxd = one_child(data, root, "BOXD", optional=True)
    if boxd is not None:
        payload = data[boxd[1] + 8:boxd[1] + boxd[2]]
        count = struct.unpack_from("<H", payload)[0]
        if count > MAX_BOXES or len(payload) != 2 + count * 20:
            raise FormatError(f"Room {room_id}: invalid BOXD")
        boxes = [payload[2 + i * 20:2 + (i + 1) * 20] for i in range(count)]
        # BOXM is a compressed itinerary (0xFF row markers + triples), kept opaque.
        boxm = one_child(data, root, "BOXM", optional=True)
        if boxm is not None:
            matrix = data[boxm[1] + 8:boxm[1] + boxm[2]]
            if len(matrix) > 4096:
                raise FormatError(f"Room {room_id}: invalid BOXM size")

    scal = one_child(data, root, "SCAL", optional=True)
    scal_payload = data[scal[1] + 8:scal[1] + scal[2]] if scal is not None else b""

    obcds = find_children(data, root[1], root[2], "OBCD")
    if len(obcds) != object_count:
        raise FormatError(f"Room {room_id}: {len(obcds)} OBCDs for {object_count} objects")
    objects = []
    for _, obcd_off, obcd_size in obcds:
        cdhd = find_children(data, obcd_off, obcd_size, "CDHD")
        if len(cdhd) != 1 or cdhd[0][2] != 21:
            raise FormatError(f"Room {room_id}: invalid CDHD")
        payload = data[cdhd[0][1] + 8:cdhd[0][1] + cdhd[0][2]]
        obj_id, x, y, w, h, flags, parent = struct.unpack_from("<HBBBBBB", payload)
        walk_x, walk_y = struct.unpack_from("<2h", payload, 8)
        actor_dir = payload[12]
        objects.append({"id": obj_id, "x": x * 8, "y": y * 8, "w": w * 8, "h": h * 8,
                        "flags": flags, "parent": parent, "walkX": walk_x, "walkY": walk_y,
                        "actorDir": actor_dir, "obcd": obcd_off, "obim": 0})

    obims = find_children(data, root[1], root[2], "OBIM")
    by_id = {obj["id"]: obj for obj in objects}
    for _, obim_off, obim_size in obims:
        imhd = find_children(data, obim_off, obim_size, "IMHD")
        if len(imhd) != 1 or imhd[0][2] < 10:
            raise FormatError(f"Room {room_id}: invalid IMHD")
        obim_id = struct.unpack_from("<H", data, imhd[0][1] + 8)[0]
        if obim_id not in by_id:
            raise FormatError(f"Room {room_id}: OBIM for unknown object {obim_id}")
        if by_id[obim_id]["obim"] != 0:
            raise FormatError(f"Room {room_id}: duplicate OBIM for object {obim_id}")
        by_id[obim_id]["obim"] = obim_off

    scripts = []
    for _, lscr_off, lscr_size in find_children(data, root[1], root[2], "LSCR"):
        if lscr_size < 9:
            raise FormatError(f"Room {room_id}: truncated LSCR")
        script_id = data[lscr_off + 8]
        if any(entry["id"] == script_id for entry in scripts):
            raise FormatError(f"Room {room_id}: duplicate local script {script_id}")
        if len(scripts) >= MAX_LOCAL_SCRIPTS:
            raise FormatError(f"Room {room_id}: too many local scripts")
        scripts.append({"id": script_id, "offset": lscr_off + 9, "size": lscr_size - 9})

    def script_offset(tag):
        found = find_children(data, root[1], root[2], tag)
        if len(found) > 1:
            raise FormatError(f"Room {room_id}: duplicate {tag}")
        return found[0][1] + 8 if found else 0xFFFFFFFF

    return {"width": width, "height": height, "palette": palette, "trans": trans,
            "boxes": boxes, "matrix": matrix, "scal": scal_payload, "objects": objects,
            "scripts": sorted(scripts, key=lambda entry: entry["id"]),
            "encd": script_offset("ENCD"), "excd": script_offset("EXCD")}


def build_blob(room, data):
    out = bytearray()
    out += struct.pack("<HH", SCHEMA_VERSION, room["width"])
    out += struct.pack("<H", room["height"])
    out += bytes((1 if room["palette"] else 0,))
    out += room["palette"]
    out += bytes((len(room["boxes"]),))
    for box in room["boxes"]:
        out += box
    out += struct.pack("<H", len(room["matrix"])) + room["matrix"]
    out += struct.pack("<H", len(room["scal"])) + room["scal"]
    out += bytes((room["trans"],))
    out += struct.pack("<H", len(room["objects"]))
    for obj in room["objects"]:
        out += struct.pack("<HhhHHBBhhBBII", obj["id"], obj["x"], obj["y"], obj["w"], obj["h"],
                           obj["flags"], obj["parent"], obj["walkX"], obj["walkY"],
                           obj["actorDir"], 0, obj["obcd"], obj["obim"])
    out += bytes((len(room["scripts"]),))
    for entry in room["scripts"]:
        out += struct.pack("<BII", entry["id"], entry["offset"], entry["size"])
    out += struct.pack("<II", room["encd"], room["excd"])
    out += struct.pack("<I", len(data)) + data
    return bytes(out)


def parse_blob(blob):
    """Parse a room blob into a dict; raises FormatError on any inconsistency."""
    offset = 0
    schema, width = struct.unpack_from("<HH", blob, offset)
    height, = struct.unpack_from("<H", blob, offset + 4)
    offset += 6
    if schema != SCHEMA_VERSION:
        raise FormatError("Unsupported room blob version")
    has_palette = blob[offset]
    offset += 1
    palette = b""
    if has_palette:
        if offset + 768 > len(blob):
            raise FormatError("Truncated room palette")
        palette = blob[offset:offset + 768]
        offset += 768
    num_boxes = blob[offset]
    offset += 1
    boxes = []
    for _ in range(num_boxes):
        if offset + 20 > len(blob):
            raise FormatError("Truncated walkboxes")
        boxes.append(blob[offset:offset + 20])
        offset += 20
    matrix_len, = struct.unpack_from("<H", blob, offset)
    offset += 2
    if offset + matrix_len > len(blob):
        raise FormatError("Truncated walk matrix")
    matrix = blob[offset:offset + matrix_len]
    offset += matrix_len
    scal_len, = struct.unpack_from("<H", blob, offset)
    offset += 2
    if offset + scal_len > len(blob):
        raise FormatError("Truncated scale table")
    scal = blob[offset:offset + scal_len]
    offset += scal_len
    trans = blob[offset]
    offset += 1
    num_objects, = struct.unpack_from("<H", blob, offset)
    offset += 2
    objects = []
    for _ in range(num_objects):
        if offset + 26 > len(blob):
            raise FormatError("Truncated object table")
        fields = struct.unpack_from("<HhhHHBBhhBBII", blob, offset)
        offset += 26
        objects.append({"id": fields[0], "x": fields[1], "y": fields[2], "w": fields[3],
                        "h": fields[4], "flags": fields[5], "parent": fields[6],
                        "walkX": fields[7], "walkY": fields[8], "actorDir": fields[9],
                        "obcd": fields[11], "obim": fields[12]})
    num_scripts = blob[offset]
    offset += 1
    scripts = []
    for _ in range(num_scripts):
        if offset + 9 > len(blob):
            raise FormatError("Truncated script directory")
        script_id, off, size = struct.unpack_from("<BII", blob, offset)
        offset += 9
        scripts.append({"id": script_id, "offset": off, "size": size})
    encd, excd = struct.unpack_from("<II", blob, offset)
    offset += 8
    room_len, = struct.unpack_from("<I", blob, offset)
    offset += 4
    if room_len < 0 or offset + room_len != len(blob):
        raise FormatError("Truncated room bytes")
    return {"width": width, "height": height, "palette": palette, "trans": trans,
            "boxes": boxes, "matrix": matrix, "scal": scal, "objects": objects,
            "scripts": scripts, "encd": None if encd == 0xFFFFFFFF else encd,
            "excd": None if excd == 0xFFFFFFFF else excd,
            "roomBytes": blob[offset:offset + room_len]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    manifest_path = args.game / "manifest.json"
    rooms_dir = args.game / "rooms"
    if not manifest_path.exists() or not rooms_dir.is_dir():
        parser.error("Game directory must contain manifest.json and rooms/ (run import_monkey.py first)")
    if args.output.exists():
        parser.error("Choose a fresh output directory; existing assets will not be overwritten")
    manifest = json.loads(manifest_path.read_text())
    if manifest.get("schemaVersion") != 1 or manifest.get("engineVersion") != 5:
        parser.error("Expected a v5 schemaVersion 1 resource manifest")
    args.output.mkdir(parents=True)
    rooms = []
    for entry in manifest["rooms"]:
        data = (rooms_dir / f"room-{entry['id']:03}.bytes").read_bytes()
        room = parse_room(data, entry["id"])
        if room["width"] != entry["width"] or room["height"] != entry["height"]:
            raise FormatError(f"Room {entry['id']}: dimensions disagree with manifest")
        path = f"rt-room-{entry['id']:03}.bytes"
        (args.output / path).write_bytes(build_blob(room, data))
        rooms.append({"id": entry["id"], "path": path, "width": room["width"],
                      "height": room["height"], "objects": len(room["objects"]),
                      "walkboxes": len(room["boxes"]), "scripts": len(room["scripts"]),
                      "hasEntry": room["encd"] != 0xFFFFFFFF, "hasExit": room["excd"] != 0xFFFFFFFF,
                      "hasPalette": bool(room["palette"])})
    (args.output / "room-manifest.json").write_text(json.dumps(
        {"schemaVersion": 1, "blobVersion": SCHEMA_VERSION, "rooms": rooms,
         "numGlobalObjects": manifest["directoryCounts"].get("DOBJ", 0),
         "limitations": ["Object graphics stay compressed inside the room bytes.",
                         "Object names, class tables, and verb scripts are not parsed.",
                         "String resources are not included; only inline script strings work."]}, indent=2) + "\n")
    print(f"Built {len(rooms)} room blobs in {args.output}")


if __name__ == "__main__":
    main()
