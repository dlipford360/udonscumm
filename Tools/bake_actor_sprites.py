#!/usr/bin/env python3
"""Bake static actor-pose sprites for GPU compositing.

Ports ScummVM's classic costume path (ClassicCostumeLoader::loadCostume /
costumeDecodeData + drawLimb geometry + ByleRLE cel decode) far enough to
evaluate one pose per (costume, facing, frame): all limbs with their cel,
relative offsets and move accumulation, composited into one sprite.

Atlas texel: R = room palette index (via costume palette), A = coverage.
Runtime selects a sprite per actor; no AKSQ interpreter runs in Udon.
Animation cycling (increaseAnims stepping) is a follow-up.

Output (git-ignored):
  Assets/Scumm/Generated/ActorSprites/<costume>/atlas.png + poses.json
  poses.json: {poses: {"<facing>:<frame>": {u0,v0,u1,v1,w,h,ax,ay}}, ...}
  (ax, ay) = hotspot: sprite pixel that sits at the actor's (x, y) foot.
"""
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from import_monkey import FormatError, chunks, decode, directory

ATLAS_MAX_W = 1024
PAD = 1


class Costume:
    def __init__(self, resource):
        # ClassicCostumeLoader::loadCostume, v5 (non-small-header: ptr += 2,
        # where ptr points at the COST chunk including tag). All table
        # offsets below are relative to this base, exactly like _baseptr.
        self.base = resource[2:]
        if len(self.base) < 24:
            raise FormatError("Costume too short")
        self.num_anim = self.base[6]
        self.format = self.base[7] & 0x7F
        if self.format not in (0x58, 0x59):
            raise FormatError(f"Unsupported costume format {self.format:#x}")
        self.num_colors = 16 if self.format == 0x58 else 32
        self.palette = list(self.base[8:8 + self.num_colors])
        ptr = 8 + self.num_colors
        self.frame_offsets = list(struct.unpack_from(f"<16H", self.base, ptr + 2))
        self.anim_table = list(struct.unpack_from(f"<{self.num_anim + 1}H", self.base, ptr + 34))
        self.anim_cmds_off = struct.unpack_from("<H", self.base, ptr)[0]

    def decode_pose(self, facing, frame, base_frame=1):
        """Evaluate a static pose, accumulating limb state like the engine.

        Fresh limbs start stopped; the init chore (frame 1) runs first, then
        the target frame overlays its limbs — mirroring costumDecodeData.
        Returns {limb: curpos} for drawable limbs.
        """
        state = {}
        stopped = set(range(16))
        steps = ([base_frame] if base_frame != frame else []) + [frame]
        for fr in steps:
            anim = facing + fr * 4
            if anim > self.num_anim or anim >= len(self.anim_table):
                continue
            chore = self.anim_table[anim]
            if chore == 0:
                continue
            r = chore
            mask = struct.unpack_from("<H", self.base, r)[0]
            r += 2
            i = 0
            use = 0xFFFF
            while mask & 0xFFFF and i < 16:
                if mask & 0x8000:
                    j = struct.unpack_from("<H", self.base, r)[0]
                    r += 2
                    if use & 0x8000:
                        if j == 0xFFFF:
                            state.pop(i, None)
                            stopped.add(i)
                        else:
                            extra = self.base[r]
                            r += 1
                            cmd = self.anim_cmds[j] if j < len(self.anim_cmds) else 0x7B
                            if cmd == 0x79:
                                stopped.add(i)
                            elif cmd == 0x7A:
                                stopped.discard(i)
                            else:
                                state[i] = j
                                stopped.discard(i)
                i += 1
                use = (use << 1) & 0xFFFF
                mask = (mask << 1) & 0x1FFFF
        return {limb: cur for limb, cur in state.items() if limb not in stopped}

    def enter_chore(self, anim, state):
        """Mirror costumeDecodeData: (re)start limbs from a chore entry.

        state maps limb -> [start, end, curpos, stopped], exactly like the
        engine's _cost arrays (curpos carries the 0x8000 highflag bit).
        """
        if anim > self.num_anim or anim >= len(self.anim_table):
            return
        chore = self.anim_table[anim]
        if chore == 0:
            return
        cmds = self.anim_cmds
        mask = struct.unpack_from("<H", self.base, chore)[0]
        r = chore + 2
        use = 0xFFFF
        i = 0
        while mask & 0xFFFF and i < 16:
            if mask & 0x8000:
                j = struct.unpack_from("<H", self.base, r)[0]
                r += 2
                if use & 0x8000:
                    if j == 0xFFFF:
                        state[i] = [0, 0, 0xFFFF, 1]
                    else:
                        extra = self.base[r]
                        r += 1
                        cmd = cmds[j] if j < len(cmds) else 0x7B
                        if cmd == 0x7A:
                            entry = state.get(i, [0, 0, 0xFFFF, 1])
                            entry[3] = 0
                            state[i] = entry
                        elif cmd == 0x79:
                            entry = state.get(i, [0, 0, 0xFFFF, 1])
                            entry[3] = 1
                            state[i] = entry
                        else:
                            state[i] = [j, j + (extra & 0x7F),
                                        j | (0x8000 if extra & 0x80 else 0), 0]
            i += 1
            use = (use << 1) & 0xFFFF
            mask = (mask << 1) & 0x1FFFF

    def step_limbs(self, state):
        """Mirror ClassicCostumeLoader::increaseAnim for every active limb."""
        cmds = self.anim_cmds
        for i in range(16):
            entry = state.get(i)
            if not entry or entry[3] or entry[2] == 0xFFFF:
                continue
            highflag = entry[2] & 0x8000
            cur = entry[2] & 0x7FFF
            end = entry[1]
            while True:
                if not highflag:
                    cur += 1
                    if cur >= end:
                        cur = entry[0]
                else:
                    if cur != end:
                        cur += 1
                nc = cmds[cur] if cur < len(cmds) else 0
                if nc == 0x7C:
                    continue
                if nc == 0x78:
                    # v5 sound marker: skip unless the range is one cel.
                    if entry[0] != end:
                        continue
                entry[2] = cur | highflag
                break

    def chore_sequence(self, facing, frame, max_steps=32):
        """Emulate limb animation for one chore: init chore first (like the
        engine, which runs frame 1 before the target), then step until the
        limb state repeats. Returns a list of {limb: cel} snapshots."""
        anim = facing + frame * 4
        if anim > self.num_anim or anim >= len(self.anim_table):
            return []
        if self.anim_table[anim] == 0:
            return []
        state = {}
        if frame != 1:
            self.enter_chore(facing + 1 * 4, state)
        self.enter_chore(anim, state)

        def snapshot():
            return tuple(sorted((limb, entry[2]) for limb, entry in state.items()
                                if not entry[3] and entry[2] != 0xFFFF))

        seen = [snapshot()]
        for _ in range(max_steps):
            self.step_limbs(state)
            key = snapshot()
            if key == seen[0] or key in seen:
                break
            seen.append(key)
        return [{limb: cur & 0x7FFF for limb, cur in key} for key in seen]

    def compose_pose(self, pose, facing):
        """Turn {limb: curpos} into (pixels, w, h, dx, dy) parts."""
        parts = []
        xmove = 0
        ymove = 0
        for limb in range(16):
            if limb not in pose:
                continue
            code = self.anim_cmds[pose[limb]] & 0x7F
            w, h, rx, ry, mx, my, data = self._cel(limb, code)
            if w <= 0 or h <= 0:
                continue
            pix = _decode_byle(self.base[data:], w, h, self.num_colors)
            parts.append((pix, w, h, xmove + rx, ymove + ry))
            xmove += mx
            ymove -= my
        return parts

    @property
    def anim_cmds(self):
        return self.base[self.anim_cmds_off:]

    def _cel(self, limb, code):
        fo = self.frame_offsets[limb]
        off = struct.unpack_from("<H", self.base, fo + (code & 0x7F) * 2)[0]
        o = off
        w, h, rx, ry, mx, my = struct.unpack_from("<6h", self.base, o)
        return (w, h, rx, ry, mx, my, o + 12)


