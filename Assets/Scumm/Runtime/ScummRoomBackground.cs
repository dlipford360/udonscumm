using UdonSharp;
using UnityEngine;

// Switches the flatscreen material to the VM's current room background.
// Textures are wired offline per room; unknown rooms clear the previous image.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummRoomBackground : UdonSharpBehaviour
{
    public ScummV5Vm vm;
    public Material screenMaterial;
    public int[] roomIds;
    public Texture2D[] indexTextures;
    public Texture2D[] paletteTextures;
    public Texture2D[] maskTextures;
    public int[] maskPlaneCounts;
    public int[] roomWidths;
    public int[] roomHeights;

    private int shownRoom = -9999;
    private float lastCamX = -99999f;

    private void Update()
    {
        if (vm == null || screenMaterial == null) return;
        int room = vm.ReadRoom();
        float scroll = vm.GetCameraScrollX();
        screenMaterial.SetFloat("_CamX", scroll);
        if (scroll != lastCamX)
        {
            lastCamX = scroll;
            Debug.Log("SCUMM boot-r9 scroll: room " + room + " camX " + scroll);
        }
        if (room == shownRoom) return;
        shownRoom = room;
        screenMaterial.SetFloat("_MaskPlanes", 0);
        screenMaterial.SetVector("_RoomSize", Vector4.zero);
        screenMaterial.SetFloat("_ShowRoom", 0);
        for (int i = 0; i < roomIds.Length; i++)
        {
            if (roomIds[i] != room) continue;
            if (i >= indexTextures.Length || i >= paletteTextures.Length) return;
            if (indexTextures[i] == null || paletteTextures[i] == null) return;
            screenMaterial.SetTexture("_IndexTex", indexTextures[i]);
            screenMaterial.SetTexture("_PaletteTex", paletteTextures[i]);
            if (maskTextures != null && i < maskTextures.Length && maskTextures[i] != null)
            {
                screenMaterial.SetTexture("_MaskTex", maskTextures[i]);
                if (maskPlaneCounts != null && i < maskPlaneCounts.Length)
                    screenMaterial.SetFloat("_MaskPlanes", maskPlaneCounts[i]);
            }
            screenMaterial.SetVector("_RoomSize", new Vector4(roomWidths[i], roomHeights[i], 0, 0));
            screenMaterial.SetFloat("_ShowRoom", 1);
            Debug.Log("SCUMM boot-r9 background: room " + room + " (" + roomWidths[i] + "x" + roomHeights[i] + ")"
                + " scroll=" + vm.GetCameraScrollX() + " maskPlanes=" + maskPlaneCounts[i]);
            return;
        }
        Debug.Log("SCUMM boot-r9 background: no preview for room " + room);
    }
}
