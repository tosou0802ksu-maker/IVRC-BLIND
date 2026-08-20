using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room16「人形の間」用。RamsterZ Free Doll のスキンメッシュを
    /// ポーズ付きの静的メッシュに焼いて .asset として保存する。
    /// 焼くことで VRChat 上のスキニング負荷が消え、ライトマップにも乗せられる。
    /// </summary>
    public static class Room16DollBaker
    {
        const string SrcPrefab = "Assets/RamsterZ_FreeDoll/Prefabs/KillerDollWood.prefab";
        const string OutDir = "Assets/_BLIND/Art/Models/Room16Dolls";

        /// <summary>ボーンを「子の方向」基準で狙わせる。ねじれは維持する。</summary>
        struct Seg
        {
            public string bone, child;
            public Vector3 dir;
            public Seg(string b, string c, float x, float y, float z) { bone = b; child = c; dir = new Vector3(x, y, z); }
        }

        struct Pose
        {
            public string name;
            public Seg[] segs;
            public Vector3 headEuler;   // head ボーンへの追加回転
            public Vector3 neckEuler;   // neck_01 への追加回転
        }

        static Seg[] Arms(float ux, float uy, float uz, float lx, float ly, float lz)
        {
            return new[]
            {
                new Seg("upperarm_l","lowerarm_l",-ux,uy,uz),
                new Seg("lowerarm_l","hand_l",    -lx,ly,lz),
                new Seg("upperarm_r","lowerarm_r", ux,uy,uz),
                new Seg("lowerarm_r","hand_r",     lx,ly,lz),
            };
        }

        static List<Seg> Cat(params IEnumerable<Seg>[] parts)
        {
            var l = new List<Seg>();
            foreach (var p in parts) l.AddRange(p);
            return l;
        }

        static Pose[] BuildPoses()
        {
            var poses = new List<Pose>();

            // P0 立ち・両腕だらり
            poses.Add(new Pose
            {
                name = "Pose0_Stand",
                segs = Arms(0.17f, -0.98f, 0.04f, 0.09f, -0.99f, 0.08f),
                neckEuler = new Vector3(0f, 0f, 0f),
                headEuler = new Vector3(3f, -6f, 4f),
            });

            // P1 立ち・腕をやや前に垂らす／首をかしげる
            poses.Add(new Pose
            {
                name = "Pose1_StandTilt",
                segs = Cat(Arms(0.15f, -0.94f, 0.30f, 0.06f, -0.72f, 0.69f)).ToArray(),
                neckEuler = new Vector3(-4f, 0f, 11f),
                headEuler = new Vector3(2f, 14f, 8f),
            });

            // P2 立ち・右腕を前方に持ち上げる
            poses.Add(new Pose
            {
                name = "Pose2_Reach",
                segs = new[]
                {
                    new Seg("upperarm_l","lowerarm_l",-0.18f,-0.96f, 0.06f),
                    new Seg("lowerarm_l","hand_l",    -0.09f,-0.97f, 0.20f),
                    new Seg("upperarm_r","lowerarm_r", 0.30f, 0.26f, 0.92f),
                    new Seg("lowerarm_r","hand_r",     0.16f, 0.08f, 0.98f),
                },
                neckEuler = new Vector3(0f, -12f, 0f),
                headEuler = new Vector3(-4f, -16f, 6f),
            });

            // P3 立ち・うつむき／背中を丸める
            poses.Add(new Pose
            {
                name = "Pose3_Bowed",
                segs = Cat(
                    new[]
                    {
                        new Seg("spine_02","spine_03", 0f, 0.985f, 0.17f),
                        new Seg("spine_03","spine_04", 0f, 0.975f, 0.22f),
                    },
                    Arms(0.14f, -0.96f, -0.24f, 0.07f, -0.98f, -0.16f)).ToArray(),
                neckEuler = new Vector3(22f, 0f, 0f),
                headEuler = new Vector3(16f, 8f, -3f),
            });

            // P4 倒れ用・手足を投げ出した形
            poses.Add(new Pose
            {
                name = "Pose4_Sprawl",
                segs = new[]
                {
                    new Seg("upperarm_l","lowerarm_l",-0.74f,-0.28f, 0.61f),
                    new Seg("lowerarm_l","hand_l",    -0.52f,-0.12f, 0.85f),
                    new Seg("upperarm_r","lowerarm_r", 0.82f,-0.34f, 0.46f),
                    new Seg("lowerarm_r","hand_r",     0.60f, 0.14f, 0.79f),
                    new Seg("thigh_l","calf_l",       -0.30f,-0.93f, 0.20f),
                    new Seg("calf_l","foot_l",        -0.14f,-0.89f,-0.43f),
                    new Seg("thigh_r","calf_r",        0.35f,-0.90f, 0.26f),
                    new Seg("calf_r","foot_r",         0.10f,-0.96f, 0.25f),
                },
                neckEuler = new Vector3(0f, 32f, 0f),
                headEuler = new Vector3(-8f, 24f, 10f),
            });

            // P5 倒れ用・体を丸めた形
            poses.Add(new Pose
            {
                name = "Pose5_Curl",
                segs = new[]
                {
                    new Seg("spine_02","spine_03", 0f, 0.96f, 0.28f),
                    new Seg("spine_03","spine_04", 0f, 0.94f, 0.34f),
                    new Seg("upperarm_l","lowerarm_l",-0.34f,-0.66f, 0.67f),
                    new Seg("lowerarm_l","hand_l",    -0.10f,-0.26f, 0.96f),
                    new Seg("upperarm_r","lowerarm_r", 0.28f,-0.72f, 0.63f),
                    new Seg("lowerarm_r","hand_r",     0.14f,-0.34f, 0.93f),
                    new Seg("thigh_l","calf_l",       -0.12f,-0.44f, 0.89f),
                    new Seg("calf_l","foot_l",        -0.06f,-0.87f,-0.49f),
                    new Seg("thigh_r","calf_r",        0.16f,-0.50f, 0.85f),
                    new Seg("calf_r","foot_r",         0.05f,-0.85f,-0.52f),
                },
                neckEuler = new Vector3(18f, -14f, 0f),
                headEuler = new Vector3(14f, -10f, -6f),
            });

            // P6 天井の手に掴まれてぶら下がる形（脱力）
            poses.Add(new Pose
            {
                name = "Pose6_Hanging",
                segs = new[]
                {
                    new Seg("spine_02","spine_03", 0f, 0.99f,-0.14f),
                    new Seg("spine_03","spine_04", 0f, 0.98f,-0.19f),
                    new Seg("upperarm_l","lowerarm_l",-0.26f,-0.95f,-0.16f),
                    new Seg("lowerarm_l","hand_l",    -0.20f,-0.97f,-0.14f),
                    new Seg("upperarm_r","lowerarm_r", 0.24f,-0.96f,-0.14f),
                    new Seg("lowerarm_r","hand_r",     0.18f,-0.97f,-0.16f),
                    new Seg("thigh_l","calf_l",       -0.10f,-0.98f,-0.17f),
                    new Seg("calf_l","foot_l",        -0.06f,-0.90f,-0.43f),
                    new Seg("thigh_r","calf_r",        0.13f,-0.97f,-0.21f),
                    new Seg("calf_r","foot_r",         0.07f,-0.88f,-0.47f),
                },
                neckEuler = new Vector3(26f, 6f, 0f),
                headEuler = new Vector3(20f, 10f, -8f),
            });

            return poses.ToArray();
        }

        [MenuItem("BLIND/room16/Bake Doll Pose Meshes")]
        public static string BakeAll()
        {
            var log = new System.Text.StringBuilder();
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "Room16Dolls");

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcPrefab);
            if (src == null) return "source prefab not found";

            foreach (var pose in BuildPoses())
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                inst.transform.localScale = Vector3.one;

                var map = new Dictionary<string, Transform>();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) map[t.name] = t;

                foreach (var s in pose.segs)
                {
                    Transform b, c;
                    if (!map.TryGetValue(s.bone, out b) || !map.TryGetValue(s.child, out c))
                    {
                        log.AppendLine("  missing bone " + s.bone + "/" + s.child);
                        continue;
                    }
                    var cur = (c.position - b.position).normalized;
                    b.rotation = Quaternion.FromToRotation(cur, s.dir.normalized) * b.rotation;
                }
                if (map.ContainsKey("neck_01")) map["neck_01"].localRotation *= Quaternion.Euler(pose.neckEuler);
                if (map.ContainsKey("head")) map["head"].localRotation *= Quaternion.Euler(pose.headEuler);

                var combines = new List<CombineInstance>();
                foreach (var name in new[] { "Unity_KillerDoll_Body", "Unity_KillerDoll_Head", "Unity_KillerDoll_Eyes" })
                {
                    Transform t;
                    if (!map.TryGetValue(name, out t)) continue;
                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    combines.Add(new CombineInstance
                    {
                        mesh = baked,
                        transform = inst.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix,
                        subMeshIndex = 0,
                    });
                }

                var mesh = new Mesh { name = pose.name };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(combines.ToArray(), false, true);
                mesh.RecalculateBounds();
                Unwrapping.GenerateSecondaryUVSet(mesh);

                string path = OutDir + "/Room16Doll_" + pose.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    existing.Clear();
                    EditorUtility.CopySerialized(mesh, existing);
                    EditorUtility.SetDirty(existing);
                }
                else AssetDatabase.CreateAsset(mesh, path);

                log.AppendLine(pose.name + " tri=" + (mesh.triangles.Length / 3) + " sub=" + mesh.subMeshCount +
                               " bounds c=" + mesh.bounds.center.ToString("F2") + " s=" + mesh.bounds.size.ToString("F2"));

                foreach (var ci in combines) Object.DestroyImmediate(ci.mesh);
                Object.DestroyImmediate(inst);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        static bool IsRightArmBone(string n)
        {
            if (!n.EndsWith("_r")) return false;
            foreach (var p in new[] { "upperarm", "lowerarm", "hand_r", "index", "middle", "ring", "pinky", "thumb", "wrist", "weapon" })
                if (n.StartsWith(p)) return true;
            return false;
        }

        /// <summary>
        /// 天井から降りてくる巨大な手。右腕だけをスキンウェイトで切り出して静的メッシュにする。
        /// ピボットは手首。腕は -Y 方向（真下）に伸びる。
        /// </summary>
        [MenuItem("BLIND/room16/Bake Giant Arm")]
        public static string BakeGiantArm()
        {
            var log = new System.Text.StringBuilder();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcPrefab);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;

            var map = new Dictionary<string, Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) map[t.name] = t;

            // 腕を真下に、わずかに手前へ倒す
            var segs = new[]
            {
                new Seg("upperarm_r","lowerarm_r", 0.10f,-0.98f, 0.17f),
                new Seg("lowerarm_r","hand_r",     0.05f,-0.99f, 0.11f),
            };
            foreach (var s in segs)
            {
                var b = map[s.bone]; var c = map[s.child];
                var cur = (c.position - b.position).normalized;
                b.rotation = Quaternion.FromToRotation(cur, s.dir.normalized) * b.rotation;
            }
            // 手：指が真下、掌が -Z を向く（掴まれる人形は -Z 側）
            map["hand_r"].rotation = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(0f, 0f, -14f);

            // 指を握り込む
            float[] curl = { 44f, 54f, 30f };
            foreach (var f in new[] { "index", "middle", "ring", "pinky" })
                for (int j = 1; j <= 3; j++)
                {
                    Transform b;
                    if (map.TryGetValue(f + "_0" + j + "_r", out b)) b.localRotation *= Quaternion.Euler(curl[j - 1], 0f, 0f);
                }
            for (int j = 1; j <= 3; j++)
            {
                Transform b;
                if (map.TryGetValue("thumb_0" + j + "_r", out b)) b.localRotation *= Quaternion.Euler(32f, 0f, 0f);
            }

            var smr = map["Unity_KillerDoll_Body"].GetComponent<SkinnedMeshRenderer>();
            var srcMesh = smr.sharedMesh;

            // 右腕に属する頂点だけ残す
            var armBone = new bool[smr.bones.Length];
            for (int i = 0; i < smr.bones.Length; i++)
                armBone[i] = smr.bones[i] != null && IsRightArmBone(smr.bones[i].name);

            var bw = srcMesh.boneWeights;
            var keep = new bool[srcMesh.vertexCount];
            for (int i = 0; i < srcMesh.vertexCount; i++)
            {
                var w = bw[i];
                float a = 0f;
                if (armBone[w.boneIndex0]) a += w.weight0;
                if (armBone[w.boneIndex1]) a += w.weight1;
                if (armBone[w.boneIndex2]) a += w.weight2;
                if (armBone[w.boneIndex3]) a += w.weight3;
                keep[i] = a > 0.5f;
            }

            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var norms = baked.normals;
            var uvs = baked.uv;
            var tris = srcMesh.triangles;

            var remap = new int[srcMesh.vertexCount];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
            var nt = new List<int>();

            // BakeMesh はレンダラのローカル空間（FBX の都合で軸が寝ている）ので
            // 人形の直立空間に戻したうえで、ピボットを手首に置く
            var M = inst.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var wristLocal = inst.transform.InverseTransformPoint(map["hand_r"].position);

            for (int t = 0; t < tris.Length; t += 3)
            {
                if (!keep[tris[t]] || !keep[tris[t + 1]] || !keep[tris[t + 2]]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int oi = tris[t + k];
                    if (remap[oi] < 0)
                    {
                        remap[oi] = nv.Count;
                        nv.Add(M.MultiplyPoint3x4(verts[oi]) - wristLocal);
                        nn.Add(M.MultiplyVector(norms[oi]).normalized);
                        nu.Add(uvs[oi]);
                    }
                    nt.Add(remap[oi]);
                }
            }

            var arm = new Mesh { name = "GiantArm" };
            arm.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            arm.SetVertices(nv); arm.SetNormals(nn); arm.SetUVs(0, nu);
            arm.SetTriangles(nt, 0);
            arm.RecalculateTangents();
            arm.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(arm);

            string path = OutDir + "/Room16_GiantArm.asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null) { ex.Clear(); EditorUtility.CopySerialized(arm, ex); EditorUtility.SetDirty(ex); }
            else AssetDatabase.CreateAsset(arm, path);

            log.AppendLine("GiantArm tri=" + (nt.Count / 3) + " verts=" + nv.Count +
                           " bounds c=" + arm.bounds.center.ToString("F3") + " s=" + arm.bounds.size.ToString("F3"));

            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }
    }
}
