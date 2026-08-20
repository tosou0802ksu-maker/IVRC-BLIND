using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room12「だるまの資料室」の配置。room12 ローカル座標（X 0..8, Z 0..20）。
    ///
    /// 動線は手描きの図の通り。入口(x=0, z=2)から入って一度南へ回り込み、
    /// 中央通路を北上して、いちばん奥で東へ折れて出口(x=8, z=18)。
    /// 西の細い通路は箱で行き止まりにしてある（図の緑のひっかけ）。
    /// </summary>
    public static class Room12Layout
    {
        const float SizeX = 8f, SizeZ = 20f;

        // --- X 方向の割り付け ---
        const float WestGap0 = 0.10f, WestGap1 = 1.00f;   // 行き止まりの細い通路
        const float WestBlk0 = 1.00f, WestBlk1 = 2.10f;   // 背中合わせの書架
        const float Aisle0 = 2.10f, Aisle1 = 5.10f;       // 中央通路（本線）
        const float EastBlk0 = 5.10f, EastBlk1 = 6.20f;
        const float EastGap0 = 6.20f, EastGap1 = 7.90f;   // 事務机を並べる副通路

        static readonly float[] RackX = {
            WestBlk0 + Room12Kit.RackD * 0.5f,   // 1.275 西向き（行き止まり側）
            WestBlk1 - Room12Kit.RackD * 0.5f,   // 1.825 東向き（本線側）
            EastBlk0 + Room12Kit.RackD * 0.5f,   // 5.375 西向き（本線側）
            EastBlk1 - Room12Kit.RackD * 0.5f,   // 5.925 東向き（副通路側）
        };
        static readonly float[] RackYaw = { -90f, 90f, -90f, 90f };  // 開口の向き
        /// <summary>本線（中央通路）に面している列。だるまはここに集める。</summary>
        static readonly bool[] FacesMainAisle = { false, true, true, false };

        const float RunSouthZ0 = 3.10f;   // 南の列の始まり
        const float RunNorthZ0 = 10.90f;  // 北の列の始まり
        const int UnitsPerRun = 6;        // 0.95m × 6 = 5.70m

        const float OfficeZ0 = 16.60f;    // 北の事務スペース

        /// <summary>通れなければならない道。ここから ClearPath 以内には物を置かない。</summary>
        static readonly Vector2[][] Paths =
        {
            // 中央のモニタの山（PileCenter 付近）は本線をふさぐので、西へ迂回して回り込む
            new[] { new Vector2(0.20f, 2.00f), new Vector2(1.55f, 1.95f), new Vector2(1.55f, 0.95f),
                    new Vector2(3.60f, 0.85f), new Vector2(3.60f, 8.10f), new Vector2(2.65f, 8.95f),
                    new Vector2(2.65f, 10.70f), new Vector2(3.60f, 11.50f), new Vector2(3.60f, 17.60f),
                    new Vector2(7.80f, 18.00f) },
            new[] { new Vector2(3.60f, 9.80f), new Vector2(7.00f, 9.80f), new Vector2(7.00f, 17.60f) },  // 東の副通路
        };
        const float ClearPath = 0.62f;

        /// <summary>図の緑のひっかけ。西の通路は途中まで入れて、箱で行き止まり。</summary>
        static readonly Vector2 DeadEndFrom = new Vector2(0.55f, 2.40f);
        static readonly Vector2 DeadEndTo = new Vector2(0.55f, 4.15f);   // 実測でここまで入れる
        const float DeadEndBlockZ = 4.70f;

        static Transform Room()
        {
            foreach (var g in Object.FindObjectsOfType<GameObject>())
            {
                if (g.name != "room12") continue;
                if (g.GetComponentsInChildren<Renderer>().Length == 0) continue;   // 空の重複よけ
                return g.transform;
            }
            return null;
        }

        static GameObject Group(Transform room, string name)
        {
            var old = room.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var g = new GameObject(name);
            g.transform.SetParent(room, false);
            return g;
        }

        static Mesh Kit(string n) { return AssetDatabase.LoadAssetAtPath<Mesh>(Room12Kit.KitDir + "/" + n + ".asset"); }
        static Material Mat(string n) { return AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/" + n + ".mat"); }

        static GameObject Piece(Transform parent, string name, Mesh mesh, Material mat,
                               Vector3 localPos, float yaw, bool collider = true, float lmScale = 1f)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (collider)
            {
                var bc = g.AddComponent<BoxCollider>();
                bc.center = mesh.bounds.center;
                bc.size = mesh.bounds.size;
            }
            GameObjectUtility.SetStaticEditorFlags(g,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            var so = new SerializedObject(mr);
            so.FindProperty("m_ScaleInLightmap").floatValue = lmScale;
            so.ApplyModifiedProperties();
            return g;
        }

        // ===============================================================
        [MenuItem("BLIND/room12/2. Place Everything")]
        public static string PlaceAll()
        {
            var room = Room();
            if (room == null) return "room12 が見つからない";

            var log = new System.Text.StringBuilder();
            log.AppendLine(PlaceRacks(room));
            log.AppendLine(PlaceOffice(room));
            log.AppendLine(PlaceClutter(room));
            log.AppendLine(PlaceDaruma(room));
            EditorUtility.SetDirty(room.gameObject);
            return log.ToString();
        }

        // ---------------------------------------------------------------
        //  書架
        // ---------------------------------------------------------------
        /// <summary>置いた書架のブロック。列(0..3) と 単位番号(0..11) が分かるようにしておく。</summary>
        public struct RackRef { public int col; public int idx; public Vector3 pos; public float yaw; }
        static readonly List<RackRef> _racks = new List<RackRef>();

        static string PlaceRacks(Transform room)
        {
            var parent = Group(room, "Prop_Racks");
            var mesh = Kit("Room12_SteelRack");
            var steel = Mat("Room12_Steel");
            var rust = Mat("Room12_SteelRust");
            if (mesh == null || steel == null) return "書架のメッシュ／マテリアルがない。先に 1. Build Kit を実行";

            _racks.Clear();
            var rnd = new System.Random(1212);
            int n = 0;
            for (int col = 0; col < RackX.Length; col++)
            {
                for (int run = 0; run < 2; run++)
                {
                    float z0 = run == 0 ? RunSouthZ0 : RunNorthZ0;
                    for (int u = 0; u < UnitsPerRun; u++)
                    {
                        float z = z0 + (u + 0.5f) * Room12Kit.RackW;
                        var pos = new Vector3(RackX[col], 0f, z);
                        // 濡れて錆びた列を数台混ぜる
                        bool rusty = rnd.NextDouble() < 0.16;
                        Piece(parent.transform, "Rack_" + col + "_" + (run * UnitsPerRun + u),
                              mesh, rusty && rust != null ? rust : steel, pos, RackYaw[col], true, 0.9f);
                        _racks.Add(new RackRef { col = col, idx = run * UnitsPerRun + u, pos = pos, yaw = RackYaw[col] });
                        n++;
                    }
                }
            }
            return "書架 " + n + " 台 (" + (n * mesh.triangles.Length / 3) + " tri)";
        }

        // ---------------------------------------------------------------
        //  事務スペース：机・椅子・CRT
        // ---------------------------------------------------------------
        struct DeskRef { public Vector3 pos; public float yaw; }
        static readonly List<DeskRef> _desks = new List<DeskRef>();

        static string PlaceOffice(Transform room)
        {
            var parent = Group(room, "Prop_Office");
            var deskMesh = Kit("Room12_Desk");
            var chairMesh = Kit("Room12_Chair");
            var deskMat = Mat("Room12_SteelDesk");
            var steel = Mat("Room12_Steel");
            var crt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Pekdata/PekdataCRTMonitor/Prefabs/CRTMonitor.prefab");
            if (deskMesh == null || deskMat == null) return "机のメッシュ／マテリアルがない";

            _desks.Clear();

            // 北の事務スペース。壁ぎわに並べて、出口(x=8,z=18)への通り道は空けておく。
            // 机の正面(+Z)＝座る側。北壁に向けるので yaw=180 で正面を南に向ける。
            AddDesk(new Vector3(1.30f, 0f, 19.05f), 180f);
            AddDesk(new Vector3(2.85f, 0f, 19.05f), 180f);
            AddDesk(new Vector3(5.35f, 0f, 19.05f), 180f);
            // 副通路（東側）に横向きで2台。壁ぎわに寄せて通路 1m を残す
            AddDesk(new Vector3(7.52f, 0f, 12.20f), -90f);
            AddDesk(new Vector3(7.52f, 0f, 14.10f), -90f);
            // 入口すぐの受付
            AddDesk(new Vector3(6.30f, 0f, 1.30f), 180f);

            var rnd = new System.Random(5150);
            int nCrt = 0, nBroken = 0;
            for (int i = 0; i < _desks.Count; i++)
            {
                var d = _desks[i];
                var g = Piece(parent.transform, "Desk_" + i, deskMesh, deskMat, d.pos, d.yaw, true, 1f);

                // 椅子。少しずらして斜めに引いた状態にする。
                // 東の副通路の2台(i=3,4)は通路が 1m しかないので椅子を机の下に押し込む。
                if (chairMesh != null && i != 5)
                {
                    bool tucked = (i == 3 || i == 4);
                    float side = (float)rnd.NextDouble() * 0.5f - 0.25f;
                    float outp = tucked ? 0.24f : 0.62f + (float)rnd.NextDouble() * 0.30f;
                    var local = new Vector3(side, 0f, outp);
                    var cp = d.pos + Quaternion.Euler(0f, d.yaw, 0f) * local;
                    Piece(parent.transform, "Chair_" + i, chairMesh, steel != null ? steel : deskMat,
                          cp, d.yaw + 180f + ((float)rnd.NextDouble() * 50f - 25f), true, 1f);
                }

                // CRT モニタ。天板は y=DeskH、モニタは原点が中央で下端 -0.189。
                if (crt == null) continue;
                int count = (i == 2) ? 2 : 1;
                for (int k = 0; k < count; k++)
                {
                    bool broken = (i == 0 && k == 0) || (i == 4);
                    float lx = (count == 1) ? ((float)rnd.NextDouble() * 0.5f - 0.25f) : (k == 0 ? -0.38f : 0.34f);
                    var local = new Vector3(lx, 0f, -0.06f);
                    var p = d.pos + Quaternion.Euler(0f, d.yaw, 0f) * local;
                    p.y = Room12Kit.DeskH + 0.189f;
                    SpawnCrt(parent.transform, crt, "CRT_" + i + "_" + k, p,
                             d.yaw + 180f + ((float)rnd.NextDouble() * 34f - 17f), broken);
                    if (broken) nBroken++; else nCrt++;
                }
            }

            // 壊れたモニタを床に積む（副通路の突き当たり）
            var stack = new[] {
                new Vector3(7.45f, 0.189f, 16.10f),
                new Vector3(7.45f, 0.189f + 0.36f, 16.05f),
                new Vector3(7.10f, 0.189f, 16.35f),
            };
            for (int i = 0; i < stack.Length; i++)
            {
                SpawnCrt(parent.transform, crt, "CRT_Junk_" + i, stack[i],
                         200f + i * 47f, true, i == 1 ? 22f : 0f);
                nBroken++;
            }

            return "机 " + _desks.Count + " / 椅子 " + (_desks.Count - 1)
                 + " / CRT 無傷 " + nCrt + " 壊れ " + nBroken;
        }

        static void AddDesk(Vector3 p, float yaw) { _desks.Add(new DeskRef { pos = p, yaw = yaw }); }

        /// <summary>
        /// CRT を1台置く。パーティクル（煙・火花）は爆発演出用なので消す。
        /// broken=true なら画面を外してガラス片だけ残す＝割れた状態。
        /// </summary>
        static void SpawnCrt(Transform parent, GameObject prefab, string name, Vector3 pos, float yaw,
                             bool broken, float roll = 0f)
        {
            SpawnCrt(parent, prefab, name, pos, Quaternion.Euler(0f, yaw, roll), broken);
        }

        static void SpawnCrt(Transform parent, GameObject prefab, string name, Vector3 pos, Quaternion rot,
                             bool broken)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            g.name = name;
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;

            foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) Object.DestroyImmediate(mb);

            // 元は「撃つと爆発して破片が飛び散る」プロップなので、
            // 本体とガラス片1枚1枚に Rigidbody（非キネマティック）と MeshCollider が付いている。
            // そのまま置くとワールドに入った瞬間に机から落ちて散らばるので全部外す。
            // 触れる／ぶつかれる判定は下で本体に BoxCollider を1つ付け直す。
            foreach (var rb in g.GetComponentsInChildren<Rigidbody>(true))
                if (rb != null) Object.DestroyImmediate(rb);
            foreach (var co in g.GetComponentsInChildren<Collider>(true))
                if (co != null) Object.DestroyImmediate(co);
            // 入れ子になっているので、先に集めてから消す（親を消すと子も道連れになる）
            var doomed = new List<GameObject>();
            foreach (var ps in g.GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null) doomed.Add(ps.gameObject);
            foreach (var d in doomed)
                if (d != null) Object.DestroyImmediate(d);

            var on = g.transform.Find("screenON");
            var off = g.transform.Find("screenOFF");
            if (on != null) on.gameObject.SetActive(false);          // 停電しているので点かない
            if (off != null) off.gameObject.SetActive(!broken);
            // ガラス片5枚は screenShards という空の親にぶら下がっていて、
            // プレハブでは既定で非表示。直接の子だと思って .001〜.005 を探すと空振りする。
            var shards = g.transform.Find("screenShards");
            if (shards != null)
            {
                shards.gameObject.SetActive(broken);
                foreach (Transform s in shards) s.gameObject.SetActive(broken);
            }

            var bc = g.GetComponent<BoxCollider>();
            if (bc == null) bc = g.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.009f, -0.101f);
            bc.size = new Vector3(0.369f, 0.360f, 0.409f);

            foreach (var mr in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(mr.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        // ---------------------------------------------------------------
        //  部屋の中央に積み上げたモニタの山
        // ---------------------------------------------------------------
        /// <summary>山の中心。南北の通路が交差する、部屋のちょうど真ん中。</summary>
        static readonly Vector2 PileCenter = new Vector2(4.00f, 9.85f);

        /// <summary>
        /// CRT 1台は 0.369(W) × 0.360(H) × 0.409(D)、原点は中心で下端が -0.189。
        /// 段ごとに (横, 奥) のずれを直接書いて、下ほど広く、上ほど狭い山にする。
        /// </summary>
        static readonly (float y, float[] dx, float[] dz)[] PileRows =
        {
            (0.189f, new[]{ -0.76f, -0.38f, 0.00f, 0.38f, 0.76f }, new[]{ -0.21f, -0.19f, -0.22f, -0.18f, -0.20f }),
            (0.189f, new[]{ -0.57f, -0.19f, 0.19f, 0.57f },        new[]{  0.21f,  0.19f,  0.22f,  0.20f }),
            (0.545f, new[]{ -0.56f, -0.18f, 0.20f, 0.57f },        new[]{ -0.17f, -0.20f, -0.16f, -0.19f }),
            (0.545f, new[]{ -0.36f,  0.02f },                       new[]{  0.20f,  0.18f }),
            (0.901f, new[]{ -0.35f,  0.03f },                       new[]{ -0.08f, -0.11f }),
            (0.901f, new[]{ -0.16f },                               new[]{  0.22f }),
            (1.257f, new[]{ -0.15f },                               new[]{ -0.02f }),
        };

        /// <summary>根本に転がり落ちた分。参考画像でも山の裾に横倒しのが数台ある。</summary>
        static readonly (float dx, float dz, float yaw, float pitch, float roll)[] PileFallen =
        {
            (-1.18f, -0.52f, 24f, -88f, 12f),
            ( 1.16f,  0.44f, 200f, -92f, -8f),
            ( 0.30f, -0.72f, 312f, 96f, 6f),
        };

        static string PlaceMonitorPile(Transform room)
        {
            var parent = Group(room, "Prop_MonitorPile");
            var crt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Pekdata/PekdataCRTMonitor/Prefabs/CRTMonitor.prefab");
            if (crt == null) return "CRT プレハブがない";

            var rnd = new System.Random(6104);
            int n = 0, nBroken = 0;
            float top = 0f;

            foreach (var row in PileRows)
                for (int i = 0; i < row.dx.Length; i++)
                {
                    // 積んだものは正面がばらばら。ただし山の外を向いている方が自然なので、
                    // 中心から外へのベクトルを基準にして ±55度ほど散らす。
                    var off = new Vector2(row.dx[i], row.dz[i]);
                    float outward = Mathf.Atan2(off.x, off.y) * Mathf.Rad2Deg;
                    float yaw = outward + (float)rnd.NextDouble() * 110f - 55f;
                    // 積み重なっているので少し傾く。上の段ほど大きく崩す。
                    float lean = 4f + row.y * 7f;
                    var rot = Quaternion.Euler(
                        ((float)rnd.NextDouble() * 2f - 1f) * lean,
                        yaw,
                        ((float)rnd.NextDouble() * 2f - 1f) * lean);

                    bool broken = rnd.NextDouble() < 0.32;
                    var pos = new Vector3(PileCenter.x + off.x, row.y, PileCenter.y + off.y);
                    SpawnCrt(parent.transform, crt, "PileCRT_" + n.ToString("00"), pos, rot, broken);
                    if (broken) nBroken++;
                    top = Mathf.Max(top, row.y + 0.18f);
                    n++;
                }

            foreach (var f in PileFallen)
            {
                var rot = Quaternion.Euler(f.pitch, f.yaw, f.roll);
                // 横倒しなので、回した後の当たり判定の下端を床に合わせる
                var pos = new Vector3(PileCenter.x + f.dx, 0.189f, PileCenter.y + f.dz);
                SpawnCrt(parent.transform, crt, "PileCRT_Fallen_" + n.ToString("00"), pos, rot, true);
                nBroken++; n++;
            }

            // 転がっている分の床めり込みを実測で直す
            foreach (Transform t in parent.transform)
            {
                if (!t.name.Contains("Fallen")) continue;
                var rs = t.GetComponentsInChildren<MeshRenderer>(true);
                var b = new Bounds(); bool first = true;
                foreach (var r in rs) { if (!r.gameObject.activeInHierarchy) continue;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                if (first) continue;
                t.localPosition += new Vector3(0f, (room.position.y + 0.005f) - b.min.y, 0f);
            }

            return "モニタの山 " + n + " 台（うち割れ " + nBroken + "）中心 ("
                 + PileCenter.x + ", " + PileCenter.y + ") 高さ " + top.ToString("F2") + "m";
        }

        [MenuItem("BLIND/room12/5. Build Monitor Pile")]
        public static string BuildPile()
        {
            var room = Room();
            if (room == null) return "room12 が見つからない";
            var r = PlaceMonitorPile(room);
            EditorUtility.SetDirty(room.gameObject);
            return r;
        }

        // ---------------------------------------------------------------
        //  雑物：文書保存箱、木箱、行き止まりの塞ぎ
        // ---------------------------------------------------------------
        static string PlaceClutter(Transform room)
        {
            var parent = Group(room, "Prop_Clutter");
            var boxMesh = Kit("Room12_ArchiveBox");
            var card = Mat("Room12_Cardboard");
            if (boxMesh == null || card == null) return "箱のメッシュ／マテリアルがない";

            var cardOld = Mat("Room12_CardboardOld") ?? card;
            var rnd = new System.Random(777);
            int n = 0;
            System.Func<Material> pickCard = () => rnd.NextDouble() < 0.35 ? cardOld : card;

            // 西の通路を塞ぐ壁。図の緑がここで折り返している
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 2; c++)
                {
                    var p = new Vector3(0.34f + c * 0.44f, r * (Room12Kit.BoxH + 0.03f),
                                        DeadEndBlockZ + (float)rnd.NextDouble() * 0.16f);
                    Piece(parent.transform, "Block_" + r + "_" + c, boxMesh, pickCard(), p,
                          (float)rnd.NextDouble() * 14f - 7f, true, 1f);
                    n++;
                }

            // 入口ホールの仕切り。これがないと扉から中央通路まで一直線で、
            // 図の「一度南へ回り込む」動きが出ない。
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                {
                    var p = new Vector3(1.15f + c * 0.44f, r * (Room12Kit.BoxH + 0.03f),
                                        2.45f + (float)rnd.NextDouble() * 0.10f);
                    Piece(parent.transform, "Hall_" + r + "_" + c, boxMesh, pickCard(), p,
                          (float)rnd.NextDouble() * 12f - 6f, true, 1f);
                    n++;
                }

            // 木箱。事務スペースに置くと明らかに浮くので、
            // 行き止まりの奥（誰も来ない、古い荷物を押し込んだ場所）だけに限る。
            var sewer = "Assets/Ata Khani/Modular Sewer Props/Prefabs/";
            var crates = new (string prefab, Vector3 pos, float yaw)[]
            {
                ("Box_A_01", new Vector3(0.55f, 0f, 5.55f), 12f),
                ("Box_B_01", new Vector3(0.52f, 0.89f, 5.50f), -20f),
                ("Box_A_02", new Vector3(0.55f, 0f, 6.60f), 34f),
            };
            foreach (var c in crates)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(sewer + c.prefab + ".prefab");
                if (pf == null) continue;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent.transform);
                g.transform.localPosition = c.pos;
                g.transform.localRotation = Quaternion.Euler(0f, c.yaw, 0f);
                foreach (var mr in g.GetComponentsInChildren<MeshRenderer>(true))
                    GameObjectUtility.SetStaticEditorFlags(mr.gameObject,
                        StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                n++;
            }

            // 床に積んだ文書箱。動線から外れた場所だけ
            var spots = new (float x, float z, int h)[]
            {
                (2.42f, 3.30f, 3), (2.42f, 3.78f, 2), (4.80f, 8.55f, 2),
                (6.55f, 4.10f, 3), (6.98f, 4.15f, 2), (2.45f, 16.15f, 2),
                (4.85f, 16.20f, 3), (0.50f, 8.90f, 2), (0.50f, 9.35f, 3),
                (0.50f, 14.40f, 2), (6.60f, 19.40f, 3), (7.20f, 19.40f, 2),
            };
            foreach (var s in spots)
            {
                if (DistToPaths(new Vector2(s.x, s.z)) < ClearPath) continue;
                for (int r = 0; r < s.h; r++)
                {
                    var p = new Vector3(s.x + ((float)rnd.NextDouble() - 0.5f) * 0.06f,
                                        r * (Room12Kit.BoxH + 0.03f),
                                        s.z + ((float)rnd.NextDouble() - 0.5f) * 0.06f);
                    Piece(parent.transform, "Boxes_" + n + "_" + r, boxMesh, pickCard(), p,
                          (float)rnd.NextDouble() * 22f - 11f, true, 1f);
                }
                n++;
            }

            // 棚に載せる文書箱。だるまが入らない段を埋める
            int onShelf = 0;
            foreach (var rk in _racks)
            {
                var srnd = new System.Random(rk.col * 977 + rk.idx * 31 + 5);
                bool main = FacesMainAisle[rk.col];
                for (int lv = 0; lv < 4; lv++)
                {
                    // 「だるまの資料室」なので、文書箱が主役になっては困る。
                    // 本線に面した棚は控えめに、裏の棚は箱で埋めておく。
                    if (srnd.NextDouble() > (main ? 0.22 : 0.50)) continue;
                    int cnt = main ? 1 : 1 + srnd.Next(2);
                    for (int k = 0; k < cnt; k++)
                    {
                        float lx = -0.30f + k * 0.42f + (float)srnd.NextDouble() * 0.08f;
                        var local = new Vector3(lx, Room12Kit.RackShelfY[lv], -0.02f);
                        var p = rk.pos + Quaternion.Euler(0f, rk.yaw, 0f) * local;
                        p.y = Room12Kit.RackShelfY[lv];
                        Piece(parent.transform, "ShelfBox_" + onShelf, boxMesh,
                              srnd.NextDouble() < 0.35 ? cardOld : card, p,
                              rk.yaw + (float)srnd.NextDouble() * 8f - 4f, false, 1.4f);
                        onShelf++;
                    }
                }
            }

            return "床の箱 " + n + " 山 / 棚の文書箱 " + onShelf + " 個";
        }

        // ---------------------------------------------------------------
        //  だるま
        // ---------------------------------------------------------------
        /// <summary>
        /// だるまだけ置き直す。書架や机を作り直すと、手で動かした物まで消えてしまうので、
        /// 書架の位置は既にシーンにあるものから読み直す。
        /// </summary>
        [MenuItem("BLIND/room12/4. Re-place Daruma Only")]
        public static string ReplaceDarumaOnly()
        {
            var room = Room();
            if (room == null) return "room12 が見つからない";
            var racks = room.Find("Prop_Racks");
            if (racks == null) return "Prop_Racks がない。先に 2. Place Everything";

            _racks.Clear();
            foreach (Transform t in racks)
            {
                // 名前は Rack_<col>_<idx>
                var parts = t.name.Split('_');
                int col, idx;
                if (parts.Length < 3 || !int.TryParse(parts[1], out col) || !int.TryParse(parts[2], out idx)) continue;
                _racks.Add(new RackRef { col = col, idx = idx, pos = t.localPosition, yaw = t.localEulerAngles.y });
            }
            if (_racks.Count == 0) return "書架を読み取れなかった";
            var r = PlaceDaruma(room);
            EditorUtility.SetDirty(room.gameObject);
            return "書架 " + _racks.Count + " 台をシーンから読み直した / " + r;
        }

        static string PlaceDaruma(Transform room)
        {
            var parent = Group(room, "Prop_Daruma");

            var meshes = new Dictionary<string, Mesh>();
            foreach (var k in new[] { "DarumaA", "DarumaB", "DarumaC", "DarumaD" })
                meshes[k] = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_BLIND/Art/Models/Room12Daruma/" + k + ".asset");
            if (meshes["DarumaA"] == null) return "だるまのメッシュがない。先に BLIND/room12/Bake Daruma";

            // 種類 × 色味。C(238tri) は遠景用に多めの重みで引かれるようにしておく
            var kinds = new (string key, string mat, float weight)[]
            {
                ("DarumaA", "Room12_DarumaA",        1.0f),
                ("DarumaA", "Room12_DarumaA_Dusty",  1.0f),
                ("DarumaA", "Room12_DarumaA_Dark",   0.5f),
                ("DarumaB", "Room12_DarumaB",        0.7f),
                ("DarumaB", "Room12_DarumaB_Dusty",  0.7f),
                ("DarumaC", "Room12_DarumaC",        1.4f),
                ("DarumaC", "Room12_DarumaC_Dusty",  1.4f),
                ("DarumaC", "Room12_DarumaC_Faded",  0.9f),
            };
            float wSum = 0f; foreach (var k in kinds) wSum += k.weight;

            // 焼き直したメッシュは「直径1.0」に正規化してあるので、高さは種類ごとに違う。
            // 大きいだるまを棚に置くとき、これを見ないと B が上の棚板を突き抜ける。
            var heightRatio = new Dictionary<string, float>
            { { "DarumaA", 0.981f }, { "DarumaB", 1.174f }, { "DarumaC", 0.956f }, { "DarumaD", 0.813f } };
            // 棚の内法 0.47m に収まる最大の直径（種類ごと）
            System.Func<string, float, float> fitDia = (key, clear) =>
                (clear - 0.025f) / Mathf.Max(heightRatio.ContainsKey(key) ? heightRatio[key] : 1f, 0.1f);

            var rnd = new System.Random(19620);
            int placed = 0; long tris = 0;

            // 本線に面した書架を優先して詰める。1台につき「ぎっしりの段」を持たせる。
            // 挿入順のまま詰めると南半分で上限に達して、北半分が箱だけの倉庫になる。
            // 本線側／裏側の優先度は保ったまま、各グループの中だけ混ぜて部屋の端から端まで散らす。
            var main0 = new List<RackRef>(); var back0 = new List<RackRef>();
            foreach (var r in _racks) (FacesMainAisle[r.col] ? main0 : back0).Add(r);
            var shuf = new System.Random(3141);
            System.Action<List<RackRef>> shuffle = (list) =>
            {
                for (int i = list.Count - 1; i > 0; i--)
                { int j = shuf.Next(i + 1); var tmp = list[i]; list[i] = list[j]; list[j] = tmp; }
            };
            shuffle(main0); shuffle(back0);
            var order = new List<RackRef>(main0);
            order.AddRange(back0);

            const int Target = 78;   // これに机・床・天板の分（下の extra）が乗って 90 強

            foreach (var rk in order)
            {
                if (placed >= Target) break;
                bool main = FacesMainAisle[rk.col];
                // 本線側は 2 段に 1 段、裏側は 5 段に 1 段くらい
                for (int lv = 0; lv < 4 && placed < Target; lv++)
                {
                    double p = main ? (lv >= 2 ? 0.62 : 0.34) : 0.14;
                    if (rnd.NextDouble() > p) continue;

                    // 段ごとに 3 通り。棚の内法は 0.47m なので、大は 0.40 前後まで入る。
                    //   ぎっしり : 小さめを横一列に並べる
                    //   大       : 0.32〜0.42 を 1〜2体。棚がぱんぱんに見えるやつ
                    //   ふつう   : 0.22〜0.32 を 1〜2体
                    double roll = rnd.NextDouble();
                    bool packed = roll < (main ? 0.34 : 0.18);
                    bool big = !packed && roll < (main ? 0.72 : 0.55);

                    float dia;
                    if (packed) dia = 0.155f + (float)rnd.NextDouble() * 0.050f;
                    else if (big) dia = 0.325f + (float)rnd.NextDouble() * 0.095f;
                    else dia = 0.215f + (float)rnd.NextDouble() * 0.105f;

                    int cnt = packed ? Mathf.FloorToInt((Room12Kit.RackUsableHalfW * 2f - 0.06f) / (dia + 0.028f))
                                     : (big ? 1 + (rnd.NextDouble() < 0.45 ? 1 : 0) : 1 + rnd.Next(2));
                    cnt = Mathf.Min(cnt, Target - placed);

                    float step = dia + 0.028f;
                    float span = (cnt - 1) * step;
                    float x0 = packed ? -span * 0.5f
                                      : -Room12Kit.RackUsableHalfW + 0.12f
                                        + (float)rnd.NextDouble() * (Room12Kit.RackUsableHalfW * 2f - 0.24f - span);

                    for (int i = 0; i < cnt; i++)
                    {
                        // 種類を重み付きで引く
                        float t = (float)rnd.NextDouble() * wSum; int ki = 0;
                        foreach (var k in kinds) { t -= k.weight; if (t <= 0f) break; ki++; }
                        ki = Mathf.Clamp(ki, 0, kinds.Length - 1);
                        var kind = kinds[ki];

                        // 種類ごとの背の高さで頭打ちにする（B は他より 2 割背が高い）
                        float d = Mathf.Min(dia * (0.9f + (float)rnd.NextDouble() * 0.2f),
                                            fitDia(kind.key, Room12Kit.RackClear));
                        // 棚の奥行き方向。手前寄りに、少しばらけさせる
                        float lz = 0.02f + (float)rnd.NextDouble() * 0.10f;
                        var local = new Vector3(x0 + i * step, Room12Kit.RackShelfY[lv], lz);
                        var pos = rk.pos + Quaternion.Euler(0f, rk.yaw, 0f) * local;
                        pos.y = Room12Kit.RackShelfY[lv];

                        // 全部が棚から真正面を向いていると、通路を歩く間はどれも横顔になって
                        // ただの赤い玉に見える。3割ほどを通路の向き（南＝入ってくる側）に
                        // 振って、歩いてくる player を顔が待ち構えている状態にする。
                        float yaw;
                        double face = rnd.NextDouble();
                        if (face < 0.30) yaw = 180f;                       // 入口の方を向く
                        else if (face < 0.40) yaw = rk.yaw + 180f;         // 棚の奥を向く
                        else yaw = rk.yaw;                                 // 通路に正対
                        yaw += (float)rnd.NextDouble() * 26f - 13f;
                        placed += Spawn(parent.transform, meshes[kind.key], Mat(kind.mat),
                                        "Daruma_" + placed, pos, yaw, d, ref tris) ? 1 : 0;
                    }
                }
            }

            // 机の上と床にも数体。ここが「誰かが並べた」感を出す
            var extra = new (float x, float y, float z, float dia, string key, string mat)[]
            {
                // 机の上。棚と違って上が開いているので大きめでいい
                (2.85f, Room12Kit.DeskH, 19.22f, 0.42f, "DarumaA", "Room12_DarumaA"),
                (5.10f, Room12Kit.DeskH, 19.22f, 0.30f, "DarumaB", "Room12_DarumaB"),
                (5.55f, Room12Kit.DeskH, 19.18f, 0.24f, "DarumaC", "Room12_DarumaC_Dusty"),
                (6.30f, Room12Kit.DeskH, 1.28f,  0.36f, "DarumaA", "Room12_DarumaA_Dusty"),
                (7.52f, Room12Kit.DeskH, 12.35f, 0.31f, "DarumaC", "Room12_DarumaC"),
                // 床置き。人の腰くらいある大物を通路ぎわに
                (2.38f, 0f, 2.62f,  0.62f, "DarumaA", "Room12_DarumaA_Dark"),
                (5.28f, 0f, 11.40f, 0.48f, "DarumaB", "Room12_DarumaB_Dusty"),   // 中央のモニタ山を避ける
                (2.32f, 0f, 13.10f, 0.55f, "DarumaA", "Room12_DarumaA_Dusty"),
                (6.45f, 0f, 8.40f,  0.44f, "DarumaC", "Room12_DarumaC_Faded"),
                (0.62f, 0f, 3.55f,  0.40f, "DarumaC", "Room12_DarumaC_Dusty"),
                (5.30f, 0f, 16.40f, 0.52f, "DarumaA", "Room12_DarumaA"),
                (2.32f, 0f, 17.10f, 0.45f, "DarumaC", "Room12_DarumaC_Dusty"),
                // 書架の天板（2.03m）。上は天井まで開いているので、ここが一番大きくできる
                (1.83f, Room12Kit.RackShelfY[4], 5.00f,  0.58f, "DarumaA", "Room12_DarumaA_Dark"),
                (1.83f, Room12Kit.RackShelfY[4], 6.60f,  0.44f, "DarumaC", "Room12_DarumaC"),
                (5.38f, Room12Kit.RackShelfY[4], 12.60f, 0.54f, "DarumaA", "Room12_DarumaA_Dusty"),
                (5.38f, Room12Kit.RackShelfY[4], 13.85f, 0.46f, "DarumaB", "Room12_DarumaB_Dusty"),
                (1.83f, Room12Kit.RackShelfY[4], 15.10f, 0.60f, "DarumaA", "Room12_DarumaA"),
                (5.38f, Room12Kit.RackShelfY[4], 4.20f,  0.50f, "DarumaC", "Room12_DarumaC_Faded"),
            };
            foreach (var e in extra)
            {
                var m = meshes[e.key];
                if (m == null) continue;
                // 床置きの大物は半径ぶん場所を食うので、動線からの距離もその分きびしく見る
                if (e.y < 0.01f && DistToPaths(new Vector2(e.x, e.z)) < 0.30f + e.dia * 0.5f) continue;
                placed += Spawn(parent.transform, m, Mat(e.mat), "Daruma_" + placed,
                                new Vector3(e.x, e.y, e.z), (float)rnd.NextDouble() * 360f, e.dia, ref tris) ? 1 : 0;
            }

            // 変わり種（招き猫だるま）を1体だけ。26,755 tri あるので棚の目立つ所に1つきり。
            if (meshes["DarumaD"] != null)
            {
                var g = new GameObject("Daruma_Neko");
                g.transform.SetParent(parent.transform, false);
                g.transform.localPosition = new Vector3(5.30f, Room12Kit.RackShelfY[2], 11.55f);
                g.transform.localRotation = Quaternion.Euler(0f, -84f, 0f);
                g.transform.localScale = Vector3.one * 0.26f;
                g.AddComponent<MeshFilter>().sharedMesh = meshes["DarumaD"];
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { Mat("Room12_DarumaD_0"), Mat("Room12_DarumaD_1"), Mat("Room12_DarumaD_2") };
                GameObjectUtility.SetStaticEditorFlags(g,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                placed++; tris += 26755;
            }

            return "だるま " + placed + " 体 (" + tris + " tri)";
        }

        static bool Spawn(Transform parent, Mesh mesh, Material mat, string name,
                          Vector3 pos, float yaw, float dia, ref long tris)
        {
            if (mesh == null || mat == null) return false;
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = Vector3.one * dia;   // メッシュは直径1.0・底が y=0 に正規化済み
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(g,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            var so = new SerializedObject(mr);
            so.FindProperty("m_ScaleInLightmap").floatValue = 2.2f;   // 小さいので相対的に上げる
            so.ApplyModifiedProperties();
            tris += mesh.triangles.Length / 3;
            return true;
        }

        // ---------------------------------------------------------------
        static float DistToPaths(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var path in Paths)
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    var a = path[i]; var b = path[i + 1];
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
            return best;
        }

        // ---------------------------------------------------------------
        //  実際にカプセルが通れるかを物理で確かめる
        // ---------------------------------------------------------------
        [MenuItem("BLIND/room12/3. Verify Circulation")]
        public static string Verify()
        {
            var room = Room();
            if (room == null) return "room12 が見つからない";
            Physics.SyncTransforms();

            const float cell = 0.20f;
            int nx = Mathf.RoundToInt(SizeX / cell), nz = Mathf.RoundToInt(SizeZ / cell);
            var free = new bool[nx, nz];
            var org = room.position;

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var w = org + new Vector3((i + 0.5f) * cell, 0f, (j + 0.5f) * cell);
                    free[i, j] = !Physics.CheckCapsule(w + new Vector3(0f, 0.40f, 0f),
                                                       w + new Vector3(0f, 1.55f, 0f), 0.26f);
                }

            // 入口(z=2, x=0付近)から幅優先で塗る
            var q = new Queue<Vector2Int>();
            var seen = new bool[nx, nz];
            for (int i = 0; i < 4; i++)
            {
                int j = Mathf.RoundToInt(2f / cell);
                if (free[i, j]) { q.Enqueue(new Vector2Int(i, j)); seen[i, j] = true; break; }
            }
            int reached = 0;
            while (q.Count > 0)
            {
                var c = q.Dequeue(); reached++;
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    int a = c.x + d.x, b = c.y + d.y;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b] || !free[a, b]) continue;
                    seen[a, b] = true; q.Enqueue(new Vector2Int(a, b));
                }
            }

            var sb = new System.Text.StringBuilder();
            // 出口(x=8, z=18)まで届いたか
            int ex = nx - 1, ez = Mathf.RoundToInt(18f / cell);
            bool exitOk = false;
            for (int i = ex; i > ex - 4; i--)
                for (int j = ez - 2; j <= ez + 2; j++)
                    if (j >= 0 && j < nz && seen[i, j]) exitOk = true;
            sb.AppendLine("入口→出口: " + (exitOk ? "通れる" : "★通れない★") + "  到達セル " + reached + "/" + (nx * nz));

            // 行き止まりの検査：西の通路は途中まで入れて、その先で止まっているか
            // 1セル狙い撃ちだと通路の中心とずれて誤判定するので、西側の帯を横に走査する
            System.Func<float, bool> westOpen = (z) =>
            {
                int j = Mathf.Clamp(Mathf.RoundToInt(z / cell), 0, nz - 1);
                for (int i = 0; i * cell < WestBlk0; i++) if (seen[i, j]) return true;
                return false;
            };
            sb.AppendLine("西の通路 手前(" + DeadEndFrom.y + "m): " + (westOpen(DeadEndFrom.y) ? "入れる" : "★入れない★"));
            sb.AppendLine("西の通路 奥  (" + DeadEndTo.y + "m): " + (westOpen(DeadEndTo.y) ? "入れる" : "★入れない★"));
            sb.AppendLine("西の通路 その先(" + (DeadEndBlockZ + 0.6f) + "m): "
                        + (westOpen(DeadEndBlockZ + 0.6f) ? "★抜けてしまう★" : "行き止まり"));

            // 通路幅の目視用に、Z を 1m ごとに刻んだ地図を出す
            sb.AppendLine("\n  x→ 0        2        4        6      8   (行は z、'#'=塞がり '.'=通れる '+'=入口から到達)");
            for (int j = nz - 1; j >= 0; j--)
            {
                var line = new System.Text.StringBuilder((j * cell).ToString("00.0") + " ");
                for (int i = 0; i < nx; i++) line.Append(!free[i, j] ? '#' : (seen[i, j] ? '+' : '.'));
                sb.AppendLine(line.ToString());
            }
            return sb.ToString();
        }
    }
}
