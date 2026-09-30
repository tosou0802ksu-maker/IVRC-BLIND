using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// ゴール（room19）の奥の壁にクレジット看板を作る。
    ///
    /// 中身は Assets/_BLIND/Credits/Credits.txt。書き換えたらメニューから作り直す。
    /// 何度押しても前の看板を消してから作るので、重複しない。
    ///
    /// ⚠️ 3人とも読めるように、同じ看板を3枚重ねている。
    ///    過去人 = layer 24（Memory）、サーモ = layer 22、エコロケ = layer 23。
    ///    サーモ／エコロケの専用シェーダーを通すと文字が熱や線になって読めないので、
    ///    3枚とも自発光の普通の材質にしてある。
    ///    過去人の分を layer 0 ではなく 24 にしているのは、BlindVisionBuilder が
    ///    layer 0 の物からサーモ・エコロケ用の複製を自動で作るため（24 は対象外）。
    /// </summary>
    public static class BlindCreditsBoard
    {
        const string CreditsPath = "Assets/_BLIND/Credits/Credits.txt";
        const string FontPath = "Assets/UnityTechnologies/ParticlePack/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const string PanelMatPath = "Assets/_BLIND/Credits/CreditsPanel.mat";
        const string RoomPath = "=== ROOMS ===/room19";
        const string RootName = "CreditsBoard_Generated";

        // room19 の北側。壁(z=1.90)の手前に柱(x 23.9〜24.7 と 28.1〜28.9, 手前の面 z≈0.52)が
        // 2本出ていて、壁に貼ると看板の両端が柱に隠れる。柱より手前に独立したボードとして立てる。
        // 入口のシャッター(x=23, z=-2)から入ると左手に見える。
        static readonly Vector3 Center = new Vector3(26.35f, 1.95f, 0.40f);
        const float Width = 5.6f;
        const float Height = 3.0f;

        static readonly int[] Layers = { 24, 22, 23 };

        [MenuItem("BLIND/クレジット/看板を作り直す")]
        public static void Build()
        {
            if (!File.Exists(CreditsPath))
            {
                Debug.LogError("クレジット看板: " + CreditsPath + " が無い");
                return;
            }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null)
            {
                Debug.LogError("クレジット看板: フォント " + FontPath + " が無い");
                return;
            }
            var room = GameObject.Find(RoomPath);
            if (room == null)
            {
                Debug.LogError("クレジット看板: " + RoomPath + " が無い");
                return;
            }

            List<string> columns = ParseColumns(File.ReadAllLines(CreditsPath));

            var old = room.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "credits board");
            root.transform.SetParent(room.transform, false);
            root.transform.position = Center;
            root.transform.rotation = Quaternion.identity;   // 文字の表が -Z（部屋の内側）を向く

            Material panelMat = PanelMaterial();

            foreach (int layer in Layers)
            {
                var set = new GameObject("Layer" + layer);
                set.transform.SetParent(root.transform, false);
                set.layer = layer;

                // 背景の板。壁の黄色の上だと白い文字が読めないので暗い板を敷く
                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                panel.name = "Panel";
                panel.layer = layer;
                panel.transform.SetParent(set.transform, false);
                panel.transform.localPosition = new Vector3(0f, 0f, 0.005f);
                panel.transform.localScale = new Vector3(Width, Height, 1f);
                panel.GetComponent<MeshRenderer>().sharedMaterial = panelMat;

                // 見出し
                MakeText(set.transform, layer, font, "Title",
                    "<b>THANK YOU FOR PLAYING  BLIND</b>\n<size=60%><color=#9fb3c8>CREDITS</color></size>",
                    new Vector2(0f, Height * 0.5f - 0.22f), new Vector2(Width - 0.3f, 0.36f), TextAlignmentOptions.Center, 0.4f);

                // 本文（列ごと）
                float top = Height * 0.5f - 0.48f;
                float bodyH = Height - 0.62f;
                float colW = (Width - 0.3f) / Mathf.Max(1, columns.Count);
                var cols = new List<TextMeshPro>();
                for (int i = 0; i < columns.Count; i++)
                {
                    float x = -((Width - 0.3f) * 0.5f) + colW * (i + 0.5f);
                    cols.Add(MakeText(set.transform, layer, font, "Column" + i, columns[i],
                        new Vector2(x, top - bodyH * 0.5f), new Vector2(colW - 0.08f, bodyH), TextAlignmentOptions.TopLeft, 0.09f));
                }
                // 列ごとに自動で大きさが変わると揃わず見苦しい。一番小さい列に全部合わせる
                float minSize = float.MaxValue;
                foreach (var t in cols) { t.ForceMeshUpdate(); minSize = Mathf.Min(minSize, t.fontSize); }
                foreach (var t in cols) { t.enableAutoSizing = false; t.fontSize = minSize; }
            }

            EditorSceneManager.MarkSceneDirty(room.scene);
            Debug.Log("クレジット看板: " + columns.Count + " 列で作り直した（" + RoomPath + "/" + RootName + "）");
        }

        // Credits.txt を列ごとのリッチテキストにする
        static List<string> ParseColumns(string[] lines)
        {
            var cols = new List<string>();
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line == "---")
                {
                    cols.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (sb.Length > 0) sb.Append("\n");
                    sb.Append("<b><color=#ffd27a>").Append(line.Substring(1, line.Length - 2)).Append("</color></b>\n");
                    continue;
                }
                // 「作品名　作者　URL」→ 1行目に作品名と作者、2行目に URL を小さく
                string[] p = line.Split('　');
                if (p.Length >= 3)
                {
                    sb.Append(p[0]).Append("  <color=#9fb3c8>by ").Append(p[1]).Append("</color>\n");
                    sb.Append("<size=58%><color=#8a97a6>").Append(string.Join(" ", p, 2, p.Length - 2)).Append("</color></size>\n");
                }
                else if (p.Length == 2)
                {
                    sb.Append(p[0]).Append("  <size=75%><color=#7d8a99>").Append(p[1]).Append("</color></size>\n");
                }
                else
                {
                    sb.Append(line).Append("\n");
                }
            }
            if (sb.Length > 0) cols.Add(sb.ToString());
            return cols;
        }

        static TextMeshPro MakeText(Transform parent, int layer, TMP_FontAsset font, string name, string text,
                             Vector2 pos, Vector2 size, TextAlignmentOptions align, float maxSize)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.text = text;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            // 入りきる最大の大きさに自動で合わせる。行が増えても看板からはみ出さない
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.05f;
            tmp.fontSizeMax = maxSize * 10f;
            tmp.rectTransform.sizeDelta = size;
            tmp.rectTransform.localPosition = new Vector3(pos.x, pos.y, 0f);
            return tmp;
        }

        static Material PanelMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PanelMatPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(0.035f, 0.04f, 0.05f, 1f);
            AssetDatabase.CreateAsset(mat, PanelMatPath);
            return mat;
        }
    }
}
