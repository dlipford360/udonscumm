#!/usr/bin/env python3
"""Inventory and extract v5 ROOM/SCRP resources; never execute game content."""
import argparse
import hashlib
import json
from pathlib import Path
import struct


class FormatError(ValueError):
    pass


def chunk(data, offset, end=None):
    end = len(data) if end is None else end
    if offset < 0 or end > len(data) or offset + 8 > end:
        raise FormatError(f"Truncated chunk header at {offset}")
    tag = data[offset:offset + 4]
    size = int.from_bytes(data[offset + 4:offset + 8], "big")
    if size < 8 or offset + size > end:
        raise FormatError(f"Invalid {tag!r} size {size} at {offset}")
    if any(c < 32 or c > 126 for c in tag):
        raise FormatError(f"Invalid tag at {offset}")
    return tag.decode("ascii"), offset, size


def chunks(data, start=0, end=None):
    end = len(data) if end is None else end
    while start < end:
        item = chunk(data, start, end)
        yield item
        start += item[2]


def decode(data, expected):
    for key in (0, 0x69):
        if bytes(c ^ key for c in data[:4]) == expected:
            return data.translate(bytes(c ^ key for c in range(256))), key
    raise FormatError(f"Expected raw or XOR-69 {expected!r} data")


def directory(payload):
    if len(payload) < 2:
        raise FormatError("Missing resource count")
    count = int.from_bytes(payload[:2], "little")
    if len(payload) != 2 + count * 5:
        raise FormatError("Resource directory length does not match count")
    return [(payload[2 + i], struct.unpack_from("<I", payload, 2 + count + i * 4)[0])
            for i in range(count)]


def room_offsets(payload):
    if not payload or len(payload) != 1 + payload[0] * 5:
        raise FormatError("Invalid LOFF length")
    result = {}
    for i in range(payload[0]):
        room, offset = struct.unpack_from("<BI", payload, 1 + i * 5)
        if room in result:
            raise FormatError(f"Duplicate LOFF room {room}")
        result[room] = offset
    return result


