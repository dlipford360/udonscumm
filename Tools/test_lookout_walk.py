"""Walk the actual distant Lookout path, including signed scale interpolation."""
import struct
import unittest
from pathlib import Path

from build_room_blobs import parse_blob
from test_vm_fixtures import ReferenceSim


class LookoutWalkTests(unittest.TestCase):
    def test_distant_path_and_scale(self):
        path = Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs/rt-room-038.bytes"
        if not path.exists():
            self.skipTest("Local game data not imported")
        room = parse_blob(path.read_bytes())
        sim = ReferenceSim({})
        sim.current_room = 38
        sim.walkboxes = [dict(pts=struct.unpack_from("<8h", b), flags=b[17],
                             mask=b[16], scale=struct.unpack_from("<H", b, 18)[0])
                         for b in room["boxes"]]
        sim.walk_matrix = room["matrix"]
        values = list(struct.unpack("<" + "H" * (len(room["scal"]) // 2), room["scal"]))
        sim.scale_slots[:len(values)] = values
        actor = sim.actors[1]
        actor.update(room=38, x=312, y=73, visible=True)
        self.assertEqual(sim.actor_render_scale(1), 216)
        self.assertEqual(sim.actor_clip_plane(1), 1)
        sim.start_actor_walk(1, 154, 106, 270)
        positions = []
        for _ in range(500):
            sim.tick(4)
            positions.append((actor["x"], actor["y"]))
            if not sim.actor_moving(1):
                break
        self.assertIn((258, 91), positions)
        self.assertGreater(len(set(positions)), 10)
        self.assertEqual(positions[-1], (154, 106))
        self.assertEqual(sim.actor_render_scale(1), 241)
        self.assertEqual(sim.actor_clip_plane(1), 1)
        self.assertEqual(actor["facing"], 270)
        # Forced clip wins; ignore-boxes and NeverClip suppress the mask.
        actor["forceClip"] = 2
        self.assertEqual(sim.actor_clip_plane(1), 2)
        actor["forceClip"] = 0
        actor["ignoreBoxes"] = 1
        self.assertEqual(sim.actor_clip_plane(1), 0)
        actor["ignoreBoxes"] = 0
        sim.object_class[1] |= (1 << 19)
        self.assertEqual(sim.actor_clip_plane(1), 0)
        # Overhead speech sits above the head with the scaled -80 offset.
        # (Overhead is armed by an explicit SO_OVERHEAD print in-game.)
        sim.globals[54] = -80
        sim.print_overhead = 1
        sim.actors[1].update(room=38, x=312, y=73, elevation=0)
        self.assertEqual(sim.talk_pos(1), (240, 1))
        sim.actors[1].update(x=154, y=106)
        self.assertEqual(sim.talk_pos(1), (154, 29))
