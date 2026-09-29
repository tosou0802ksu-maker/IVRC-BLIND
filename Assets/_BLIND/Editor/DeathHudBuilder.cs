using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 死亡時に目の前に出す文字の板を作り、CheckpointManager につなぐ。
    ///
    /// ・UI レイヤー(5)に置く。役ごとにカメラの映すレイヤーが分かれているので、
    ///   PlayerVisionController が全役のカメラに UI レイヤーを足している
    /// ・日本語用の TextMeshPro フォントがプロジェクトに無いので、標準の UI Text を使う
    ///   （OS の日本語フォントで表示される）
    /// ・壁の中に顔を入れても文字が隠れないよう、奥行きを無視して手前に描くマテリアルにする
    /// 何度押しても作り直すだけ。Ctrl+Z で戻せる。
    /// </summary>
    public static class DeathHudBuilder
    {
        const string HudName = "DeathHud";
        const int LayerUI = 5;
        const string MatPath = "Assets/_BLIND/Art/Materials/DeathHud_Overlay.mat";

        [MenuItem("BLIND/死亡演出/文字を表示する板を作る")]
        public static void BuildMenu()
        {
            var msg = Build();
            Debug.Log(msg);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        public static string Build()
        {
            var system = GameObject.Find("=== SYSTEM ===");
            if (system == null) return "=== SYSTEM === が無い";

            var cm = Object.FindObjectsOfType<MonoBehaviour>(true).FirstOrDefault(c => c != null && c.GetType().Name == "CheckpointManager");
            if (cm == null) return "CheckpointManager が見つからない";

            Undo.SetCurrentGroupName("死亡演出の文字");
            int group = Undo.GetCurrentGroup();

            var old = system.transform.Find(HudName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // --- 板（ワールド空間のキャンバス。1単位=1mm）---
            var root = new GameObject(HudName, typeof(RectTransform), typeof(Canvas));
            Undo.RegisterCreatedObjectUndo(root, "death hud");
            root.transform.SetParent(system.transform, false);
            root.layer = LayerUI;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1600f, 500f);
            rt.localScale = Vector3.one * 0.0006f;   // 幅 96cm 相当（大きい文字でも1行に収まりやすく）

            // --- 暗転の幕（文字より先に作る＝後ろに描かれる）---
            // 顔から 0.7m 先で視界を覆い切る大きさ（幅・高さ 6m 相当）にする
            var blackGo = new GameObject("DeathBlackout", typeof(RectTransform), typeof(Image));
            blackGo.transform.SetParent(root.transform, false);
            blackGo.layer = LayerUI;
            var brt = blackGo.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(10000f, 10000f);
            var black = blackGo.GetComponent<Image>();
            black.color = Color.black;
            black.raycastTarget = false;
            black.material = OverlayMaterial();

            // --- 文字 ---
            var textGo = new GameObject("DeathText", typeof(RectTransform), typeof(Text), typeof(Outline));
            textGo.transform.SetParent(root.transform, false);
            textGo.layer = LayerUI;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 110;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(1f, 0.25f, 0.2f, 1f);   // 赤みの白抜き
            text.text = "やられた…";
            text.raycastTarget = false;
            text.material = OverlayMaterial();

            var outline = textGo.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);

            root.SetActive(false);

            // --- CheckpointManager につなぐ ---
            var so = new SerializedObject(cm);
            so.FindProperty("deathHud").objectReferenceValue = root;
            so.FindProperty("deathText").objectReferenceValue = text;
            so.FindProperty("deathBlackout").objectReferenceValue = black;
            so.ApplyModifiedProperties();
            PushUdon(cm);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(system.scene);
            return "死亡演出の文字の板を === SYSTEM ===/" + HudName + " に作り、" + cm.name + " の CheckpointManager につないだ\n"
                 + "文字の色・大きさは DeathText で変えられる。標準の文字は CheckpointManager の Default Death Message";
        }

        /// <summary>名前に laser を含む物の下にある HazardZone に、まとめて同じ文字（と音）を入れる。</summary>
        [MenuItem("BLIND/死亡演出/レーザーの死亡判定にまとめて文字を入れる")]
        public static void LaserMessageMenu()
        {
            var zones = Object.FindObjectsOfType<MonoBehaviour>(true)
                .Where(c => c != null && c.GetType().Name == "HazardZone" && UnderLaser(c.transform)).ToList();
            if (zones.Count == 0) { EditorUtility.DisplayDialog("BLIND", "レーザーの死亡判定が見つからない", "OK"); return; }

            // 1つ目に入れた文字と音を、残り全部に写す
            var first = new SerializedObject(zones[0]);
            string msg = first.FindProperty("deathMessage").stringValue;
            var clip = first.FindProperty("deathClip").objectReferenceValue;
            if (string.IsNullOrEmpty(msg) && clip == null)
            {
                Selection.activeObject = zones[0].gameObject;
                EditorUtility.DisplayDialog("BLIND",
                    "先に1本目（今選択した " + zones[0].transform.parent.name + " の " + zones[0].name + "）の HazardZone に\n"
                    + "Death Message と Death Clip を入れてから、もう一度押してください。\n残り " + (zones.Count - 1) + " 本に同じ内容を写します。", "OK");
                return;
            }

            Undo.SetCurrentGroupName("レーザーの死亡演出");
            int group = Undo.GetCurrentGroup();
            foreach (var z in zones)
            {
                var so = new SerializedObject(z);
                so.FindProperty("deathMessage").stringValue = msg;
                so.FindProperty("deathClip").objectReferenceValue = clip;
                so.ApplyModifiedProperties();
                PushUdon(z);
                EditorSceneManager.MarkSceneDirty(z.gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);
            EditorUtility.DisplayDialog("BLIND", "レーザーの死亡判定 " + zones.Count + " 本に「" + msg + "」" + (clip != null ? "と " + clip.name : "") + " を入れた（Ctrl+Z で戻せる）", "OK");
        }

        static bool UnderLaser(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name.ToLower().Contains("laser")) return true;
            return false;
        }

        static Material OverlayMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null)
            {
                m = new Material(Shader.Find("UI/Default"));
                AssetDatabase.CreateAsset(m, MatPath);
            }
            // 奥行きを無視して手前に描く（壁に近づいても文字が隠れない）
            m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void PushUdon(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour; if (usb == null) return;
            if (UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) == null) return;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
        }
    }
}
