using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BLIND.EditorTools
{
    /// <summary>
    /// タイトル画面（入室した人の目の前に出す板＋背景の動画）を作り、TitleScreen につなぐ。
    ///
    /// ・板は UI レイヤー(5)のワールド空間キャンバス（1単位=1mm）。DeathHudBuilder と同じく、
    ///   奥行きを無視して手前に描くマテリアル（DeathHud_Overlay.mat）を使う
    /// ・背景の動画は VRCUnityVideoPlayer → RenderTexture → RawImage
    /// ・ボタンは「影」「枠」「面」「文字」の重ね。選ぶと面が影の位置まで沈む（TitleScreen.RefreshButtons）
    /// ・何度押しても作り直すだけ。動画の URL は前の値を引き継ぐ。Ctrl+Z で戻せる。
    /// </summary>
    public static class TitleScreenBuilder
    {
        const string RootName = "TitleScreen";
        const int LayerUI = 5;
        const string ArtDir = "Assets/_BLIND/Art/Title";
        const string TitleTexPath = ArtDir + "/title_THREE_VIEWS_ONE_WORLD_1920x1080.png";
        const string RenderTexPath = ArtDir + "/TitleVideo.renderTexture";
        const string OverlayMatPath = "Assets/_BLIND/Art/Materials/DeathHud_Overlay.mat";

        [MenuItem("BLIND/タイトル画面/作り直す")]
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

            var titleTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TitleTexPath);
            if (titleTex == null) return "タイトル画像が無い: " + TitleTexPath;
            var overlay = OverlayMaterial();
            var videoTex = VideoTexture();

            Undo.SetCurrentGroupName("タイトル画面");
            int group = Undo.GetCurrentGroup();

            // 前の URL を引き継いでから作り直す
            string oldUrl = "";
            var old = system.transform.Find(RootName);
            if (old != null)
            {
                var oldTs = old.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null && c.GetType().Name == "TitleScreen");
                if (oldTs != null)
                {
                    var p = new SerializedObject(oldTs).FindProperty("videoUrl.url");
                    if (p != null) oldUrl = p.stringValue;
                }
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "title screen");
            root.transform.SetParent(system.transform, false);

            // --- 動画プレイヤー ---
            // TitleScreen と同じ GameObject に付ける。動画の「始まった」「エラー」の知らせ
            // (OnVideoStart / OnVideoError)は、動画プレイヤーと同じ GameObject の Udon にしか届かないため
            // (別の子に付けていたときは、TitleScreen に一度も届かなかった)。
            var vp = AddVrc(root, "VRC.SDK3.Video.Components.VRCUnityVideoPlayer");
            if (vp == null) return "VRCUnityVideoPlayer が付けられない";
            var vso = new SerializedObject(vp);
            vso.FindProperty("autoPlay").boolValue = false;   // TitleScreen が入室時に再生する
            vso.FindProperty("loop").boolValue = true;
            var rm = vso.FindProperty("renderMode");
            int rtIdx = System.Array.IndexOf(rm.enumNames, "RenderTexture");
            if (rtIdx >= 0) rm.enumValueIndex = rtIdx;
            vso.FindProperty("targetTexture").objectReferenceValue = videoTex;
            vso.FindProperty("maximumResolution").intValue = 1080;
            vso.ApplyModifiedProperties();

            // --- 板（ワールド空間のキャンバス。1単位=1mm）---
            // 文字とボタンは、デスクトップの視野（顔から 0.6m 先で高さ約 0.7m）に収まる ±300mm の中に置く。
            // 背景の動画だけは VR の広い視野でも端が見えないよう大きくする
            var panel = new GameObject("TitlePanel", typeof(RectTransform), typeof(Canvas));
            panel.transform.SetParent(root.transform, false);
            panel.layer = LayerUI;
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1100;
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(2400f, 1350f);
            prt.localScale = Vector3.one * 0.001f;   // 幅 2.4m。顔から 0.6m 先で視界の大部分を覆う

            // 視界の外側まで黒で覆う幕（動画が届かない端用）
            var backdrop = MakeImage(panel.transform, "Backdrop", new Vector2(12000f, 12000f), Vector2.zero, Color.black, overlay);

            // 背景の動画（少し暗くして文字を読みやすく）
            var video = MakeGo(panel.transform, "Video", typeof(RawImage));
            SetRect(video, new Vector2(1240f, 698f), Vector2.zero);   // 大きさは TitleScreen が VR かどうかで決め直す
            var vraw = video.GetComponent<RawImage>();
            vraw.texture = videoTex;
            vraw.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            vraw.material = overlay;
            vraw.raycastTarget = false;

            // タイトル（背景透過の画像）
            var title = MakeGo(panel.transform, "Title", typeof(RawImage));
            SetRect(title, new Vector2(900f, 506f), new Vector2(0f, 140f));
            var traw = title.GetComponent<RawImage>();
            traw.texture = titleTex;
            traw.material = overlay;
            traw.raycastTarget = false;

            // --- メニュー（今はスタートだけ）---
            var menu = MakeGo(panel.transform, "Menu");
            SetRect(menu, new Vector2(400f, 200f), new Vector2(0f, -120f));

            var start = MakeGo(menu.transform, "Start");
            SetRect(start, new Vector2(300f, 80f), Vector2.zero);

            // 影（ボタンが浮いているときだけ見える）
            var shadow = MakeImage(start.transform, "Shadow", new Vector2(306f, 86f), Vector2.zero, new Color(0f, 0f, 0f, 1f), overlay);

            // 面（この RectTransform を上下させて「沈む」を表す）
            var face = MakeGo(start.transform, "Face");
            SetRect(face, new Vector2(306f, 86f), Vector2.zero);
            var frame = MakeImage(face.transform, "Frame", new Vector2(306f, 86f), Vector2.zero, new Color(1f, 1f, 1f, 0.55f), overlay);
            // 色の計算がリニアなので、半透明にすると下の枠の白が思ったより強く透けて灰色に見える。不透明にする
            MakeImage(face.transform, "Fill", new Vector2(296f, 76f), Vector2.zero, new Color(0.07f, 0.07f, 0.09f, 1f), overlay);
            var label = MakeText(face.transform, "Label", "スタート", 40, new Vector2(296f, 76f), Vector2.zero, Color.white, overlay);

            // 操作の説明
            MakeText(panel.transform, "Hint", "スティック（W / S）で選んで、トリガー（左クリック）で決定\n1〜2人で遊ぶときは、Q キー（VR は右スティックを上）で視点を切り替え", 22,
                     new Vector2(1200f, 80f), new Vector2(0f, -245f), new Color(1f, 1f, 1f, 0.75f), overlay);

            panel.SetActive(false);   // TitleScreen が入室時に表示する

            // --- TitleScreen ---
            var ts = AddUdon(root, "TitleScreen");
            if (ts == null) return "TitleScreen が付けられない（コンパイルエラーがないか確認）";
            SetSyncMode(ts, "None");
            var so = new SerializedObject(ts);
            so.FindProperty("panel").objectReferenceValue = panel;
            SetArray(so.FindProperty("buttonFaces"), face.GetComponent<RectTransform>());
            SetArray(so.FindProperty("buttonFrames"), frame);
            SetArray(so.FindProperty("buttonShadows"), shadow.gameObject);
            so.FindProperty("videoPlayer").objectReferenceValue = vp;
            so.FindProperty("videoRect").objectReferenceValue = video.GetComponent<RectTransform>();
            so.FindProperty("videoUrl.url").stringValue = oldUrl;
            so.ApplyModifiedProperties();
            PushUdon(ts);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(system.scene);
            return "タイトル画面を === SYSTEM ===/" + RootName + " に作った\n"
                 + "背景動画の URL: " + (oldUrl.Length > 0 ? oldUrl : "（未設定。TitleScreen の Video Url に入れる）");
        }

        // ------------------------------------------------------------

        static GameObject MakeGo(Transform parent, string name, params System.Type[] comps)
        {
            var all = new System.Type[comps.Length + 1];
            all[0] = typeof(RectTransform);
            comps.CopyTo(all, 1);
            var go = new GameObject(name, all);
            go.transform.SetParent(parent, false);
            go.layer = LayerUI;
            return go;
        }

        static void SetRect(GameObject go, Vector2 size, Vector2 pos)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        static Image MakeImage(Transform parent, string name, Vector2 size, Vector2 pos, Color color, Material mat)
        {
            var go = MakeGo(parent, name, typeof(Image));
            SetRect(go, size, pos);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.material = mat;
            img.raycastTarget = false;
            return img;
        }

        static Text MakeText(Transform parent, string name, string s, int size, Vector2 rect, Vector2 pos, Color color, Material mat)
        {
            var go = MakeGo(parent, name, typeof(Text));
            SetRect(go, rect, pos);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");   // 日本語は OS のフォントで出る
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = color;
            t.text = s;
            t.material = mat;
            t.raycastTarget = false;
            return t;
        }

        static void SetArray(SerializedProperty p, Object value)
        {
            p.arraySize = 1;
            p.GetArrayElementAtIndex(0).objectReferenceValue = value;
        }

        static Material OverlayMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OverlayMatPath);
            if (m == null)
            {
                m = new Material(Shader.Find("UI/Default"));
                AssetDatabase.CreateAsset(m, OverlayMatPath);
            }
            m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            EditorUtility.SetDirty(m);
            return m;
        }

        static RenderTexture VideoTexture()
        {
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexPath);
            if (rt == null)
            {
                rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
                AssetDatabase.CreateAsset(rt, RenderTexPath);
            }
            return rt;
        }

        static Component AddUdon(GameObject go, string typeName)
        {
            var t = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (t == null) { Debug.LogError("BLIND: 型が見つからない " + typeName); return null; }
            return UdonSharpEditor.UdonSharpUndo.AddComponent(go, t);
        }

        static Component AddVrc(GameObject go, string fullName)
        {
            System.Type t = null;
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(fullName);
                if (t != null) break;
            }
            if (t == null) { Debug.LogError("BLIND: VRC の型が見つからない " + fullName); return null; }
            return Undo.AddComponent(go, t);
        }

        static void SetSyncMode(Component c, string mode)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour;
            if (usb == null) return;
            var ub = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb);
            if (ub == null) return;
            var so = new SerializedObject(ub);
            var p = so.FindProperty("_syncMethod");
            if (p == null) return;
            int idx = System.Array.IndexOf(p.enumNames, mode);
            if (idx >= 0) { p.enumValueIndex = idx; so.ApplyModifiedProperties(); }
        }

        static void PushUdon(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour;
            if (usb == null) return;
            if (UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) == null) return;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
        }
    }
}