def _decode_byle(data, width, height, num_colors=16):
    """Classic ByleRLE, 16 colors (mask 0xF, shr 4). 0 = transparent.

    Runs continue across column boundaries exactly like ScummVM's
    byleRLEDecode (the remaining run length is preserved, not dropped).
    """
    if width <= 0 or height <= 0 or num_colors not in (16, 32):
        raise FormatError("Invalid cel dimensions or color count")
    shift = 4 if num_colors == 16 else 3
    mask = (1 << shift) - 1
    out = bytearray(width * height)
    i = 0
    n = len(data)
    x = 0
    y = 0
    color = 0
    left = 0
    while x < width:
        if left == 0:
            if i >= n:
                raise FormatError("Truncated costume RLE")
            code = data[i]
            i += 1
            left = code & mask
            color = code >> shift
            if left == 0:
                if i >= n:
                    raise FormatError("Truncated costume RLE extended run")
                left = data[i]
                i += 1
                if left == 0:
                    raise FormatError("Zero-length costume RLE run")
        if color:
            out[y * width + x] = color
        y += 1
        left -= 1
        if y >= height:
            y = 0
            x += 1
    return bytes(out)


def pack_shelf(sizes):
    shelves = []
    placements = {}
    for oid, w, h in sizes:
        done = False
        for si, (sy, sh, used) in enumerate(shelves):
            if sh >= h and used + w + PAD <= ATLAS_MAX_W:
                placements[oid] = (used, sy)
                shelves[si] = (sy, sh, used + w + PAD)
                done = True
                break
        if not done:
            y = shelves[-1][0] + shelves[-1][1] + PAD if shelves else 0
            shelves.append((y, h, w + PAD))
            placements[oid] = (0, y)
    aw = max(used for _, _, used in shelves)
    ah = shelves[-1][0] + shelves[-1][1]
    return aw, ah, placements


