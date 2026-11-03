# SCUMM v5 flatscreen prototype

## Current state

The intro executes and background/object rendering has been tested in ClientSim;
Monkey Island is **not yet playable end-to-end**. Natural boot still stalls in
room 0. The Lookout bring-up explicitly enters room 38 for isolated testing.

- Offline importer: validates and extracts v5 room resources, global scripts, room entry/exit scripts, and local scripts.
- UdonSharp VM foundation (`ScummV5Vm`): global/bit/local variables with 0x2000 indirection, 80 script slots, nested calls, chain-script replacement (o5, opcode 66), delays, frame-budgeted scheduler, explicit faults with script/offset/opcode for unsupported operations.
- Driver (`ScummV5Harness`): boot diagnostic mode and fixture-verification mode with PASS/FAIL reporting on a TextMesh.
- Semantic fixtures: variables/indirection/wrap/branches, nesting/delay/arguments, and divide-by-zero fault, cross-checked by an independent Python reference simulator.
- UdonSharp synthetic benchmark: single-script move/add/subtract, global variable operands, equal-zero branch, relative jump, yield, and stop. Unsupported instructions fail with the byte offset.
- Flatscreen diagnostic: 320x200 game coordinates on a 4:3 quad, right-hand ray in VR, head-directed pointer on desktop, and Use input.
- Background preview: decodes the supplied game's Lookout room (38) offline and renders its indexed pixels with a separate palette texture. The background is 320x144; the unused lower 56 screen rows are black.
- Unity editor builder: adds the harness two metres ahead of the world's first spawn point, and selects/frames it in Scene view. Wires VM fixtures and the boot bring-up from generated assets.

Partial engine services cover room loading and actor/object/verb bookkeeping.
Object atlases render the title (user-validated); static costume poses now have
an offline baker and a GPU layer. The user has now confirmed the transition from
the title to a man standing in front of the fire in ClientSim. Missing: complete
game boot, faithful actor movement/animation, masking, speech/music playback,
saving, and multiplayer. This is not yet a representative animated-game benchmark.

### Actor rendering audit and bring-up

From `udonscumm`, regenerate the costume set (full list in "Fresh-clone
setup" above; 1/33/48 were the initial three):

```sh
python3 Tools/bake_actor_sprites.py --costumes 1 14 18 23 24 26 33 42 48 59
```

These are classic v5 COST resources, **not AKOS**. Output includes Guybrush (1),
the lookout (33), and the fire (48). Previous visual claims identifying costume
33 as Guybrush were incorrect. The baker carries RLE runs across columns,
handles 16/32-color streams, includes the final animation-table entry, and keeps
coverage independent of the mapped room-palette index. Truncated and zero-length
RLE runs fail explicitly.

In Edit mode, with the existing screen/background/object setup:

1. Run **SCUMM → Wire All (Prototype + Lookout Play Test)**. One click creates
   the flatscreen prototype if missing, then wires the Lookout bring-up first
   (so every layer binds straight to its VM), followed by Room Backgrounds,
   Verb Display, Object Atlases, Actor Sprites, and Talk Text, with a single
   UdonSharp compile and one summary dialog at the end. Save.
2. The individual **SCUMM → Wire ...** menus remain and behave exactly as
   before for targeted rewiring; Wire All simply runs them in the right order
   without the intermediate dialogs and compiles.
3. Enter ClientSim and watch from the first second: the boot plays the
   Lucasfilm logo (right half of room 10) with a camera sweep, then the
   mountain vista with drifting clouds and rolling credits. Room 38 is
   entered at tick 1060, right as the title releases — no black gap.
   At tick 1260 the explicit walk diagnostic places Guybrush at (312,73)
   and starts his walk to (154,106). Watch without clicking first. Console
   should show `SCUMM lookout-walk diagnostic` with room=38, scale=216,
   moving=1, then `lookout-walk stopped` at position=154,106, scale=241.
   The route takes 33 logical ticks at four jiffies/tick in host tests,
   so watch immediately after scene entry.

The room jump alone leaves Guybrush in room 0. `lookoutWalkTest`, enabled by
Wire Lookout Bring-Up, explicitly places him in room 38 after the title
releases so the movement test exercises the displayed actor — unless you
click first, in which case the diagnostic stands down and logs that you
took control. It does not repair the natural boot transition or recreate
the original entrance sequence.

### On-screen verbs and sentence line

The bottom-row verbs and the sentence line now render as world-space labels
directly on the game screen (`ScummVerbDisplay` drives a 16-label pool built
by **SCUMM → Wire Verb Display**). Visibility mirrors `drawVerb` /
`findVerbAtPos` exactly: text verbs with an id, a name, mode != 0 and no
save-tag; saved-away verbs (the Lookout), image verbs and the empty slots
stay hidden. Colors come through the live room palette (normal / dim for
mode 2 / hi-color on hover), centered verbs offset by half their 8px-per-char
width. Labels sit on a screen-sized canvas in front of the quad, so they
scroll correctly with nothing to update — positions are pure game pixels.
Re-run Wire All (or just Wire Verb Display) to replace the old floating
debug list.

### Playable path: Lookout to town

The game is now walkable from the Lookout to the town. Walk Guybrush down
the cliff path (Walk to the path at the bottom of room 38): the farewell
plays, room 96 shows the baked-in "Part One / The Three Trials" card, the
cliff walk continues through room 33 (a scrolling room — the camera follows
Guybrush), and clicking the far-east object exits to room 35 (town at night)
with the full bottom-row verb bar restored. Verbs are intentionally
unavailable in room 38 itself: the entry scripts save them away (matching
`findVerbAtPos`/`drawVerb`, which skip saved verbs), so Use/Push/Pull
correctly do nothing on the lookout — they come back in rooms 33/35.
Fixes that unblocked the path: object-script entry points resolve the VERB
sub-chunk *inside* each OBCD (bounded sibling walk, like `findResource`),
plus `o5_chainScript`, `o5_getActorWalkBox`, `o5_getDist` (Chebyshev, 0xFF
when unresolvable), `o5_putActorAtObject` (walk point or 240,120 fallback),
and the `0x40` second-operand mask on `o5_setState`/`o5_setOwnerOf`
(room-35 script 209 wrote through a local with a direct state byte).
The actor layer selects stand/talk poses
from the VM's own walk/stand frame numbers (walk pose while the VM reports the
actor moving), or falls back to the stand pose; it does not cycle animation
cels. Mirroring and cel cycling remain follow-ups. Missing target animations can retain the init pose; baked pose
counts are not evidence of distinct animation support.

