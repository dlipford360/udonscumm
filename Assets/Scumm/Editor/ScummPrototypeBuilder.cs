#if UDONSHARP
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using VRC.SDKBase;

[System.Serializable]
public class ScummFixtureSpec
{
    public string name;
    public string file;
    public int entry;
    public int[] vars;
    public int[] values;
    public int[] bits;
    public int[] bitValues;
    public int minTicks;
    public int expectFault;
    public int faultScript;
    public int faultOpcode;
}

[System.Serializable]
public class ScummFixtureList
{
    public ScummFixtureSpec[] fixtures;
}

public static class ScummPrototypeBuilder
{
    private const string Generated = "Assets/Scumm/Generated/Prototype";
    private const string RoomPreview = "Assets/Scumm/Generated/RoomPreview";

    [MenuItem("SCUMM/Create Flatscreen Prototype")]
    public static void Create()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        GameObject existing = GameObject.Find("SCUMM Prototype");
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log("Selected the existing SCUMM screen. Use SCUMM/Show Lookout Room Preview to update its image.");
            return;
        }
        Shader shader = Shader.Find("Scumm/ScreenDiagnostic");
        if (shader == null) throw new System.InvalidOperationException("Diagnostic shader has not imported.");
        Directory.CreateDirectory(Generated);
        // move v0,0; move v1,1000; add v0,1; sub v1,1;
        // equalZero v1 (branch back if false); breakHere; stop.
        byte[] program = {
            0x1a,0,0,0,0, 0x1a,1,0,0xe8,3,
            0x5a,0,0,1,0, 0x3a,1,0,1,0,
            0x28,1,0,0xf1,0xff, 0x80,0x00
        };
        string programPath = Generated + "/arithmetic-loop.bytes";
        if (!File.Exists(programPath)) File.WriteAllBytes(programPath, program);
        AssetDatabase.Refresh();
        string materialPath = AssetDatabase.GenerateUniqueAssetPath(Generated + "/screen.mat");
        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, materialPath);
        bool hasPreview = ApplyRoomPreview(material);

        GameObject root = new GameObject("SCUMM Prototype");
        Undo.RegisterCreatedObjectUndo(root, "Create SCUMM prototype");
        PositionAtSpawn(root.transform);
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "320x200 Screen (4:3 presentation)";
        screen.transform.SetParent(root.transform);
        screen.transform.localPosition = new Vector3(0, 1.8f, 2);
        screen.transform.localScale = new Vector3(1.6f, 1.2f, 1);
        screen.GetComponent<Renderer>().sharedMaterial = material;
        MakeText(root.transform, "Preview title", new Vector3(-.8f, 2.5f, 2),
            hasPreview ? "MONKEY ISLAND - Lookout background preview\nGame interpreter not connected yet"
                       : "SCUMM - Flatscreen diagnostic");

        Text inputText = MakeText(root.transform, "Input status", new Vector3(-.8f, 1.1f, 2),
            "SCUMM flatscreen input diagnostic\n320x200 logical pixels / 4:3 display");
        ScummScreenInput input = screen.AddUdonSharpComponent<ScummScreenInput>();
        input.screen = screen.transform;
        input.screenMaterial = material;
        input.inputText = inputText;

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Interact to restart VM benchmark";
        cube.transform.SetParent(root.transform);
        cube.transform.localPosition = new Vector3(1.2f, 1, 2);
        cube.transform.localScale = Vector3.one * .25f;
        Text status = MakeText(root.transform, "VM status", new Vector3(-.8f, .85f, 2), "VM benchmark pending");
        ScummV5Benchmark benchmark = cube.AddUdonSharpComponent<ScummV5Benchmark>();
        benchmark.program = AssetDatabase.LoadAssetAtPath<TextAsset>(programPath);
        benchmark.statusText = status;

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("SCUMM prototype added. Save this scene with a VRChat descriptor and spawn. "
            + "The synthetic probe expects 3004 instructions and variables [1000, 0].");
    }

    [MenuItem("SCUMM/Show Lookout Room Preview")]
    public static void ShowRoomPreview()
    {
        if (!RequireEditMode()) return;
        GameObject root = GameObject.Find("SCUMM Prototype");
        if (root == null)
        {
            Create();
            return;
        }
        ScummScreenInput input = root.GetComponentInChildren<ScummScreenInput>(true);
        if (input == null || input.screenMaterial == null)
        {
            EditorUtility.DisplayDialog("SCUMM", "The existing prototype has no configured screen input/material.", "OK");
            return;
        }
        Undo.RecordObject(input.screenMaterial, "Show SCUMM room preview");
        if (!ApplyRoomPreview(input.screenMaterial))
        {
            EditorUtility.DisplayDialog("SCUMM", "Room preview textures are missing. Run Tools/decode_room.py as described in SCUMM.md.", "OK");
            return;
        }
        Transform title = root.transform.Find("Preview title");
        if (title == null)
            MakeText(root.transform, "Preview title", new Vector3(-.8f, 2.5f, 2),
                "MONKEY ISLAND - Lookout background preview\nGame interpreter not connected yet");
        Text titleLabel = title.GetComponentInChildren<Text>();
        if (titleLabel != null)
        {
            Undo.RecordObject(titleLabel, "Update SCUMM title");
            titleLabel.text = "MONKEY ISLAND - Lookout background preview\nGame interpreter not connected yet";
        }
        EditorUtility.SetDirty(input.screenMaterial);
        EditorSceneManager.MarkSceneDirty(root.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    [MenuItem("SCUMM/Move Screen In Front Of Spawn")]
    public static void MoveToSpawn()
    {
        if (!RequireEditMode()) return;
        GameObject root = GameObject.Find("SCUMM Prototype");
        if (root == null) { Create(); return; }
        Undo.RecordObject(root.transform, "Move SCUMM screen to spawn");
        PositionAtSpawn(root.transform);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
    }

    [MenuItem("SCUMM/Wire VM Fixtures")]
    public static void WireFixtures()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        string dir = "Assets/Scumm/Generated/VmFixtures";
        string jsonPath = dir + "/fixtures.json";
        if (!File.Exists(jsonPath))
        {
            EditorUtility.DisplayDialog("SCUMM", "Run Tools/make_vm_fixtures.py first (see SCUMM.md).", "OK");
            return;
        }
        ScummFixtureList list = null;
        try
        {
            list = JsonUtility.FromJson<ScummFixtureList>(File.ReadAllText(jsonPath));
        }
        catch (System.Exception e)
        {
            Debug.LogError("SCUMM fixture JSON parse failed: " + e.Message);
        }
        if (list == null || list.fixtures == null || list.fixtures.Length == 0)
        {
            EditorUtility.DisplayDialog("SCUMM",
                "Could not read any fixtures from " + jsonPath + ". See Console for details.", "OK");
            return;
        }
        ReplaceRoot("SCUMM VM Fixtures");
        GameObject root = new GameObject("SCUMM VM Fixtures");
        Undo.RegisterCreatedObjectUndo(root, "Wire SCUMM VM fixtures");
        PositionAtSpawn(root.transform);
        for (int i = 0; i < list.fixtures.Length; i++)
        {
            ScummFixtureSpec spec = list.fixtures[i];
            int size = spec.entry + 1;
            foreach (string path in Directory.GetFiles(dir, "*-child-*.bytes"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int dash = name.LastIndexOf('-');
                int id;
                if (dash >= 0 && int.TryParse(name.Substring(dash + 1), out id) && id + 1 > size)
                    size = id + 1;
            }
            TextAsset[] scripts = new TextAsset[size];
            scripts[spec.entry] = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/" + spec.file);
            if (scripts[spec.entry] == null)
                throw new System.InvalidOperationException("Missing fixture bytes: " + spec.file);
            foreach (string path in Directory.GetFiles(dir, "*-child-*.bytes"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int dash = name.LastIndexOf('-');
                int id;
                if (dash >= 0 && int.TryParse(name.Substring(dash + 1), out id))
                {
                    TextAsset child = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (child == null)
                        Debug.LogWarning("SCUMM fixture child not imported yet: " + path);
                    scripts[id] = child;
                }
            }
            WireHarness(root.transform, "Fixture " + spec.name, i, scripts, spec.entry,
                200, 600, 1, spec.vars, spec.values, spec.bits, spec.bitValues,
                spec.minTicks, spec.expectFault, spec.faultScript, spec.faultOpcode);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("SCUMM wired " + list.fixtures.Length + " VM fixtures.");
        EditorUtility.DisplayDialog("SCUMM",
            "Wired " + list.fixtures.Length + " VM fixture harnesses. Save the scene, then enter Play mode.", "OK");
    }

    [MenuItem("SCUMM/Wire Boot Bring-Up")]
    public static void WireBoot()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        string dir = "Assets/Scumm/Generated/MonkeyIsland/scripts";
        if (!Directory.Exists(dir))
        {
            EditorUtility.DisplayDialog("SCUMM", "Run Tools/import_monkey.py first (see SCUMM.md).", "OK");
            return;
        }
        int size = 0;
        foreach (string path in Directory.GetFiles(dir, "global-*.bytes"))
        {
            int id;
            if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "global-", out id) && id + 1 > size)
                size = id + 1;
        }
        if (size == 0) throw new System.InvalidOperationException("No global scripts in " + dir);
        TextAsset[] scripts = new TextAsset[size];
        foreach (string path in Directory.GetFiles(dir, "global-*.bytes"))
        {
            int id;
            if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "global-", out id))
                scripts[id] = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }
        ReplaceRoot("SCUMM Boot Bring-Up");
        GameObject root = new GameObject("SCUMM Boot Bring-Up");
        Undo.RegisterCreatedObjectUndo(root, "Wire SCUMM boot bring-up");
        PositionAtSpawn(root.transform);
        root.transform.Translate(2.2f, 0f, 0f, Space.Self);
        // Room blobs + object tables the boot needs past loadRoom (rooms 22 -> 0 -> 10).
        TextAsset tables = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes");
        int roomSize = 0;
        foreach (string path in Directory.GetFiles("Assets/Scumm/Generated/RoomBlobs", "rt-room-*.bytes"))
        {
            int id;
            if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "rt-room-", out id) && id + 1 > roomSize)
                roomSize = id + 1;
        }
        TextAsset[] rooms = roomSize > 0 ? new TextAsset[roomSize] : null;
        if (rooms != null)
        {
            foreach (string path in Directory.GetFiles("Assets/Scumm/Generated/RoomBlobs", "rt-room-*.bytes"))
            {
                int id;
                if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "rt-room-", out id))
                    rooms[id] = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
        }
        // Matches ScummEngine boot: runScript(1, 0, 0, args) with args[0] = boot param 0.
        WireHarness(root.transform, "Boot script 1", 0, scripts, 1,
            500, 3600, 0, null, null, null, null, 0, 0, -1, -1, rooms, tables);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        int assigned = 0;
        foreach (TextAsset script in scripts) if (script != null) assigned++;
        Debug.Log("SCUMM boot-r9 bring-up wired with " + assigned + " of " + scripts.Length + " global scripts. Project: " + Application.dataPath);
        EditorUtility.DisplayDialog("SCUMM",
            "boot-r9 bring-up wired (" + assigned + " scripts). Wait for UdonSharp compilation, check the Console for errors, save the scene, then enter Play mode.", "OK");
    }

    [MenuItem("SCUMM/Wire Room Backgrounds")]
    public static void WireBackgrounds()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        ScummScreenInput input = FindScreenInput();
        if (input == null)
        {
            Debug.Log("SCUMM boot-r9 backgrounds: no ScummScreenInput in scene '"
                + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "'.");
            AbortWire("Create the flatscreen prototype first so a screen material exists.");
            return;
        }
        if (input.screenMaterial == null)
        {
            Renderer renderer = input.GetComponent<Renderer>();
            if (renderer != null) input.screenMaterial = renderer.sharedMaterial as Material;
        }
        if (input.screenMaterial == null)
        {
            AbortWire("The screen has no material assigned.");
            return;
        }
        ScummV5Vm vm = FindScreenVm(input);
        if (vm == null)
        {
            AbortWire("Wire the boot bring-up first so a VM exists.");
            return;
        }
        string previewDir = "Assets/Scumm/Generated/RoomPreviews";
        if (!Directory.Exists(previewDir))
        {
            AbortWire("No room previews found. Decode them as described in SCUMM.md.");
            return;
        }
        System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Texture2D> indices = new System.Collections.Generic.List<Texture2D>();
        System.Collections.Generic.List<Texture2D> palettes = new System.Collections.Generic.List<Texture2D>();
        System.Collections.Generic.List<Texture2D> masks = new System.Collections.Generic.List<Texture2D>();
        System.Collections.Generic.List<int> widths = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> heights = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> maskPlanes = new System.Collections.Generic.List<int>();
        foreach (string dir in Directory.GetDirectories(previewDir))
        {
            int id;
            if (!int.TryParse(Path.GetFileName(dir), out id)) continue;
            string indexPath = Path.Combine(dir, "indices.png").Replace('\\', '/');
            string palettePath = Path.Combine(dir, "palette.png").Replace('\\', '/');
            string metaPath = Path.Combine(dir, "room.json").Replace('\\', '/');
            if (!File.Exists(indexPath) || !File.Exists(palettePath) || !File.Exists(metaPath)) continue;
            ConfigureTexture(indexPath, false);
            ConfigureTexture(palettePath, true);
            Texture2D indexTex = AssetDatabase.LoadAssetAtPath<Texture2D>(indexPath);
            Texture2D paletteTex = AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath);
            if (indexTex == null || paletteTex == null) continue;
            string meta = File.ReadAllText(metaPath);
            // Parse dimensions without a JSON dependency.
            int width = ParseMetaInt(meta, "\"width\"", 320);
            int height = ParseMetaInt(meta, "\"height\"", 200);
            ids.Add(id);
            indices.Add(indexTex);
            palettes.Add(paletteTex);
            string maskPath = Path.Combine(dir, "masks.png").Replace('\\', '/');
            if (File.Exists(maskPath)) ConfigureTexture(maskPath, false);
            masks.Add(File.Exists(maskPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath) : null);
            widths.Add(width);
            heights.Add(height);
            maskPlanes.Add(ParseMetaInt(meta, "\"maskPlanes\"", 0));
        }
        if (ids.Count == 0)
        {
            AbortWire("No usable room previews found.");
            return;
        }
        ScummRoomBackground backgrounds = input.GetComponent<ScummRoomBackground>();
        if (backgrounds == null) backgrounds = input.gameObject.AddUdonSharpComponent<ScummRoomBackground>();
        backgrounds.vm = vm;
        backgrounds.screenMaterial = input.screenMaterial;
        backgrounds.roomIds = ids.ToArray();
        backgrounds.indexTextures = indices.ToArray();
        backgrounds.paletteTextures = palettes.ToArray();
        backgrounds.maskTextures = masks.ToArray();
        backgrounds.maskPlaneCounts = maskPlanes.ToArray();
        backgrounds.roomWidths = widths.ToArray();
        backgrounds.roomHeights = heights.ToArray();
        // New program asset triggers a U# compile; surface real errors now.
        Debug.Log("SCUMM boot-r9 backgrounds wired for rooms: " + string.Join(",", ids.ConvertAll(i => i.ToString()).ToArray()));
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
        DoneWire("Room backgrounds (" + ids.Count + " rooms)",
            "Room backgrounds wired (" + ids.Count + " rooms). Save, then enter Play mode.");
    }

    private static int ParseMetaInt(string meta, string key, int fallback)
    {
        int i = meta.IndexOf(key);
        if (i < 0) return fallback;
        int j = i + key.Length;
        while (j < meta.Length && (meta[j] < '0' || meta[j] > '9')) j++;
        int k = j;
        while (k < meta.Length && meta[k] >= '0' && meta[k] <= '9') k++;
        int value;
        return (k > j && int.TryParse(meta.Substring(j, k - j), out value)) ? value : fallback;
    }

    [MenuItem("SCUMM/Wire Verb Display")]
    public static void WireVerbs()    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        ScummScreenInput input = FindScreenInput();
        GameObject prototypeRoot = GameObject.Find("SCUMM Prototype");
        if (input == null || prototypeRoot == null)
        {
            AbortWire("Create the flatscreen prototype first.");
            return;
        }
        ScummV5Vm vm = FindScreenVm(input);
        if (vm == null)
        {
            AbortWire("Wire the boot bring-up first so a VM exists.");
            return;
        }
        Transform screenTransform = input.screen != null ? input.screen : input.transform;
        // Replace the old debug list with labels drawn on the game screen.
        Transform old = prototypeRoot.transform.Find("Verb verbs");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Transform row = prototypeRoot.transform.Find("Verb row");
        if (row == null)
        {
            GameObject rowObj = new GameObject("Verb row");
            rowObj.transform.SetParent(prototypeRoot.transform, false);
            row = rowObj.transform;
            Canvas canvas = rowObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = rowObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(320, 200);
            rowObj.transform.localScale = new Vector3(0.005f, 0.006f, 1f);
        }
        row.localPosition = screenTransform.localPosition + new Vector3(0, 0, -0.01f);
        const int labelCount = 16;
        Text[] labels = new Text[labelCount];
        for (int i = 0; i < labelCount; i++)
        {
            Transform existing = row.Find("Verb " + i);
            Text label = existing != null ? existing.GetComponent<Text>() : null;
            if (label == null)
            {
                GameObject labelObj = new GameObject("Verb " + i);
                labelObj.transform.SetParent(row, false);
                label = labelObj.AddComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                RectTransform rect = label.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.up;
                rect.anchorMax = Vector2.up;
                rect.pivot = Vector2.up;
                rect.sizeDelta = new Vector2(320, 200);
                rect.anchoredPosition = Vector2.zero;
                Shadow shadow = labelObj.AddComponent<Shadow>();
                shadow.effectColor = Color.black;
                shadow.effectDistance = new Vector2(1, -1);
            }
            label.fontSize = 10;
            label.alignment = TextAnchor.UpperLeft;
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = "";
            labels[i] = label;
        }
        ScummVerbDisplay display = row.GetComponent<ScummVerbDisplay>();
        if (display == null) display = row.gameObject.AddUdonSharpComponent<ScummVerbDisplay>();
        display.vm = vm;
        display.screen = screenTransform;
        display.screenInput = input;
        display.verbLabels = labels;
        Debug.Log("SCUMM boot-r9 verb row wired (" + labelCount + " on-screen labels).");
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(prototypeRoot.scene);
        DoneWire("Verb display", "Verb row wired on the game screen. Save, then enter Play mode.");
    }

    [MenuItem("SCUMM/Wire Object Atlases")]
    public static void WireObjectAtlases()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        ScummScreenInput input = FindScreenInput();
        if (input == null || input.screenMaterial == null)
        {
            AbortWire("Create the flatscreen prototype first so a screen material exists.");
            return;
        }
        ScummV5Vm vm = FindScreenVm(input);
        if (vm == null)
        {
            AbortWire("Wire the boot bring-up first so a VM exists.");
            return;
        }
        string atlasDir = "Assets/Scumm/Generated/ObjectAtlases";
        if (!Directory.Exists(atlasDir))
        {
            AbortWire("No object atlases found. Run Tools/bake_object_atlases.py first (see SCUMM.md).");
            return;
        }
        System.Collections.Generic.List<int> roomIds = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Texture2D> atlases = new System.Collections.Generic.List<Texture2D>();
        System.Collections.Generic.List<int> tableRoom = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> tableObj = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Vector4> tableUV = new System.Collections.Generic.List<Vector4>();
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        foreach (string dir in Directory.GetDirectories(atlasDir))
        {
            int id;
            if (!int.TryParse(Path.GetFileName(dir), out id)) continue;
            string atlasPath = Path.Combine(dir, "atlas.png").Replace('\\', '/');
            string tablePath = Path.Combine(dir, "table.txt").Replace('\\', '/');
            if (!File.Exists(tablePath)) continue;
            Texture2D atlasTex = null;
            if (File.Exists(atlasPath))
            {
                ConfigureAtlasTexture(atlasPath);
                atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                if (atlasTex == null) continue;
                roomIds.Add(id);
                atlases.Add(atlasTex);
            }
            foreach (string line in File.ReadAllLines(tablePath))
            {
                string[] parts = line.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5) continue;
                int objId;
                float u0, v0, u1, v1;
                if (!int.TryParse(parts[0], out objId)) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, invariant, out u0)) continue;
                if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, invariant, out v0)) continue;
                if (!float.TryParse(parts[3], System.Globalization.NumberStyles.Float, invariant, out u1)) continue;
                if (!float.TryParse(parts[4], System.Globalization.NumberStyles.Float, invariant, out v1)) continue;
                tableRoom.Add(id);
                tableObj.Add(objId);
                tableUV.Add(new Vector4(u0, v0, u1, v1));
            }
        }
        if (roomIds.Count == 0 && tableRoom.Count == 0)
        {
            AbortWire("No usable object atlases found.");
            return;
        }
        ScummObjectLayer layers = input.GetComponent<ScummObjectLayer>();
        if (layers == null) layers = input.gameObject.AddUdonSharpComponent<ScummObjectLayer>();
        layers.vm = vm;
        layers.screenMaterial = input.screenMaterial;
        layers.roomIds = roomIds.ToArray();
        layers.atlases = atlases.ToArray();
        layers.tableRoom = tableRoom.ToArray();
        layers.tableObj = tableObj.ToArray();
        layers.tableUV = tableUV.ToArray();
        Debug.Log("SCUMM boot-r9 object atlases wired for rooms: " + string.Join(",", roomIds.ConvertAll(i => i.ToString()).ToArray()));
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
        DoneWire("Object atlases (" + roomIds.Count + " rooms, " + tableRoom.Count + " objects)",
            "Object atlases wired (" + roomIds.Count + " rooms, " + tableRoom.Count + " objects). Save, then enter Play mode.");
    }

    private static ScummV5Vm FindScreenVm(ScummScreenInput input)
    {
        ScummRoomBackground background = input != null ? input.GetComponent<ScummRoomBackground>() : null;
        if (background != null && background.vm != null) return background.vm;
        GameObject lookout = GameObject.Find("SCUMM Lookout Bring-Up");
        if (lookout != null)
        {
            ScummV5Harness harness = lookout.GetComponentInChildren<ScummV5Harness>();
            if (harness != null && harness.vm != null) return harness.vm;
        }
        ScummV5Vm result = null;
        foreach (ScummV5Harness harness in Object.FindObjectsOfType<ScummV5Harness>())
        {
            if (harness.mode != 0 || harness.vm == null) continue;
            if (result != null) return null;
            result = harness.vm;
        }
        return result;
    }

    // The input component lives on the screen quad itself, so a stale or
    // missing screen reference can be healed from its own transform.
    private static ScummScreenInput FindScreenInput()
    {
        GameObject prototypeRoot = GameObject.Find("SCUMM Prototype");
        ScummScreenInput input = null;
        if (prototypeRoot != null)
            input = prototypeRoot.GetComponentInChildren<ScummScreenInput>(true);
        if (input == null)
            input = Object.FindObjectOfType<ScummScreenInput>(true);
        return input;
    }

    private static bool s_wireAllActive;
    private static string s_wireAllError;
    private static readonly System.Collections.Generic.List<string> s_wireAllDone =
        new System.Collections.Generic.List<string>();

    // Records the first failure; shows a dialog only for single-menu runs.
    // Wire All collects the error and reports once at the end instead.
    private static void AbortWire(string message)
    {
        if (s_wireAllError == null) s_wireAllError = message;
        if (!s_wireAllActive) EditorUtility.DisplayDialog("SCUMM", message, "OK");
    }

    private static void DoneWire(string summary, string dialogMessage)
    {
        if (s_wireAllActive) s_wireAllDone.Add(summary);
        else EditorUtility.DisplayDialog("SCUMM", dialogMessage, "OK");
    }

    // Per-menu U# compile; Wire All defers to one compile at the end.
    private static bool CompileWired()
    {
        if (s_wireAllActive) return true;
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
        {
            EditorUtility.DisplayDialog("SCUMM compile failed",
                "UdonSharp reports compilation errors. See the Console before running the harness.", "OK");
            return false;
        }
        return true;
    }

    private static void ConfigureAtlasTexture(string path)
    {
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
    }

    [MenuItem("SCUMM/Wire Actor Sprites")]
    public static void WireActorSprites()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        ScummScreenInput input = FindScreenInput();
        if (input == null || input.screenMaterial == null)
        {
            AbortWire("Create the flatscreen prototype first so a screen material exists.");
            return;
        }
        ScummRoomBackground background = input.GetComponent<ScummRoomBackground>();
        ScummV5Vm vm = background != null ? background.vm : null;
        if (vm == null)
        {
            foreach (ScummV5Harness harness in Object.FindObjectsOfType<ScummV5Harness>())
            {
                if (harness.mode == 0 && harness.vm != null)
                {
                    if (vm != null)
                    {
                        AbortWire("Multiple gameplay VMs found. Wire Room Backgrounds to the intended VM first.");
                        return;
                    }
                    vm = harness.vm;
                }
            }
        }
        if (vm == null)
        {
            AbortWire("Wire a boot or Lookout bring-up first so a VM exists.");
            return;
        }
        string actorDir = "Assets/Scumm/Generated/ActorSprites";
        if (!Directory.Exists(actorDir))
        {
            AbortWire("No actor sprites found. Run Tools/bake_actor_sprites.py first (see SCUMM.md).");
            return;
        }
        System.Collections.Generic.List<int> costumeIds = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Texture2D> atlases = new System.Collections.Generic.List<Texture2D>();
        System.Collections.Generic.List<int> tableCostume = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> tableKey = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> tableAtlas = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Vector4> tableUV = new System.Collections.Generic.List<Vector4>();
        System.Collections.Generic.List<Vector4> tableBox = new System.Collections.Generic.List<Vector4>();
        System.Collections.Generic.List<int> seqCostume = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> seqKey = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> seqBase = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<int> seqLen = new System.Collections.Generic.List<int>();
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        foreach (string dir in Directory.GetDirectories(actorDir))
        {
            int id;
            if (!int.TryParse(Path.GetFileName(dir), out id)) continue;
            string atlasPath = Path.Combine(dir, "atlas.png").Replace('\\', '/');
            string tablePath = Path.Combine(dir, "table.txt").Replace('\\', '/');
            if (!File.Exists(tablePath)) continue;
            int atlasIndex = -1;
            if (File.Exists(atlasPath))
            {
                ConfigureAtlasTexture(atlasPath);
                Texture2D atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                if (atlasTex == null) continue;
                atlasIndex = atlases.Count;
                costumeIds.Add(id);
                atlases.Add(atlasTex);
            }
            if (atlasIndex < 0) continue;
            string seqPath = Path.Combine(dir, "seq.txt").Replace('\\', '/');
            if (File.Exists(seqPath))
            {
                foreach (string line in File.ReadAllLines(seqPath))
                {
                    string[] parts = line.Trim().Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 4) continue;
                    int seqFacing, seqFrame, seqStart, seqLength;
                    if (!int.TryParse(parts[0], out seqFacing)) continue;
                    if (!int.TryParse(parts[1], out seqFrame)) continue;
                    if (!int.TryParse(parts[2], out seqStart)) continue;
                    if (!int.TryParse(parts[3], out seqLength) || seqLength < 2) continue;
                    seqCostume.Add(id);
                    seqKey.Add(seqFacing * 8 + seqFrame);
                    seqBase.Add(seqStart);
                    seqLen.Add(seqLength);
                }
            }
            foreach (string line in File.ReadAllLines(tablePath))
            {
                string[] parts = line.Trim().Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 10) continue;
                int facing, frame;
                float u0, v0, u1, v1;
                int w, h, ax, ay;
                if (!int.TryParse(parts[0], out facing) || !int.TryParse(parts[1], out frame)) continue;
                if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, invariant, out u0)) continue;
                if (!float.TryParse(parts[3], System.Globalization.NumberStyles.Float, invariant, out v0)) continue;
                if (!float.TryParse(parts[4], System.Globalization.NumberStyles.Float, invariant, out u1)) continue;
                if (!float.TryParse(parts[5], System.Globalization.NumberStyles.Float, invariant, out v1)) continue;
                if (!int.TryParse(parts[6], out w) || !int.TryParse(parts[7], out h)) continue;
                if (!int.TryParse(parts[8], out ax) || !int.TryParse(parts[9], out ay)) continue;
                tableCostume.Add(id);
                tableKey.Add(facing * 8 + frame);
                tableAtlas.Add(atlasIndex);
                tableUV.Add(new Vector4(u0, v0, u1, v1));
                tableBox.Add(new Vector4(w, h, ax, ay));
            }
        }
        if (costumeIds.Count == 0 || tableCostume.Count == 0)
        {
            AbortWire("No usable actor sprites found.");
            return;
        }
        ScummActorLayer layers = input.GetComponent<ScummActorLayer>();
        if (layers == null) layers = input.gameObject.AddUdonSharpComponent<ScummActorLayer>();
        layers.vm = vm;
        layers.screenMaterial = input.screenMaterial;
        layers.costumeIds = costumeIds.ToArray();
        layers.atlases = atlases.ToArray();
        layers.tableCostume = tableCostume.ToArray();
        layers.tableKey = tableKey.ToArray();
        layers.tableAtlas = tableAtlas.ToArray();
        layers.tableUV = tableUV.ToArray();
        layers.tableBox = tableBox.ToArray();
        layers.seqCostume = seqCostume.ToArray();
        layers.seqKey = seqKey.ToArray();
        layers.seqBase = seqBase.ToArray();
        layers.seqLen = seqLen.ToArray();
        Debug.Log("SCUMM boot-r9 actor sprites wired for costumes: " + string.Join(",", costumeIds.ConvertAll(i => i.ToString()).ToArray()));
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
        DoneWire("Actor sprites (" + costumeIds.Count + " costumes, " + tableCostume.Count + " poses)",
            "Actor sprites wired (" + costumeIds.Count + " costumes, " + tableCostume.Count + " poses). Save, then enter Play mode.");
    }

    [MenuItem("SCUMM/Wire Talk Text")]
    public static void WireTalkText()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        GameObject prototypeRoot = GameObject.Find("SCUMM Prototype");
        ScummScreenInput input = FindScreenInput();
        if (input == null)
        {
            AbortWire("Create the flatscreen prototype first so a screen exists.");
            return;
        }
        // The input component lives on the screen quad itself; heal a stale
        // screen reference instead of failing when it was never assigned.
        Transform screenTransform = input.screen != null ? input.screen : null;
        if (screenTransform == null && input.GetComponent<Renderer>() != null)
        {
            screenTransform = input.transform;
            input.screen = screenTransform;
        }
        if (screenTransform == null)
        {
            AbortWire("Create the flatscreen prototype first so a screen exists.");
            return;
        }
        ScummV5Vm vm = FindScreenVm(input);
        if (vm == null)
        {
            AbortWire("Wire the boot bring-up first so a VM exists.");
            return;
        }
        Transform parent = prototypeRoot != null ? prototypeRoot.transform
            : (screenTransform.parent != null ? screenTransform.parent : screenTransform);
        Text talkText = null;
        Transform old = parent.Find("Talk overhead");
        if (old != null) talkText = old.GetComponentInChildren<Text>();
        if (talkText == null)
        {
            talkText = MakeText(parent, "Talk overhead", new Vector3(0f, 2.2f, 1.99f), "");
            talkText.alignment = TextAnchor.MiddleCenter;
            talkText.fontSize = 28;
            Shadow shadow = talkText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(2, -2);
        }
        ScummTalkLayer layer = talkText.GetComponentInParent<ScummTalkLayer>();
        if (layer == null) layer = talkText.gameObject.AddUdonSharpComponent<ScummTalkLayer>();
        layer.vm = vm;
        layer.screen = screenTransform;
        layer.talkCanvas = talkText.transform.parent;
        layer.talkText = talkText;
        Debug.Log("SCUMM boot-r9 talk text wired above actor heads.");
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        DoneWire("Talk text", "Talk text wired. Save, then enter Play mode.");
    }

    [MenuItem("SCUMM/Wire Room Readout")]
    public static void WireRoomReadout()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        GameObject prototypeRoot = GameObject.Find("SCUMM Prototype");
        ScummScreenInput input = FindScreenInput();
        if (input == null || prototypeRoot == null)
        {
            AbortWire("Create the flatscreen prototype first.");
            return;
        }
        ScummV5Vm vm = FindScreenVm(input);
        if (vm == null)
        {
            AbortWire("Wire the boot bring-up first so a VM exists.");
            return;
        }
        Transform title = prototypeRoot.transform.Find("Preview title");
        Text titleLabel = title != null ? title.GetComponentInChildren<Text>() : null;
        if (titleLabel == null)
        {
            titleLabel = MakeText(prototypeRoot.transform, "Preview title", new Vector3(-.8f, 2.5f, 2), "");
            title = titleLabel.transform.parent;
            Debug.Log("SCUMM boot-r9 room readout: re-created missing Preview title text.");
        }
        ScummRoomReadout readout = title.GetComponent<ScummRoomReadout>();
        if (readout == null) readout = title.gameObject.AddUdonSharpComponent<ScummRoomReadout>();
        readout.vm = vm;
        readout.screenInput = input;
        readout.readoutText = titleLabel;
        Debug.Log("SCUMM boot-r9 room readout wired to Preview title.");
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(prototypeRoot.scene);
        DoneWire("Room readout", "Top text now shows the live room number. Save, then enter Play mode.");
    }

    [MenuItem("SCUMM/Wire Sentence Line")]
    public static void WireSentenceDisplay()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        GameObject prototypeRoot = GameObject.Find("SCUMM Prototype");
        ScummScreenInput input = FindScreenInput();
        if (input == null || prototypeRoot == null)
        {
            AbortWire("Create the flatscreen prototype first.");
            return;
        }
        ScummV5Vm vm = FindScreenVm(input);
        if (vm == null)
        {
            AbortWire("Wire the boot bring-up first so a VM exists.");
            return;
        }
        Transform row = prototypeRoot.transform.Find("Verb row");
        if (row == null)
        {
            Transform screenTransform = input.screen != null ? input.screen : input.transform;
            GameObject rowObj = new GameObject("Verb row");
            rowObj.transform.SetParent(prototypeRoot.transform, false);
            row = rowObj.transform;
            Canvas canvas = rowObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = rowObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(320, 200);
            rowObj.transform.localScale = new Vector3(0.005f, 0.006f, 1f);
            row.localPosition = screenTransform.localPosition + new Vector3(0, 0, -0.01f);
            Debug.Log("SCUMM boot-r9 sentence line: re-created missing Verb row canvas.");
        }
        Transform existing = row.Find("Sentence line");
        Text sentenceText = existing != null ? existing.GetComponent<Text>() : null;
        if (sentenceText == null)
        {
            GameObject sentenceObj = new GameObject("Sentence line");
            sentenceObj.transform.SetParent(row, false);
            sentenceText = sentenceObj.AddComponent<Text>();
            sentenceText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform rect = sentenceText.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(320, 16);
            rect.anchoredPosition = new Vector2(160, -132);
            Shadow shadow = sentenceObj.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(1, -1);
        }
        sentenceText.fontSize = 10;
        sentenceText.alignment = TextAnchor.MiddleCenter;
        sentenceText.color = Color.white;
        sentenceText.horizontalOverflow = HorizontalWrapMode.Overflow;
        sentenceText.verticalOverflow = VerticalWrapMode.Overflow;
        sentenceText.text = "";
        ScummSentenceDisplay display = row.GetComponent<ScummSentenceDisplay>();
        if (display == null) display = row.gameObject.AddUdonSharpComponent<ScummSentenceDisplay>();
        display.vm = vm;
        display.screenInput = input;
        display.sentenceText = sentenceText;
        Debug.Log("SCUMM boot-r9 sentence line wired above the verb row.");
        if (!CompileWired()) return;
        EditorSceneManager.MarkSceneDirty(prototypeRoot.scene);
        DoneWire("Sentence line", "Hover sentence line wired. Save, then enter Play mode.");
    }

    [MenuItem("SCUMM/Wire Lookout Bring-Up")]
    public static void WireLookout()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        string dir = "Assets/Scumm/Generated/MonkeyIsland/scripts";
        if (!Directory.Exists(dir))
        {
            AbortWire("Run Tools/import_monkey.py first (see SCUMM.md).");
            return;
        }
        int size = 0;
        foreach (string path in Directory.GetFiles(dir, "global-*.bytes"))
        {
            int id;
            if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "global-", out id) && id + 1 > size)
                size = id + 1;
        }
        if (size == 0) throw new System.InvalidOperationException("No global scripts in " + dir);
        TextAsset[] scripts = new TextAsset[size];
        foreach (string path in Directory.GetFiles(dir, "global-*.bytes"))
        {
            int id;
            if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "global-", out id))
                scripts[id] = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }
        ReplaceRoot("SCUMM Lookout Bring-Up");
        GameObject root = new GameObject("SCUMM Lookout Bring-Up");
        Undo.RegisterCreatedObjectUndo(root, "Wire SCUMM lookout bring-up");
        PositionAtSpawn(root.transform);
        root.transform.Translate(4.4f, 0f, 0f, Space.Self);
        TextAsset tables = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Scumm/Generated/MonkeyIsland/object-tables.bytes");
        int roomSize = 0;
        foreach (string path in Directory.GetFiles("Assets/Scumm/Generated/RoomBlobs", "rt-room-*.bytes"))
        {
            int id;
            if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "rt-room-", out id) && id + 1 > roomSize)
                roomSize = id + 1;
        }
        TextAsset[] rooms = roomSize > 0 ? new TextAsset[roomSize] : null;
        if (rooms != null)
        {
            foreach (string path in Directory.GetFiles("Assets/Scumm/Generated/RoomBlobs", "rt-room-*.bytes"))
            {
                int id;
                if (TryParseScriptId(Path.GetFileNameWithoutExtension(path), "rt-room-", out id))
                    rooms[id] = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
        }
        // Gameplay scene, not a boot probe: the engine idles in its input
        // waiters indefinitely, so the runaway guard is ~100 minutes, not 60s.
        WireHarness(root.transform, "Lookout boot", 0, scripts, 1,
            500, 360000, 0, null, null, null, null, 0, 0, -1, -1, rooms, tables);
        ScummV5Harness harness = root.GetComponentInChildren<ScummV5Harness>();
        if (harness == null) throw new System.InvalidOperationException("Lookout harness was not created.");
        harness.autoEnterRoom = 38;
        harness.autoEnterTicks = 1060;
        harness.lookoutWalkTest = true;
        // Point every screen layer at the Lookout VM so one Play session shows it.
        ScummScreenInput input = FindScreenInput();
        if (input != null)
        {
            ScummRoomBackground backgrounds = input.GetComponent<ScummRoomBackground>();
            if (backgrounds != null) backgrounds.vm = harness.vm;
            ScummVerbDisplay verbs = input.GetComponentInChildren<ScummVerbDisplay>();
            if (verbs == null) verbs = Object.FindObjectOfType<ScummVerbDisplay>();
            if (verbs != null) verbs.vm = harness.vm;
            ScummObjectLayer objects = input.GetComponent<ScummObjectLayer>();
            if (objects != null) objects.vm = harness.vm;
            ScummActorLayer actors = input.GetComponent<ScummActorLayer>();
            if (actors != null) actors.vm = harness.vm;
            ScummTalkLayer talk = null;
            foreach (ScummTalkLayer candidate in Object.FindObjectsOfType<ScummTalkLayer>(true))
            {
                talk = candidate;
                break;
            }
            if (talk != null) talk.vm = harness.vm;
            ScummRoomReadout readout = null;
            foreach (ScummRoomReadout candidate in Object.FindObjectsOfType<ScummRoomReadout>(true))
            {
                readout = candidate;
                break;
            }
            if (readout != null) readout.vm = harness.vm;
            if (readout != null) readout.screenInput = input;
            ScummSentenceDisplay sentence = null;
            foreach (ScummSentenceDisplay candidate in Object.FindObjectsOfType<ScummSentenceDisplay>(true))
            {
                sentence = candidate;
                break;
            }
            if (sentence != null) sentence.vm = harness.vm;
            harness.screenInput = input;
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("SCUMM boot-r9 Lookout bring-up wired (auto-enter room 38 once the title releases, ~tick 1060).");
        DoneWire("Lookout bring-up (auto-enter room 38 once the title releases)",
            "Lookout walk diagnostic wired. After the title releases, Guybrush is explicitly placed at (312,73) and walks toward (154,106). This is a diagnostic, not the original entrance sequence. Save and enter Play mode.");
    }

    [MenuItem("SCUMM/Wire All (Prototype + Lookout Play Test)")]
    public static void WireAll()
    {
        if (!RequireEditMode()) return;
        if (!EnsureProgramAssets()) return;
        s_wireAllActive = true;
        s_wireAllError = null;
        s_wireAllDone.Clear();
        try
        {
            // One click: prototype if missing, then Lookout first so every
            // layer below binds straight to its VM (no rebind pass needed).
            if (s_wireAllError == null && FindScreenInput() == null) Create();
            if (s_wireAllError == null) WireLookout();
            if (s_wireAllError == null) WireBackgrounds();
            if (s_wireAllError == null) WireVerbs();
            if (s_wireAllError == null) WireObjectAtlases();
            if (s_wireAllError == null) WireActorSprites();
            if (s_wireAllError == null) WireTalkText();
            if (s_wireAllError == null) WireRoomReadout();
            if (s_wireAllError == null) WireSentenceDisplay();
            if (s_wireAllError == null)
            {
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                    AbortWire("UdonSharp reports compilation errors. See the Console before running the harness.");
            }
        }
        catch (System.Exception e)
        {
            AbortWire("Wire All stopped: " + e.Message);
        }
        finally
        {
            s_wireAllActive = false;
        }
        if (s_wireAllError != null)
        {
            string done = s_wireAllDone.Count > 0 ? "\n\nCompleted before the error:\n- "
                + string.Join("\n- ", s_wireAllDone.ToArray()) : "";
            EditorUtility.DisplayDialog("SCUMM Wire All stopped",
                s_wireAllError + done, "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("SCUMM Wire All complete",
                "Wired:\n- " + string.Join("\n- ", s_wireAllDone.ToArray())
                + "\n\nSave, then enter Play mode.", "OK");
        }
    }

    private static bool TryParseScriptId(string name, string prefix, out int id)
    {
        id = 0;
        return name.StartsWith(prefix) && int.TryParse(name.Substring(prefix.Length), out id);
    }

    private static void WireHarness(Transform root, string title, int index, TextAsset[] scripts,
        int entry, int budget, int maxTicks, int mode, int[] vars, int[] values,
        int[] bitIndices, int[] bitValues, int minTicks, int expectFault,
        int faultScript, int faultOpcode, TextAsset[] rooms = null, TextAsset tables = null)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Interact to run: " + title;
        cube.transform.SetParent(root);
        cube.transform.localPosition = new Vector3(index * 0.7f - 0.7f, 1.2f, 2);
        cube.transform.localScale = Vector3.one * 0.25f;
        ScummV5Vm vm = cube.AddUdonSharpComponent<ScummV5Vm>();
        vm.globalScripts = scripts;
        vm.roomBlobs = rooms;
        vm.globalTables = tables;
        Text status = MakeText(root.transform, title + " status",
            new Vector3(index * 0.7f - 1.05f, 0.85f, 2), title + ": pending");
        ScummV5Harness harness = cube.AddUdonSharpComponent<ScummV5Harness>();
        harness.vm = vm;
        harness.scripts = scripts;
        harness.entryScript = entry;
        harness.instructionsPerFrame = budget;
        harness.maxTicks = maxTicks;
        harness.mode = mode;
        harness.checkVars = vars;
        harness.checkValues = values;
        harness.checkBitIndices = bitIndices;
        harness.checkBitValues = bitValues;
        harness.minTicks = minTicks;
        harness.expectFault = expectFault;
        harness.expectedFaultScript = faultScript;
        harness.expectedFaultOpcode = faultOpcode;
        harness.statusText = status;
        ScummScreenInput existingInput = Object.FindObjectOfType<ScummScreenInput>();
        if (existingInput != null) harness.screenInput = existingInput;
    }

    private static readonly string[] RuntimeScripts = {
        "Assets/Scumm/Runtime/ScummV5Vm.cs",
        "Assets/Scumm/Runtime/ScummV5Harness.cs",
        "Assets/Scumm/Runtime/ScummV5Benchmark.cs",
        "Assets/Scumm/Runtime/ScummScreenInput.cs",
        "Assets/Scumm/Runtime/ScummRoomBackground.cs",
        "Assets/Scumm/Runtime/ScummVerbDisplay.cs",
        "Assets/Scumm/Runtime/ScummObjectLayer.cs",
        "Assets/Scumm/Runtime/ScummTalkLayer.cs",
        "Assets/Scumm/Runtime/ScummActorLayer.cs",
        "Assets/Scumm/Runtime/ScummRoomReadout.cs",
        "Assets/Scumm/Runtime/ScummSentenceDisplay.cs",
    };

    // UdonSharp only sets up behaviours whose script has a sibling program asset.
    // Create missing program assets and force U# compilation even when all exist.
    private static bool EnsureProgramAssets()
    {
        foreach (string csPath in RuntimeScripts)
        {
            string assetPath = Path.ChangeExtension(csPath, ".asset");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                continue;
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(csPath);
            if (script == null)
            {
                EditorUtility.DisplayDialog("SCUMM",
                    "Missing script " + csPath + ". The runtime scripts must exist before wiring.", "OK");
                return false;
            }
            UdonSharpProgramAsset programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = script;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            Debug.Log("SCUMM created program asset " + assetPath);
        }
        AssetDatabase.Refresh();
        Debug.Log("SCUMM boot-r9: requesting forced UdonSharp compilation. Project: " + Application.dataPath);
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
        {
            EditorUtility.DisplayDialog("SCUMM compile failed",
                "UdonSharp reports compilation errors. See the Console before running the harness.", "OK");
            return false;
        }
        return true;
    }

    private static void ReplaceRoot(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("SCUMM removed previous " + name + " before re-wiring.");
        }
    }

    private static bool RequireEditMode()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
        EditorUtility.DisplayDialog("Exit Play mode first", "Stop Play mode before creating or updating the screen, then save the scene. Play-mode scene changes would be lost.", "OK");
        return false;
    }

    private static void PositionAtSpawn(Transform root)
    {
        VRC_SceneDescriptor descriptor = Object.FindObjectOfType<VRC_SceneDescriptor>();
        if (descriptor == null || descriptor.spawns == null || descriptor.spawns.Length == 0 || descriptor.spawns[0] == null)
        {
            Debug.LogWarning("No VRChat spawn found; SCUMM screen will use the scene origin.");
            return;
        }
        Transform spawn = descriptor.spawns[0];
        root.position = spawn.position;
        root.rotation = Quaternion.Euler(0, spawn.eulerAngles.y, 0);
    }

    private static bool ApplyRoomPreview(Material material)
    {
        string indicesPath = RoomPreview + "/indices.png";
        string palettePath = RoomPreview + "/palette.png";
        if (!File.Exists(indicesPath) || !File.Exists(palettePath)) return false;
        ConfigureTexture(indicesPath, false);
        ConfigureTexture(palettePath, true);
        Texture2D indices = AssetDatabase.LoadAssetAtPath<Texture2D>(indicesPath);
        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath);
        if (indices == null || palette == null || palette.width != 256 || palette.height != 1)
            throw new System.InvalidOperationException("Room preview textures did not import correctly.");
        material.SetTexture("_IndexTex", indices);
        material.SetTexture("_PaletteTex", palette);
        material.SetVector("_RoomSize", new Vector4(indices.width, indices.height, 0, 0));
        material.SetFloat("_ShowRoom", 1);
        EditorUtility.SetDirty(material);
        return true;
    }

    private static void ConfigureTexture(string path, bool sRgb)
    {
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = sRgb;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
    }

    private static Text MakeText(Transform parent, string name, Vector3 position, string text)
    {
        GameObject canvasObj = new GameObject(name);
        canvasObj.transform.SetParent(parent);
        canvasObj.transform.localPosition = position;
        canvasObj.transform.localScale = Vector3.one * 0.005f;
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(320, 120);
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(canvasObj.transform, false);
        Text label = textObj.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 24;
        label.alignment = TextAnchor.UpperLeft;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform textRect = label.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        label.text = text;
        return label;
    }
}
#else
using UnityEditor;

public static class ScummPrototypeBuilder
{
    [MenuItem("SCUMM/Create Flatscreen Prototype")]
    public static void Create()
    {
        EditorUtility.DisplayDialog("VRChat Worlds SDK required",
            "The SCUMM prototype needs the VRChat Worlds SDK, which includes UdonSharp. "
            + "Allow the template's package restore to finish. If it fails, add this existing "
            + "project in VRChat Creator Companion and install/restore the Worlds SDK through Manage Project. "
            + "Then reopen Unity and wait for compilation. Do not add UDONSHARP manually to Player Settings.",
            "OK");
    }
}
#endif
