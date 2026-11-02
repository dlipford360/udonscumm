using UdonSharp;
using UnityEngine;

// Composites decoded room-object images over the background on the GPU.
// Object pixels come from a per-room index atlas (R = palette index,
// A = coverage); the room palette resolves color, so palette animation
// keeps working. Visibility, position, and size come live from the VM;
// atlas UVs are wired offline per room by SCUMM/Wire Object Atlases.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummObjectLayer : UdonSharpBehaviour
{
    public const int MaxObjects = 16;

    public ScummV5Vm vm;
    public Material screenMaterial;
    public int[] roomIds;
    public Texture2D[] atlases;
    public int[] tableRoom;
    public int[] tableObj;
    public Vector4[] tableUV;

    private int shownRoom = -9999;
    private int frames;
    private int lastLoggedCount = -1;
    private int lastLoggedRoom = -99999;

    private void Update()
    {
        if (vm == null || screenMaterial == null) return;
        frames++;
        int room = vm.ReadRoom();
        if (room != shownRoom)
        {
            shownRoom = room;
            Texture2D atlas = FindAtlas(room);
            if (atlas != null) screenMaterial.SetTexture("_ObjAtlas", atlas);
            else screenMaterial.SetTexture("_ObjAtlas", null);
        }
        PushObjects(room);
    }

    private Texture2D FindAtlas(int room)
    {
        if (roomIds == null || atlases == null) return null;
        for (int i = 0; i < roomIds.Length && i < atlases.Length; i++)
            if (roomIds[i] == room) return atlases[i];
        return null;
    }

    private void PushObjects(int room)
    {
        int count = 0;
        int slots = vm.GetObjectSlotCount();
        for (int slot = 1; slot <= slots && count < MaxObjects; slot++)
        {
            int id = vm.GetObjectSlotId(slot);
            if (id == 0) continue;
            if (vm.GetObjectState(id) == 0) continue;
            int w = vm.GetObjectSlotW(slot);
            int h = vm.GetObjectSlotH(slot);
            if (w <= 0 || h <= 0) continue;
            Vector4 uv = FindUv(room, id);
            if (uv == Vector4.zero) continue;
            Vector4 rect = new Vector4(
                vm.GetObjectSlotX(slot) - vm.GetCameraScrollX(), vm.GetObjectSlotY(slot), w, h);
            SetObject(count, rect, uv);
            count++;
        }
        screenMaterial.SetFloat("_ObjCount", count);
        if (count != lastLoggedCount || room != lastLoggedRoom)
        {
            lastLoggedCount = count;
            lastLoggedRoom = room;
            Debug.Log("SCUMM boot-r9 objects: room " + room + " count " + count
                + " scroll " + vm.GetCameraScrollX());
        }
    }

    private void SetObject(int index, Vector4 rect, Vector4 uv)
    {
        if (index == 0) { screenMaterial.SetVector("_Obj0", rect); screenMaterial.SetVector("_ObjUV0", uv); }
        else if (index == 1) { screenMaterial.SetVector("_Obj1", rect); screenMaterial.SetVector("_ObjUV1", uv); }
        else if (index == 2) { screenMaterial.SetVector("_Obj2", rect); screenMaterial.SetVector("_ObjUV2", uv); }
        else if (index == 3) { screenMaterial.SetVector("_Obj3", rect); screenMaterial.SetVector("_ObjUV3", uv); }
        else if (index == 4) { screenMaterial.SetVector("_Obj4", rect); screenMaterial.SetVector("_ObjUV4", uv); }
        else if (index == 5) { screenMaterial.SetVector("_Obj5", rect); screenMaterial.SetVector("_ObjUV5", uv); }
        else if (index == 6) { screenMaterial.SetVector("_Obj6", rect); screenMaterial.SetVector("_ObjUV6", uv); }
        else if (index == 7) { screenMaterial.SetVector("_Obj7", rect); screenMaterial.SetVector("_ObjUV7", uv); }
        else if (index == 8) { screenMaterial.SetVector("_Obj8", rect); screenMaterial.SetVector("_ObjUV8", uv); }
        else if (index == 9) { screenMaterial.SetVector("_Obj9", rect); screenMaterial.SetVector("_ObjUV9", uv); }
        else if (index == 10) { screenMaterial.SetVector("_Obj10", rect); screenMaterial.SetVector("_ObjUV10", uv); }
        else if (index == 11) { screenMaterial.SetVector("_Obj11", rect); screenMaterial.SetVector("_ObjUV11", uv); }
        else if (index == 12) { screenMaterial.SetVector("_Obj12", rect); screenMaterial.SetVector("_ObjUV12", uv); }
        else if (index == 13) { screenMaterial.SetVector("_Obj13", rect); screenMaterial.SetVector("_ObjUV13", uv); }
        else if (index == 14) { screenMaterial.SetVector("_Obj14", rect); screenMaterial.SetVector("_ObjUV14", uv); }
        else if (index == 15) { screenMaterial.SetVector("_Obj15", rect); screenMaterial.SetVector("_ObjUV15", uv); }
    }

    private Vector4 FindUv(int room, int id)
    {
        if (tableRoom == null || tableObj == null || tableUV == null) return Vector4.zero;
        for (int i = 0; i < tableRoom.Length && i < tableObj.Length && i < tableUV.Length; i++)
            if (tableRoom[i] == room && tableObj[i] == id) return tableUV[i];
        return Vector4.zero;
    }
}