The SCUMM Bar door (room 33, object 428) now opens end to end in headless
sims: Open runs the door's verb-2 entry into shared script 25, which sets
state 1 silently, and a following Walk-to checks state==1 and loads room 28
with ego placed at the passage walk point. Two engine bugs had hidden this:
`o5_ifClassOfIs` (0x1D/0x9D) had inverted polarity versus ScummVM (it jumped
when the class was absent instead of present), so every locked-door check
decided backwards; and `o5_panCameraTo` (0x12/0x92) was unimplemented, which
faulted room 28's entry script on its first camera pan. Object-table parsing
was also fixed on both VMs (states live in their own array, not in the owner
nibbles; C# additionally read class words with strided instead of LE32
offsets). Only object 730 has nonzero state in the data. With the door open,
room 28 boots clean (userput 1, verbs live) and its ambient door script runs
without fault. Room 28's live set is 14 state-1 objects (bar frames/patrons)
plus Guybrush (costume 1) and the pirates (actor 3, costume 24); the object
layer now composites up to 16 (was 8, which clipped 5–6 of them) and actor
costumes 14/18/23/24/26/42 are baked alongside 1/33/48/59. Object atlases are
re-baked so every room has `table.txt` (previously only room 10 did, so Wire
Object Atlases silently wired room 10 alone and rooms 28/33 showed no sprites
— including the open-door sprite for 428). After re-running **SCUMM → Wire
Object Atlases** and **Wire Actor Sprites**, Open on the town door draws the
open-door sprite at its rect and the bar shows pirates plus cycling animated
objects. Talk-to the lookout, town NPC chatter, and the town exit chain
(33 archway <-> 35 <-> 34 square <-> 32 circus street / 36 mansion) still
verify clean after the fix.
Lights (`o5_lights` 0x70/0xF0) are now implemented and `VAR_CURRENT_LIGHTS`
(9) resets to 11 (base palette + colors + room lights, matching ScummVM);
previously it stuck at 0 so sentence script 2's Look gate always printed
"It's too dark to see it." Look at the town poster (33/429) now reaches its
verb code and prints "Re-elect Governor Marley." + "When there's only one
candidate, there's only one choice."
Talk-to the important-looking pirates (28/322) is not a freeze: it walks
Guybrush over, plays "What be ye wantin', boy?", then waits on dialogue
choices (verbs 120-122: fireman / kill-you-all / pirate). Click a choice line
and the trials dialogue advances (headless sims go on to GROG etc. with no
fault). If it looks stuck, look for the three choice labels, not the bottom
verb bar.
Two follow-up engine bugs hid behind that wait and are now fixed (headless
C# repro confirmed choices appear post-fix): `slotCutscene` was never cleared
on slot reuse/kill, so stopping the hover tracker after a cutscene-carrying
slot got recycled faulted "stopped with active cutscene/override" and froze
the talk; and the actor layer let the ambient costume chore (ego's stuck
`anim` 5) override the walk cycle, so legs never moved — ambient is now a
fallback when no walk/stand/talk pose exists, and west walks face 270 with
the baked walk seq cycling.
A third talk-path freeze is also fixed: clicking Talk then leaving the
cursor over the verb buttons (no object under the cursor) ran
`o5_ifClassOfIs(0, ...)` in the hover tracker (global script 23) with
object 0. Object 0 is valid (ScummVM `getClass` allows 0), but
`ValidObject` returned the id itself, so `== 0` looked like failure and
the handler returned mid-instruction without consuming its mode/FF/jump
bytes. The next fetch decoded the class byte as opcode 1 (`o5_putActor`
with act 141) and stopped with `Invalid actor 141` at `23:168`. `ValidObject`
now returns 1/0 like `ValidActor`, and both simulators cover
`ifClassOfIs(0)` present/absent branching without fault.
Room-81's close-up dialogue (Talk to 28/336 → 81, global 92: introduce /
dart-board / drink / bye) stalled with clicks dispatching to verb script 4
and fizzling while script 92 waited on g194 at 1537. Three input-path fixes
unblock it headlessly end-to-end (choices advance, Bye returns to 28, no
fault): verb clicks (area 1) no longer abort an armed cutscene override
(scene clicks still skip, room-32), the 14→4 heal only runs with no armed
override (dialogue legitimately uses 14 with input on), and choice clicks
(120+) report via g194 alongside the normal dispatch (cutscene verb 14
already did; normal verb 4 does not).
Leaving 81 hid Guybrush: Bye does putActorInRoom(ego,28) then
putActor(ego,201,129) before loadRoom(28). `o5_putActor` keeps `_room`
(`putActor(x,y)` → `putActor(x,y,_room)`), so the second call must not snap
ego back to 81. Both VMs now leave room untouched on putActor/putActorAtObject
(boot pre-places ego in 38 via putActorInRoom at script 1:5859; forced entry
no longer moves it).
Bye left 124/125 mode 1 over the bar verbs in 28 (overlap): the exit path
has no VerbOps hide, so both VMs now hide verbs 120-199 on every room change
(InstallRoom/start_scene, including room 0), before entry scripts re-arm.

