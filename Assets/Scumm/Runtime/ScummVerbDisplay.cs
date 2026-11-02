using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// On-screen verb row: one world-space label per visible text verb,
// positioned at the VM's verb coordinates like drawVerb (left/top,
// centered when the verb requests it). Hidden verbs (saved away, mode 0,
// image verbs, unnamed) get no label, matching findVerbAtPos. Hovering a
// label highlights it with the verb's hi-color.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ScummVerbDisplay : UdonSharpBehaviour
{
    public ScummV5Vm vm;
    public Transform screen;
    public ScummScreenInput screenInput;
    public Text[] verbLabels;

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
        if (active == null || verbLabels == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.Log("SCUMM boot-r9 verbs: missing vm or labels (run SCUMM/Wire Verb Display).");
            }
            return;
        }
        frames++;
        if (frames % 10 != 1) return;
        int hover = -1;
        if (screenInput != null && screenInput.pointerOverScreen)
            hover = active.FindVerbAt(screenInput.pixelX, screenInput.pixelY);
        int count = 0;
        string log = "";
        for (int slot = 1; slot < 100 && count < verbLabels.Length; slot++)
        {
            if (active.GetVerbMode(slot) == 0) continue;
            if (active.GetVerbId(slot) == 0) continue;
            if (active.GetVerbSaveId(slot) != 0) continue;
            if (active.GetVerbType(slot) != 0) continue;
            string name = active.GetVerbName(slot);
            if (name == "") continue;
            log += active.GetVerbId(slot) + " " + name + "\n";
            Text label = verbLabels[count];
            count++;
            if (label == null) continue;
            if (label.text != name) label.text = name;
            label.color = active.GetVerbTextColor(slot, slot == hover ? 1 : 0);
            label.alignment = TextAnchor.UpperLeft;
            int lx = active.GetVerbLeft(slot);
            if (active.GetVerbCenter(slot) != 0) lx -= active.GetVerbNameWidth(slot) / 2;
            RectTransform labelRect = label.GetComponent<RectTransform>();
            if (labelRect != null) labelRect.anchoredPosition = new Vector2(lx, -active.GetVerbTop(slot));
        }
        for (int i = count; i < verbLabels.Length; i++)
        {
            if (verbLabels[i] != null && verbLabels[i].text != "") verbLabels[i].text = "";
        }
        if (log != shown)
        {
            shown = log;
            Debug.Log("SCUMM boot-r9 verbs (" + count + "):\n" + (log == "" ? "(no verbs)" : log));
        }
    }
}