def fingerprint(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return {"name": path.name, "size": path.stat().st_size, "sha256": digest.hexdigest()}


def inspect(source):
    paths = {}
    for path in source.iterdir():
        if path.is_file():
            key = path.name.lower()
            if key in paths:
                raise FormatError(f"Ambiguous case-insensitive filename: {key}")
            paths[key] = path
    if not {"monkey.000", "monkey.001"}.issubset(paths):
        raise FormatError("Expected monkey.000 and monkey.001")
    index, index_key = decode(paths["monkey.000"].read_bytes(), b"RNAM")
    disk, disk_key = decode(paths["monkey.001"].read_bytes(), b"LECF")
    index_blocks = {}
    for tag, offset, size in chunks(index):
        if tag in index_blocks:
            raise FormatError(f"Duplicate index block {tag}")
        index_blocks[tag] = index[offset + 8:offset + size]
    if len(index_blocks.get("MAXS", b"")) != 18:
        raise FormatError("Expected the 18-byte v5 MAXS layout")
    limits = struct.unpack("<9H", index_blocks["MAXS"])
    root = chunk(disk, 0)
    if root[2] != len(disk):
        raise FormatError("LECF size does not match file length")
    outer = list(chunks(disk, 8))
    loff = [entry for entry in outer if entry[0] == "LOFF"]
    if len(loff) != 1:
        raise FormatError("Expected exactly one LOFF directory")
    _, off, size = loff[0]
    offsets = room_offsets(disk[off + 8:off + size])
    tables = {}
    for tag in ("DROO", "DSCR", "DSOU", "DCOS", "DCHR", "DOBJ"):
        if tag not in index_blocks:
            raise FormatError(f"Missing directory {tag}")
        tables[tag] = directory(index_blocks[tag])

    # Object tables: owner/state nibbles plus class data live in the DOBJ payload.
    dobj = index_blocks["DOBJ"]
    if len(dobj) < 2:
        raise FormatError("Truncated DOBJ block")
    num_objects = struct.unpack_from("<H", dobj)[0]
    expected = 2 + num_objects + 4 * num_objects
    if len(dobj) != expected or num_objects == 0 or num_objects > 4096:
        raise FormatError("Invalid DOBJ payload size")
    owners = bytes(b & 0x0F for b in dobj[2:2 + num_objects])
    states = bytes(b >> 4 for b in dobj[2:2 + num_objects])
    classes = b"".join(struct.pack("<I", value) for value in
                       struct.unpack_from(f"<{num_objects}I", dobj, 2 + num_objects))
    object_tables = (struct.pack("<H", num_objects) + owners + states + classes)
    object_count = num_objects
    # Resolve only actual LFLF child boundaries, not arbitrary byte patterns.
    resources = {}
    for tag, off, size in outer:
        if tag == "LFLF":
            for child in chunks(disk, off + 8, off + size):
                resources[child[1]] = child
    rooms, scripts, output = [], [], {}
    output["object-tables.bytes"] = object_tables
    for room_id, base in sorted(offsets.items()):
        if room_id >= len(tables["DROO"]) or tables["DROO"][room_id][0] != 1:
            raise FormatError(f"Room {room_id}: expected disk 1")
        entry = resources.get(base)
        if entry is None or entry[0] != "ROOM":
            raise FormatError(f"Room {room_id}: LOFF is not a ROOM boundary")
        _, _, size = entry
        children = list(chunks(disk, base + 8, base + size))
        headers = [x for x in children if x[0] == "RMHD"]
        if len(headers) != 1 or headers[0][2] != 14:
            raise FormatError(f"Room {room_id}: invalid RMHD")
        width, height, objects = struct.unpack_from("<3H", disk, headers[0][1] + 8)
        filename = f"rooms/room-{room_id:03}.bytes"
        output[filename] = disk[base:base + size]
        rooms.append({"id": room_id, "fileOffset": base, "size": size,
                      "width": width, "height": height, "objectCount": objects,
                      "path": filename,
                      "chunks": [{"tag": t, "offset": o - base, "size": s}
                                 for t, o, s in children]})
        for tag, off, length in children:
            if tag not in ("ENCD", "EXCD", "LSCR"):
                continue
            if tag == "LSCR" and length < 9:
                raise FormatError("Missing local script ID")
            script_id = disk[off + 8] if tag == "LSCR" else -1
            code = off + (9 if tag == "LSCR" else 8)
            path = f"scripts/room-{room_id:03}-{tag.lower()}-{script_id}.bytes"
            if path in output:
                raise FormatError(f"Duplicate script {path}")
            output[path] = disk[code:off + length]
            scripts.append({"kind": tag, "id": script_id, "room": room_id,
                            "fileOffset": code, "roomOffset": code - base,
                            "size": off + length - code, "path": path})
    for script_id, (room, relative) in enumerate(tables["DSCR"]):
        if relative == 0xFFFFFFFF or room == 0:
            continue
        if room not in offsets:
            raise FormatError(f"Script {script_id}: missing room {room}")
        base = offsets[room] + relative
        entry = resources.get(base)
        if entry is None or entry[0] != "SCRP":
            raise FormatError(f"Script {script_id}: directory is not a SCRP boundary")
        size = entry[2]
        path = f"scripts/global-{script_id:03}.bytes"
        output[path] = disk[base + 8:base + size]
        scripts.append({"kind": "SCRP", "id": script_id, "room": room,
                        "fileOffset": base + 8, "roomOffset": relative + 8,
                        "size": size - 8, "path": path})
    manifest = {
        "schemaVersion": 1, "engineVersion": 5,
        "releaseLabel": "unverified v5 layout; identify using source fingerprints",
        "indexXorKey": index_key, "diskXorKey": disk_key,
        "sources": [fingerprint(paths[n]) for n in ("monkey.000", "monkey.001", "monster.sou")
                    if n in paths],
        "limits": {"globals": limits[0], "bitVariables": limits[2],
                   "localObjects": limits[3], "charsets": limits[5], "inventory": limits[8],
                   "globalObjects": object_count},
        "directoryCounts": {tag: len(items) for tag, items in tables.items()},
        "rooms": rooms, "scripts": scripts,
        "limitations": ["Graphics are still compressed inside ROOM resources.",
                        "Object verb bytecode remains inside OBCD chunks.",
                        "Costumes, sounds, and speech are not converted.",
                        "This manifest does not certify complete game compatibility."]}
    return manifest, output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--release-label", default="Monkey Island v5 (unverified release)")
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Output already exists; choose a fresh directory to avoid overwriting assets")
    try:
        manifest, files = inspect(args.source)
        manifest["releaseLabel"] = args.release_label
        args.output.mkdir(parents=True)
        for name, data in files.items():
            path = args.output / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    except (OSError, FormatError) as error:
        parser.exit(1, f"Import failed: {error}\n")
    print(f"Extracted {len(manifest['rooms'])} rooms and {len(manifest['scripts'])} scripts")
    print(f"Manifest: {args.output / 'manifest.json'}")


if __name__ == "__main__":
    main()