### Foreground masks

`ZP01`–`ZP03` chunks decode offline with the ScummVM RLE (`decompressMaskImg`:
high-bit repeats the next byte, otherwise literal bytes), one byte per scanline
per 8px strip, MSB-first across the strip, packed R/G/B into `masks.png`. The VM
exposes per-actor clip planes mirroring `prepareDrawActorCostume`: nonzero
`forceClip` wins, `NeverClip` class and ignore-boxes suppress, otherwise the
walkbox mask, clamped in-shader to the room's available planes. The shader
samples the mask at the actor pixel and skips the sprite where covered, so
actors pass behind scenery instead of always drawing on top. Guybrush and the
old man share the same path. All 86 rooms have backgrounds and masks baked;
rooms without masks decode to empty planes. Host coverage includes literal and
repeated runs, offsets, bit ordering, empty strips, malformed data, the actual
room-38 mask (plane 1, 6123 covered pixels), and clip-plane precedence in both
simulators. ClientSim occlusion verification is pending: run Wire All, save,
and Play.

### Walk and box scaling

Walk commands now schedule gradual movement instead of teleporting: straight
segments when the actor ignores walkboxes, otherwise a walkbox itinerary from
the room matrix with shared-corner portals. Movement advances once per tick
(240 jiffies/sec, one update per four jiffies) at the actor's walk speed scaled
by the current box scale, keeping fractional pixels so distant actors still
progress. `wait-for-actor` yields while the actor is moving; the stop
animation clears the walk and turn animations set facing. Room scale slots
(`SO_ROOM_SCALE`) program the Y-interpolated slot table the boxes reference.

Verified against room 38 data: scale slot 1 gives 255 at y=124, 241 at
y=106, and 216 at y=73 (signed integer division truncates toward zero).
This is modest perspective scaling, not a tiny distant actor. The original
tiny mountain entrance remains unverified. The (154,106)->(244,106) itinerary walk arrives, straight
walks step 8px/tick at scale 255, and Python/C# agree on scales, steps, and
arrival. The stationary lookout is actor 2 and ignores walkboxes; Guybrush is
actor 1 and obeys them. Earlier statements attributing Guybrush's missing walk
to the lookout's ignore-box flag were incorrect. Host tests now cover the full
Guybrush diagnostic route (312,73)->(154,106), including intermediate positions.

### Room exits (investigating, not yet playable)

Boundary and object clicks dispatch through the original verb (script 4) and
sentence (script 2) scripts — no hard-coded switching. Walk routing itself is
verified: a direct walk to (313,68) follows boxes 1→3→5 and arrives at
(312,73). But no room change has been observed from the Lookout in host traces:
edge clicks walk to walkbox clamps, Walk-to on the cliff object walks to its
trail point, and the room stays 38. After the intro settles only the boot
waiters (scripts 23/159) remain; no polling exit script is alive. Natural entry
also leaves ego in room 0 — the forced room jump bypasses the scripted
`loadRoomWithEgo` entrance — so exit conditions may depend on state the
diagnostic never sets up. Destination assets are ready regardless: all 86 room
backgrounds and masks are baked and wired by the Room Backgrounds menu, so a
future scripted exit will not land on a black screen. The Lookout diagnostic
itself no longer idles through the empty room 0 either: it enters room 38 the
moment the title releases (~tick 1060) instead of waiting for a fixed 1500.

The natural boot-to-gameplay start remains open: after the title, room 0
idles with only the hover/cursor waiters (scripts 23/159) and no input —
clicks, keys poked as `VAR_KEYPRESS`, or sentence — advances it in host
traces. The game logic itself works once placed (walk, dialogue, objects all
verified), so this is purely a missing start-input semantic, not an engine
fault.

Rendering menus now follow the screen background's selected VM instead of
independently taking the first boot/Lookout harness found. **Wire All** runs
the full order (Lookout first so layers bind directly, then backgrounds,
verbs, objects, actors, talk) with one compile and one summary dialog; the
individual menus are unchanged for targeted rewiring.

