using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummScreenInput : UdonSharpBehaviour
{
    public Transform screen;
    public Material screenMaterial;
    public Text inputText;
    public float maximumDistance = 5f;
    public int pixelX;
    public int pixelY;
    public int clickSequence;
    public int clickX;
    public int clickY;
    public ScummV5Vm clickVm;
    public bool pointerOverScreen;
    private VRCPlayerApi player;
    private ScummRoomBackground background;
    private bool useHeld;

    private int statusFrames;
    private int diagFrames;

    private void Start()
    {
        player = Networking.LocalPlayer;
        if (screen == null) screen = transform;
    }

    private void Update()
    {
        if (!Utilities.IsValid(player)) player = Networking.LocalPlayer;
        if (screen == null) screen = transform;
        UpdatePointer();
        statusFrames++;
        if (inputText != null && statusFrames % 10 == 1)
        {
            if (pointerOverScreen)
                inputText.text = "Pointer: (" + pixelX + ", " + pixelY + ") on screen\n"
                    + "Aim + Use to click. Clicks: " + clickSequence;
            else
                inputText.text = "Pointer: not on screen\nAim at the 320x200 screen. Clicks: " + clickSequence;
        }
        diagFrames++;
        if (diagFrames % 300 == 1)
        {
            bool valid = Utilities.IsValid(player);
            string detail = "SCUMM boot-r9 pointer: playerValid=" + (valid ? "1" : "0");
            if (valid)
            {
                VRCPlayerApi.TrackingData tracking = player.GetTrackingData(player.IsUserInVR()
                    ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.Head);
                Vector3 direction = tracking.rotation * Vector3.forward;
                float denominator = 0f;
                float distance = -1f;
                float u = -1f;
                float v = -1f;
                if (screen != null)
                {
                    denominator = Vector3.Dot(direction, screen.forward);
                    if (Mathf.Abs(denominator) > 0.0001f)
                        distance = Vector3.Dot(screen.position - tracking.position, screen.forward) / denominator;
                    Vector3 local = screen.InverseTransformPoint(tracking.position + direction * distance);
                    u = local.x + 0.5f;
                    v = 0.5f - local.y;
                }
                detail += " pos=" + tracking.position + " denom=" + denominator + " dist=" + distance
                    + " uv=" + u + "," + v + " over=" + (pointerOverScreen ? "1" : "0");
            }
            detail += " clicks=" + clickSequence;
            detail += " screenSet=" + (screen != null ? "1" : "0");
            Debug.Log(detail);
        }
    }

    private void UpdatePointer()
    {
        pointerOverScreen = false;
        if (!Utilities.IsValid(player) || screen == null || screenMaterial == null) return;
        VRCPlayerApi.TrackingData tracking = player.GetTrackingData(player.IsUserInVR()
            ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.Head);
        Vector3 direction = tracking.rotation * Vector3.forward;
        float denominator = Vector3.Dot(direction, screen.forward);
        if (Mathf.Abs(denominator) > 0.0001f)
        {
            float distance = Vector3.Dot(screen.position - tracking.position, screen.forward) / denominator;
            if (distance > 0 && distance <= maximumDistance && denominator > 0)
            {
                Vector3 local = screen.InverseTransformPoint(tracking.position + direction * distance);
                float u = local.x + 0.5f;
                float v = 0.5f - local.y;
                if (u >= 0 && u < 1 && v >= 0 && v < 1)
                {
                    pointerOverScreen = true;
                    pixelX = Mathf.FloorToInt(u * 320);
                    pixelY = Mathf.FloorToInt(v * 200);
                }
            }
        }
        screenMaterial.SetVector("_Cursor", new Vector4(pixelX, pixelY, pointerOverScreen ? 1 : 0, 0));
    }

    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!Utilities.IsValid(player)) player = Networking.LocalPlayer;
        if (Utilities.IsValid(player) && player.IsUserInVR() && args.handType != HandType.RIGHT) return;
        if (!value) { useHeld = false; return; }
        if (useHeld) return;
        useHeld = true;
        HandleClick();
    }

    // InputUse owns presses. Interact can be emitted for the same physical
    // press by the quad collider, so it must not dispatch a second click.
    public ScummV5Vm ReadTargetVm()
    {
        if (background == null) background = GetComponent<ScummRoomBackground>();
        return background != null ? background.vm : null;
    }

    private void HandleClick()
    {
        UpdatePointer();
        if (!pointerOverScreen) return;
        clickVm = ReadTargetVm();
        if (clickVm == null) return;
        clickX = pixelX;
        clickY = pixelY;
        clickSequence++;
        screenMaterial.SetVector("_Click", new Vector4(pixelX, pixelY, 1, 0));
        if (inputText != null)
            inputText.text = "Screen click " + clickSequence + ": (" + pixelX + ", " + pixelY + ")"
                + "\nDesktop: aim at the image + click/Use. VR: right hand + Use.";
    }
}
