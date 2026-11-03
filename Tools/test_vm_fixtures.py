import json
import struct
import unittest
from pathlib import Path

from make_vm_fixtures import FIXTURES, Asm, move, move_indirect, word

OUTPUT = Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/VmFixtures"


def trunc_div(numerator, denominator):
    quotient = abs(numerator) // abs(denominator)
    return -quotient if (numerator < 0) != (denominator < 0) else quotient


def signed16(value):
    value &= 0xFFFF
    return value - 0x10000 if value >= 0x8000 else value


def wrap32(value):
    value &= 0xFFFFFFFF
    return value - 0x100000000 if value >= 0x80000000 else value


def decode_printable(data):
    """Printable subset of an inline string, mirroring ScummV5Vm.DecodePrintText."""
    out = []
    i = 0
    while i < len(data):
        c = data[i]
        i += 1
        if c == 0:
            break
        if c == 0xFF:
            if i >= len(data):
                break
            control = data[i]
            i += 1
            if control not in (1, 2, 3, 8):
                i += 2
            continue
        if 32 <= c < 127:
            out.append(chr(c))
    return "".join(out)


class ReferenceSim:
    """Independent v5-subset interpreter written from the ScummVM opcode table."""

    def __init__(self, programs):
        self.programs = programs
        self.globals = [0] * 800
        self.bits = [False] * 2048
        self.strings = {}
        self.prints = []
        self.inventory = [0] * 80
        self.slots = {}
        self.stack = []
        self.ticks = 0
        self.fault = ""
        self.fault_script = -1
        self.fault_offset = -1
        self.fault_opcode = -1
        self.instructions = 0
        self.op_histogram = {}
        self.cursor_state = 0
        self.user_put = 0
        self.last_sound = 0
        self.sound_running = 0
        self.talk_delay = 0
        self.rnd = 1
        self.globals[48] = 3
        self.globals[49] = 19
        self.globals[67] = 3
        self.globals[74] = 1225
        self.globals[37] = 4
        self.globals[54] = -80
        # ScummEngine_v5::resetScummVars: lights on (actor base palette +
        # colors + room lights = 1|2|8 = 11). Sentence script 2 checks
        # VAR_CURRENT_LIGHTS (9) for Look; without this every Look says
        # "too dark".
        self.globals[9] = 11
        self.talking_actor = -1
        self.print_overhead = 0
        self.print_x = 0
        self.print_y = 0
        self.print_color = -1
        self.camera_x = 160
        self.camera_dest_x = 160
        self.camera_min_x = 0
        self.camera_max_x = 0
        self.cut_scene_slot = -1
        self.sentence = (0, 0, 0)
        self.sentence_num = 0
        self.cut_stack = []
        self.screen_w = 320
        self.screen_h = 200
        self.screen_top = 0
        self.screen_bot = 200
        self.current_room = 0
        self.room_bytes = None
        self.room_width = 0
        self.room_height = 0
        self.room_en_cd = None
        self.room_ex_cd = None
        self.room_lscr = {}
        self.room_blobs = {}
        self.object_owner = [0] * 1024
        self.object_state = [0] * 1024
        self.object_room = [0] * 1024
        self.object_class = [0] * 1024
        self.obj_slots = {}
        self.actors = {}
        for i in range(1, 48):
            self.actors[i] = {"room": 0, "x": 0, "y": 0, "visible": False,
                              "costume": 0, "facing": 0, "anim": 0, "elevation": 0,
                              "width": 0, "scaleX": 255, "scaleY": 255,
                              "walkSpeedX": 0, "walkSpeedY": 0, "talkColor": 0,
                              "initFrame": 0, "walkFrame": 0, "standFrame": 0,
                              "talkStart": 0, "talkStop": 0, "ignoreBoxes": 0,
                              "forceClip": 0, "shadow": 0, "animSpeed": 0,
                              "animProgress": 0, "animStep": 0,
                              "sound": 0, "name": b"", "palette": list(range(32)),
                              "walkCount": 0, "walkIndex": 0, "path": [],
                              "endDir": -1, "credit": 0, "remX": 0, "remY": 0}
        self.scale_slots = [0] * 64
        self.walkboxes = []
        self.walk_matrix = b""
        self.tick_jiffies = 0

    def load_tables(self, path):
        data = Path(path).read_bytes()
        count = struct.unpack_from("<H", data)[0]
        assert len(data) == 2 + count + count + count * 4
        for i in range(count):
            self.object_owner[i] = data[2 + i] & 15
            self.object_state[i] = data[2 + count + i] & 15
            self.object_class[i] = struct.unpack_from("<I", data, 2 + count + count + i * 4)[0]

    def load_room_blobs(self, directory):
        from build_room_blobs import parse_blob
        for path in Path(directory).glob("rt-room-*.bytes"):
            room = parse_blob(path.read_bytes())
            self.room_blobs[int(path.stem.split("-")[2])] = room

    def start(self, script, args):
        self.launch(script, args, -1)

    def launch(self, script, args, caller, where=0, offs=0):
        slot = next(i for i in range(1, 80) if i not in self.slots)
        if where == 0 and script >= 200:
            where = 1
        if where == 0:
            code = self.programs[script]
        elif where == 1:
            if script not in self.room_lscr:
                self.fault = f"Missing local script {script}"
                return -1
            code = self.room_bytes
            offs = self.room_lscr[script][0]
        else:
            code = self.room_bytes
        self.slots[slot] = {"script": script, "pc": offs, "status": "run",
                             "delay": 0, "locals": list(args) + [0] * (25 - len(args)),
                             "didexec": False, "where": where, "code": code,
                             "frozen": 0, "freezeRes": False, "recursive": False}
        if caller >= 0:
            caller_state = self.slots.get(caller, {})
            self.stack.append((caller, caller_state.get("script"), caller_state.get("where")))
        return slot

    def resolve_result(self, slot):
        """Mirror getResultPos: address word, then index word, before any value."""
        address = self.fetch_word(slot)
        if address & 0x2000:
            index = self.fetch_word(slot)
            disp = self.read_var(slot, index & ~0x2000) if index & 0x2000 else index & 0xFFF
            address = (address + disp) & ~0x2000
        return address

    def read_raw(self, slot, address):
        if (address & 0xF000) == 0:
            if address >= len(self.globals):
                self.fault = f"Variable address {address} out of range"
                return 0
            return self.globals[address]
        if address & 0x8000:
            if (address & 0x7FFF) >= len(self.bits):
                self.fault = f"Variable address {address} out of range"
                return 0
            return 1 if self.bits[address & 0x7FFF] else 0
        if (address & 0xFFF) >= len(self.slots[slot]["locals"]):
            self.fault = f"Variable address {address} out of range"
            return 0
        return self.slots[slot]["locals"][address & 0xFFF]

    def write_raw(self, slot, address, value):
        if (address & 0xF000) == 0:
            if address >= len(self.globals):
                self.fault = f"Variable address {address} out of range"
                return
            self.globals[address] = value
        elif address & 0x8000:
            if (address & 0x7FFF) >= len(self.bits):
                self.fault = f"Variable address {address} out of range"
                return
            self.bits[address & 0x7FFF] = value != 0
        else:
            if (address & 0xFFF) >= len(self.slots[slot]["locals"]):
                self.fault = f"Variable address {address} out of range"
                return
            self.slots[slot]["locals"][address & 0xFFF] = value

    def read_var(self, slot, address):
        if address & 0x2000:
            index = self.fetch_word(slot)
            disp = self.read_var(slot, index & ~0x2000) if index & 0x2000 else index & 0xFFF
            address = (address + disp) & ~0x2000
        return self.read_raw(slot, address)

    def write_var(self, slot, address, value):
        if address & 0x2000:
            index = self.fetch_word(slot)
            disp = self.read_var(slot, index & ~0x2000) if index & 0x2000 else index & 0xFFF
            address = (address + disp) & ~0x2000
        self.write_raw(slot, address, value)

    def run_input_script(self, click_area, val, mode):
        # A scene click during an armed cutscene override skips it
        # (abortCutscene) instead of dispatching; the click is consumed.
        # (ScummVM aborts on ESC/both-buttons; our no-keyboard build aborts
        # on scene clicks so cutscenes remain skippable.) Verb clicks
        # (area 1, e.g. dialogue choices 120+) must dispatch to the verb
        # script even during a cutscene (room 81 uses cutscene verb 14
        # to report the pick via g194); aborting them fizzles dialogue.
        if click_area != 1 and self.abort_cutscene():
            return -1
        # Heal UI stranded by entry cutscenes once input is live again: room
        # 32's spiel swaps in a locked verb script and stops the hover tracker
        # (script 23) without restoring either, and its tail re-locks the verb
        # script even after the cutscene ends. Without this, clicks fizzle.
        # Do not touch 14 while an override is still armed: dialogue menus
        # legitimately use 14 with input on (room 81).
        top = self.cut_stack[-1] if getattr(self, "cut_stack", None) else {}
        override_armed = bool(top.get("active", False) and top.get("slot", -1) >= 0)
        if len(self.globals) > 53 and self.globals[53] > 0:
            if not override_armed and len(self.globals) > 32 and self.globals[32] == 14:
                self.globals[32] = 4
            if 23 in self.programs and not any(v.get("script") == 23 for v in self.slots.values()):
                self.launch(23, [0] * 25, -1, 0, 0)
        verb_script = self.globals[32] if len(self.globals) > 32 else 0
        if verb_script == 0:
            return -1
        # Leaving chat (non-dialogue click: scene Walk-to or a standard verb)
        # ends the conversation: hide any dialogue choices (120-199) so they
        # don't linger over the bottom bar (room 28 trials leftovers after
        # walking away, etc.). Choice clicks (120+) keep others for the next
        # menu (scripts hide the selected one as it's used).
        is_choice = click_area == 1 and 120 <= val < 200
        if not is_choice:
            for v in self.__dict__.get("verbs", {}).values():
                if 120 <= v.get("id", 0) < 200:
                    v["mode"] = 0
        # Dialogue choices (verbs 120+) unblock waiting dialogue scripts via
        # g194 (room 81 global 92, room 28 local 220 poll 194 != 0). The
        # cutscene verb script (14) reports the pick this way; the normal
        # verb script (4) does not, so report it here as well to keep late
        # menus (input on, g32=4) working. Stale picks are harmless: each
        # wait loop clears 194 to 0 before polling.
        if click_area == 1 and val >= 120 and len(self.globals) > 194:
            self.globals[194] = val
        return self.launch(verb_script, [click_area, val, mode] + [0] * 22, -1)

    def abort_cutscene(self):
        """Mirrors ScummEngine::abortCutscene: resume the overridden script
        at the stored position and report it via VAR_OVERRIDE. Also restores
        the hover tracker (script 23) that entry cutscenes stop, since the
        verb script needs it to turn clicks into sentences. Returns True
        when an armed override was aborted (click consumed)."""
        if not getattr(self, "cut_stack", None):
            return False
        top = self.cut_stack[-1]
        if not top.get("active", False):
            return False
        slot = top.get("slot", -1)
        if slot is None or slot < 0:
            # Cutscene running but override never armed: clicks fall through
            # to the verb script as before (no abort without an override).
            return False
        pos = top.get("pos", 0)
        script = top.get("script", 0)
        where = top.get("where", 0)
        code = top.get("code")
        if (slot is not None and slot >= 0 and slot in self.slots
                and self.slots[slot].get("script") == script
                and self.slots[slot].get("where") == where):
            self.slots[slot]["pc"] = pos
            self.slots[slot]["status"] = "run"
            self.slots[slot]["frozen"] = 0
        elif script and code is not None and 0 <= pos < len(code):
            fresh = next(i for i in range(1, 80) if i not in self.slots)
            self.slots[fresh] = {"script": script, "pc": pos, "status": "run",
                                 "delay": 0, "locals": [0] * 25, "didexec": False,
                                 "where": where, "code": code,
                                 "frozen": 0, "freezeRes": False, "recursive": False,
                                 "cutscene": 0}
        top["slot"] = -1
        top["pos"] = 0
        if len(self.globals) > 5:
            self.globals[5] = 1
        if 23 in self.programs and not any(v.get("script") == 23 for v in self.slots.values()):
            self.launch(23, [0] * 25, -1, 0, 0)
        return True

    def check_sentence(self):
        """Mirrors checkAndRunSentenceScript: run the sentence script once
        per tick with the queued (verb, objectA, objectB)."""
        if self.fault or getattr(self, "sentence_num", 0) <= 0:
            return
        sentence_script = self.globals[33] if len(self.globals) > 33 else 0
        verb, obj_a, obj_b = self.sentence
        self.sentence_num = 0
        self.sentence = (0, 0, 0)
        if sentence_script == 0:
            return
        if any(v["script"] == sentence_script for v in self.slots.values()):
            return
        self.launch(sentence_script, [verb, obj_a, obj_b] + [0] * 22, -1)

    def verb_name_width(self, verbs, key):
        name = verbs[key].get("name", b"")
        width = 0
        i = 0
        while i < len(name):
            c = name[i]
            i += 1
            if c == 0:
                break
            if c == 0xFF:
                if i >= len(name):
                    break
                control = name[i]
                i += 1
                if control not in (1, 2, 3, 8):
                    i += 2
                continue
            if 32 <= c < 127:
                width += 1
        return width * 8

    def obj_act_xy(self, oid):
        """Resolve an object-or-actor id to (x, y, actor or None).
        None means unresolvable (distance 0xFF). v5 ids below 13 are actors;
        room objects use their walk point."""
        if 1 <= oid < 13:
            actor = self.actors.get(oid)
            if actor is None or actor["room"] != self.current_room:
                return None
            return (actor["x"], actor["y"], oid)
        if not 0 <= oid < 1024:
            return None
        if self.object_owner[oid] != 15:
            owner = self.object_owner[oid]
            if 1 <= owner < 48:
                actor = self.actors.get(owner)
                if actor is not None and actor["room"] == self.current_room:
                    return (actor["x"], actor["y"], owner)
            return None
        target = next((o for o in self.obj_slots.values() if o["id"] == oid), None)
        if target is None:
            return None
        return (target["walkX"], target["walkY"], None)

    def obj_act_dist(self, a, b):
        pa = self.obj_act_xy(a)
        pb = self.obj_act_xy(b)
        if pa is None or pb is None:
            return 0xFF
        (x, y, acta) = pa
        (x2, y2, actb) = pb
        if acta and actb:
            ra = self.actors[acta]["room"]
            if ra == self.actors[actb]["room"] and ra != 0 and ra != self.current_room:
                return 0
        if acta and not actb:
            actor = self.actors[acta]
            for i in range(1, len(self.walkboxes)):
                if self._box_point_in(self.walkboxes[i], actor["x"], actor["y"]):
                    x2, y2 = self._box_closest(self.walkboxes[i], x2, y2)
                    break
        return max(abs(x - x2), abs(y - y2))

    def find_verb_at(self, x, y):
        """Mirrors findVerbAtPos: topmost clickable text verb under cursor."""
        verbs = self.__dict__.get("verbs", {})
        for key in sorted(verbs.keys(), reverse=True):
            v = verbs[key]
            if v.get("mode", 0) != 1 or v.get("id", 0) == 0 or v.get("save", 0) != 0:
                continue
            if v.get("type", 0) != 0:
                continue
            top = v.get("top", 0)
            if y < top or y >= top + 8:
                continue
            width = self.verb_name_width(verbs, key)
            if width <= 0:
                continue
            if v.get("center", 0):
                if x < v.get("left", 0) - width // 2 or x >= v.get("left", 0) + width // 2:
                    continue
            elif x < v.get("left", 0) or x >= v.get("left", 0) + width:
                continue
            return key
        return 0

    def fetch(self, slot):
        state = self.slots[slot]
        code = state.get("code")
        if code is None:
            code = self.programs[state["script"]]
        value = code[state["pc"]]
        state["pc"] += 1
        return value

    def fetch_word(self, slot):
        return self.fetch(slot) | (self.fetch(slot) << 8)

    def word_operand(self, slot, mode, mask=0x80):
        if mode & mask:
            return self.read_var(slot, self.fetch_word(slot))
        value = self.fetch_word(slot)
        return value - 0x10000 if value >= 0x8000 else value

    def kill_room_slots(self):
        for key, value in list(self.slots.items()):
            if value["where"] in (1, 2):
                del self.slots[key]
        live = set(self.slots.keys())
        self.stack = [entry for entry in self.stack if entry[0] in live]

    def start_scene(self, room):
        self.globals[72] = room
        if not hasattr(self, "rooms_entered"):
            self.rooms_entered = []
        self.rooms_entered.append(room)
        # Exit phase (mirrors runExitScript): VAR_EXIT_SCRIPT then room EXCD,
        # drained synchronously like ScummVM's nested executeScript.
        if self.globals[30] != 0:
            slot = self.launch(self.globals[30], [0] * 25, -1, 0, 0)
            if slot >= 0:
                self.run_slot(slot)
            if self.fault:
                return
        if self.room_ex_cd is not None and self.room_bytes is not None:
            slot = self.launch(10001, [0] * 25, -1, 2, self.room_ex_cd)
            if slot >= 0:
                self.run_slot(slot)
        if self.fault:
            return
        for key in list(self.slots):
            if self.slots[key]["where"] in (1, 2):
                del self.slots[key]
        live = set(self.slots.keys())
        self.stack = [entry for entry in self.stack if entry[0] in live]
        self.current_room = room
        # startScene resets the camera on every entry: centered, held still
        # until scripts pan or a followed actor walks.
        self.camera_x = 160
        self.camera_dest_x = 160
        if room == 0:
            self.room_bytes = None
            self.room_en_cd = None
            self.room_ex_cd = None
            self.room_lscr = {}
            self.obj_slots = {}
            self.globals[4] = 0
            self.__dict__["pending_place"] = None
            for v in self.__dict__.get("verbs", {}).values():
                if 120 <= v.get("id", 0) < 200:
                    v["mode"] = 0
            return
        if room not in self.room_blobs:
            self.fault = f"Room {room} not converted"
            return
        blob = self.room_blobs[room]
        self.room_bytes = blob["roomBytes"]
        self.room_width = blob["width"]
        self.room_height = blob["height"]
        # Walk landscape for movement and box scaling (mirrors ParseRoomBlob).
        self.walkboxes = []
        for raw in blob.get("boxes", []):
            pts = struct.unpack_from("<8h", raw, 0)
            self.walkboxes.append({"pts": pts, "mask": raw[16],
                                   "flags": raw[17],
                                   "scale": struct.unpack_from("<H", raw, 18)[0]})
        self.walk_matrix = blob.get("matrix", b"")
        scal = blob.get("scal", b"")
        self.scale_slots = [0] * 64
        for i in range(min(len(scal) // 2, 64)):
            self.scale_slots[i] = struct.unpack_from("<H", scal, i * 2)[0]
        self.room_lscr = {e["id"]: (e["offset"], e["size"]) for e in blob["scripts"]}
        self.room_en_cd = blob["encd"]
        self.room_ex_cd = blob["excd"]
        self.obj_slots = {}
        for i, obj in enumerate(blob["objects"], start=1):
            self.obj_slots[i] = obj
            self.object_room[obj["id"]] = room
        self.globals[4] = room
        self.globals[22] = room
        self.globals[17] = self.screen_w // 2
        self.globals[18] = blob["width"] - self.screen_w // 2
        # Dialogue choices (verbs 120-199) are per-conversation; a room change
        # ends the conversation, so hide any leftovers (e.g. room 81 Bye
        # exits leaving 124/125 visible over the bar verbs in 28).
        for v in self.__dict__.get("verbs", {}).values():
            if 120 <= v.get("id", 0) < 200:
                v["mode"] = 0
        # A loadRoomWithEgo transition places ego at the object's walk point
        # (falling back to the target), snaps the camera there, and walks it
        # to the target unless it is (-1, -1). Applied here, once the room
        # exists and before entry scripts run.
        pending = self.__dict__.get("pending_place")
        self.__dict__["pending_place"] = None
        if pending is not None:
            ego, obj, x, y = pending
            wx, wy = x, y
            target = next((o for o in self.obj_slots.values() if o["id"] == obj), None)
            if target is not None and (target["walkX"] != 0 or target["walkY"] != 0):
                wx, wy = target["walkX"], target["walkY"]
            self.actors[ego].update(room=self.current_room, x=wx, y=wy, visible=True,
                                    walkCount=0, walkIndex=0, path=[])
            half = getattr(self, "screen_w", 320) // 2
            lo, hi = half, self.room_width - half
            if hi < lo:
                hi = lo
            self.camera_x = min(max(wx, lo), hi)
            self.camera_dest_x = self.camera_x
            if x != -1:
                self.start_actor_walk(ego, x, y, -1)
        # Entry phase (mirrors runEntryScript): VAR_ENTRY_SCRIPT, room ENCD,
        # VAR_ENTRY_SCRIPT2, drained synchronously.
        if self.globals[28] != 0:
            slot = self.launch(self.globals[28], [0] * 25, -1, 0, 0)
            if slot >= 0:
                self.run_slot(slot)
            if self.fault:
                return
        if self.room_en_cd is not None:
            slot = self.launch(10002, [0] * 25, -1, 2, self.room_en_cd)
            if slot >= 0:
                self.run_slot(slot)
            if self.fault:
                return
        if self.globals[29] != 0:
            slot = self.launch(self.globals[29], [0] * 25, -1, 0, 0)
            if slot >= 0:
                self.run_slot(slot)

    def pop_nested(self):
        """Resume the nested caller iff its slot is unchanged and unfrozen
        (mirrors runScriptNested's resume check). Drops dead/frozen entries."""
        while self.stack:
            caller, number, where = self.stack.pop()
            state = self.slots.get(caller)
            if (state is not None and state["script"] == number
                    and state.get("where", 0) == where
                    and state["status"] == "run"
                    and state.get("frozen", 0) == 0):
                return caller
        return -1

    def run_slot(self, slot):
        """Drain a script chain synchronously (exit/entry scripts), following
        nested launches and nested resumes like executeScript does. Uses a
        local nest chain so the tick scheduler's chain is undisturbed."""
        saved = self.stack
        self.stack = []
        current = slot
        try:
            while not self.fault:
                if current < 0:
                    current = self.pop_nested()
                    if current < 0:
                        return
                    continue
                state = self.slots.get(current)
                if state is None:
                    current = self.pop_nested()
                    continue
                if state["status"] != "run" or state.get("frozen", 0) != 0:
                    return
                offset = state["pc"]
                opcode = self.fetch(current)
                state["didexec"] = True
                self.instructions += 1
                done = self.execute(current, opcode)
                if self.fault:
                    self.fault_script = state["script"]
                    self.fault_offset = offset
                    self.fault_opcode = opcode
                    return
                if isinstance(done, int):
                    current = done
                elif done == "yield":
                    current = self.pop_nested()
                elif done == "stop":
                    if current in self.slots:
                        del self.slots[current]
                    current = self.pop_nested()
        finally:
            rest = [entry for entry in self.stack if entry[0] in self.slots]
            self.stack = saved + rest

    def read_inline(self, slot):
        state = self.slots[slot]
        code = state.get("code")
        if code is None:
            code = self.programs[state["script"]]
        start = state["pc"]
        end = start
        while True:
            c = code[end]
            end += 1
            if c == 0:
                break
            if c == 0xFF:
                control = code[end]
                end += 1
                if control not in (1, 2, 3, 8):
                    end += 2
        state["pc"] = end
        return bytes(code[start:end - 1])

    @staticmethod
    def _sides_touch(x1, y1, x2, y2, x3, y3, x4, y4):
        """One side pair touches (mirrors areBoxesNeighbors): collinear
        overlap counts, except a single-point touch of two sides that both
        have length."""
        if x1 == x2 and x3 == x4 and x1 == x3:
            lo1, hi1 = (y1, y2) if y1 <= y2 else (y2, y1)
            lo2, hi2 = (y3, y4) if y3 <= y4 else (y4, y3)
            if hi1 < lo2 or hi2 < lo1:
                return False
            if (hi1 == lo2 or hi2 == lo1) and hi1 != lo1 and hi2 != lo2:
                return False
            return True
        if y1 == y2 and y3 == y4 and y1 == y3:
            lo1, hi1 = (x1, x2) if x1 <= x2 else (x2, x1)
            lo2, hi2 = (x3, x4) if x3 <= x4 else (x4, x3)
            if hi1 < lo2 or hi2 < lo1:
                return False
            if (hi1 == lo2 or hi2 == lo1) and hi1 != lo1 and hi2 != lo2:
                return False
            return True
        return False

    def _boxes_touch(self, a, b):
        if a < 0 or b < 0 or a >= len(self.walkboxes) or b >= len(self.walkboxes):
            return False
        if self.walkboxes[a]["flags"] & 0x80 or self.walkboxes[b]["flags"] & 0x80:
            return False
        pa, pb = self.walkboxes[a]["pts"], self.walkboxes[b]["pts"]
        for k in range(4):
            ax, ay = pa[k * 2], pa[k * 2 + 1]
            bx, by = pa[((k + 1) % 4) * 2], pa[((k + 1) % 4) * 2 + 1]
            for m in range(4):
                cx, cy = pb[m * 2], pb[m * 2 + 1]
                dx, dy = pb[((m + 1) % 4) * 2], pb[((m + 1) % 4) * 2 + 1]
                if self._sides_touch(ax, ay, bx, by, cx, cy, dx, dy):
                    return True
        return False

    def create_box_matrix(self):
        """Mirrors createBoxMatrix: Floyd-Warshall over touching boxes,
        RLE-compressed per row (no leading 0xFF, matching room blobs)."""
        num = len(self.walkboxes)
        if num < 1:
            self.walk_matrix = b""
            return
        if num > 64:
            num = 64
        size = 64
        INF = 255
        adj = [INF] * (size * size)
        nxt = [0xFF] * (size * size)
        for i in range(num):
            for j in range(num):
                if i == j:
                    adj[i * size + j] = 0
                    nxt[i * size + j] = j
                elif self._boxes_touch(i, j):
                    adj[i * size + j] = 1
                    nxt[i * size + j] = j
        for k in range(num):
            for i in range(num):
                for j in range(num):
                    if i == j:
                        continue
                    via = adj[i * size + k] + adj[k * size + j]
                    if via < adj[i * size + j]:
                        adj[i * size + j] = via
                        nxt[i * size + j] = nxt[i * size + k]
        out = bytearray()
        for i in range(num):
            j = 0
            while j < num:
                n = nxt[i * size + j]
                if n == 0xFF:
                    j += 1
                    continue
                lo = j
                while j + 1 < num and nxt[i * size + j + 1] == n:
                    j += 1
                out += bytes((lo, j, n))
                j += 1
            out.append(0xFF)
        self.walk_matrix = bytes(out)

    def byte_operand(self, slot, mode, mask=0x80):
        if mode & mask:
            return self.read_var(slot, self.fetch_word(slot))
        return self.fetch(slot)

    def branch(self, slot, condition):
        displacement = signed16(self.fetch_word(slot))
        if not condition:
            self.slots[slot]["pc"] += displacement

    def actor_moving(self, act):
        actor = self.actors[act]
        return 1 if actor["walkIndex"] < actor["walkCount"] else 0

    def _box_point_in(self, box, x, y):
        pts = box["pts"]
        xs = pts[0::2]
        ys = pts[1::2]
        positive = negative = False
        for k in range(4):
            ax, ay = pts[k * 2], pts[k * 2 + 1]
            bx, by = pts[((k + 1) % 4) * 2], pts[((k + 1) % 4) * 2 + 1]
            cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax)
            if cross > 0:
                positive = True
            if cross < 0:
                negative = True
        return not (positive and negative) and min(xs) <= x <= max(xs) and min(ys) <= y <= max(ys)

    def _box_closest(self, box, x, y):
        if self._box_point_in(box, x, y):
            return (x, y)
        pts = box["pts"]
        best = None
        for k in range(4):
            ax, ay = pts[k * 2], pts[k * 2 + 1]
            bx, by = pts[((k + 1) % 4) * 2], pts[((k + 1) % 4) * 2 + 1]
            dx, dy = bx - ax, by - ay
            length = dx * dx + dy * dy
            dot = (x - ax) * dx + (y - ay) * dy
            dot = max(0, min(dot, length))
            px = ax if length == 0 else ax + trunc_div(dx * dot, length)
            py = ay if length == 0 else ay + trunc_div(dy * dot, length)
            dist = (px - x) ** 2 + (py - y) ** 2
            if best is None or dist < best[0]:
                best = (dist, px, py)
        return (best[1], best[2])

    def _nearest_walkbox(self, x, y):
        result, best, bx, by = -1, None, x, y
        for index in range(len(self.walkboxes) - 1, 0, -1):
            box = self.walkboxes[index]
            if box["flags"] & 0x80:
                continue
            px, py = self._box_closest(box, x, y)
            dist = (px - x) ** 2 + (py - y) ** 2
            if best is None or dist < best:
                best, result, bx, by = dist, index, px, py
        return result, bx, by

    def _next_walkbox(self, frm, to):
        if frm == to:
            return to
        pos = 0
        for _ in range(frm):
            while pos < len(self.walk_matrix) and self.walk_matrix[pos] != 0xFF:
                pos += 3
            pos += 1
        while pos + 2 < len(self.walk_matrix) and self.walk_matrix[pos] != 0xFF:
            if self.walk_matrix[pos] <= to <= self.walk_matrix[pos + 1]:
                nxt = self.walk_matrix[pos + 2]
                return -1 if nxt == 0xFF else nxt
            pos += 3
        return -1

    def _walkbox_portal(self, frm, to):
        best, px, py = None, 0, 0
        for side in range(2):
            a, b = (frm, to) if side == 0 else (to, frm)
            for k in range(4):
                x = self.walkboxes[a]["pts"][k * 2]
                y = self.walkboxes[a]["pts"][k * 2 + 1]
                qx, qy = self._box_closest(self.walkboxes[b], x, y)
                dist = (qx - x) ** 2 + (qy - y) ** 2
                if best is None or dist < best:
                    best, px, py = dist, trunc_div(x + qx, 2), trunc_div(y + qy, 2)
        return (best is not None and best <= 4, px, py)

    def start_actor_walk(self, act, x, y, end_dir):
        actor = self.actors[act]
        actor.update(walkCount=0, walkIndex=0, path=[], endDir=end_dir,
                     credit=0, remX=0, remY=0)
        if actor["room"] != self.current_room:
            actor.update(x=x, y=y)
            if end_dir >= 0:
                actor["facing"] = end_dir
            return
        path = []
        if actor["ignoreBoxes"] == 0 and len(self.walkboxes) > 1:
            frm, start_x, start_y = self._nearest_walkbox(actor["x"], actor["y"])
            to, x, y = self._nearest_walkbox(x, y)
            if frm < 1 or to < 1:
                return
            path.append((start_x, start_y))
            hops = 0
            while frm != to and hops < 62:
                nxt = self._next_walkbox(frm, to)
                if nxt < 1 or nxt >= len(self.walkboxes) or nxt == frm:
                    return
                if self.walkboxes[nxt]["flags"] & 0x80:
                    return
                ok, px, py = self._walkbox_portal(frm, nxt)
                if not ok:
                    return
                path.append((px, py))
                frm = nxt
                hops += 1
            if frm != to:
                return
        path.append((x, y))
        actor["path"] = path
        actor["walkCount"] = len(path)

    def actor_render_scale(self, act):
        actor = self.actors[act]
        if actor["room"] != self.current_room or actor["ignoreBoxes"] != 0:
            return actor["scaleX"]
        box, _, _ = self._nearest_walkbox(actor["x"], actor["y"])
        if box < 1:
            return actor["scaleX"]
        scale = self.walkboxes[box]["scale"]
        if scale & 0x8000:
            slot = (scale & 0x7FFF) * 4
            if slot + 3 >= len(self.scale_slots):
                return actor["scaleX"]
            s1, y1, s2, y2 = self.scale_slots[slot:slot + 4]
            if y1 == y2:
                return actor["scaleX"]
            y = max(actor["y"], 0)
            scale = s1 + trunc_div((s2 - s1) * (y - y1), y2 - y1)
        return min(max(scale, 1), 255)

    def actor_clip_plane(self, act):
        actor = self.actors[act]
        if actor["room"] != self.current_room:
            return 0
        if actor["forceClip"] != 0:
            return actor["forceClip"]
        if self.object_class[act] & (1 << 19) or actor["ignoreBoxes"] != 0:
            return 0
        box, _, _ = self._nearest_walkbox(actor["x"], actor["y"])
        return self.walkboxes[box]["mask"] if box > 0 else 0

    def move_camera(self):
        follow = self.__dict__.get("camera_follow", 0)
        # A followed actor takes the destination only while moving, so room
        # entries hold their view until the actor walks (mirrors moveCamera).
        if (1 <= follow < 48
                and self.actors[follow]["room"] == self.current_room
                and self.actor_moving(follow)):
            self.camera_dest_x = self.actors[follow]["x"]
        if self.camera_max_x > self.camera_min_x:
            self.camera_dest_x = min(max(self.camera_dest_x, self.camera_min_x), self.camera_max_x)
        self.camera_x &= ~7
        before = self.camera_x
        if self.camera_x < self.camera_dest_x:
            self.camera_x += 8
        elif self.camera_x > self.camera_dest_x:
            self.camera_x -= 8
        if self.camera_dest_x - 8 < self.camera_x < self.camera_dest_x + 8:
            self.camera_x = self.camera_dest_x
        half = getattr(self, "screen_w", 320) // 2
        lo, hi = half, self.room_width - half
        if hi < lo:
            hi = lo
        self.camera_x = min(max(self.camera_x, lo), hi)
        if self.camera_x != before and len(self.globals) > 27 and self.globals[27] != 0:
            self.globals[2] = self.camera_x

    def camera_scroll(self):
        max_scroll = max(0, self.room_width - 320)
        return min(max(self.camera_x - 160, 0), max_scroll)

    def talk_actor(self):
        return self.talking_actor if self.talk_delay > 0 else -1

    def actor_anim_step(self, act):
        return self.actors[act]["animStep"] if 1 <= act < 48 else 0
    def talk_color_index(self, act):
        if self.print_color >= 0:
            return self.print_color
        return self.actors[act]["talkColor"] if 1 <= act < 48 else 15

    def talk_pos(self, act):
        if self.print_overhead == 0:
            return (self.print_x, self.print_y)
        if not 1 <= act < 48:
            return (-1, -1)
        actor = self.actors[act]
        x = min(max(actor["x"], 80), 240)
        y = actor["y"] - actor["elevation"]
        v = self.globals[54] if len(self.globals) > 54 else -80
        if v < 0:
            s = trunc_div(self.actor_render_scale(act) * v, 255)
            y += trunc_div(v - s, 2) + s
        else:
            y = v
        return (x, max(y, 1))

    def advance_actor_walks(self):
        for act in range(1, 48):
            actor = self.actors[act]
            if actor["walkIndex"] >= actor["walkCount"]:
                continue
            if actor["room"] != self.current_room:
                actor["walkCount"] = 0
                continue
            actor["credit"] += self.tick_jiffies
            while actor["credit"] >= 4 and actor["walkIndex"] < actor["walkCount"]:
                actor["credit"] -= 4
                tx, ty = actor["path"][actor["walkIndex"]]
                dx, dy = tx - actor["x"], ty - actor["y"]
                ax, ay = abs(dx), abs(dy)
                speed_x = actor["walkSpeedX"] or 8
                speed_y = actor["walkSpeedY"] or 2
                scale = self.actor_render_scale(act)
                vx, vy = speed_x * scale, speed_y * scale
                if ax * speed_y > ay * speed_x:
                    vy = 0 if ax == 0 else vx * ay // ax
                else:
                    vx = 0 if ay == 0 else vy * ax // ay
                actor["remX"] += vx
                actor["remY"] += vy
                sx, sy = actor["remX"] // 255, actor["remY"] // 255
                actor["remX"] %= 255
                actor["remY"] %= 255
                sx, sy = min(sx, ax), min(sy, ay)
                actor["x"] += -sx if dx < 0 else sx
                actor["y"] += -sy if dy < 0 else sy
                if ax > ay:
                    actor["facing"] = 270 if dx < 0 else 90
                elif ay > 0:
                    actor["facing"] = 0 if dy < 0 else 180
                if actor["x"] == tx and actor["y"] == ty:
                    actor["walkIndex"] += 1
                    actor["remX"] = actor["remY"] = 0
                    if actor["walkIndex"] >= actor["walkCount"] and actor["endDir"] >= 0:
                        actor["facing"] = actor["endDir"]

    def tick(self, delta, budget=10000):
        self.tick_jiffies = max(min(delta, 15), 0)
        for state in self.slots.values():
            state["didexec"] = False
            if state["status"] == "paused":
                state["delay"] -= delta
                if state["delay"] < 0:
                    state["status"] = "run"
        if self.talk_delay > 0:
            self.talk_delay -= delta
            if self.talk_delay <= 0:
                self.talk_delay = 0
                self.globals[3] = 0
        for act in range(1, 48):
            actor = self.actors[act]
            if actor["room"] != self.current_room or actor["costume"] == 0:
                continue
            actor["animProgress"] += 1
            if actor["animProgress"] >= actor["animSpeed"]:
                actor["animProgress"] = 0
                actor["animStep"] += 1
        self.move_camera()
        consumed = 0
        current = -1
        while consumed < budget:
            if current < 0:
                # Nested resume (mirrors runScriptNested): the caller resumes
                # only if its slot is unchanged and unfrozen; otherwise unwind.
                current = self.pop_nested()
                if current < 0:
                    runnable = [s for s, st in self.slots.items()
                                if st["status"] == "run" and st.get("frozen", 0) == 0 and not st["didexec"]]
                    if not runnable:
                        self.ticks += 1
                        self.advance_actor_walks()
                        self.check_sentence()
                        return
                    current = runnable[0]
            state = self.slots[current]
            offset = state["pc"]
            opcode = self.fetch(current)
            state["didexec"] = True
            self.instructions += 1
            consumed += 1
            done = self.execute(current, opcode)
            if self.fault:
                self.fault_script = state["script"]
                self.fault_offset = offset
                self.fault_opcode = opcode
                return
            # The opcode may have killed our own slot (room transitions drop
            # room scripts, chain-scripts die): unwind like runScriptNested.
            if current not in self.slots:
                current = -1
                continue
            if isinstance(done, int):
                current = done
            elif done == "yield":
                current = -1
            elif done == "stop":
                del self.slots[current]
                current = -1
        self.ticks += 1

    def execute(self, slot, opcode):
        state = self.slots[slot]
        self.op_histogram[opcode] = self.op_histogram.get(opcode, 0) + 1
        if opcode in (0x00, 0xA0):
            return "stop"
        if opcode == 0x80:
            return "yield"
        if opcode == 0x2C:
            subop = self.fetch(slot)
            if subop == 1:
                self.cursor_state = 1
            elif subop == 2:
                self.cursor_state = 0
            elif subop == 3:
                self.user_put = 1
            elif subop == 4:
                self.user_put = 0
            elif subop == 5:
                self.cursor_state += 1
            elif subop == 6:
                self.cursor_state -= 1
            elif subop == 7:
                self.user_put += 1
            elif subop == 8:
                self.user_put -= 1
            elif subop in (10, 11, 12, 13):
                masks = {10: (0x80, 0x40), 11: (0x80, 0x40, 0x20), 12: (0x80,), 13: (0x80,)}[subop]
                for mask in masks:
                    if subop & mask:
                        self.read_var(slot, self.fetch_word(slot))
                    else:
                        self.fetch(slot)
            elif subop == 14:
                for _ in range(16):
                    mode = self.fetch(slot)
                    if mode == 0xFF:
                        break
                    self.read_var(slot, self.fetch_word(slot)) if mode & 0x80 else signed16(self.fetch_word(slot))
                # remaining entries default to 0 in the VM; sim only needs the bytes consumed
            self.globals[52] = self.cursor_state
            self.globals[53] = self.user_put
            return None
        if opcode in (0x70, 0xF0):
            # o5_lights: a = varOrDirectByte, b/c = direct bytes.
            # c==0 sets VAR_CURRENT_LIGHTS (9); c==1 is flashlight (ignored).
            a = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
            b = self.fetch(slot)
            c = self.fetch(slot)
            if c == 0 and len(self.globals) > 9:
                self.globals[9] = a
            return None
        if opcode in (0x02, 0x82, 0x1C, 0x9C):
            sound = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
            if opcode in (0x1C, 0x9C):
                self.globals[14] = 0
            self.last_sound = sound
            self.sound_running = 0
            return None
        if opcode == 0x20:
            self.last_sound = 0
            self.sound_running = 0
            return None
        if opcode in (0x3C, 0xBC):
            sound = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
            if sound == self.last_sound:
                self.sound_running = 0
            return None
        if opcode == 0x4C:
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                self.read_var(slot, self.fetch_word(slot)) if mode & 0x80 else signed16(self.fetch_word(slot))
            return None
        if opcode in (0x7C, 0xFC):
            address = self.resolve_result(slot)
            sound = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
            self.write_raw(slot, address, (sound != 0 and sound == self.last_sound and self.sound_running))
            return None
        if opcode in (0x14, 0x94, 0xD8):
            actor = self.globals[1] if opcode == 0xD8 else (self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot))
            self.talking_actor = actor
            # Slot state persists across prints (loadDefault/saveDefault in
            # decodeParseString): only explicit SO_* ops change it.
            while True:
                op = self.fetch(slot)
                if op == 0xFF:
                    break
                kind = op & 0xF
                if kind == 0:
                    self.print_x = self.word_operand(slot, op)
                    self.print_y = self.word_operand(slot, op, 0x40)
                    self.print_overhead = 0
                elif kind == 1:
                    self.print_color = self.read_var(slot, self.fetch_word(slot)) if op & 0x80 else self.fetch(slot)
                elif kind == 2:
                    self.word_operand(slot, op)
                elif kind in (4, 6):
                    self.print_overhead = 0
                elif kind == 7:
                    self.print_overhead = 1
                elif kind == 15:
                    text = self.read_inline(slot)
                    decoded = decode_printable(text)
                    self.prints.append((actor, decoded))
                    self.globals[3] = 0xFF
                    char_inc = self.globals[37] if len(self.globals) > 37 else 4
                    self.talk_delay = 60 + len(decoded) * char_inc
                    break
                else:
                    self.fault = f"Print parse op {op} unsupported"
                    return "stop"
            return None
        if opcode == 0x27:
            subop = self.fetch(slot)
            kind = subop & 0x1F
            if kind == 1:
                slot_id = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                self.strings[slot_id] = self.read_inline(slot) + b"\x00"
            elif kind == 2:
                to = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                frm = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                self.strings[to] = self.strings.get(frm)
            elif kind == 3:
                slot_id = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                index = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                value = self.read_var(slot, self.fetch_word(slot)) if subop & 0x20 else self.fetch(slot)
                buf = bytearray(self.strings.get(slot_id) or b"")
                if 0 <= index < len(buf):
                    buf[index] = value & 0xFF
                    self.strings[slot_id] = bytes(buf)
            elif kind == 4:
                address = self.resolve_result(slot)
                slot_id = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                index = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                buf = self.strings.get(slot_id) or b""
                self.write_raw(slot, address, buf[index] if 0 <= index < len(buf) else 0)
            elif kind == 5:
                slot_id = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                size = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                self.strings[slot_id] = bytes(size)
            else:
                self.fault = f"String op {subop} unsupported"
                return "stop"
            return None
        if opcode in (0x33, 0x73, 0xB3, 0xF3):
            subop = self.fetch(slot)
            kind = subop & 0x1F
            mode = subop
            def woperand(mask=0x80):
                return self.read_var(slot, self.fetch_word(slot)) if mode & mask else signed16(self.fetch_word(slot))
            def boperand(mask=0x80):
                return self.read_var(slot, self.fetch_word(slot)) if mode & mask else self.fetch(slot)
            def newmode():
                return self.fetch(slot)
            if kind == 1:
                self.globals[17] = woperand()
                self.globals[18] = woperand(0x40)
                self.camera_min_x, self.camera_max_x = self.globals[17], self.globals[18]
            elif kind == 2:
                self.fault = "Room color subop removed in v5"
                return "stop"
            elif kind == 4:
                r, g, b = woperand(), woperand(0x40), woperand(0x20)
                mode = newmode()
                idx = boperand()
            elif kind == 3:
                self.screen_top = woperand()
                self.screen_bot = woperand(0x40)
            elif kind in (5, 6):
                self.shake = kind == 5
            elif kind == 7:
                s1, y1 = boperand(), boperand(0x40)
                mode = newmode(); s2, y2 = boperand(), boperand(0x40)
                mode = newmode(); index = boperand(0x40) - 2
                if not 0 <= index < 16:
                    self.fault = "Invalid room scale slot"
                    return "stop"
                self.scale_slots[index * 4:index * 4 + 4] = [s1, y1, s2, y2]
            elif kind == 8:
                boperand(); boperand(0x40); boperand(0x20)
            elif kind == 9:
                self.save_flag = boperand()
                boperand(0x40)
            elif kind == 10:
                self.room_effect = woperand()
            elif kind in (11, 12):
                woperand(); woperand(0x40); woperand(0x20)
                mode = newmode(); boperand(); boperand(0x40)
            elif kind in (13, 14):
                boperand()
                while self.fetch(slot) != 0:
                    pass
            elif kind == 15:
                boperand()
                mode = newmode(); boperand(); boperand(0x40)
                mode = newmode(); boperand()
            elif kind == 16:
                boperand(); boperand(0x40)
            else:
                self.fault = f"Room op {subop} unsupported"
                return "stop"
            return None
        if opcode == 0x30:
            # o5_matrixOps: walkbox flags/scale and itinerary rebuild.
            subop = self.fetch(slot)
            kind = subop & 0x1F
            def moperand(mask=0x80):
                return self.read_var(slot, self.fetch_word(slot)) if subop & mask else self.fetch(slot)
            if kind in (1, 2, 3):
                box = moperand()
                val = moperand(0x40)
                if not 0 <= box < len(self.walkboxes):
                    pass
                elif kind == 1:
                    self.walkboxes[box]["flags"] = val & 0xFF
                elif kind == 2:
                    self.walkboxes[box]["scale"] = val & 0xFFFF
                else:
                    self.walkboxes[box]["scale"] = ((val - 1) | 0x8000) & 0xFFFF
            elif kind == 4:
                self.create_box_matrix()
            else:
                self.fault = f"Matrix op {subop} unsupported"
                return "stop"
            return None
        if opcode in (0x0C, 0x8C):
            subop = self.fetch(slot)
            resid = 0
            if subop != 17:
                resid = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
            op = subop & 0x3F
            if 1 <= op <= 8:
                pass
            elif op == 9:
                pass
            elif 10 <= op <= 16:
                pass
            elif op == 17:
                pass
            elif op == 18:
                pass
            elif op == 19:
                pass
            elif op == 20:
                self.word_operand(slot, subop)
            elif op in (32, 33):
                pass
            else:
                self.fault = f"Resource op {subop} unsupported"
                return "stop"
            return None
        if opcode == 0x18:
            self.branch(slot, False)
            return None
        if opcode in (0x2B,):
            state["delay"] = self.read_var(slot, self.fetch_word(slot))
            state["status"] = "paused"
            return "yield"
        if opcode == 0x2E:
            delay = self.fetch(slot) | (self.fetch(slot) << 8) | (self.fetch(slot) << 16)
            state["delay"] = delay
            state["status"] = "paused"
            return "yield"
        if opcode in (0x28, 0xA8):
            value = self.read_var(slot, self.fetch_word(slot))
            self.branch(slot, (value == 0) if opcode == 0x28 else (value != 0))
            return None
        if opcode in (0x46, 0xC6):
            address = self.resolve_result(slot)
            self.write_raw(slot, address, self.read_raw(slot, address) + (1 if opcode == 0x46 else -1))
            return None
        if (opcode & 0x1F) == 0x0A:
            script = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            args = []
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                args.append(self.read_var(slot, self.fetch_word(slot)) if mode & 0x80
                            else signed16(self.fetch_word(slot)))
            freeze_res = (opcode & 0x20) != 0
            recursive = (opcode & 0x40) != 0
            if not recursive:
                for key, value in list(self.slots.items()):
                    if value["script"] == script and value["where"] in (0, 1):
                        if value.get("cutscene", 0) > 0:
                            self.fault = f"Script {script} stopped with active cutscene/override"
                            return "stop"
                        del self.slots[key]
            child = self.launch(script, args, slot, 0, 0)
            if child >= 0:
                self.slots[child]["freezeRes"] = freeze_res
                self.slots[child]["recursive"] = recursive
            return child
        if opcode == 0x42:
            # o5_chainScript: like startScript args, but the current slot dies
            # and the chained script inherits its freeze/recursive flags.
            script = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
            args = []
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                args.append(self.read_var(slot, self.fetch_word(slot)) if mode & 0x80
                            else signed16(self.fetch_word(slot)))
            cur = self.slots.get(slot, {})
            freeze_res = cur.get("freezeRes", False)
            recursive = cur.get("recursive", False)
            if script == 0:
                if slot in self.slots:
                    del self.slots[slot]
                return -1
            if not recursive:
                for key, value in list(self.slots.items()):
                    if key != slot and value["script"] == script and value["where"] in (0, 1):
                        if value.get("cutscene", 0) > 0:
                            self.fault = f"Script {script} stopped with active cutscene/override"
                            return "stop"
                        del self.slots[key]
            if slot in self.slots:
                del self.slots[slot]
            child = self.launch(script, args, -1, 0, 0)
            if child >= 0:
                self.slots[child]["freezeRes"] = freeze_res
                self.slots[child]["recursive"] = recursive
            return child
        if opcode in (0x62, 0xE2):
            script = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if script == 0:
                if self.slots[slot].get("cutscene", 0) > 0:
                    self.fault = "Script stopped with active cutscene/override"
                    return "stop"
                if self.slots[slot].get("where", 0) == 2:
                    number = self.slots[slot]["script"]
                    for key, value in list(self.slots.items()):
                        if value["script"] == number and value.get("where", 0) == 2:
                            if value.get("cutscene", 0) > 0:
                                self.fault = "Object stopped with active cutscene/override"
                                return "stop"
                            del self.slots[key]
                else:
                    del self.slots[slot]
                return "stop"
            for key, value in list(self.slots.items()):
                if value["script"] == script and value["where"] in (0, 1):
                    if value.get("cutscene", 0) > 0:
                        self.fault = f"Script {script} stopped with active cutscene/override"
                        return "stop"
                    del self.slots[key]
            return None
        if opcode in (0x68, 0xE8):
            address = self.resolve_result(slot)
            script = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            self.write_raw(slot, address, 1 if any(s["script"] == script for s in self.slots.values()) else 0)
            return None
        if opcode in (0x72, 0xF2):
            room = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            self.start_scene(room)
            return None
        if opcode in (0x60, 0xE0):
            flag = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if flag != 0:
                for key, value in self.slots.items():
                    if key != slot and key != self.cut_scene_slot and value["status"] != "dead" and (not value.get("freezeRes", False) or flag >= 0x80):
                        value["frozen"] = value.get("frozen", 0) + 1
            else:
                for value in self.slots.values():
                    if value.get("frozen", 0) > 0:
                        value["frozen"] -= 1
            return None
        if opcode in (0x01, 0x21, 0x41, 0x61, 0x81, 0xA1, 0xC1, 0xE1):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            x = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x40 else signed16(self.fetch_word(slot))
            y = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x20 else signed16(self.fetch_word(slot))
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            # ScummVM putActor(x,y) keeps _room; only position changes.
            self.actors[act].update(x=x, y=y, visible=True,
                                    walkCount=0, walkIndex=0, path=[])
            return None
        if opcode in (0x0E, 0x4E, 0x8E, 0xCE):
            # o5_putActorAtObject: actor at the object's walk point,
            # or (240,120) when the object is not around. Room unchanged.
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            obj = self.word_operand(slot, opcode, 0x40)
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            x, y = 240, 120
            if self.object_owner[obj] == 15:
                target = next((o for o in self.obj_slots.values() if o["id"] == obj), None)
                if target is not None:
                    x, y = target["walkX"], target["walkY"]
            else:
                owner = self.object_owner[obj]
                if 1 <= owner < 48 and self.actors[owner]["room"] == self.current_room:
                    x = self.actors[owner]["x"]
                    y = self.actors[owner]["y"]
            self.actors[act].update(x=x, y=y, visible=True,
                                    walkCount=0, walkIndex=0, path=[])
            return None
        if opcode in (0x24, 0x64, 0xA4, 0xE4):
            obj = self.word_operand(slot, opcode)
            room = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x40 else self.fetch(slot)
            x = signed16(self.fetch_word(slot))
            y = signed16(self.fetch_word(slot))
            ego = self.globals[1] if len(self.globals) > 1 else 0
            if not 1 <= ego < 48:
                self.fault = f"Invalid actor {ego}"
                return "stop"
            # Placement, camera snap, and arrival walk are applied inside
            # start_scene once the room exists (mirrors the deferred
            # InstallRoom path in the C# VM).
            self.__dict__["pending_place"] = (ego, obj, x, y)
            self.__dict__["camera_follow"] = ego
            self.start_scene(room)
            if self.fault:
                return "stop"
            if len(self.globals) > 38:
                self.globals[38] = obj
            if len(self.globals) > 38:
                self.globals[38] = 0
            return None
        if opcode in (0x2D, 0x6D, 0xAD, 0xED):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            room = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x40 else self.fetch(slot)
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            self.actors[act]["room"] = room
            self.actors[act].update(walkCount=0, walkIndex=0, path=[])
            if room == 0:
                self.actors[act]["x"] = 0
                self.actors[act]["y"] = 0
            return None
        if opcode in (0x03, 0x83, 0x43, 0xC3, 0x23, 0xA3, 0x63, 0xE3, 0x71, 0xF1):
            address = self.resolve_result(slot)
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            key = {0x03: "room", 0x83: "room", 0x43: "x", 0xC3: "x", 0x23: "y",
                   0xA3: "y", 0x63: "facing", 0xE3: "facing",
                   0x71: "costume", 0xF1: "costume"}[opcode & 0xFF]
            self.write_raw(slot, address, self.actors[act][key])
            return None
        if opcode in (0x11, 0x51, 0x91, 0xD1):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            anim = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x40 else self.fetch(slot)
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            self.actors[act]["anim"] = anim
            if anim >= 252:
                if self.actors[act]["room"] == self.current_room:
                    self.actors[act].update(walkCount=0, walkIndex=0, path=[])
            elif anim >= 244:
                self.actors[act]["facing"] = [270, 90, 180, 0][anim & 3]
            return None
        if opcode in (0x07, 0x47, 0x87, 0xC7):
            obj = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else signed16(self.fetch_word(slot))
            state = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x40 else self.fetch(slot)
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            self.object_state[obj] = state
            return None
        if opcode in (0x0F, 0x8F):
            address = self.resolve_result(slot)
            obj = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else signed16(self.fetch_word(slot))
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            self.write_raw(slot, address, self.object_state[obj])
            return None
        if opcode in (0x29, 0x69, 0xA9, 0xE9):
            obj = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else signed16(self.fetch_word(slot))
            owner = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x40 else self.fetch(slot)
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            self.object_owner[obj] = owner
            return None
        if opcode in (0x10, 0x90):
            address = self.resolve_result(slot)
            obj = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else signed16(self.fetch_word(slot))
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            self.write_raw(slot, address, self.object_owner[obj])
            return None
        if opcode in (0x5D, 0xDD):
            obj = self.word_operand(slot, opcode)
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                cls = self.word_operand(slot, mode)
                if (cls & 0xFF) == 0:
                    self.object_class[obj] = 0
                else:
                    bit = 1 << ((cls & 0x7F) - 1)
                    if cls & 0x80:
                        self.object_class[obj] |= bit
                    else:
                        self.object_class[obj] &= ~bit
            return None
        if opcode in (0x1D, 0x9D):
            obj = self.word_operand(slot, opcode)
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            cond = True
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                cls = self.word_operand(slot, mode)
                bit = (cls & 0x7F)
                present = 0
                if 1 <= bit <= 32:
                    present = 1 if self.object_class[obj] & (1 << (bit - 1)) else 0
                if ((cls & 0x80) and not present) or (not (cls & 0x80) and present):
                    cond = False
            self.branch(slot, cond)
            return None
        if opcode in (0x26, 0xA6):
            address = self.resolve_result(slot)
            count = self.fetch(slot)
            while count > 0 and not self.fault:
                count -= 1
                value = signed16(self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
                self.write_raw(slot, address, value)
                address += 1
            return None
        if opcode == 0xAC:
            address = self.resolve_result(slot)
            stack = []
            while True:
                subop = self.fetch(slot)
                if subop == 0xFF:
                    break
                kind = subop & 0x1F
                if kind == 1:
                    stack.append(self.word_operand(slot, subop))
                elif 2 <= kind <= 5:
                    if len(stack) < 2:
                        self.fault = "Expression stack underflow"
                        return "stop"
                    divisor = stack.pop()
                    left = stack.pop()
                    if kind == 2:
                        stack.append(wrap32(left + divisor))
                    elif kind == 3:
                        stack.append(wrap32(left - divisor))
                    elif kind == 4:
                        stack.append(wrap32(left * divisor))
                    else:
                        if divisor == 0:
                            self.fault = "Division by zero"
                            return "stop"
                        stack.append(int(left / divisor))
                elif kind == 6:
                    nested = self.fetch(slot)
                    result = self.execute(slot, nested)
                    if self.fault:
                        return "stop"
                    if result == "stop":
                        return "stop"
                    if isinstance(result, int):
                        return result
                    stack.append(self.globals[0] if len(self.globals) > 0 else 0)
            if not stack:
                self.fault = "Empty expression"
                return "stop"
            self.write_raw(slot, address, stack.pop())
            return None
        if opcode in (0x7A, 0xFA):
            verb = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            verbs = self.__dict__.setdefault("verbs", {})
            vslot = next((k for k, v in verbs.items() if v["id"] == verb and v.get("save", 0) == 0), 0)
            if vslot == 0:
                verbs[0] = {"id": verb, "mode": 0, "color": 0, "hi": 0, "dim": 8,
                            "bk": 0, "left": 0, "top": 0, "key": 0, "center": 0,
                            "type": 0, "img": 0, "save": 0, "name": b""}
            else:
                verbs[vslot]["id"] = verb
            def vget():
                return verbs[vslot] if vslot in verbs else verbs[0]
            while True:
                subop = self.fetch(slot)
                if subop == 0xFF:
                    break
                kind = subop & 0x1F
                if kind == 1:
                    img = self.word_operand(slot, subop)
                    if vslot != 0:
                        vget()["img"] = img
                        vget()["type"] = 1
                elif kind == 2:
                    name = self.read_inline(slot)
                    if vslot != 0:
                        vget()["name"] = name
                        vget()["type"] = 0
                        vget()["img"] = 0
                elif kind == 3:
                    v = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                    if vslot != 0:
                        vget()["color"] = v
                elif kind == 4:
                    v = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                    if vslot != 0:
                        vget()["hi"] = v
                elif kind == 5:
                    left = self.word_operand(slot, subop)
                    right = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else signed16(self.fetch_word(slot))
                    if vslot != 0:
                        vget()["left"] = left
                        vget()["top"] = right
                elif kind == 6:
                    if vslot != 0:
                        vget()["mode"] = 1
                elif kind == 7:
                    if vslot != 0:
                        vget()["mode"] = 0
                elif kind == 8:
                    if vslot != 0:
                        verbs[vslot]["id"] = 0
                        verbs[vslot]["mode"] = 0
                elif kind == 9:
                    vslot = next((k for k, v in verbs.items() if v["id"] == verb and v.get("save", 0) == 0), 0)
                    if vslot == 0:
                        vslot = next((k for k in range(1, 100) if k not in verbs or verbs[k]["id"] == 0), 0)
                        if vslot == 0:
                            self.fault = "Too many verbs"
                            return "stop"
                        verbs[vslot] = {}
                    verbs[vslot] = {"id": verb, "mode": 0, "color": 2, "hi": 0, "dim": 8,
                                    "bk": 0, "left": 0, "top": 0, "key": 0, "center": 0,
                                    "type": 0, "img": 0, "save": 0, "name": b""}
                elif kind == 16:
                    v = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                    if vslot != 0:
                        vget()["dim"] = v
                elif kind == 17:
                    if vslot != 0:
                        vget()["mode"] = 2
                elif kind == 18:
                    v = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                    if vslot != 0:
                        vget()["key"] = v
                elif kind == 19:
                    if vslot != 0:
                        vget()["center"] = 1
                elif kind == 20:
                    strid = self.word_operand(slot, subop)
                    if vslot != 0:
                        vget()["name"] = self.strings.get(strid, b"")
                        vget()["type"] = 0
                        vget()["img"] = 0
                elif kind == 22:
                    img = self.word_operand(slot, subop)
                    obj = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                    if vslot != 0 and vget()["img"] != img:
                        vget()["img"] = img
                        vget()["type"] = 1
                elif kind == 23:
                    v = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else self.fetch(slot)
                    if vslot != 0:
                        vget()["bk"] = v
                else:
                    self.fault = f"Verb op {subop} unsupported"
                    return "stop"
            return None
        if opcode == 0xCC:
            self.fetch(slot)
            while self.fetch(slot) != 0:
                pass
            return None
        if opcode in (0x16, 0x96):
            address = self.resolve_result(slot)
            maximum = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else self.fetch(slot)
            if maximum <= 0:
                self.write_raw(slot, address, 0)
            else:
                self.rnd = (self.rnd * 1103515245 + 12345) & 0x7FFFFFFF
                self.write_raw(slot, address, self.rnd % (maximum + 1))
            return None
        if opcode == 0x40:
            args = []
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                args.append(self.word_operand(slot, mode))
            while len(args) < 25:
                args.append(0)
            self.slots[slot]["cutscene"] = self.slots[slot].get("cutscene", 0) + 1
            self.cut_scene_slot = slot
            self.cut_stack.append({"data": args[0], "slot": -1, "pos": 0, "active": True})
            if self.globals[35] != 0:
                child = self.launch(self.globals[35], list(args), slot)
                return child
            return None
        if opcode == 0x58:
            flag = self.fetch(slot)
            if flag != 0:
                if self.cut_stack:
                    self.cut_stack[-1]["slot"] = slot
                    self.cut_stack[-1]["pos"] = self.slots[slot]["pc"]
                    self.cut_stack[-1]["script"] = self.slots[slot].get("script", 0)
                    self.cut_stack[-1]["where"] = self.slots[slot].get("where", 0)
                    self.cut_stack[-1]["code"] = self.slots[slot].get("code")
                self.fetch(slot)
                self.fetch_word(slot)
                self.globals[5] = 0
            else:
                if self.cut_stack:
                    self.cut_stack[-1]["slot"] = -1
                    self.cut_stack[-1]["pos"] = 0
                self.globals[5] = 0
            return None
        if opcode == 0xC0:
            if self.slots[slot].get("cutscene", 0) > 0:
                self.slots[slot]["cutscene"] -= 1
                if self.cut_stack and self.cut_stack[-1]["active"]:
                    self.cut_stack[-1]["active"] = False
                    if self.slots[slot]["cutscene"] > 0:
                        self.slots[slot]["cutscene"] -= 1
            self.globals[5] = 0
            if self.cut_stack:
                self.cut_stack.pop()
            # Restore UI torn down by entry cutscenes: room 32's spiel swaps
            # in a locked verb script and stops the hover tracker (script 23)
            # without restoring either, which fizzles every later click.
            if len(self.globals) > 32 and self.globals[32] == 14:
                self.globals[32] = 4
            if 23 in self.programs and not any(v.get("script") == 23 for v in self.slots.values()):
                self.launch(23, [0] * 25, -1, 0, 0)
            if self.globals[36] != 0:
                child = self.launch(self.globals[36], [0] * 25, slot)
                return child
            return None
        if (opcode & 0x1F) == 0x19:
            verb = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if verb == 0xFE:
                self.sentence_num = 0
                for key, value in list(self.slots.items()):
                    if value["script"] == self.globals[33] and value["where"] in (0, 1):
                        del self.slots[key]
                return None
            object_a = self.word_operand(slot, opcode, 0x40)
            object_b = self.word_operand(slot, opcode, 0x20)
            self.sentence = (verb, object_a, object_b)
            self.sentence_num = 1
            return None
        if opcode == 0xAB:
            subop = self.fetch(slot)
            mode = subop
            a = self.read_var(slot, self.fetch_word(slot)) if mode & 0x80 else self.fetch(slot)
            b = self.read_var(slot, self.fetch_word(slot)) if mode & 0x40 else self.fetch(slot)
            c = self.read_var(slot, self.fetch_word(slot)) if mode & 0x20 else self.fetch(slot)
            verbs = self.__dict__.setdefault("verbs", {})
            def find(verb, save):
                for k, v in verbs.items():
                    if v["id"] == verb and v.get("save", 0) == save:
                        return k
                return 0
            if subop == 1:
                while a <= b:
                    s = find(a, 0)
                    if s != 0 and verbs[s].get("save", 0) == 0:
                        verbs[s]["save"] = c
                    a += 1
            elif subop == 2:
                while a <= b:
                    s = find(a, c)
                    if s != 0:
                        s2 = find(a, 0)
                        if s2 != 0:
                            verbs[s2]["id"] = 0
                            verbs[s2]["mode"] = 0
                        s = find(a, c)
                        verbs[s]["save"] = 0
                    a += 1
            elif subop == 3:
                while a <= b:
                    s = find(a, c)
                    if s != 0:
                        verbs[s]["id"] = 0
                        verbs[s]["mode"] = 0
                    a += 1
            else:
                self.fault = f"SaveRestoreVerbs op {subop} unsupported"
                return "stop"
            return None
        if opcode in (0x12, 0x92):
            self.camera_dest_x = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else signed16(self.fetch_word(slot))
            self.__dict__["camera_follow"] = 0
            return None
        if opcode in (0x32, 0xB2):
            self.camera_dest_x = self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80 else signed16(self.fetch_word(slot))
            return None
        if opcode in (0x0B, 0x4B, 0x8B, 0xCB):
            address = self.resolve_result(slot)
            obj = self.word_operand(slot, opcode)
            entry = self.word_operand(slot, opcode, 0x40)
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            result = 0
            owned = self.object_owner[obj] == 15
            in_room = owned and any(o["id"] == obj for o in self.obj_slots.values())
            if in_room and self.room_bytes is not None:
                base = next(o["obcd"] for o in self.obj_slots.values() if o["id"] == obj)
                data = self.room_bytes
                # VERB is a sub-chunk inside the object's OBCD chunk
                # (ScummEngine::findResource): size-walk siblings bounded by
                # the OBCD size instead of hopping across neighboring chunks.
                total = struct.unpack_from(">I", data, base + 4)[0] if base + 7 < len(data) else 0
                limit = base + total if total >= 8 else base
                end = base + 8
                found = False
                while end + 3 < limit and end + 3 < len(data):
                    if data[end:end + 4] == b"VERB":
                        found = True
                        break
                    if end + 7 >= limit:
                        break
                    size = struct.unpack_from(">I", data, end + 4)[0]
                    if size < 8:
                        break
                    end += size
                if found:
                    verboffs = end - base
                    p = end + 8
                    while p < len(data):
                        vid = data[p]
                        if vid == 0:
                            break
                        if vid == entry or vid == 0xFF:
                            break
                        p += 3
                    if p + 2 < len(data) and data[p] != 0:
                        result = verboffs + struct.unpack_from("<H", data, p + 1)[0]
            self.write_raw(slot, address, result)
            return None
        if opcode in (0x05, 0x85):
            obj = self.word_operand(slot, opcode)
            subop = self.fetch(slot)
            kind = subop & 0x1F
            state, xpos, ypos = 1, 0xFF, 0xFF
            if kind == 1:
                xpos = self.word_operand(slot, subop, 0x80)
                ypos = self.word_operand(slot, subop, 0x40)
            elif kind == 2:
                state = self.word_operand(slot, subop, 0x80)
            elif kind != 0x1F:
                self.fault = f"DrawObject subop {kind} unsupported"
                return "stop"
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            target = next((o for o in self.obj_slots.values() if o["id"] == obj), None)
            if target is None:
                return None
            if xpos != 0xFF:
                target["walkX"] += (xpos * 8) - target["x"]
                target["walkY"] += (ypos * 8) - target["y"]
                target["x"] = xpos * 8
                target["y"] = ypos * 8
            for other in self.obj_slots.values():
                if (other["x"], other["y"], other["w"], other["h"]) == (
                        target["x"], target["y"], target["w"], target["h"]):
                    self.object_state[other["id"]] = 0
            self.object_state[obj] = state
            return None
        if opcode in (0x52, 0xD2):
            # actorFollowCamera snaps when the actor is over half a screen
            # away (like setCameraAt).
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            self.__dict__["camera_follow"] = act
            if 1 <= act < 48 and self.actors[act]["room"] == self.current_room:
                if abs(self.actors[act]["x"] - self.camera_x) > 160:
                    half = getattr(self, "screen_w", 320) // 2
                    lo, hi = half, self.room_width - half
                    if hi < lo:
                        hi = lo
                    self.camera_x = min(max(self.actors[act]["x"], lo), hi)
                    self.camera_dest_x = self.camera_x
            return None
        if opcode in (0x56, 0xD6):
            # o5_getActorMoving: whether the actor is walking.
            address = self.resolve_result(slot)
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            self.write_raw(slot, address, self.actor_moving(act))
            return None
        if opcode in (0x35, 0x75, 0xB5, 0xF5):
            address = self.resolve_result(slot)
            x = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            y = self.fetch(slot) if not opcode & 0x40 else self.read_var(slot, self.fetch_word(slot))
            found = 0
            for entry in self.obj_slots.values():
                if entry["id"] < 1:
                    continue
                if self.object_class[entry["id"]] & (1 << 31):
                    continue
                if entry["x"] <= x < entry["x"] + entry["w"] and entry["y"] <= y < entry["y"] + entry["h"]:
                    found = entry["id"]
                    break
            self.write_raw(slot, address, found)
            return None
        if opcode in (0x55, 0xD5):
            address = self.resolve_result(slot)
            self.word_operand(slot, opcode)
            self.word_operand(slot, opcode, 0x40)
            self.write_raw(slot, address, 0)
            return None
        if opcode in (0x1E, 0x3E, 0x5E, 0x7E, 0x9E, 0xBE, 0xDE, 0xFE):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            x = self.word_operand(slot, opcode, 0x40)
            y = self.word_operand(slot, opcode, 0x20)
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            self.start_actor_walk(act, x, y, -1)
            return None
        if opcode in (0x37, 0x77, 0xB7, 0xF7):
            obj = self.word_operand(slot, opcode)
            entry = self.fetch(slot) if not opcode & 0x40 else self.read_var(slot, self.fetch_word(slot))
            args = []
            while True:
                mode = self.fetch(slot)
                if mode == 0xFF:
                    break
                args.append(self.word_operand(slot, mode))
            while len(args) < 25:
                args.append(0)
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            owned = self.object_owner[obj] == 15
            in_room = owned and any(o["id"] == obj for o in self.obj_slots.values())
            if not in_room:
                return None
            base = next(o["obcd"] for o in self.obj_slots.values() if o["id"] == obj)
            end = base + 8
            # VERB is a sub-chunk inside the object's OBCD chunk
            # (ScummEngine::findResource): bound the sibling walk by the
            # OBCD size instead of hopping across neighboring chunks.
            total = struct.unpack_from(">I", self.room_bytes, base + 4)[0] if base + 7 < len(self.room_bytes) else 0
            limit = base + total if total >= 8 else base
            found = False
            while end + 3 < limit and end + 3 < len(self.room_bytes):
                if self.room_bytes[end:end + 4] == b"VERB":
                    found = True
                    break
                if end + 7 >= limit:
                    break
                size = struct.unpack_from(">I", self.room_bytes, end + 4)[0]
                if size < 8:
                    break
                end += size
            if not found:
                return None
            verboffs = end - base
            p = end + 8
            result = 0
            while p < len(self.room_bytes):
                vid = self.room_bytes[p]
                if vid == 0:
                    break
                if vid == entry or vid == 0xFF:
                    break
                p += 3
            if p + 2 < len(self.room_bytes) and self.room_bytes[p] != 0:
                result = verboffs + struct.unpack_from("<H", self.room_bytes, p + 1)[0]
            if result == 0:
                return None
            for key, value in list(self.slots.items()):
                if value["script"] == obj and value.get("where", 0) == 2:
                    del self.slots[key]
            self.launch(obj, args, slot, 2, base + result)
            return None
        if opcode in (0x25, 0x65, 0xA5, 0xE5):
            obj = self.word_operand(slot, opcode)
            room = self.fetch(slot) if not opcode & 0x40 else self.read_var(slot, self.fetch_word(slot))
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            if room == 0:
                room = self.current_room
            try:
                inv = self.inventory.index(0)
            except ValueError:
                self.fault = "Inventory full"
                return "stop"
            self.inventory[inv] = obj
            for key, value in list(self.obj_slots.items()):
                if value["id"] == obj:
                    del self.obj_slots[key]
            self.object_owner[obj] = self.globals[1] if len(self.globals) > 1 else 0
            self.object_class[obj] |= (1 << 31)
            self.object_state[obj] = 1
            if len(self.globals) > 34 and self.globals[34] != 0:
                self.launch(self.globals[34], [1] + [0] * 24, slot)
            return None
        if opcode in (0x34, 0x74, 0xB4, 0xF4):
            # o5_getDist: Chebyshev distance between two object/actor
            # endpoints, 0xFF when either is unresolvable.
            address = self.resolve_result(slot)
            o1 = self.word_operand(slot, opcode)
            o2 = self.word_operand(slot, opcode, 0x40)
            self.write_raw(slot, address, self.obj_act_dist(o1, o2))
            return None
        if opcode in (0x7B, 0xFB):
            # o5_getActorWalkBox: the walkbox the actor stands in.
            address = self.resolve_result(slot)
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            box = 0
            actor = self.actors[act]
            if actor["room"] == self.current_room:
                for i in range(1, len(self.walkboxes)):
                    if self._box_point_in(self.walkboxes[i], actor["x"], actor["y"]):
                        box = i
                        break
            self.write_raw(slot, address, box)
            return None
        if opcode in (0x67, 0xE7):
            # o5_getStringWidth: 8px per printable char, like verb widths.
            address = self.resolve_result(slot)
            strid = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            width = 0
            text = self.__dict__.get("strings", {}).get(strid)
            if text:
                i = 0
                while i < len(text):
                    c = text[i]
                    i += 1
                    if c == 0:
                        break
                    if c == 0xFF:
                        if i >= len(text):
                            break
                        control = text[i]
                        i += 1
                        if control not in (1, 2, 3, 8):
                            i += 2
                        continue
                    if 32 <= c < 127:
                        width += 8
            self.write_raw(slot, address, width)
            return None
        if opcode in (0xAE,):
            offset = self.slots[slot]["pc"] - 1
            subop = self.fetch(slot)
            kind = subop & 0x1F
            if kind == 1:
                act = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                if not 1 <= act < 48:
                    self.fault = f"Invalid actor {act}"
                    return "stop"
                if self.actors[act]["room"] == self.current_room and self.actor_moving(act):
                    self.slots[slot]["pc"] = offset
                    return "yield"
                return None
            elif kind == 2:
                if len(self.globals) > 3 and self.globals[3] != 0:
                    self.slots[slot]["pc"] = offset
                    return "yield"
                return None
            elif kind == 3:
                return None
            elif kind == 4:
                sentence_script = self.globals[33] if len(self.globals) > 33 else 0
                if getattr(self, "sentence_num", 0) > 0 or any(
                        v["script"] == sentence_script for v in self.slots.values()):
                    self.slots[slot]["pc"] = offset
                    return "yield"
                return None
            self.fault = f"Wait subop {kind} unsupported"
            return "stop"
        if opcode in (0x36, 0x76, 0xB6, 0xF6):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            obj = self.word_operand(slot, opcode, 0x40)
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            target = next((o for o in self.obj_slots.values() if o["id"] == obj), None)
            if target is None:
                return None
            direction = target.get("actorDir", 0)
            end = 270 if direction == 0 else (90 if direction == 1 else (180 if direction == 2 else 0))
            self.start_actor_walk(act, target["walkX"], target["walkY"], end)
            return None
        if opcode in (0x09, 0x49, 0x89, 0xC9):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            obj = self.word_operand(slot, opcode, 0x40)
            if not 1 <= act < 48:
                return None
            if not 0 <= obj < 1024:
                self.fault = f"Invalid object {obj}"
                return "stop"
            target = next((o for o in self.obj_slots.values() if o["id"] == obj), None)
            if target is None:
                return None
            self.actors[act]["facing"] = 90 if target["x"] > self.actors[act]["x"] else 270
            return None
        if opcode in (0x31, 0xB1):
            address = self.resolve_result(slot)
            owner = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            count = sum(1 for o in self.inventory
                        if o != 0 and 0 <= o < 1024 and self.object_owner[o] == owner)
            self.write_raw(slot, address, count)
            return None
        if opcode in (0x7D, 0xFD):
            address = self.resolve_result(slot)
            owner = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            index = self.fetch(slot) if not opcode & 0x40 else self.read_var(slot, self.fetch_word(slot))
            count = 0
            found = 0
            for o in self.inventory:
                if o != 0 and 0 <= o < 1024 and self.object_owner[o] == owner:
                    count += 1
                    if count == index:
                        found = o
                        break
            self.write_raw(slot, address, found)
            return None
        if opcode in (0x6B, 0xEB):
            self.word_operand(slot, opcode)
            return None
        if opcode == 0xA7:
            return None
        if opcode in (0x13, 0x53, 0x93, 0xD3):
            act = self.fetch(slot) if not opcode & 0x80 else self.read_var(slot, self.fetch_word(slot))
            if not 1 <= act < 48:
                self.fault = f"Invalid actor {act}"
                return "stop"
            actor = self.actors[act]
            while True:
                subop = self.fetch(slot)
                if subop == 0xFF:
                    break
                kind = subop & 0x1F
                if kind == 0:
                    self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 1:
                    actor["costume"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 2:
                    actor["walkSpeedX"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                    actor["walkSpeedY"] = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                elif kind == 3:
                    actor["sound"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 4:
                    actor["walkFrame"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 5:
                    actor["talkStart"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                    actor["talkStop"] = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                elif kind == 6:
                    actor["standFrame"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 7:
                    self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                    self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                    self.read_var(slot, self.fetch_word(slot)) if subop & 0x20 else self.fetch(slot)
                elif kind == 8:
                    actor["costume"] = 0
                    actor["walkSpeedX"] = 0
                    actor["walkSpeedY"] = 0
                elif kind == 9:
                    actor["elevation"] = self.read_var(slot, self.fetch_word(slot)) if subop & 0x80 else signed16(self.fetch_word(slot))
                elif kind == 10:
                    actor["initFrame"] = 1
                    actor["walkFrame"] = 2
                    actor["standFrame"] = 3
                    actor["talkStart"] = 4
                    actor["talkStop"] = 5
                elif kind == 11:
                    i = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                    j = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                    if 0 <= i < 32:
                        actor["palette"][i] = j
                elif kind == 12:
                    actor["talkColor"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 13:
                    actor["name"] = self.read_inline(slot)
                elif kind == 14:
                    actor["initFrame"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 16:
                    actor["width"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 17:
                    actor["scaleX"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                    actor["scaleY"] = self.read_var(slot, self.fetch_word(slot)) if subop & 0x40 else self.fetch(slot)
                elif kind == 18:
                    actor["forceClip"] = 0
                elif kind == 19:
                    actor["forceClip"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind in (20, 21):
                    actor["ignoreBoxes"] = 0 if subop & 1 else 1
                    actor["forceClip"] = 0
                elif kind == 22:
                    actor["animSpeed"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                elif kind == 23:
                    actor["shadow"] = self.fetch(slot) if not subop & 0x80 else self.read_var(slot, self.fetch_word(slot))
                else:
                    self.fault = f"Actor op {subop} unsupported"
                    return "stop"
            return None
        op = opcode & 0x7F
        if op in (0x1A, 0x5A, 0x3A, 0x1B, 0x5B, 0x17, 0x57):
            address = self.resolve_result(slot)
            right = (self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80
                     else signed16(self.fetch_word(slot)))
            if op == 0x1A:
                self.write_raw(slot, address, right)
            else:
                left = self.read_raw(slot, address)
                if op == 0x5B and right == 0:
                    self.fault = "Division by zero"
                    return "stop"
                result = {0x5A: left + right, 0x3A: left - right, 0x1B: left * right,
                          0x5B: int(left / right),
                          0x17: left & right, 0x57: left | right}[op]
                self.write_raw(slot, address, wrap32(result) if op in (0x5A, 0x3A, 0x1B) else result)
            return None
        if op in (0x04, 0x08, 0x38, 0x44, 0x48, 0x78):
            left = signed16(self.read_var(slot, self.fetch_word(slot)))
            right = signed16(self.read_var(slot, self.fetch_word(slot)) if opcode & 0x80
                             else self.fetch_word(slot))
            conditions = {0x04: right >= left, 0x08: right != left, 0x38: right <= left,
                          0x44: right < left, 0x48: right == left, 0x78: right > left}
            self.branch(slot, conditions[op])
            return None
        self.fault = f"Unsupported opcode {opcode}"
        return "stop"


class FixtureTests(unittest.TestCase):
    def test_hand_computed_wrap(self):
        self.assertEqual(wrap32(32767 * 32767 * 4), -262140)

    def test_indirect_encoding_layout(self):
        code = move_indirect(20, 0x2000 | 100, 42)
        self.assertEqual(len(code), 7)
        self.assertEqual(code[0], 0x1A)
        self.assertEqual(struct.unpack_from("<H", code, 1)[0], 0x2014)
        self.assertEqual(struct.unpack_from("<H", code, 3)[0], 0x2064)
        self.assertEqual(struct.unpack_from("<H", code, 5)[0], 42)

    def test_jump_targets_land_on_boundaries(self):
        data = next(f["build"]() for f in FIXTURES if f["name"] == "vars-branches")
        # Walk the stream; every jump displacement must resolve inside the program.
        script = {10: data}
        sim = ReferenceSim(script)
        sim.start(10, [0])
        for _ in range(40):
            sim.tick(4)
            if not sim.slots:
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[19], 5)
        self.assertEqual(sim.globals[21], 1)

    def test_printable_decode_skips_controls(self):
        self.assertEqual(decode_printable(b"Hi!\x00"), "Hi!")
        self.assertEqual(decode_printable(b"\xff\x07\x22\x00Hi\x00"), "Hi")
        self.assertEqual(decode_printable(b"A\xff\x02B\x00"), "AB")
        self.assertEqual(decode_printable(b"\xff\x07\x22\x00"), "")

    def test_print_captures_actor_text(self):
        # print actor 7, color 3, position, then "Hi" text, then stop.
        sim = ReferenceSim({1: bytes([0x14, 7, 0x01, 3, 0x00,
                                      0xA0, 0, 0xA0, 0, 0x0F,
                                      ord("H"), ord("i"), 0, 0x00])})
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.prints, [(7, "Hi")])

    def test_print_tracks_overhead_position_and_color(self):
        # Same script: color 3 plus an explicit position is not overhead.
        sim = ReferenceSim({1: bytes([0x14, 7, 0x01, 3, 0x00,
                                      0xA0, 0, 0xA0, 0, 0x0F,
                                      ord("H"), ord("i"), 0, 0x00])})
        sim.start(1, [0])
        sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.prints, [(7, "Hi")])
        self.assertEqual(sim.print_overhead, 0)
        self.assertEqual((sim.print_x, sim.print_y), (160, 160))
        self.assertEqual(sim.print_color, 3)
        self.assertEqual(sim.talk_actor(), 7)
        self.assertEqual(sim.talk_pos(7), (160, 160))
        self.assertEqual(sim.talk_color_index(7), 3)
        for _ in range(100):
            sim.tick(4)
        self.assertEqual(sim.talk_actor(), -1)
        self.assertEqual(sim.globals[3], 0)

    def test_costume_anim_steps_once_per_tick(self):
        sim = ReferenceSim({})
        sim.current_room = 38
        sim.actors[3].update(room=38, costume=48, animSpeed=0)
        for _ in range(3):
            sim.tick(4)
        self.assertEqual(sim.actor_anim_step(3), 3)
        # Speed gates the advance: speed 2 steps every second tick.
        sim.actors[3].update(animProgress=0, animStep=0, animSpeed=2)
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.actor_anim_step(3), 2)
        # Off-room and costumeless actors never step.
        sim.actors[4].update(room=0, costume=48)
        sim.actors[5].update(room=38, costume=0)
        for _ in range(3):
            sim.tick(4)
        self.assertEqual(sim.actor_anim_step(4), 0)
        self.assertEqual(sim.actor_anim_step(5), 0)
        self.assertEqual(sim.actor_anim_step(99), 0)

    def test_camera_pans_eight_px_per_tick_and_scrolls(self):
        sim = ReferenceSim({1: bytes([0x33, 0x03, 0, 0, 0xC8, 0, 0x00])})
        sim.start(1, [0])
        sim.tick(4)
        # SO_ROOM_SCREEN sets vertical bounds, never the 320x200 hardware size.
        self.assertEqual((sim.screen_w, sim.screen_h), (320, 200))
        self.assertEqual((sim.screen_top, sim.screen_bot), (0, 200))
        sim.current_room = 10
        sim.room_width = 640
        sim.globals[27] = 9
        sim.camera_dest_x = 480
        for _ in range(40):
            sim.tick(4)
        self.assertEqual(sim.camera_x, 480)
        self.assertEqual(sim.camera_scroll(), 320)
        self.assertEqual(sim.globals[2], 480)
        sim.camera_dest_x = 160
        for _ in range(40):
            sim.tick(4)
        self.assertEqual(sim.camera_x, 160)
        self.assertEqual(sim.camera_scroll(), 0)
        # Room entry resets the camera; the entry view holds until walking.
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.camera_x = 600
        sim.camera_dest_x = 600
        sim.camera_follow = 5
        sim.start_scene(33)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 33)
        self.assertEqual((sim.camera_x, sim.camera_dest_x), (160, 160))
        for _ in range(10):
            sim.tick(4)
        self.assertEqual(sim.camera_x, 160)
        # Narrow rooms never scroll.
        sim.room_width = 100
        for _ in range(100):
            sim.tick(4)
        self.assertEqual(sim.camera_x, 160)
        self.assertEqual(sim.camera_scroll(), 0)

    def test_load_room_with_ego_enters_at_door_and_walks_in(self):        # loadRoomWithEgo places ego at the object's walk point (room 33's
        # west door), holds the entry view, then walks to the target.
        sim = ReferenceSim({1: bytes([0x24, 0xAA, 0x01, 33, 0x5A, 0x01, 0x85, 0x00, 0x00])})
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.globals[1] = 1
        sim.start(1, [0])
        for _ in range(50):
            sim.tick(4)
            if sim.current_room == 33:
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 33)
        self.assertLessEqual(sim.actors[1]["x"], 15)
        self.assertLessEqual(abs(sim.actors[1]["y"] - 75), 5)
        self.assertEqual(sim.camera_x, 160)
        self.assertEqual(sim.actor_moving(1), 1)
        for _ in range(2000):
            sim.tick(4)
            if sim.fault or (not sim.actor_moving(1) and sim.camera_x == sim.camera_dest_x):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual((sim.actors[1]["x"], sim.actors[1]["y"]), (346, 133))
        # Camera followed east and settled (trailing the walker by design).
        self.assertEqual(sim.camera_x, sim.camera_dest_x)
        self.assertGreater(sim.camera_scroll(), 100)

    def test_camera_follow_engages_on_walk_with_snap(self):
        # 0xD2 far from the actor snaps (like setCameraAt past half a screen).
        sim = ReferenceSim({1: bytes([0xD2, 0x01, 0x00, 0x00])})
        sim.current_room = 10
        sim.room_width = 640
        sim.actors[1].update(room=10, x=500)
        sim.globals[1] = 1
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.camera_x, 480)
        self.assertEqual(sim.camera_dest_x, 480)
        # A near follow holds the entry view until the actor walks.
        sim2 = ReferenceSim({1: bytes([0xD2, 0x01, 0x00, 0x00])})
        sim2.current_room = 10
        sim2.room_width = 640
        sim2.actors[5].update(room=10, x=200)
        sim2.globals[1] = 5
        sim2.start(1, [0])
        for _ in range(5):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual(sim2.camera_x, 160)
        # Walking takes the destination along; idling holds it.
        sim2.start_actor_walk(5, 500, 100, -1)
        for _ in range(20):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertGreater(sim2.camera_x, 160)
        # Destination tracks the walker within one tick of lag.
        self.assertLessEqual(abs(sim2.camera_dest_x - sim2.actors[5]["x"]), 8)
        sim2.actors[5].update(walkCount=0, walkIndex=0, path=[])
        dest = sim2.camera_dest_x
        sim2.tick(4)
        self.assertEqual(sim2.camera_dest_x, dest)

    def test_print_slot_state_persists_across_prints(self):
        # First print arms overhead; the bare follow-up inherits it.
        sim = ReferenceSim({1: bytes([0x14, 7, 0x07, 0x0F,
                                      ord("H"), ord("i"), 0,
                                      0x14, 7, 0x0F,
                                      ord("Y"), ord("o"), 0, 0x00])})
        sim.actors[7].update(x=100, y=120, elevation=0)
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.prints, [(7, "Hi"), (7, "Yo")])
        self.assertEqual(sim.print_overhead, 1)
        self.assertEqual(sim.talk_pos(7), (100, 40))
        # Screen clamps keep the line on the 320-wide screen.
        sim.actors[7].update(x=10, y=10)
        self.assertEqual(sim.talk_pos(7), (80, 1))
        sim.actors[7].update(x=300, y=120)
        self.assertEqual(sim.talk_pos(7), (240, 40))
        # Fixed talk-string Y replaces the head offset.
        sim.globals[54] = 100
        sim.actors[7].update(x=100, y=120)
        self.assertEqual(sim.talk_pos(7), (100, 100))

    def test_positioned_print_state_carries_to_bare_prints(self):
        # Explicit AT + color print, then a bare one inherits everything.
        sim = ReferenceSim({1: bytes([0x14, 7, 0x01, 3, 0x00,
                                      0xA0, 0, 0xA0, 0, 0x0F,
                                      ord("H"), ord("i"), 0,
                                      0x14, 7, 0x0F,
                                      ord("Y"), ord("o"), 0, 0x00])})
        sim.start(1, [0])
        sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.prints, [(7, "Hi"), (7, "Yo")])
        self.assertEqual(sim.print_overhead, 0)
        self.assertEqual((sim.print_x, sim.print_y), (160, 160))
        self.assertEqual(sim.print_color, 3)
        self.assertEqual(sim.talk_pos(7), (160, 160))

    def test_slot_defaults_start_cleared(self):
        # A bare first print has no arming to inherit: fixed at (0, 0).
        sim = ReferenceSim({1: bytes([0x14, 7, 0x0F,
                                      ord("H"), ord("i"), 0, 0x00])})
        sim.start(1, [0])
        sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.print_overhead, 0)
        self.assertEqual((sim.print_x, sim.print_y), (0, 0))

    def test_sentence_runs_sentence_script(self):
        # doSentence verb 5, objA 10 -> sentence script copies local0 to v100.
        sim = ReferenceSim({
            1: bytes([0x19, 5, 10, 0, 0, 0, 0x00]),
            2: bytes([0x9A, 100, 0, 0x00, 0x40, 0x00]),
        })
        sim.globals[33] = 2
        sim.start(1, [0])
        for _ in range(10):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 5)

    def test_chain_script_replaces_caller_with_args(self):
        # A starts B; B chains to C with arg 7. C stores local0 to v90,
        # B's trailing write must never run, A's trailing write must.
        sim = ReferenceSim({
            1: bytes([0x0A, 0x02, 0xFF,
                      0x1A, 91, 0, 1, 0, 0x00]),
            2: bytes([0x42, 0x03, 0x00, 0x07, 0x00, 0xFF,
                      0x1A, 92, 0, 1, 0, 0x00]),
            3: bytes([0x9A, 90, 0, 0x00, 0x40, 0x00]),
        })
        sim.start(1, [0])
        for _ in range(20):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[90], 7)
        self.assertEqual(sim.globals[91], 1)
        self.assertEqual(sim.globals[92], 0)

    def test_chain_to_zero_kills_caller(self):
        sim = ReferenceSim({1: bytes([0x42, 0x00, 0xFF, 0x00])})
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.slots, {})

    def test_verb_entrypoint_inside_obcd(self):
        # VERB is a sub-chunk inside OBCD: the scan must not hop to neighbors.
        import struct
        verb = b"VERB" + struct.pack(">I", 12) + bytes([11, 45, 0, 0])
        room = b"OBCD" + struct.pack(">I", 8 + len(verb)) + verb
        room += b"OBCD" + struct.pack(">I", 8)  # neighbor, no VERB
        sim = ReferenceSim({1: bytes([0x0B, 100, 0, 50, 0, 11, 0, 0x00])})
        sim.room_bytes = room
        sim.obj_slots = {1: {"id": 50, "obcd": 0}}
        sim.object_owner[50] = 15
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 8 + 45)

    def test_get_actor_walkbox(self):
        sim = ReferenceSim({1: bytes([0x7B, 100, 0, 1, 0x00])})
        sim.walkboxes = [
            {"pts": (0, 0, 0, 0, 0, 0, 0, 0), "flags": 0},
            {"pts": (0, 0, 100, 0, 100, 100, 0, 100), "flags": 0},
            {"pts": (200, 200, 300, 200, 300, 300, 200, 300), "flags": 0},
        ]
        sim.current_room = 10
        sim.actors[1].update(room=10, x=50, y=50)
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 1)

    def test_get_dist_chebyshev_and_unresolvable(self):
        sim = ReferenceSim({1: bytes([0x34, 100, 0, 1, 0, 2, 0, 0x00])})
        sim.current_room = 10
        sim.actors[1].update(room=10, x=100, y=100)
        sim.actors[2].update(room=10, x=140, y=115)
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 40)

    def test_get_string_width_counts_printables(self):
        sim = ReferenceSim({1: bytes([0x67, 100, 0, 5, 0x00])})
        sim.strings = {5: b"Hi!\x00"}
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 24)

    def test_matrix_ops_flags_scale_and_rebuild(self):
        sim = ReferenceSim({1: bytes([
            0x30, 0x01, 0x01, 0x80,
            0x30, 0x02, 0x00, 0x90,
            0x30, 0x03, 0x00, 0x05,
            0x30, 0x04, 0x00])})
        sim.walkboxes = [
            {"pts": (0, 0, 100, 0, 100, 100, 0, 100), "flags": 0, "mask": 0, "scale": 255},
            {"pts": (200, 200, 300, 200, 300, 300, 200, 300), "flags": 0, "mask": 0, "scale": 255},
        ]
        sim.start(1, [0])
        for _ in range(6):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.walkboxes[1]["flags"], 0x80)
        self.assertEqual(sim.walkboxes[0]["scale"], 0x8004)
        # Rebuilt matrix keeps the two isolated boxes disconnected.
        self.assertEqual(sim._next_walkbox(0, 1), -1)
        self.assertEqual(sim._next_walkbox(1, 0), -1)

    def test_put_actor_at_object_and_fallback(self):
        sim = ReferenceSim({1: bytes([0x0E, 1, 50, 0, 0x00])})
        sim.current_room = 10
        sim.obj_slots = {1: {"id": 50, "walkX": 200, "walkY": 100}}
        sim.object_owner[50] = 15
        sim.start(1, [0])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual((sim.actors[1]["x"], sim.actors[1]["y"]), (200, 100))
        sim2 = ReferenceSim({1: bytes([0x0E, 1, 0xB2, 0x01, 0x00])})
        sim2.current_room = 10
        sim2.start(1, [0])
        for _ in range(5):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual((sim2.actors[1]["x"], sim2.actors[1]["y"]), (240, 120))

    def test_set_state_second_operand_mask(self):
        # 0x87: var object, DIRECT state byte (room-35 script 209 case).
        sim = ReferenceSim({1: bytes([0x87, 0x01, 0x40, 7, 0x00])})
        sim.start(1, [60, 60])
        for _ in range(5):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.object_state[60], 7)
        # 0x47: direct object, VAR state byte (mask 0x40, not 0x80).
        sim2 = ReferenceSim({1: bytes([0x47, 0xB2, 0x01, 0x05, 0x40, 0x00])})
        sim2.start(1, [0])
        sim2.slots[1]["locals"][5] = 3
        for _ in range(5):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual(sim2.object_state[0x1B2], 3)

    def test_pickup_object_moves_to_inventory(self):
        sim = ReferenceSim({
            1: bytes([0x25, 109, 0, 0, 0x00]),
            4: bytes([0x9A, 100, 0, 0x00, 0x40, 0x00]),
        })
        sim.globals[1] = 1
        sim.globals[34] = 4
        sim.object_owner[109] = 15
        sim.obj_slots = {1: {"id": 109, "x": 0, "y": 0, "w": 8, "h": 8,
                             "walkX": 0, "walkY": 0, "obcd": 0, "obim": 0}}
        sim.start(1, [0])
        for _ in range(10):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.object_owner[109], 1)
        self.assertEqual(sim.object_state[109], 1)
        self.assertIn(109, sim.inventory)
        self.assertNotIn(109, [o["id"] for o in sim.obj_slots.values()])
        self.assertEqual(sim.globals[100], 1)

    def test_wait_message_yields_until_talked_out(self):
        sim = ReferenceSim({1: bytes([0xAE, 0x02, 0x00])})
        sim.globals[3] = 255
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.slots[1]["pc"], 0)
        sim.globals[3] = 0
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertNotIn(1, sim.slots)

    def test_wait_sentence_yields_while_pending(self):
        sim = ReferenceSim({
            1: bytes([0xAE, 0x04, 0x00]),
            2: bytes([0x1A, 100, 0, 7, 0, 0x00]),
        })
        sim.globals[33] = 2
        sim.start(1, [0])
        sim.sentence = (1, 2, 0)
        sim.sentence_num = 1
        for _ in range(8):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 7)
        self.assertEqual(sim.slots, {})

    def test_walk_actor_to_object_uses_walk_box_point(self):
        sim = ReferenceSim({1: bytes([0x36, 1, 109, 0, 0x00])})
        sim.obj_slots = {1: {"id": 109, "x": 0, "y": 0, "w": 8, "h": 8,
                             "walkX": 50, "walkY": 60, "actorDir": 0, "obcd": 0, "obim": 0}}
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.actor_moving(1), 1)
        arrived = (sim.actors[1]["x"], sim.actors[1]["y"])
        self.assertNotEqual(arrived, (0, 0))
        self.assertNotEqual(arrived, (50, 60))
        for _ in range(500):
            if not sim.actor_moving(1):
                break
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.actor_moving(1), 0)
        self.assertEqual((sim.actors[1]["x"], sim.actors[1]["y"]), (50, 60))
        self.assertEqual(sim.actors[1]["facing"], 270)

    def test_face_actor_turns_toward_object(self):
        sim = ReferenceSim({1: bytes([0x09, 1, 109, 0, 0x00])})
        sim.obj_slots = {1: {"id": 109, "x": 100, "y": 0, "w": 8, "h": 8,
                             "walkX": 0, "walkY": 0, "obcd": 0, "obim": 0}}
        sim.actors[1]["x"] = 0
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.actors[1]["facing"], 90)
        # Missing object is a silent no-op.
        sim2 = ReferenceSim({1: bytes([0x09, 1, 110, 0, 0x00])})
        sim2.start(1, [0])
        for _ in range(4):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")

    def test_inventory_count_counts_owner_items(self):
        sim = ReferenceSim({1: bytes([0xB1, 100, 0, 1, 0, 0x00])})
        sim.globals[1] = 1
        sim.object_owner[109] = 1
        sim.object_owner[110] = 2
        sim.inventory = [109, 110] + [0] * 78
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 1)

    def test_find_inventory_returns_nth_owned_item(self):
        sim = ReferenceSim({1: bytes([0xFD, 100, 0, 1, 0, 2, 0, 0x00])})
        sim.globals[1] = 1
        sim.globals[2] = 2
        sim.object_owner[109] = 1
        sim.object_owner[110] = 1
        sim.object_owner[111] = 2
        sim.inventory = [109, 110, 111] + [0] * 77
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 110)

    def test_find_verb_at_hits_topmost_text_verb(self):
        sim = ReferenceSim({1: bytes([0x00])})
        sim.verbs = {
            1: {"id": 4, "mode": 1, "save": 0, "type": 0, "left": 22, "top": 155,
                "center": 1, "name": b"Give"},
            2: {"id": 9, "mode": 1, "save": 0, "type": 0, "left": 72, "top": 155,
                "center": 1, "name": b"Pick up"},
            3: {"id": 7, "mode": 0, "save": 0, "type": 0, "left": 124, "top": 155,
                "center": 1, "name": b"Use"},
        }
        self.assertEqual(sim.find_verb_at(30, 160), 1)
        self.assertEqual(sim.find_verb_at(60, 160), 2)
        self.assertEqual(sim.find_verb_at(200, 160), 0)
        self.assertEqual(sim.find_verb_at(130, 160), 0)

    def test_if_class_of_is_matches_scummvm_polarity(self):
        # ScummVM jumps when the class MATCHES: plain class jumps when
        # present, 0x80 class jumps when absent.
        prog = bytes([0x9D, 10, 0, 0x01, 0x06, 0x00, 0xFF, 0x08, 0x00,
                      0x1A, 100, 0, 7, 0, 0x18, 0x05, 0x00,
                      0x1A, 100, 0, 9, 0, 0x00])
        sim = ReferenceSim({1: prog})
        sim.globals[10] = 10
        sim.object_class[10] |= 0x20
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[100], 9)
        sim2 = ReferenceSim({1: prog})
        sim2.globals[10] = 10
        sim2.start(1, [0])
        for _ in range(4):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual(sim2.globals[100], 7)

    def test_click_aborts_armed_override_and_restores_hover(self):
        # Cutscene begin + armed override, then stop. A click must abort
        # (resume the skipped jump, set VAR_OVERRIDE) instead of running
        # the verb script, and relaunch the missing hover tracker.
        prog = bytes([0x40, 0xFF, 0x58, 0x01, 0x18, 0x00, 0x00,
                      0x1A, 100, 0, 7, 0, 0x00])
        sim = ReferenceSim({1: prog, 4: bytes([0x1A, 101, 0, 42, 0, 0x00]), 23: bytes([0x2E, 0xFF, 0xFF, 0x7F, 0x00])})
        sim.globals[35] = 0
        sim.globals[32] = 4
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertTrue(sim.cut_stack and sim.cut_stack[-1].get("slot", -1) >= 0)
        # kill the suspended slot like a finished cutscene script would be gone
        for key, value in list(sim.slots.items()):
            if value["script"] == 1:
                del sim.slots[key]
        r = sim.run_input_script(2, 0, 1)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[5], 1)
        self.assertEqual(sim.globals[101], 0)
        self.assertTrue(any(v["script"] == 23 for v in sim.slots.values()))
        for _ in range(10):
            sim.tick(4)
        self.assertEqual(sim.fault, "")

    def test_endcutscene_restores_verb_script_and_hover(self):
        sim = ReferenceSim({1: bytes([0xC0, 0x00]), 23: bytes([0x2E, 0xFF, 0xFF, 0x7F, 0x00])})
        sim.globals[32] = 14
        sim.globals[36] = 0
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[32], 4)
        self.assertTrue(any(v["script"] == 23 for v in sim.slots.values()))

    def test_room32_exit_unstuck_by_abort_then_click(self):
        # Room 32's entry spiel locks input and stops the hover tracker;
        # a click aborts the cutscene, hover resumes, and the street exit
        # then fires its sentence and transitions out.
        from trace_boot import load_scripts
        sim = ReferenceSim(load_scripts(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/scripts"))
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.start(1, [0])
        for _ in range(1500):
            sim.tick(4)
        sim.start_scene(38)
        for _ in range(200):
            sim.tick(4)
        sim.actors[1].update(room=38, x=154, y=106, visible=True)
        for leg, room in [((11, 486, 0), 33), ((11, 427, 0), 35),
                          ((11, 451, 0), 34), ((11, 432, 0), 32)]:
            sim.sentence = leg
            sim.sentence_num = 1
            for _ in range(15000):
                sim.tick(4)
                if sim.fault or sim.current_room == room:
                    break
            for _ in range(10000):
                sim.tick(4)
                if sim.fault or sim.current_room != room:
                    break
                if sim.globals[53] != 0 and not sim.actor_moving(1):
                    break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 32)
        self.assertEqual(sim.globals[53], 0)
        sim.globals[20] = 200
        sim.globals[21] = 100
        sim.globals[44] = 200
        sim.globals[45] = 100
        sim.run_input_script(2, 0, 1)
        for _ in range(20000):
            sim.tick(4)
            if sim.fault or sim.globals[53] != 0:
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[53], 1)
        self.assertTrue(any(v["script"] == 23 for v in sim.slots.values()))
        sim.globals[20] = 36
        sim.globals[21] = 64
        sim.globals[44] = 36
        sim.globals[45] = 64
        for _ in range(5000):
            sim.tick(4)
            if sim.fault or sim.globals[108] != 0:
                break
        self.assertNotEqual(sim.globals[108], 0)
        sim.run_input_script(2, 0, 1)
        for _ in range(40000):
            sim.tick(4)
            if sim.fault or sim.current_room != 32:
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 34)

    def test_pan_camera_to_sets_dest_and_drops_follow(self):
        sim = ReferenceSim({1: bytes([0x12, 0x00, 0x01, 0x00])})
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.camera_dest_x, 256)
        self.assertEqual(sim.__dict__.get("camera_follow", 0), 0)
        sim2 = ReferenceSim({1: bytes([0x92, 100, 0, 0x00])})
        sim2.globals[100] = 480
        sim2.start(1, [0])
        for _ in range(4):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual(sim2.camera_dest_x, 480)

    def test_object_tables_states_and_classes(self):
        sim = ReferenceSim({1: bytes([0x00])})
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        self.assertEqual(sim.object_state[730], 1)
        self.assertEqual(sim.object_state[428], 0)
        self.assertEqual(sim.object_class[442], 0x20)
        self.assertEqual(sim.object_class[285], 0x4020)

    def test_if_class_of_is_allows_object_zero(self):
        # Room-28 hover runs ifClassOfIs(local0, ...) with local0 == 0 when
        # the cursor is over no object (verb-button hover). Object 0 is
        # valid (ScummVM getClass allows 0); the VM must consume the full
        # instruction and branch, not stop mid-stream and misdecode the
        # class byte as the next opcode (Invalid actor 141 at 23:168).
        prog = bytes([0x1D, 0, 0, 0x01, 0x06, 0x00, 0xFF, 0x08, 0x00,
                      0x1A, 100, 0, 7, 0, 0x18, 0x05, 0x00,
                      0x1A, 100, 0, 9, 0, 0x00])
        sim = ReferenceSim({1: prog})
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        # class 6 absent on object 0: plain check must fall through to 7
        self.assertEqual(sim.globals[100], 7)
        sim2 = ReferenceSim({1: prog})
        sim2.object_class[0] |= 0x20
        sim2.start(1, [0])
        for _ in range(4):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual(sim2.globals[100], 9)

    def test_lights_default_on_and_opcode_sets_var9(self):
        sim = ReferenceSim({1: bytes([0x70, 0x05, 0x00, 0x00, 0x00])})
        self.assertEqual(sim.globals[9], 11)
        sim.start(1, [0])
        for _ in range(4):
            sim.tick(4)
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[9], 5)
        # flashlight form (c==1) consumes bytes without touching lights
        sim2 = ReferenceSim({1: bytes([0xF0, 100, 0, 0x02, 0x01, 0x00])})
        sim2.globals[100] = 7
        sim2.start(1, [0])
        for _ in range(4):
            sim2.tick(4)
        self.assertEqual(sim2.fault, "")
        self.assertEqual(sim2.globals[9], 11)

    def test_walk_west_faces_west_and_arrives(self):
        # Guybrush walking west must face 270 while moving and arrive.
        from trace_boot import load_scripts
        sim = ReferenceSim(load_scripts(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/scripts"))
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.start(1, [0])
        for _ in range(1500):
            sim.tick(4)
        sim.start_scene(38)
        for _ in range(200):
            sim.tick(4)
        sim.actors[1].update(room=38, x=154, y=106, visible=True)
        sim.sentence = (11, 486, 0)
        sim.sentence_num = 1
        for _ in range(15000):
            sim.tick(4)
            if sim.fault or sim.current_room == 33:
                break
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 33)
        start_x = sim.actors[1]["x"]
        sim.start_actor_walk(1, 100, 130, -1)
        for _ in range(500):
            sim.tick(4)
            if sim.fault or (sim.actor_moving(1) and sim.actors[1]["x"] < start_x):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.actor_moving(1), 1)
        self.assertLess(sim.actors[1]["x"], start_x)
        self.assertEqual(sim.actors[1]["facing"], 270)
        for _ in range(10000):
            sim.tick(4)
            if sim.fault or not sim.actor_moving(1):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.actor_moving(1), 0)
        self.assertLess(sim.actors[1]["x"], start_x)

    def test_look_poster_reads_reelect_with_lights_on(self):
        from trace_boot import load_scripts
        sim = ReferenceSim(load_scripts(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/scripts"))
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.start(1, [0])
        for _ in range(1500):
            sim.tick(4)
        sim.start_scene(38)
        for _ in range(200):
            sim.tick(4)
        sim.actors[1].update(room=38, x=154, y=106, visible=True)
        sim.sentence = (11, 486, 0)
        sim.sentence_num = 1
        for _ in range(15000):
            sim.tick(4)
            if sim.fault or sim.current_room == 33:
                break
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.globals[9], 11)
        sim.sentence = (8, 429, 0)
        sim.sentence_num = 1
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
        self.assertEqual(sim.fault, "")
        texts = [t for _, t in sim.prints[-6:]]
        self.assertTrue(any("Re-elect Governor Marley" in t for t in texts))
        self.assertTrue(any("only one candidate" in t for t in texts))

    def test_talk_pirates_choice_advances_dialogue(self):
        # Talk to the important-looking pirates opens a choice menu (verbs
        # 120-122); picking "I want to be a pirate." (122) must advance the
        # trials dialogue instead of stalling. The game is waiting, not frozen.
        from trace_boot import load_scripts
        sim = ReferenceSim(load_scripts(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/scripts"))
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.start(1, [0])
        for _ in range(1500):
            sim.tick(4)
        sim.start_scene(38)
        for _ in range(200):
            sim.tick(4)
        sim.actors[1].update(room=38, x=154, y=106, visible=True)
        sim.sentence = (11, 486, 0)
        sim.sentence_num = 1
        for _ in range(15000):
            sim.tick(4)
            if sim.fault or sim.current_room == 33:
                break
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        sim.sentence = (2, 428, 0)
        sim.sentence_num = 1
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
        sim.sentence = (11, 428, 0)
        sim.sentence_num = 1
        for _ in range(15000):
            sim.tick(4)
            if sim.fault or sim.current_room == 28:
                break
        for _ in range(10000):
            sim.tick(4)
            if sim.fault or sim.current_room != 28:
                break
            if sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 28)
        sim.sentence = (10, 322, 0)
        sim.sentence_num = 1
        for _ in range(10000):
            sim.tick(4)
            if sim.fault:
                break
            if any("wantin" in t for _, t in sim.prints[-2:]):
                break
        self.assertEqual(sim.fault, "")
        # choices must be offered as clickable text verbs (poll: they arm
        # shortly after the opening line). Click immediately like a player
        # would; retry over the choice window if the first click lands early.
        n0 = len(sim.prints)
        advanced = False
        for _ in range(6):
            ids = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and v.get("id") in (120, 121, 122))
            # click pirate choice each round; harmless if menu not yet armed
            sim.run_input_script(1, 122, 1)
            for _ in range(3000):
                sim.tick(4)
                if sim.fault:
                    break
                if len(sim.prints) > n0 + 1:
                    advanced = True
                    break
            if advanced or sim.fault:
                break
        self.assertEqual(sim.fault, "")
        self.assertTrue(advanced)

    def test_room81_dialogue_choices_advance_and_exit(self):
        # Talk to the bar pirate (336) loads the close-up (81) with a choice
        # menu (introduce/dart/drink/bye). Clicking choices must advance the
        # dialogue (via g194) instead of fizzling, and Bye must return to 28.
        from trace_boot import load_scripts
        sim = ReferenceSim(load_scripts(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/scripts"))
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.start(1, [0])
        for _ in range(1500):
            sim.tick(4)
        sim.start_scene(38)
        for _ in range(200):
            sim.tick(4)
        sim.actors[1].update(room=38, x=154, y=106, visible=True)
        sim.sentence = (11, 486, 0)
        sim.sentence_num = 1
        for _ in range(12000):
            sim.tick(4)
            if sim.fault or sim.current_room == 33:
                break
        for _ in range(30000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        sim.sentence = (2, 428, 0)
        sim.sentence_num = 1
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
        sim.sentence = (11, 428, 0)
        sim.sentence_num = 1
        for _ in range(15000):
            sim.tick(4)
            if sim.fault or sim.current_room != 33:
                break
        for _ in range(20000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.current_room == 28 and sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 28)
        sim.sentence = (10, 336, 0)
        sim.sentence_num = 1
        for _ in range(20000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.current_room == 81 and any("looking at me" in t for _, t in sim.prints[-3:]):
                break
        self.assertEqual(sim.fault, "")
        self.assertEqual(sim.current_room, 81)
        # Play through the menu until Bye returns to the bar. Click whatever
        # choice is offered (verb ids 120+); prefer Bye (125) once seen.
        exited = False
        for rnd in range(12):
            ids = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and v.get("id") >= 120)
            if not ids:
                for _ in range(2000):
                    sim.tick(4)
                    if sim.fault:
                        break
                continue
            choice = 125 if (rnd >= 4 and 125 in ids) else ids[0]
            sim.run_input_script(1, choice, 1)
            for _ in range(8000):
                sim.tick(4)
                if sim.fault or sim.current_room != 81:
                    break
            if sim.fault or sim.current_room != 81:
                exited = sim.current_room != 81
                break
        self.assertEqual(sim.fault, "")
        self.assertTrue(exited, "Bye should return to room 28")
        self.assertEqual(sim.current_room, 28)
        # Returning to the bar must clear the close-up choices; otherwise
        # 124/125 stay mode 1 over the standard verbs (overlap).
        leftover = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and 120 <= v.get("id", 0) < 200)
        self.assertEqual(leftover, [], f"Dialogue choices must clear on room change, got {leftover}")

    def test_leaving_chat_hides_dialogue_choices(self):
        # Room 28 trials arms 120-122; walking away (scene Walk-to) must hide
        # them so they don't linger over the bottom bar. Choice clicks must
        # keep the others for the next menu (scripts hide the selected one).
        from trace_boot import load_scripts
        sim = ReferenceSim(load_scripts(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/scripts"))
        sim.load_tables(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes")
        sim.load_room_blobs(Path(__file__).resolve().parent.parent / "Assets/Scumm/Generated/RoomBlobs")
        sim.start(1, [0])
        for _ in range(1500):
            sim.tick(4)
        sim.start_scene(38)
        for _ in range(200):
            sim.tick(4)
        sim.actors[1].update(room=38, x=154, y=106, visible=True)
        sim.sentence = (11, 486, 0)
        sim.sentence_num = 1
        for _ in range(12000):
            sim.tick(4)
            if sim.fault or sim.current_room == 33:
                break
        for _ in range(30000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        sim.sentence = (2, 428, 0)
        sim.sentence_num = 1
        for _ in range(8000):
            sim.tick(4)
            if sim.fault:
                break
        sim.sentence = (11, 428, 0)
        sim.sentence_num = 1
        for _ in range(15000):
            sim.tick(4)
            if sim.fault or sim.current_room != 33:
                break
        for _ in range(20000):
            sim.tick(4)
            if sim.fault:
                break
            if sim.current_room == 28 and sim.globals[53] != 0 and not sim.actor_moving(1):
                break
        self.assertEqual(sim.current_room, 28)
        sim.sentence = (10, 322, 0)
        sim.sentence_num = 1
        for _ in range(10000):
            sim.tick(4)
            if sim.fault:
                break
            if any("wantin" in t for _, t in sim.prints[-2:]):
                break
        for _ in range(3000):
            sim.tick(4)
            if sim.fault:
                break
        ids = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and 120 <= v.get("id", 0) < 200)
        # Trials menu arms 120-122 plus 126-128 (same row); assert the core
        # three are present without over-fitting the full set.
        self.assertTrue(set([120, 121, 122]).issubset(set(ids)))
        # Choice click keeps the menu (for the next question).
        sim.run_input_script(1, 122, 1)
        for _ in range(5000):
            sim.tick(4)
            if sim.fault:
                break
        self.assertEqual(sim.fault, "")
        ids = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and 120 <= v.get("id", 0) < 200)
        self.assertTrue(set([120, 121, 122]).issubset(set(ids)))
        # Walking away (scene click) ends chat and must clear choices,
        # leaving only the standard bottom bar.
        sim.run_input_script(2, 0, 1)
        for _ in range(3000):
            sim.tick(4)
            if sim.fault:
                break
        self.assertEqual(sim.fault, "")
        leftover = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and 120 <= v.get("id", 0) < 200)
        self.assertEqual(leftover, [])
        standard = sorted(v["id"] for v in sim.verbs.values() if v.get("mode") == 1 and v.get("id", 0) < 120)
        self.assertTrue(set([2, 3, 4, 5, 6, 7, 8, 9, 10]).issubset(set(standard)))

    def test_all_fixtures_meet_expectations(self):
        programs = {}
        for fixture in FIXTURES:
            programs[fixture["entry"]] = fixture["build"]()
            for script_id, build in fixture.get("extra", {}).items():
                programs[int(script_id)] = build()
        for fixture in FIXTURES:
            sim = ReferenceSim(programs)
            sim.start(fixture["entry"], [0])
            for _ in range(60):
                sim.tick(4)
                if sim.fault or not sim.slots:
                    break
            spec = next(s for s in json.loads((OUTPUT / "fixtures.json").read_text())["fixtures"]
                        if s["name"] == fixture["name"]) if OUTPUT.exists() else None
            if fixture["expectFault"]:
                self.assertNotEqual(sim.fault, "")
            else:
                self.assertEqual(sim.fault, "", fixture["name"])
                for var, value in zip(fixture["vars"], fixture["values"]):
                    self.assertEqual(sim.globals[var], value, f"{fixture['name']} v{var}")
                for bit, value in zip(fixture["bits"], fixture["bitValues"]):
                    self.assertEqual(1 if sim.bits[bit] else 0, value)
            if spec is not None:
                self.assertEqual(spec["values"], fixture["values"])


if __name__ == "__main__":
    unittest.main()