Audit checks: the Python fixture suite (42 tests) and all C# VM checks pass; C# VM regressions, walk/scale/clip/talk/anim/camera/lights checks, and
boot/gameplay budget parity pass at budgets 1 and 500. All runtime C# sources
compile against local Unity/SDK references (one existing unused-field warning).
This is not UdonSharp, shader, or ClientSim validation. Actor compositing now
works independently of object count, and object getter slot 200 is rejected
rather than indexed.
The user subsequently confirmed the lookout/fire scene renders in ClientSim.
This validates the initial actor display, not animation or the natural room-0
transition; the Lookout harness still explicitly enters room 38.

Room-32 note (circus alley, verified in sim): the entry runs a cutscene that
never ends on its own path (local script 200 stops without `endCutscene`),
leaves input locked, stops the hover tracker (global script 23, which feeds
clicks the object under the cursor via `g108`), and swaps in a cutscene-only
verb script (14) without restoring the normal one (4) — so clicks fizzle and
the street exit never fires. A click during the armed override now aborts the
cutscene (ScummVM behavior; on our no-keyboard build any click counts as the
skip instead of only ESC), the end-of-cutscene path restores the verb script
and relaunches the hover tracker, hover updates `g108` again, and Walk-to on
the street exits to room 34. Without the abort the room genuinely stalls, so
if clicks ever stop responding there, click once to skip first.

### Overhead talk text

Dialogue prints above the speaker's head, as in the original. The VM tracks
print slots the way `decodeParseString` does: each slot keeps sticky state
(overhead flag, position, color) across prints — `loadDefault` at entry,
`saveDefault` at exit — changed only by explicit `SO_AT` / `SO_CENTER` /
`SO_LEFT` / `SO_OVERHEAD` / `SO_COLOR` ops. A bare print therefore inherits
whatever the previous print set: the title sequence positions once
(centered, 160,150) and every credit line reuses it, while the first
Lookout line re-arms overhead and the rest of the dialogue follows the
speaker. `VAR_V5_TALK_STRING_Y` (engine var 54) resets to -80, and the
above-head offset is scaled by the actor's box scale exactly like
`displayDialog` (truncating division, y clamped to >= 1, x clamped
to 80..240 on the 320-wide screen). The label is visible while the talk
timer runs; overhead lines additionally require their speaker to be a
visible actor in the displayed room, while positioned narration (title
credits) draws at its fixed coordinates with no speaker. Color resolves
the print override first, else the actor's talk color, through the live
room palette with a white fallback.

A new `ScummTalkLayer` holds a world-space label parented to the screen quad
(centered, outlined for readability) and repositions it every frame from the
screen's own local position/scale, so moving the screen keeps the text glued
to the game pixels. Host coverage: overhead default vs positioned/centered
prints, the scaled (312,73)->top-of-screen and (154,106)->y29
Lookout placements, timer expiry clearing the actor and `HAVE_MSG`, and
palette resolution. ClientSim verification (readability at distance, overlap
with verb lines at the screen bottom) is pending.

Pacing follows the original `displayDialog` timing exactly: base delay 60 plus
one `CHARINC` (engine var 37, default 4, read live so text-speed changes apply)
per printed character, in the same tick units as the scheduler. The earlier
placeholder (`12 + raw length`) flashed lines for a fraction of the intended
time; `wait-for-message` gates now hold for the full duration, so dialogue
rhythm and cutscene timing follow the game data again.

### Costume animation (limb stepping)

Actors no longer freeze on a single cel. The VM advances every in-room
costumed actor's limb animation once per tick, gated by its anim speed exactly
like `processActors`/`animateCostume` (speed 0 steps every tick). The offline
baker emulates the true `costumeDecodeData`/`increaseAnim` semantics — limb
ranges with the `extra` byte, highflag ping-pong, `0x7C` skips — and bakes each
distinct limb state of a looping chore as extra frames (`seq.txt` per costume).
The actor layer picks the step matching the VM counter, falling back to the
static pose when no loop exists.

Visible result: the lookout fire flickers through its 3-cel loop, and
Guybrush/the lookout use their real walk (5-step) and talk (12–16-step) cycles
— the data confirms stands stay static, so idle actors correctly hold still.
Bake with the full costume list from "Fresh-clone setup" above
(sequences are automatic), then re-run Wire All so the layer picks up the new
`seq.txt` tables. Step counters, speed gating, off-room suppression, sequence
enumeration, and the baked fire loop are covered on both simulators; per-limb
runtime compositing (e.g. partial gestures) remains future work.

Ambient actors with no walk/stand frames (the room-10 sky clouds, costume 59)
run a costume command directly via `startAnimActor`, so the layer selects the
actor's `anim` chore when poses exist for it — static cels drifting across the
sky on the engine's own walk paths. Bake 59 with the same command; no layer
rewiring beyond Wire All is needed. The ambient chore is a fallback only:
normal walk/talk/stand poses (with their baked step sequences) are tried
first, otherwise Guybrush's stuck `anim` 5 hijacks the walk cycle and legs
never move. West walks face 270 through the `newDirToOldDir`-matching map
with the baked walk seq cycling. Classic COST cels decode left-to-right
and the renderer mirrors that output for oldDir 0
(`ClassicCostumeRenderer::setFacing`, xStep flips sign), so the layer
mirrors U and re-anchors the hotspot exactly for facing 0 — pixel renders
prove facing 0 and 1 bake identically, so without the flip west renders
the east base sprite and reads as walking backwards. The actor table is
cached per actor for the same reason as objects below.

