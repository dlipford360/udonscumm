using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Classic sentence line: hovering the scene shows the default verb plus the
// object name (Walk to door), hovering a verb button shows that verb.
// Clicking through still runs the real verb/sentence scripts, so Guybrush
// replies with the scripted Threepwood line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummSentenceDisplay : UdonSharpBehaviour
{
    public ScummV5Vm vm;
    public ScummScreenInput screenInput;
    public Text sentenceText;

    private int frames;
    private string shown = "\0";
    private bool warned;

    private void Update()
    {
        ScummV5Vm active = vm;
        if (screenInput != null)
        {
            ScummV5Vm target = screenInput.ReadTargetVm();
            if (target != null) active = target;
        }
        if (active == null || sentenceText == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.Log("SCUMM boot-r9 sentence: missing vm or text (run SCUMM/Wire Sentence Line).");
            }
            return;
        }
        frames++;
        if (frames % 10 != 1) return;
        string line = "";
        if (screenInput != null && screenInput.pointerOverScreen && active.ReadGlobal(53) > 0)
        {
            int verbSlot = active.FindVerbAt(screenInput.pixelX, screenInput.pixelY);
            if (verbSlot != 0)
            {
                line = active.GetVerbName(verbSlot);
            }
            else
            {
                int worldX = screenInput.pixelX + active.GetCameraScrollX();
                int obj = active.GetObjectAt(worldX, screenInput.pixelY);
                if (obj != 0)
                {
                    string name = active.GetObjectName(obj);
                    if (name != "")
                    {
                        int verb = active.GetActiveVerbId();
                        string verbName = active.GetVerbNameById(verb);
                        if (verbName == "") verbName = "Walk to";
                        line = verbName + " " + name;
                    }
                }
            }
        }
        if (line != shown)
        {
            shown = line;
            sentenceText.text = line;
            if (line != "") Debug.Log("SCUMM boot-r9 hover: " + line);
        }
    }
}