def _write_png(path, width, height, rgba):
    import zlib
    def chunk(tag, payload):
        out = struct.pack(">I", len(payload)) + tag + payload
        out += struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
        return out
    raw = b"".join(b"\x00" + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def find_costume_resource(source, costume_id):
    """Locate a raw COST chunk for one costume id in the supplied game dir."""
    from import_monkey import decode, chunks, directory, room_offsets
    src = Path(source)
    paths = {p.name.lower(): p for p in src.iterdir() if p.is_file()}
    index, _ = decode(paths["monkey.000"].read_bytes(), b"RNAM")
    blocks = {}
    for tag, off, size in chunks(index):
        blocks[tag] = index[off + 8:off + size]
    dcos = directory(blocks["DCOS"])
    disk, _ = decode(paths["monkey.001"].read_bytes(), b"LECF")
    outer = list(chunks(disk, 8))
    loff = [e for e in outer if e[0] == "LOFF"][0]
    offs = room_offsets(disk[loff[1] + 8:loff[1] + loff[2]])
    room, rel = dcos[costume_id]
    if room not in offs:
        raise FormatError("no room base")
    base = offs[room] + rel
    if disk[base:base + 4] != b"COST":
        raise FormatError("not a COST chunk")
    size = int.from_bytes(disk[base + 4:base + 8], "big")
    return disk[base:base + size]


def main():
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default="../monkeyisland")
    ap.add_argument("--output", default="Assets/Scumm/Generated/ActorSprites")
    ap.add_argument("--costumes", nargs="*", type=int, default=None)
    args = ap.parse_args()
    ids = args.costumes if args.costumes is not None else None
    if ids is None:
        from import_monkey import decode, chunks, directory
        src = Path(args.source)
        paths = {p.name.lower(): p for p in src.iterdir() if p.is_file()}
        index, _ = decode(paths["monkey.000"].read_bytes(), b"RNAM")
        blocks = {}
        for tag, off, size in chunks(index):
            blocks[tag] = index[off + 8:off + size]
        dcos = directory(blocks["DCOS"])
        ids = [i for i in range(len(dcos)) if dcos[i][1] != 0]
    baked, failed = [], []
    for cid in ids:
        out_dir = Path(args.output) / f"{cid:03d}"
        try:
            meta = bake_costume(cid, find_costume_resource(args.source, cid), out_dir)
        except (FormatError, FileNotFoundError, KeyError) as e:
            failed.append((cid, str(e)))
            continue
        baked.append(cid)
    print(f"baked={len(baked)} failed={len(failed)}")
    for cid in baked:
        print(f"  costume {cid}")
    for cid, err in failed:
        print(f"  FAILED costume {cid}: {err}")
    return 1 if failed else 0