### Camera scrolling (logo, vista, wide rooms)

Room 10 is 640 pixels wide: the Lucasfilm logo occupies the right half, the
mountain vista the left. The boot pans between them, and the renderer now
follows: `panCameraTo`/`setCameraAt` set only a destination (never snaps),
the VM walks the camera 8px per tick toward it, clamped to the room with
strip alignment, exactly like `moveCamera`/`cameraMoved`. A deviation note:
we settle inside 8px instead of oscillating ±8 around non-aligned
destinations — identical whenever both ends are strip-aligned, as in boot.
Every room entry resets the camera to centered and holds it there (matching
`startScene`); a followed actor takes over the destination only while it is
moving, so wide rooms (33, 35) hold their entry view until Guybrush walks
and then track him — no slide on entry. `loadRoomWithEgo` additionally snaps
the camera to Guybrush and follows him (plus walks him to the target unless
it is -1), so he is always on screen after a transition. `loadRoomWithEgo`
places him at the passage object's walk point (room 33: the west door at
x≈5, camera holding scroll 0) and walks him to the script target with the
camera following — the entry you see is the walk-in, not a teleport. Implementation note:
the snap is recorded by the opcode and applied at the end of `InstallRoom` —
the install runs in a later scheduler phase, so snapping in the opcode itself
gets wiped by the entry reset; and placement must use the destination room
operand, not the still-installed room. `actorFollowCamera` additionally
snaps when the actor starts over half a screen away. The shader offsets
background and mask sampling by `_CamX`;
actors, objects, and overhead text shift by the same scroll; scene clicks and
hover coords are converted back to world pixels. Verified: 160→480 in 40
ticks, hold, 480→160 in 40 ticks; room-33 entry holds 160 while idle and
tracks east on walk. The shader offsets background and mask sampling by `_CamX`;
actors, objects, and overhead text shift by the same scroll; scene clicks and
hover coords are converted back to world pixels. Verified: 160→480 in 40
ticks, hold, 480→160 in 40 ticks. Host coverage includes pan timing, clamps,
follow mode, and the scroll-script-gated `VAR_CAMERA_POS_X` publication.
Unknown rooms (like the black room 0) now render black instead of the
diagnostic grid once any room has shown; the grid remains only before the
first room. Note: `SO_ROOM_SCREEN` sets the main virtual screen's vertical
bounds, not the resolution — an earlier build stored its first operand as
the screen width (0), shifting every scroll by half a screen; both
simulators now keep the hardware 320x200 and the bug has regression
coverage.

### Screen input ownership
`InputUse` now owns screen presses; `Interact` does not dispatch a second event.
A right-hand/desktop press is accepted only with a valid on-screen ray and a
VM assigned to the screen's `ScummRoomBackground`. There is no center fallback.
Holding Use while moving onto the screen does not generate a click: release and
press again. VR left-hand events cannot clear the right-hand press latch.

Each accepted click captures its pixel coordinates and VM. Only the displayed
gameplay harness receives pointer/click input; fixture and hidden boot harnesses
discard it. Restarting a harness synchronizes its click sequence so an earlier
click is not replayed. Rewiring the background VM also changes input ownership.

```sh
python3 Tools/check_screen_input.py
```

Host tests of the actual input class pass for duplicate events, off-screen
presses, held-button movement, coordinate capture, VR hand filtering and target
switches. Runtime C# compilation passes. ClientSim must still verify this updated
SDK event path: click on-screen once (one dispatch), then click off-screen (no
dispatch), and confirm no fixture VM input logs. The natural room-0 stall remains
open.

## Fresh-clone setup (share this)

Everything under `Assets/Scumm/Generated/` is git-ignored (24M of converted
game data plus baked atlases) and must be regenerated locally. The Monkey
Island data itself is **not** in the repo (copyrighted): supply your own
**Monkey Island 1 Ultimate Talkie Edition 1.02** (`monkey.000`,
`monkey.001`, `monster.sou`; verify SHA-256 against the table under
"Exact supplied target" below).

Prereqs: **Unity 2022.3.22f1**, VRChat Worlds SDK (restores via the
template's `Packages/vpm-manifest.json`), Python 3.9+ (stdlib only, no pip
packages needed). All commands run from this `udonscumm/` directory.
`check_csharp_vm.py` reuses Unity's bundled dotnet (`UNITY_CONTENTS`
defaults to `/Applications/Unity/Unity.app/Contents` on macOS).

