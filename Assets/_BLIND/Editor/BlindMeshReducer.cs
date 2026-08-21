using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// メッシュの頂点クラスタリングによる簡略化。
    ///
    /// なぜ必要か：
    /// room16 の人形は1体 31,220 三角形あり、17体で 53万三角形。room16 だけで 93万、
    /// マップ全体の3割を人形が占めていた。VRChat のワールドは 20万三角形を超えると
    /// 「Very Poor」判定になるので、このままでは Quest はもちろん PC でも重い。
    ///
    /// やっていること：
    /// 空間を格子に切って、同じマスに入った頂点を1つに統合する。統合後に
    /// 潰れた（3頂点のうち2つ以上が同じになった）三角形を捨てる。
    /// 二次誤差計量(QEM)のような賢い手法ではないが、
    ///   - 元メッシュのトポロジーが汚くても破綻しない
    ///   - 結果が決定的（毎回同じ）
    ///   - 外形（シルエット）が保たれる
    /// という性質があり、「暗い部屋でシルエットとして見える人形」には十分。
    ///
    /// 元のメッシュアセットは消さずに `<名前>_lite.asset` を別に作る。
    /// やり直したくなったら MeshFilter の参照を戻すだけでよい。
    /// </summary>
    public static class BlindMeshReducer
    {
        public const string LiteDir = "Assets/_BLIND/Art/Models/Lite";

        /// <summary>格子の一辺を指定して簡略化する。cell が大きいほど粗くなる。</summary>
        public static Mesh Reduce(Mesh src, float cell)
        {
            if (src == null || !src.isReadable || cell <= 0f) return null;

            var srcV = src.vertices;
            var srcN = src.normals;
            var srcU = src.uv;
            bool hasN = srcN != null && srcN.Length == srcV.Length;
            bool hasU = srcU != null && srcU.Length == srcV.Length;

            // --- 頂点をマスごとにまとめる ---
            var cellOf = new Dictionary<Vector3Int, int>();   // マス → 新しい頂点番号
            var remap = new int[srcV.Length];
            var accP = new List<Vector3>();
            var accN = new List<Vector3>();
            var accU = new List<Vector2>();
            var accC = new List<int>();

            for (int i = 0; i < srcV.Length; i++)
            {
                var p = srcV[i];
                var key = new Vector3Int(
                    Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                int idx;
                if (!cellOf.TryGetValue(key, out idx))
                {
                    idx = accP.Count;
                    cellOf[key] = idx;
                    accP.Add(Vector3.zero); accN.Add(Vector3.zero); accU.Add(Vector2.zero); accC.Add(0);
                }
                accP[idx] += p;
                if (hasN) accN[idx] += srcN[i];
                if (hasU) accU[idx] += srcU[i];
                accC[idx]++;
                remap[i] = idx;
            }

            var nv = new Vector3[accP.Count];
            var nn = new Vector3[accP.Count];
            var nu = new Vector2[accP.Count];
            for (int i = 0; i < accP.Count; i++)
            {
                float c = Mathf.Max(1, accC[i]);
                nv[i] = accP[i] / c;                                   // マスの重心に寄せる
                nn[i] = accN[i].sqrMagnitude > 1e-8f ? accN[i].normalized : Vector3.up;
                nu[i] = accU[i] / c;
            }

            var dst = new Mesh { name = src.name + "_lite" };
            dst.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            dst.SetVertices(new List<Vector3>(nv));
            if (hasN) dst.SetNormals(new List<Vector3>(nn));
            if (hasU) dst.SetUVs(0, new List<Vector2>(nu));
            dst.subMeshCount = src.subMeshCount;

            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = remap[tris[i]], b = remap[tris[i + 1]], c = remap[tris[i + 2]];
                    if (a == b || b == c || a == c) continue;          // 潰れた三角形は捨てる
                    keep.Add(a); keep.Add(b); keep.Add(c);
                }
                dst.SetTriangles(keep, s);
            }

            if (!hasN) dst.RecalculateNormals();
            dst.RecalculateBounds();
            return dst;
        }

        /// <summary>
        /// 目標の三角形数に近づくように格子の大きさを二分探索する。
        /// 格子を細かくすれば必ず三角形が増える、という単調性があるので二分法で足りる。
        /// </summary>
        public static Mesh ReduceToTarget(Mesh src, int targetTris)
        {
            if (src == null || !src.isReadable) return null;
            int srcTris = src.triangles.Length / 3;
            if (srcTris <= targetTris) return null;

            float ext = src.bounds.size.magnitude;
            float lo = ext / 500f, hi = ext / 4f;   // 細かい ← → 粗い
            Mesh best = null;

            // 巨大なメッシュ（100万頂点級の草など）は1回あたりが重いので探索回数を絞る。
            // 目標ちょうどでなくても桁が合っていれば目的は果たせる。
            int iterations = src.vertexCount > 300000 ? 6 : 12;
            for (int it = 0; it < iterations; it++)
            {
                float mid = Mathf.Sqrt(lo * hi);    // 対数の中点。大きさが桁で効くため
                var m = Reduce(src, mid);
                if (m == null) break;
                int t = m.triangles.Length / 3;

                if (best == null) best = m;
                else
                {
                    // 目標を下回るものの中で一番三角形が多い＝一番きれいなものを残す
                    int bt = best.triangles.Length / 3;
                    bool betterCandidate = (t <= targetTris && (bt > targetTris || t > bt));
                    if (betterCandidate) { Object.DestroyImmediate(best); best = m; }
                    else Object.DestroyImmediate(m);
                }

                if (t > targetTris) lo = mid; else hi = mid;
            }
            return best;
        }

        /// <summary>簡略化した複製をアセットとして保存する。既にあれば中身を差し替える。</summary>
        public static Mesh SaveLite(Mesh src, int targetTris) { return SaveLite(src, targetTris, "_lite"); }

        /// <summary>
        /// 簡略化した複製をアセットとして保存する。既にあれば中身を差し替える。
        /// suffix を変えれば、同じ元メッシュから粗さ違いを何種類でも作れる
        /// （表示用は細かく、エコロケの輪郭用はもっと粗く、など）。
        /// </summary>
        public static Mesh SaveLite(Mesh src, int targetTris, string suffix)
        {
            if (!AssetDatabase.IsValidFolder(LiteDir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_BLIND/Art/Models"))
                    AssetDatabase.CreateFolder("Assets/_BLIND/Art", "Models");
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "Lite");
            }
            var made = ReduceToTarget(src, targetTris);
            if (made == null) return null;
            made.name = src.name + suffix;

            var path = LiteDir + "/" + src.name + suffix + ".asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null)
            {
                EditorUtility.CopySerialized(made, ex);
                EditorUtility.SetDirty(ex);
                Object.DestroyImmediate(made);
                return ex;
            }
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        // -------------------------------------------------------------
        /// <summary>置き換える対象。メッシュ名の先頭一致と目標三角形数。</summary>
        struct Rule
        {
            public string prefix; public int target; public string note;
            public Rule(string p, int t, string n) { prefix = p; target = t; note = n; }
        }

        /// <summary>
        /// どれをどこまで削るかの判断。
        /// 「近くでまじまじ見る物」は残し、「暗がりにたくさん並んでいる物」を削る。
        /// </summary>
        static readonly Rule[] Rules =
        {
            new Rule("Grass v2",        3000, "room6 の草。1個112万頂点が5枚でマップ全体の88%を占めていた。" +
                                              "しかも5枚はほぼ同じ場所に重ねて置かれている"),
            new Rule("Grass",           1500, "room6 の草（別種）"),
            new Rule("Garage_Frame",    1600, "room3 のシャッター枠。1個で90,707三角形。骨組みなので角さえ残れば形は変わらない"),
            new Rule("Garage_Shutter",  1200, "room3 のシャッター。1個で49,563三角形。波板の凹凸が全部ポリゴンだった"),
            new Rule("pCube6",           600, "room3 の箱。8個で51,720三角形"),
            new Rule("Room16Doll_Pose", 3600, "人形。17体で53万三角形。暗い部屋でシルエットとして見える物なので大きく削れる"),
            new Rule("model",           1400, "Sketchfab のマネキン頭部。10個で12万三角形。room16 にしか無い"),
            new Rule("door",             420, "ロッカーの扉。room9 に55枚あり11万三角形。平たい板なので削っても形は変わらない"),
            new Rule("RubberDuck_LowPoly", 260, "小さいアヒル。room7 に61個。名前に反して1個902三角形あった"),
            new Rule("wooden_bookshelf_worn", 900, "書架。6個で3.9万。棚板の集合なので角を残せば十分"),
            new Rule("bookshelf",        700, "書架(別種)。7個"),
            new Rule("Barrel_Box_03",    800, "木箱。1個で2万頂点あった"),
            new Rule("SKM_KillerDollParts", 600, "人形の部位。合計6万頂点"),
            new Rule("lowpoly monitor",  900, "CRTモニタ。35台。元々角ばった形なので格子統合と相性が良い"),
            new Rule("DarumaD",         1200, "だるまD。1個で26,755三角形と他の20倍あった"),
            new Rule("DarumaA",          520, "だるまA。42個で6万三角形"),
        };

        /// <summary>
        /// 対象メッシュの Read/Write を有効にする。
        /// FBX は既定で Read/Write が off なので、CPU から頂点を読めず簡略化できない。
        /// 簡略化した結果は独立した .asset として保存するので、
        /// この設定はここで一時的に必要になるだけ。
        /// </summary>
        /// <summary>
        /// メッシュを CPU から読めるようにする。読めないと三角形を数えることも
        /// 減量することもできず、エコロケ層が箱の代用品になってしまう。
        /// BlindVisionBuilder からも呼ぶので public。
        /// </summary>
        public static int EnsureReadable(IEnumerable<Mesh> meshes)
        {
            var paths = new HashSet<string>();
            foreach (var m in meshes)
            {
                if (m == null || m.isReadable) continue;
                var p = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(p)) paths.Add(p);
            }
            int n = 0;
            foreach (var p in paths)
            {
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null || imp.isReadable) continue;
                imp.isReadable = true;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                n++;
            }
            return n;
        }

        [MenuItem("BLIND/軽量化/1. メッシュを軽くする")]
        public static string ReduceMine()
        {
            // MainWorld に全員の部屋を統合した後なので、=== ROOMS === の下を全部見る
            var rooms = new List<string>();
            var root = GameObject.Find("=== ROOMS ===");
            if (root != null) foreach (Transform t in root.transform) rooms.Add(t.name);
            else rooms.AddRange(new[] { "room2", "room7", "room9", "room12", "room15", "room16" });

            // 対象のメッシュを集める（2周する。1周目で Read/Write を立ててから読み直す）
            var targets = new Dictionary<Mesh, int>();
            var users = new Dictionary<Mesh, List<MeshFilter>>();
            int reimported = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                targets.Clear(); users.Clear();
                var needReadable = new List<Mesh>();
                foreach (var rn in rooms)
                {
                    var room = FindRoom(rn);
                    if (room == null) continue;
                    foreach (var mf in room.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = mf.sharedMesh;
                        if (m == null || m.name.EndsWith("_lite")) continue;
                        foreach (var r in Rules)
                        {
                            if (!m.name.StartsWith(r.prefix)) continue;
                            if (!m.isReadable) { needReadable.Add(m); break; }
                            targets[m] = r.target;
                            if (!users.ContainsKey(m)) users[m] = new List<MeshFilter>();
                            users[m].Add(mf);
                            break;
                        }
                    }
                }
                if (pass == 0 && needReadable.Count > 0) reimported = EnsureReadable(needReadable);
                else break;
            }

            var log = new System.Text.StringBuilder("元の三角形  →  削減後   個数  メッシュ\n");
            long before = 0, after = 0;
            foreach (var kv in targets)
            {
                var lite = SaveLite(kv.Key, kv.Value);
                if (lite == null) continue;
                int n = users[kv.Key].Count;
                int bt = kv.Key.triangles.Length / 3, at = lite.triangles.Length / 3;
                foreach (var mf in users[kv.Key])
                {
                    Undo.RecordObject(mf, "Reduce Mesh");
                    mf.sharedMesh = lite;
                    EditorUtility.SetDirty(mf);
                }
                before += (long)bt * n; after += (long)at * n;
                log.AppendLine(bt.ToString("N0").PadLeft(9) + "  → " + at.ToString("N0").PadLeft(8)
                             + "   x" + n.ToString().PadLeft(3) + "  " + kv.Key.name);
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("\n合計 " + before.ToString("N0") + " → " + after.ToString("N0")
                         + " 三角形（" + (before - after).ToString("N0") + " 削減）"
                         + (reimported > 0 ? " / Read/Write を有効化したモデル " + reimported + "個" : ""));
            return log.ToString();
        }

        [MenuItem("BLIND/軽量化/9. 軽量化を元に戻す")]
        public static string RestoreMine()
        {
            int n = 0;
            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null || !m.name.EndsWith("_lite")) continue;
                var orig = FindOriginal(m.name.Substring(0, m.name.Length - 5));
                if (orig == null) continue;
                Undo.RecordObject(mf, "Restore Mesh");
                mf.sharedMesh = orig;
                EditorUtility.SetDirty(mf);
                n++;
            }
            return n + " 個を元のメッシュに戻した";
        }

        static Mesh FindOriginal(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Mesh"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(LiteDir)) continue;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var m = o as Mesh;
                    if (m != null && m.name == name) return m;
                }
            }
            return null;
        }

        static Transform FindRoom(string n)
        {
            foreach (var g in Object.FindObjectsOfType<GameObject>())
                if (g.name == n && g.GetComponentsInChildren<Renderer>().Length > 0) return g.transform;
            return null;
        }
    }
}
