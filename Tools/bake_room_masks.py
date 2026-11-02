"""Refresh mask textures alongside existing room previews; game data stays local."""
import argparse
import json
from pathlib import Path
from decode_room import decode_room, decode_room_masks, png_rgb


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rooms", type=Path, default=Path("Assets/Scumm/Generated/MonkeyIsland/rooms"))
    parser.add_argument("--previews", type=Path, default=Path("Assets/Scumm/Generated/RoomPreviews"))
    args = parser.parse_args()
    for preview in sorted(args.previews.iterdir()):
        if not preview.is_dir() or not preview.name.isdigit():
            continue
        data = (args.rooms / f"room-{int(preview.name):03d}.bytes").read_bytes()
        width, height, _, _, _ = decode_room(data)
        pixels, planes = decode_room_masks(data, width, height)
        (preview / "masks.png").write_bytes(png_rgb(width, height, pixels))
        meta_path = preview / "room.json"
        meta = json.loads(meta_path.read_text())
        meta["maskPlanes"] = max(planes, default=0)
        meta_path.write_text(json.dumps(meta, indent=2) + "\n")
        print(f"Room {preview.name}: mask planes {planes}, covered pixels {sum(v != 0 for v in pixels)}")


if __name__ == "__main__":
    main()