```sh
# 0. Unpack the game next to the repo so ../monkeyisland holds it
#    (or point --source/--output at your own paths).
# 1. Extract + validate v5 resources
python3 Tools/import_monkey.py ../monkeyisland --output Assets/Scumm/Generated/MonkeyIsland --release-label "Monkey Island 1 Ultimate Talkie Edition 1.02 (supplied readme)"
# 2. Per-room runtime blobs (walkboxes, objects, local scripts)
python3 Tools/build_room_blobs.py --game Assets/Scumm/Generated/MonkeyIsland --output Assets/Scumm/Generated/RoomBlobs
# 3. Room previews (backgrounds) + foreground masks
python3 Tools/decode_room.py Assets/Scumm/Generated/MonkeyIsland/rooms/room-038.bytes --output Assets/Scumm/Generated/RoomPreview
for f in Assets/Scumm/Generated/MonkeyIsland/rooms/room-*.bytes; do
  n=$(basename "$f" .bytes); python3 Tools/decode_room.py "$f" --output "Assets/Scumm/Generated/RoomPreviews/${n#room-}";
done
python3 Tools/bake_room_masks.py
# 4. Room-object atlases (creates table.txt per room; Wire Object Atlases needs it)
python3 Tools/bake_object_atlases.py
# 5. Actor sprites — this exact costume set (Guybrush, pirates, townsfolk, lookout, fire, clouds)
python3 Tools/bake_actor_sprites.py --costumes 1 14 18 23 24 26 33 42 48 59
# 6. VM fixtures
python3 Tools/make_vm_fixtures.py --output Assets/Scumm/Generated/VmFixtures
# 7. Verify (both must be green before opening Unity)
python3 Tools/test_vm_fixtures.py
python3 Tools/check_csharp_vm.py
```

Then in Unity (Edit mode, 2022.3.22f1): let packages/VPM resolve, open
`Assets/Scenes/VRCDefaultWorldScene`, choose **SCUMM → Wire All
(Prototype + Lookout Play Test)**, wait for the UdonSharp compile, save the
scene, and enter Play mode (ClientSim). Walk Guybrush down the cliff path to
reach town; Open the SCUMM Bar door, Walk-to it, and Talk to the pirates
(click one of the three choice lines when they appear).

## Exact supplied target

The supplied `../monkeyisland/readme.txt` identifies **Monkey Island 1 Ultimate Talkie Edition 1.02**, a modified v5 release with speech. This differs from the original CD edition assumed in the initial plan. Treat the label as readme evidence and the following hashes as the exact build identity.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| monkey.000 | 9135 | `34827cd5664361650c3081dc143e75167d31d80c462d22f2b8c35d45f5c8f74f` |
| monkey.001 | 6653379 | `2954d067d56c1190456a911cd7bcc507ad6560365c1a24936115e3d9faa9c62f` |
| monster.sou | 196689623 | `7990dd22199c76aa75eda2952b7e6c9cec3048451e8babd7766f19eabeff2b47` |

Validated inventory: 86 rooms, 187 global scripts, 389 local scripts, 86 entry scripts, and 86 exit scripts. Object verb code remains in room OBCD chunks. Directory capacities are not the same as populated resource counts.

The source directory contains MIDI drivers and speech data but no separate CD music tracks. Track-based playback cannot be assumed for this dataset; audio planning must include the talkie modifications and available music resources.

## Import game resources

From this project's directory:

```sh
python3 Tools/import_monkey.py ../monkeyisland --output Assets/Scumm/Generated/MonkeyIsland --release-label "Monkey Island 1 Ultimate Talkie Edition 1.02 (supplied readme)"
python3 -m unittest discover -s Tools -p 'test_*.py' -v
```

The initial import has already been run locally. The importer refuses an existing output directory: use a fresh directory for subsequent experiments. All structural validation happens before output is created. It reads game data, not the supplied executable.

`manifest.json` records original offsets, hashes, dimensions, script kinds, and limitations. ROOM exports include their headers; script exports contain bytecode only. Graphics remain compressed. Game-derived output is local and ignored by Git.

### Decode room backgrounds

Backgrounds for all 86 rooms decode offline (Lookout preview was the first):

```sh
python3 Tools/decode_room.py Assets/Scumm/Generated/MonkeyIsland/rooms/room-022.bytes --output /tmp/rp/022
```

Boot-room previews (22, 10, 38) are checked into `Assets/Scumm/Generated/RoomPreviews/` (local, git-ignored). In Unity, **SCUMM → Wire Room Backgrounds** attaches them to the screen material and follows the VM's current room at runtime, logging `SCUMM boot-r8 background: room N` on change. Room 0 is black (no room resource); room 10 is 640 wide, so the 320-wide screen shows its left half until camera panning lands.

The original Lookout decoding (already run locally):

```sh
python3 Tools/decode_room.py Assets/Scumm/Generated/MonkeyIsland/rooms/room-038.bytes --output Assets/Scumm/Generated/RoomPreview
```

The output contains lossless index and palette PNGs, an RGB reference preview, and room metadata. The decoder supports raw, horizontal/vertical zigzag, and major/minor v5 strip encodings, with bounded streams and explicit errors for unsupported data. It also refuses to overwrite an existing output directory.

Unity setup configures the index texture as linear, uncompressed, point-filtered data with no mipmaps or NPOT resizing. The palette is imported as sRGB color. The shader performs the palette lookup on the GPU; Udon never iterates over these room pixels.

## Open the prototype in Unity

