using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Overhead dialogue: shows the VM's current talk string in a world-space
// label above the speaking actor's head, mirroring displayDialog placement
// (GetTalkX/GetTalkY). Positioned prints use their fixed coordinates.
// Hidden while no talk timer is running or the speaker leaves the room.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummTalkLayer : UdonSharpBehaviour
{
    public ScummV5Vm vm;
    public Transform screen;
    public Transform talkCanvas;
    public Text talkText;

    private void Update()
    {
        if (vm == null || screen == null || talkCanvas == null || talkText == null) return;
        if (vm.GetTalkTimer() <= 0)
        {
            if (talkText.text != "") talkText.text = "";
            return;
        }
        int gx = vm.GetTalkX(0);
        int gy = vm.GetTalkY(0);
        if (vm.GetTalkOverhead() != 0)
        {
            // Overhead speech needs a visible speaker in this room.
            int actor = vm.GetTalkActor();
            if (actor < 1 || vm.GetActorRoom(actor) != vm.ReadRoom() || vm.GetActorVisible(actor) == 0)
            {
                if (talkText.text != "") talkText.text = "";
                return;
            }
            gx = vm.GetTalkX(actor);
            gy = vm.GetTalkY(actor);
            if (gx < 0 || gy < 0)
            {
                if (talkText.text != "") talkText.text = "";
                return;
            }
            // Screen-space clamp after scrolling, like displayDialog.
            gx -= vm.GetCameraScrollX();
            if (gx < 80) gx = 80;
            if (gx > 240) gx = 240;
        }
        string line = vm.lastPrintText;
        if (talkText.text != line) talkText.text = line;
        talkText.color = vm.GetTalkColor(vm.GetTalkActor());
        Vector3 basePos = screen.localPosition;
        Vector3 baseScale = screen.localScale;
        float x = basePos.x + (gx / 320f - 0.5f) * baseScale.x;
        float y = basePos.y + (0.5f - gy / 200f) * baseScale.y;
        talkCanvas.localPosition = new Vector3(x, y, basePos.z - 0.01f);
    }
}
