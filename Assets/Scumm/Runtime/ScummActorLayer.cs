using UdonSharp;
using UnityEngine;

// Composites live actors over the background on the GPU. Poses are baked
// offline per (costume, facing, frame); each frame the layer selects one
// sprite per visible actor: talk frame while VAR_TALK_ACTOR matches,
// stand frame otherwise, or a walk pose while the VM is moving the actor.
// Cel cycling and mirroring are follow-ups.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummActorLayer : UdonSharpBehaviour
{
    public const int MaxActors = 8;
    public const int TalkActorVar = 25;

    public ScummV5Vm vm;
    public Material screenMaterial;
    public int[] costumeIds;
    public Texture2D[] atlases;
    public int[] tableCostume;
    public int[] tableKey;
    public int[] tableAtlas;
    public Vector4[] tableUV;
    public Vector4[] tableBox;
    public int[] seqCostume;
    public int[] seqKey;
    public int[] seqBase;
    public int[] seqLen;

    private int shownRoom = -9999;
    private int frames;
    private int lastLoggedCount = -1;
    private int lastLoggedRoom = -99999;

    // FindPose scans the whole pose table (~660 entries after the full
    // costume bake) per visible actor per frame. Cache the resolved table
    // slot per actor; the (costume, facing*8+frame) key revalidates every
    // frame, so walks/talks that change the frame still animate.
    private int[] cacheActorCostume = new int[48];
    private int[] cacheActorKey = new int[48];
    private int[] cacheActorSlot = new int[48];
    private bool cacheActorInit;

    private void Update()
    {
        if (screenMaterial == null) return;
        if (vm == null) { screenMaterial.SetFloat("_ActCount", 0); return; }
        frames++;
        int room = vm.ReadRoom();
        if (room != shownRoom) shownRoom = room;
        PushActors(room);
    }

    private int OldDir(int facing)
    {
        int a = facing % 360;
        if (a < 0) a += 360;
        if (a >= 71 && a <= 109) return 1;
        if (a > 109 && a <= 251) return 2;
        if (a > 251 && a <= 289) return 0;
        return 3;
    }

    private void PushActors(int room)
    {
        int count = 0;
        if (!cacheActorInit)
        {
            cacheActorInit = true;
            for (int i = 0; i < 48; i++) cacheActorCostume[i] = -1;
        }
        int talkActor = vm.ReadGlobal(TalkActorVar);
        for (int actor = 1; actor < 48 && count < MaxActors; actor++)
        {
            if (vm.GetActorRoom(actor) != room) continue;
            if (vm.GetActorVisible(actor) == 0) continue;
            int costume = vm.GetActorCostume(actor);
            if (costume == 0) continue;
            int facing = OldDir(vm.GetActorFacing(actor));
            int rawWalk = vm.GetActorWalkFrame(actor);
            int rawStand = vm.GetActorStandFrame(actor);
            int walkFrame = rawWalk;
            if (walkFrame < 1 || walkFrame > 7) walkFrame = 2;
            int standFrame = rawStand;
            if (standFrame < 1 || standFrame > 7) standFrame = 3;
            int frame = vm.GetActorMoving(actor) != 0 ? walkFrame : ((talkActor == actor) ? 4 : standFrame);
            // Ambient actors (birds) never get walk/stand frames from scripts;
            // they run a costume command directly, like startAnimActor does.
            // This is a fallback only: actors with real walk/stand/talk poses
            // (e.g. Guybrush, whose scripts leave walk/stand at 0 and rely on
            // the fallback frames 2/3) must use those first, or the ambient
            // chore hijacks the walk cycle and legs never move.
            int anim = vm.GetActorAnim(actor);
            int key = facing * 8 + frame;
            int seq = FindSeq(costume, key);
            if (seq >= 0) frame = seqBase[seq] + vm.GetActorAnimStep(actor) % seqLen[seq];
            int slot;
            int wantKey = facing * 8 + frame;
            if (actor >= 0 && actor < 48 && cacheActorCostume[actor] == costume && cacheActorKey[actor] == wantKey)
                slot = cacheActorSlot[actor];
            else
            {
                slot = FindPose(costume, facing * 8 + frame);
                if (actor >= 0 && actor < 48)
                {
                    cacheActorCostume[actor] = costume;
                    cacheActorKey[actor] = wantKey;
                    cacheActorSlot[actor] = slot;
                }
            }
            if (slot < 0 && rawWalk < 1 && rawStand < 1 && anim >= 1)
            {
                frame = anim;
                wantKey = facing * 8 + frame;
                seq = FindSeq(costume, wantKey);
                if (seq >= 0) frame = seqBase[seq] + vm.GetActorAnimStep(actor) % seqLen[seq];
                slot = FindPose(costume, facing * 8 + frame);
                if (actor >= 0 && actor < 48)
                {
                    cacheActorCostume[actor] = costume;
                    cacheActorKey[actor] = wantKey;
                    cacheActorSlot[actor] = slot;
                }
            }
            if (slot < 0) slot = FindPose(costume, facing * 8 + standFrame);
            if (slot < 0 || atlases == null || tableAtlas == null || slot >= tableAtlas.Length
                || tableAtlas[slot] < 0
                || tableAtlas[slot] >= atlases.Length || atlases[tableAtlas[slot]] == null) continue;
            Vector4 uv = tableUV[slot];
            Vector4 box = tableBox[slot];
            float scaleX = vm.GetActorRenderScaleX(actor) / 255f;
            float scaleY = vm.GetActorRenderScaleY(actor) / 255f;
            float sx = vm.GetActorX(actor) - vm.GetCameraScrollX() - box.z * scaleX;
            float sy = vm.GetActorY(actor) - vm.GetActorElevation(actor) - box.w * scaleY;
            SetActor(count, atlases[tableAtlas[slot]],
                new Vector4(sx, sy, box.x * scaleX, box.y * scaleY), uv);
            SetActorClip(count, vm.GetActorClipPlane(actor));
            count++;
        }
        screenMaterial.SetFloat("_ActCount", count);
        if (count != lastLoggedCount || room != lastLoggedRoom)
        {
            lastLoggedCount = count;
            lastLoggedRoom = room;
            Debug.Log("SCUMM boot-r9 actors: room " + room + " count " + count
                + " scroll " + vm.GetCameraScrollX());
        }
    }

    private int FindSeq(int costume, int key)
    {
        if (seqCostume == null || seqKey == null || seqBase == null || seqLen == null)
            return -1;
        for (int i = 0; i < seqCostume.Length && i < seqKey.Length
            && i < seqBase.Length && i < seqLen.Length; i++)
            if (seqCostume[i] == costume && seqKey[i] == key && seqLen[i] > 1) return i;
        return -1;
    }

    private int FindPose(int costume, int key)
    {
        if (tableCostume == null || tableKey == null || tableUV == null || tableBox == null || tableAtlas == null)
            return -1;
        for (int i = 0; i < tableCostume.Length && i < tableKey.Length
            && i < tableUV.Length && i < tableBox.Length && i < tableAtlas.Length; i++)
            if (tableCostume[i] == costume && tableKey[i] == key) return i;
        return -1;
    }

    private void SetActorClip(int index, float plane)
    {
        if (index == 0) screenMaterial.SetFloat("_ActClip0", plane);
        else if (index == 1) screenMaterial.SetFloat("_ActClip1", plane);
        else if (index == 2) screenMaterial.SetFloat("_ActClip2", plane);
        else if (index == 3) screenMaterial.SetFloat("_ActClip3", plane);
        else if (index == 4) screenMaterial.SetFloat("_ActClip4", plane);
        else if (index == 5) screenMaterial.SetFloat("_ActClip5", plane);
        else if (index == 6) screenMaterial.SetFloat("_ActClip6", plane);
        else if (index == 7) screenMaterial.SetFloat("_ActClip7", plane);
    }

    private void SetActor(int index, Texture2D atlas, Vector4 rect, Vector4 uv)
    {
        if (index == 0) { screenMaterial.SetTexture("_ActAtlas0", atlas); screenMaterial.SetVector("_Act0", rect); screenMaterial.SetVector("_ActUV0", uv); }
        else if (index == 1) { screenMaterial.SetTexture("_ActAtlas1", atlas); screenMaterial.SetVector("_Act1", rect); screenMaterial.SetVector("_ActUV1", uv); }
        else if (index == 2) { screenMaterial.SetTexture("_ActAtlas2", atlas); screenMaterial.SetVector("_Act2", rect); screenMaterial.SetVector("_ActUV2", uv); }
        else if (index == 3) { screenMaterial.SetTexture("_ActAtlas3", atlas); screenMaterial.SetVector("_Act3", rect); screenMaterial.SetVector("_ActUV3", uv); }
        else if (index == 4) { screenMaterial.SetTexture("_ActAtlas4", atlas); screenMaterial.SetVector("_Act4", rect); screenMaterial.SetVector("_ActUV4", uv); }
        else if (index == 5) { screenMaterial.SetTexture("_ActAtlas5", atlas); screenMaterial.SetVector("_Act5", rect); screenMaterial.SetVector("_ActUV5", uv); }
        else if (index == 6) { screenMaterial.SetTexture("_ActAtlas6", atlas); screenMaterial.SetVector("_Act6", rect); screenMaterial.SetVector("_ActUV6", uv); }
        else if (index == 7) { screenMaterial.SetTexture("_ActAtlas7", atlas); screenMaterial.SetVector("_Act7", rect); screenMaterial.SetVector("_ActUV7", uv); }
    }
}
