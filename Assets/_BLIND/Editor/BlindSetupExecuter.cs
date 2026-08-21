using UnityEngine;
using UnityEditor;
using System.Linq;
using BLIND.EditorTools;
using System.Collections.Generic;

[InitializeOnLoad]
public class BlindSetupExecuter
{
    static BlindSetupExecuter()
    {
        EditorApplication.delayCall += RunOnce;
    }

    static void RunOnce()
    {
        if (EditorPrefs.GetBool("BlindAutoSetupDone_v4", false)) return;

        Debug.Log("--- BLIND Auto Setup Executing ---");

        // 1. Ensure SystemManager and GameObjects are placed
        EnsureSystemManagers();

        // 2. Door Fixes
        try {
            var df = System.Type.GetType("BLIND.EditorTools.BlindDoorFixes, Assembly-CSharp-Editor");
            if (df != null) Debug.Log((string)df.GetMethod("FixAll").Invoke(null, null));
        } catch(System.Exception e) { Debug.LogError(e); }

        // 3. Fix room19 Lights
        try {
            Debug.Log(BlindRoomFixes.FixRoom19Lights());
        } catch(System.Exception e) { Debug.LogError(e); }

        // 4. Gimmick Builder
        try {
            var gb = System.Type.GetType("BLIND.EditorTools.BlindGimmickBuilder, Assembly-CSharp-Editor");
            if (gb != null) Debug.Log((string)gb.GetMethod("BuildAll").Invoke(null, null));
        } catch(System.Exception e) { Debug.LogError(e); }

        // 5. Vision Builder
        try {
            var vb = System.Type.GetType("BLIND.EditorTools.BlindVisionBuilder, Assembly-CSharp-Editor");
            if (vb != null) Debug.Log((string)vb.GetMethod("BuildMine").Invoke(null, null));
        } catch(System.Exception e) { Debug.LogError(e); }

        // 6. EchoReceiver Collector
        try {
            var col = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                .FirstOrDefault(x => x.Name == "EchoReceiverCollector");
            if (col != null) col.GetMethod("Collect").Invoke(null, null);
        } catch(System.Exception e) { Debug.LogError(e); }

        // 7. Tuning Apply
        try {
            var tu = System.Type.GetType("BLIND.EditorTools.BlindTuning1200, Assembly-CSharp-Editor");
            if (tu != null) tu.GetMethod("Apply").Invoke(null, null);
        } catch(System.Exception e) { Debug.LogError(e); }

        EditorPrefs.SetBool("BlindAutoSetupDone_v4", true);
        Debug.Log("--- BLIND Auto Setup Finished ---");
        
        // Remove self to keep things clean
        AssetDatabase.DeleteAsset("Assets/_BLIND/Editor/BlindSetupExecuter.cs");
    }

    static void EnsureSystemManagers()
    {
        var gm = GameObject.Find("GameManagement");
        if (gm == null)
        {
            gm = new GameObject("GameManagement");
        }

        // Add PlayerVisionController
        var pvc = AddUdonComponentIfMissing(gm, "PlayerVisionController");
        if (pvc != null) {
            SetObj(pvc, "echoCullingMask", 1 << 23); // Echo layer
            SetObj(pvc, "thermalCullingMask", 1 << 22); // Thermal layer
            SetObj(pvc, "memoryCullingMask", (1 << 0) | (1 << 24) | (1 << 9) | (1 << 10)); // Default, Memory, Player, PlayerLocal
            PushUdon(pvc);
        }

        // Add CheckpointManager
        var cm = AddUdonComponentIfMissing(gm, "CheckpointManager");

        // Add ViewpointShuffleManager
        var vsm = AddUdonComponentIfMissing(gm, "ViewpointShuffleManager");
        if (vsm != null && pvc != null) {
            SetObjRef(vsm, "localVisionController", pvc);
            PushUdon(vsm);
        }

        // Add ShuffleSequenceManager
        var ssm = AddUdonComponentIfMissing(gm, "ShuffleSequenceManager");
        if (ssm != null) {
            if (vsm != null) SetObjRef(ssm, "shuffleManager", vsm);
            if (cm != null) SetObjRef(ssm, "checkpointManager", cm);
            SetArray(ssm, "correctOrder", new int[] {0, 1, 2});
            PushUdon(ssm);
        }
        
        // Add EchoEmitter
        var echo = AddUdonComponentIfMissing(gm, "EchoEmitter");
        if (echo != null && pvc != null) {
            SetObjRef(echo, "localVisionController", pvc);
            PushUdon(echo);
        }
    }
    
    static Component AddUdonComponentIfMissing(GameObject go, string typeName)
    {
        var t = System.Type.GetType(typeName + ", Assembly-CSharp");
        if (t == null) return null;
        if (go.GetComponent(t) != null) return go.GetComponent(t);

        var undoType = System.Type.GetType("UdonSharpEditor.UdonSharpUndo, UdonSharp.Editor");
        Component c = null;
        if (undoType != null)
        {
            var mi = undoType.GetMethod("AddComponent", new[] { typeof(GameObject), typeof(System.Type) });
            if (mi != null) c = mi.Invoke(null, new object[] { go, t }) as Component;
        }
        if (c == null) c = go.AddComponent(t);
        return c;
    }

    static void PushUdon(Component c)
    {
        var usb = c as UdonSharp.UdonSharpBehaviour;
        if (usb == null) return;
        if (UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) == null) return;
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
    }
    
    static void SetObj(Component c, string field, int value)
    {
        var so = new SerializedObject(c);
        var p = so.FindProperty(field);
        if (p != null) { p.intValue = value; so.ApplyModifiedProperties(); }
    }
    
    static void SetObjRef(Component c, string field, Object value)
    {
        var so = new SerializedObject(c);
        var p = so.FindProperty(field);
        if (p != null) { p.objectReferenceValue = value; so.ApplyModifiedProperties(); }
    }
    
    static void SetArray(Component c, string field, int[] values)
    {
        var so = new SerializedObject(c);
        var p = so.FindProperty(field);
        if (p != null) {
            p.arraySize = values.Length;
            for(int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).intValue = values[i];
            so.ApplyModifiedProperties();
        }
    }
}