def _composite_limbs(cost, limbs):
    """Composite limb cels into one sprite; track bounds + hotspot."""
    xs = [dx for _, _, _, dx, _ in limbs]
    ys = [dy for _, _, _, _, dy in limbs]
    x0 = min(xs)
    y0 = min(ys)
    x1 = max(dx + w for _, w, _, dx, _ in limbs)
    y1 = max(dy + h for _, _, h, _, dy in limbs)
    sw, sh = x1 - x0, y1 - y0
    if sw <= 0 or sh <= 0 or sw > 512 or sh > 512:
        return None
    spr = bytearray(sw * sh)
    coverage = bytearray(sw * sh)
    for pix, w, h, dx, dy in limbs:
        for y in range(h):
            for x in range(w):
                v = pix[y * w + x]
                if v:
                    dest = (dy - y0 + y) * sw + (dx - x0 + x)
                    spr[dest] = cost.palette[v]
                    coverage[dest] = 255
    return (sw, sh, bytes(spr), -x0, -y0, coverage)


def bake_costume(costume_id, cost_resource, out_dir, facings=(0, 1, 2, 3), frames=(1, 2, 3, 4, 5, 6, 7),
                 seq_frames=(1, 2, 3, 4, 5, 6), seq_base=64, seq_stride=32):
    cost = Costume(cost_resource)
    poses = {}
    seqs = {}
    for facing in facings:
        for frame in frames:
            pose = cost.decode_pose(facing, frame)
            if not pose:
                continue
            limbs = cost.compose_pose(pose, facing)
            if not limbs:
                continue
            sprite = _composite_limbs(cost, limbs)
            if sprite is not None:
                poses[f"{facing}:{frame}"] = sprite
        for frame in seq_frames:
            steps = cost.chore_sequence(facing, frame)
            if len(steps) < 2 or len(steps) > seq_stride:
                continue
            base = seq_base + frame * seq_stride
            staged = {}
            for step, pose in enumerate(steps):
                limbs = cost.compose_pose(pose, facing)
                if not limbs:
                    break
                sprite = _composite_limbs(cost, limbs)
                if sprite is None:
                    break
                staged[f"{facing}:{base + step}"] = sprite
            if len(staged) == len(steps):
                poses.update(staged)
                seqs[f"{facing}:{frame}"] = [base, len(steps)]
    if not poses:
        return None
    sizes = sorted(((k, v[0], v[1]) for k, v in poses.items()), key=lambda t: -t[2])
    aw, ah, placements = pack_shelf([(k, w, h) for k, w, h in sizes])
    atlas = bytearray(aw * ah * 4)
    meta = {}
    for key, (sw, sh, spr, hx, hy, coverage) in poses.items():
        x0, y0 = placements[key]
        for y in range(sh):
            for x in range(sw):
                v = spr[y * sw + x]
                o = ((y0 + y) * aw + (x0 + x)) * 4
                atlas[o] = v
                atlas[o + 3] = coverage[y * sw + x]
        meta[key] = {"u0": x0 / aw, "v0": 1.0 - (y0 + sh) / ah,
                     "u1": (x0 + sw) / aw, "v1": 1.0 - y0 / ah,
                     "w": sw, "h": sh, "ax": hx, "ay": hy}
    out_dir.mkdir(parents=True, exist_ok=True)
    _write_png(out_dir / "atlas.png", aw, ah, bytes(atlas))
    (out_dir / "poses.json").write_text(json.dumps(
        {"costume": costume_id, "poses": meta, "seq": seqs, "transparentIndex": 0}, indent=1) + "\n")
    with open(out_dir / "table.txt", "w") as f:
        for key in sorted(meta.keys(), key=lambda k: tuple(int(p) for p in k.split(":"))):
            uv = meta[key]
            facing, frame = key.split(":")
            f.write(f"{facing} {frame} {uv['u0']!r} {uv['v0']!r} {uv['u1']!r} {uv['v1']!r} "
                    f"{uv['w']} {uv['h']} {uv['ax']} {uv['ay']}\n")
    with open(out_dir / "seq.txt", "w") as f:
        for key in sorted(seqs.keys(), key=lambda k: tuple(int(p) for p in k.split(":"))):
            facing, frame = key.split(":")
            base, length = seqs[key]
            f.write(f"{facing} {frame} {base} {length}\n")
    return meta


if __name__ == "__main__":
    raise SystemExit(main())
