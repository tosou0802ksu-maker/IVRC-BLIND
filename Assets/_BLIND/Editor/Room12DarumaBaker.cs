using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// glTF で入れただるま4種を、そのまま大量配置できる形に焼き直す。
    ///
    /// 素のままだと (1) 4種とも Z-up で寝ている (2) 原点がばらばら
    /// (3) 尺度が 0.054m〜2.02m とばらばら (4) マテリアルが glTFast シェーダで、
    /// うち2種は Unlit なので暗い部屋で光を受けない、という状態。
    /// ここで「直径1.0m・底が y=0・正面が +Z」に正規化した静的メッシュと
    /// Standard マテリアルを作っておき、配置側はスケールを置くだけにする。
    /// </summary>
    public static class Room12DarumaBaker
    {
        const string OutDir = "Assets/_BLIND/Art/Models/Room12Daruma";
        const string MatDir = "Assets/_BLIND/Art/Materials";

        /// <summary>4種とも Blender 由来の Z-up。-90 で起こすと顔が +Z を向く。</summary>
        static readonly Quaternion Fix = Quaternion.Euler(-90f, 0f, 0f);

        struct Src
        {
            public string key;      // 出力名
            public string path;     // scene.gltf
            public float yawFix;    // 種類ごとの正面のずれ
        }

        static readonly Src[] Sources =
        {
            new Src { key = "DarumaA", path = "Assets/_BLIND/Art/Models/Daruma/Daruma_Ramujiro_CCBY/scene.gltf",      yawFix = 0f },
            new Src { key = "DarumaB", path = "Assets/_BLIND/Art/Models/Daruma/Daruma_Jan_CCBY/scene.gltf",           yawFix = 0f },
            new Src { key = "DarumaC", path = "Assets/_BLIND/Art/Models/Daruma/Daruma_AdrianCrisandy_CCBY/scene.gltf", yawFix = 0f },
            new Src { key = "DarumaD", path = "Assets/_BLIND/Art/Models/Daruma/Daruma_Neko_higan69_CCBY/scene.gltf",  yawFix = 0f },
        };

        [MenuItem("BLIND/room12/Bake Daruma")]
        public static string Bake()
        {
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "Room12Daruma");

            var log = new System.Text.StringBuilder();

            foreach (var src in Sources)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(src.path);
                if (pf == null) { log.AppendLine(src.key + ": source not found"); continue; }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.Euler(0f, src.yawFix, 0f) * Fix;
                inst.transform.localScale = Vector3.one;

                var rends = inst.GetComponentsInChildren<MeshRenderer>(true);

                // 直径1.0・底が y=0 になる相似変換を先に求める
                var raw = WorldBounds(rends);
                float dia = Mathf.Max(raw.size.x, raw.size.z);
                float scale = 1f / Mathf.Max(dia, 1e-4f);
                inst.transform.localScale = Vector3.one * scale;
                var b2 = WorldBounds(rends);
                inst.transform.position = new Vector3(-b2.center.x, -b2.min.y, -b2.center.z);

                // レンダラごとにサブメッシュとして結合（マテリアル順を保つ）
                var combines = new List<CombineInstance>();
                var mats = new List<Material>();
                foreach (var r in rends)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    {
                        combines.Add(new CombineInstance
                        {
                            mesh = mf.sharedMesh,
                            subMeshIndex = s,
                            transform = r.transform.localToWorldMatrix,
                        });
                        mats.Add(s < r.sharedMaterials.Length ? r.sharedMaterials[s] : null);
                    }
                }

                var mesh = new Mesh { name = src.key };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(combines.ToArray(), false, true);
                mesh.RecalculateBounds();
                Unwrapping.GenerateSecondaryUVSet(mesh);

                WriteMesh(mesh, OutDir + "/" + src.key + ".asset");

                // マテリアルは glTFast → Standard に張り替え。Unlit の2種はこれで光を受けるようになる。
                var outMats = new List<string>();
                for (int i = 0; i < mats.Count; i++)
                {
                    var name = src.key + (mats.Count > 1 ? "_" + i : "");
                    MakeStandard(mats[i], MatDir + "/Room12_" + name + ".mat");
                    outMats.Add(name);
                }

                var fin = WorldBounds(rends);
                log.AppendLine(src.key + "  tris=" + (mesh.triangles.Length / 3)
                    + "  sub=" + mesh.subMeshCount
                    + "  size=" + fin.size.ToString("F3")
                    + "  mats=" + string.Join(",", outMats));

                Object.DestroyImmediate(inst);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        static Bounds WorldBounds(Renderer[] rends)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var r in rends) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            return b;
        }

        /// <summary>既存アセットがあれば GUID を保ったまま中身だけ差し替える。</summary>
        static void WriteMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(mesh, path);
        }

        static void MakeStandard(Material src, string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Standard");
            if (src != null)
            {
                if (src.HasProperty("baseColorTexture")) m.SetTexture("_MainTex", src.GetTexture("baseColorTexture"));
                if (src.HasProperty("normalTexture"))
                {
                    var n = src.GetTexture("normalTexture");
                    if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
                }
            }
            // 漆塗りの張り子。てかりは弱く、金属ではない。
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Glossiness", 0.34f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
        }

        /// <summary>色違い。同じテクスチャを _Color で乗算するだけの安い変化付け。</summary>
        public static readonly (string suffix, Color tint)[] Tints =
        {
            ("",       Color.white),
            ("_Dusty", new Color(0.68f, 0.63f, 0.58f)),
            ("_Dark",  new Color(0.40f, 0.34f, 0.32f)),
            ("_Faded", new Color(0.86f, 0.78f, 0.70f)),
        };

        [MenuItem("BLIND/room12/Bake Daruma Tints")]
        public static string BakeTints()
        {
            var log = new System.Text.StringBuilder();
            foreach (var key in new[] { "DarumaA", "DarumaB", "DarumaC" })
            {
                var baseMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Room12_" + key + ".mat");
                if (baseMat == null) { log.AppendLine(key + ": base material missing"); continue; }
                foreach (var t in Tints)
                {
                    if (t.suffix == "") continue;
                    var path = MatDir + "/Room12_" + key + t.suffix + ".mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) { m = new Material(baseMat); AssetDatabase.CreateAsset(m, path); }
                    else { EditorUtility.CopySerialized(baseMat, m); }
                    m.SetColor("_Color", t.tint);
                    EditorUtility.SetDirty(m);
                    log.AppendLine("  " + System.IO.Path.GetFileName(path));
                }
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }
    }
}