1. Open this `udonscumm` project using its pinned Unity **2022.3.22f1** version. Let the template bootstrap resolve VRChat Worlds packages. If that version is no longer supported by the installed SDK, migrate through the supported VRChat tooling and record the resulting versions.
2. Wait for package import and UdonSharp compilation. The current project resolves VRChat Base and Worlds SDK **3.10.6** in `Packages/vpm-manifest.json`.
3. Open the SDK's Udon example scene and save your own copy, or use an existing scene with a VRChat world descriptor, floor, and spawn.
4. **Exit Play mode**, then choose **SCUMM → Create Flatscreen Prototype**. Entering Play mode alone does not create it. The harness is positioned relative to the world's first spawn and faces the spawning player. It uses the Lookout preview when the decoded textures are available, or a diagnostic grid otherwise.
5. **Save the scene**, then enter Play mode. Test with ClientSim for wiring, then Windows VRChat Build & Test for actual Udon performance.
6. Aim at the mountain/image quad (not the verb legend below it), then press Use.
   The green crosshair must be on-screen. Input requires the room-background
   component to reference the intended VM. The base prototype is a static
   preview; wire the gameplay and rendering layers for the Lookout bring-up.
7. The VM probe runs on startup. Interact with the small cube to repeat it.

If a screen already exists, use **SCUMM → Show Lookout Room Preview** to update it. Use **SCUMM → Move Screen In Front Of Spawn** if you previously created it at the world origin or moved your spawn. These commands require Edit mode so the changes persist. Creating again selects the existing screen instead of duplicating it.

Expected probe result: **PASS, 3004 instructions, variables 1000 and 0**. Change `instructionsPerFrame` on the benchmark behaviour (try 1, 100, and 1000) and check that results remain identical. Budget exhaustion preserves the program counter. The fixture has one explicit yield; it is not a test of multi-script scheduling.

### Wire the VM fixtures and boot bring-up

1. Generate fixtures (already run locally; rerun only into a fresh directory):
   ```sh
   python3 Tools/make_vm_fixtures.py --output Assets/Scumm/Generated/VmFixtures
   ```
2. In Unity, **exit Play mode**, then choose **SCUMM → Wire VM Fixtures**. Three interactable cubes appear near spawn, one per fixture (variables/branches, nesting/delay/arguments, divide-by-zero fault).
3. **Save the scene**, enter Play mode, and read each status text. Expected: `PASS` for all three, with the fault fixture reporting `Division by zero` in script 13.
4. Interact with a cube to rerun its fixture. Change the harness `instructionsPerFrame` (try 1 and 200) and confirm results are identical: budget exhaustion must not change logical results.
5. Exit Play mode, choose **SCUMM → Wire Boot Bring-Up**, let UdonSharp compile, save, and enter Play mode. The Console must show `SCUMM boot-r9 harness start` and `VM boot-r9`. The harness reports progress every 300 frames and live-slot details at completion, fault, or timeout. Copy those Console messages rather than relying on the world-space text. **Boot now reaches the room-0 input wait** (scripts 23/159 alive); the 3,600-tick timeout there is the waiting game, not a stall. The Lookout play-test bring-up instead runs ~100 minutes: when a harness stops early, every layer freezes on its last frame, which is why the fire used to stop after the dialogue.

Timing model: display-rate Unity frames feed wall-clock jiffies into an
accumulator that deals whole 60Hz ticks (`ScummTickPacer`, host-proven equal
tick counts at 30/60/120fps with clamps), matching the reference
`waitForTimer(delta * 4)` pacing. Per-tick work — walks, costume animation —
therefore runs at the same speed on every headset; previously one tick ran per
Unity frame, so 90/120Hz+ displays ran the game fast. Talkie voice durations
are not yet modeled (no audio pipeline), so subtitle timing follows the text
formula rather than the spoken line lengths.

The screen pointer uses a mathematical plane intersection and does not yet check occluding world geometry. Desktop input is head-directed rather than a captured free mouse cursor. This harness is intended for an unobstructed screen.

## Validation and performance recording

### UdonSharp program assets

The runtime scripts require the VRChat Worlds SDK (which includes UdonSharp). If Unity reports missing `UdonSharp`/`VRC` namespaces, let the template's package restore finish; if restoration fails, add the existing project in VRChat Creator Companion and install/restore **VRChat SDK - Worlds** in Manage Project.

Each behaviour script needs a sibling UdonSharp program asset (`ScummV5Vm.cs` ↔ `ScummV5Vm.asset`). The **SCUMM** menus create any missing ones automatically before wiring components, then trigger a U# compile. If the Console shows UdonSharp compile errors naming our scripts after running a menu, fix those first, then re-run the menu. Commit the generated `.asset` files alongside the scripts; they are required for the behaviours to work. The menus refuse to run in Play mode so generated objects persist, and re-running a wiring menu replaces its previous output instead of duplicating it.

Validation layers:

