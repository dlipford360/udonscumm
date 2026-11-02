using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Debugging readout: replaces the static preview title with the live room
// number so playtesters always know where Guybrush is.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummRoomReadout : UdonSharpBehaviour
{
    public ScummV5Vm vm;
    public ScummScreenInput screenInput;
    public Text readoutText;

    private string lastText = "\0";
    private bool warned;

    private void Update()
    {
        ScummV5Vm active = vm;
        if (screenInput != null)
        {
            ScummV5Vm target = screenInput.ReadTargetVm();
            if (target != null) active = target;
        }
        if (active == null || readoutText == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.Log("SCUMM boot-r9 room readout: missing vm or text (run SCUMM/Wire Room Readout).");
            }
            return;
        }
        int room = active.ReadRoom();
        int ego = active.ReadGlobal(1);
        string coords = "";
        if (ego >= 1 && ego < 48) coords = " (" + active.GetActorX(ego) + "," + active.GetActorY(ego) + ")";
        string text = "MONKEY ISLAND - room " + room + coords;
        if (text == lastText) return;
        lastText = text;
        readoutText.text = text;
    }
}