- All 35 Python tests pass (verb hit-testing, sentence runner, room entry/exit, object-atlas baking). Python and C# agree on real-data boot to the input-wait loop (rooms 22 → 0 → 10 → 0, scripts 23 + 159 alive, ~15k instructions, no fault) and on a Lookout gameplay segment: room entry, object click, and the full 19-line Guybrush/lookout dialogue play identically in both, fault-free. Budgets 1 and 500 agree. Boot prints 66 dialogue lines identically in both. A debug-configured run matched an initial 535-op ScummVM trace prefix. Audio is stubbed, so sound waits diverge; Lookout is room 38, not room 10.
- `python3 Tools/check_csharp_vm.py` compiles and executes the actual C# VM with minimal host-only Unity/Udon type stubs, including the v2 room-blob object layout, text-control decoding and dialogue capture, the sentence runner, room entry/exit with object pickup, waits, walk-to/face-actor, draw/find/actor-from-pos, startObject verb entries, inventory count/find, verb hit-testing, object-slot getters, the 0x2000 indirection fix, and engine vars VAR_INPUTMODE = 3 / VAR 74 = 1225 (from ScummVM setupScummVars/resetScummVars). Also covers machine reset (verbs/sentence/inventory clear, no leakage across restarts — the (0)→(10)→(20) duplication seen in-app). Boot and Lookout-click gameplay segments agree exactly across budgets.
- Runtime C# compilation against Unity/SDK references passes. ClientSim `boot-r8` verified: 3,600 ticks / 43,261 instructions to room 0 with scripts 23/159 waiting; all three original fixtures still PASS.
- `boot-r8` baseline: correct override decoding, ordered room continuations (exit var 30 → EXCD → var 31; entry var 28 → ENCD → var 29), text 0xFF control lengths, drawObject overlap/putState, findObject rect query, actorFollowCamera record, embedded-expression opcode execution, and v2 room objects. Versioned progress/live-slot logs identify fresh builds; the reported screen-program-load warning remains unresolved.

The benchmark reports time around its interpreter loop only; text formatting and input/rendering are outside that sample. `Time.realtimeSinceStartup` has platform-dependent precision, so tiny samples may report zero. Repeat measurements and use the Unity/VRChat profiler for trustworthy frame-cost measurements.

Record:

| Field | Value to capture |
| --- | --- |
| Environment | Actual VRChat client or ClientSim; never conflate them |
| Versions | Unity, Worlds SDK, UdonSharp, VRChat client |
| Hardware | CPU, GPU, headset, refresh rate |
| Probe | Instruction budget, expected values, PASS/FAIL |
| Timing | Total and peak loop time, whole-frame spikes, repetitions |
| World | Empty/populated, other active scripts |

This first synthetic arithmetic loop establishes a lower-complexity baseline. It does not prove the planned 1–2 ms game-runtime budget. Nested scripts, real room-entry code, actor animation, palette changes, and masked graphics remain separate benchmark gates.

### Decode room-object atlases

`python3 Tools/bake_object_atlases.py` decodes every OBIM image (v5 strip codec, plus concatenated-BOMP variant) across all room blobs and packs per-room index atlases:

- 73 rooms baked, 13 skipped (no/imageless objects), 0 failed. One BOMP-encoded object (room 99 object 1045) is skipped with a warning — its line framing is still undetermined.
- Atlas texel: R = palette index, A = coverage (0 for index 0, else 255). Transparency rule is index-0 for now; the ZP01 mask plane decodes structurally (28 LE16 strip offsets verified) but its strip encoding is still unknown, and object 109's title card renders correctly without it.
- Output (local, git-ignored like RoomPreviews): `Assets/Scumm/Generated/ObjectAtlases/<rrr>/atlas.png` (linear, point, uncompressed, clamp, alpha kept) + `atlas.json` + `table.txt` (`objId u0 v0 u1 v1`, Unity-v convention).
- In Unity, **SCUMM → Wire Object Atlases** attaches atlases + UV tables to the screen material and a `ScummObjectLayer` (max 16 layers, slot order = z-order) that follows VM object visibility/position/size live. The shader composites atlas texels through the room palette, so palette animation keeps working for objects. The layer caches resolved UVs per object slot (tables hold ~700 entries across 65 rooms; a full scan per slot per frame collapsed ClientSim to single-digit fps in room 28, which has 52 slots).

## Next implementation increment

1. ClientSim object test on the new build: rewire boot + backgrounds + verb display + **SCUMM → Wire Object Atlases**, save, play. The intro vista should show the "Secret of Monkey Island" title card composited over the mountain (object 109 at 48,24 in room 10). Paste any `background:` / atlas log lines and whether the card appears.
2. Unstick room 0 (status: fully characterized, exit not yet found). Room 0's waiter (script 23) polls gates bit 563 / var 394 / var 396 that no boot script, mouse/verb/scene/keyboard input (all areas, modes, all 255 keys via kKeyClickArea, right-click), sentence flow, or 20000 ticks ever sets; script 23 contains no loadRoom and input/sentence scripts dead-end there. Recommended decisive experiment: run the bundled ScummVM.app against the game data, reach room 0, and dump live slots/vars via the debugger to diff against our sim (identifies engine divergence vs. genuinely-gated game logic).
3. Masked compositing follow-ups: apply the ZP01 mask plane (strip encoding TBD), decode the BOMP variant (room 99 object 1045), camera follow on wide rooms.
4. Implement actor movement/animation on top of the recorded walk targets.
5. Work toward the two-room interaction slice using this exact talkie release.

Behavioral references are the sibling ScummVM source's `engines/scumm/script.cpp`, `script_v5.cpp`, and `resource.cpp`. No files in that reference tree were modified. These prototype files were AI-assisted and require human review before contribution/publication.
