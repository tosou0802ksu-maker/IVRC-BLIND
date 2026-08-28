using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BLIND.EditorTools
{
    /// <summary>
    /// ゲーム進行ギミックの一括構築。
    ///
    /// ここで作るもの：
    ///   1. 赤・青・緑のゲートボタン（room7 / room11 / room19）
    ///      3個すべて押すと room3 のガレージシャッターが開いて room4（ゴール）へ抜けられる。
    ///   2. 落とし穴フロア（room5 / room10 / room18）
    ///      同じ床でも役割ごとに「穴が見える／見えない」が違う。
    ///   3. 復帰地点（ボタンを押した位置）
    ///   4. 死亡判定（レーザー・燃えている人・落とし穴）
    ///   5. 管理用の空オブジェクト
    ///
    /// 何度実行しても同じ結果になる（生成物は名前で消してから作り直す）。
    /// モーダルダイアログは出さない。自動化から呼べなくなるため。
    ///
    /// ---------------------------------------------------------------
    /// 3方向分岐という前提について
    /// ---------------------------------------------------------------
    /// マップは room1（スタート）から見て3方向に伸びている。
    ///   西  : room5（落とし穴）→ room6 → room7        …… 赤
    ///   南  : room10（落とし穴）→ room9 / room11      …… 青
    ///   東  : room18（落とし穴）→ room16 → room17 → room19 …… 緑
    /// どの色から回ってもよいので、復帰地点は「番号が大きい方」ではなく
    /// 「最後に押したボタン」にしている（CheckpointManager.SetCheckpointDirect）。
    /// </summary>
    public static class BlindGimmickBuilder
    {
        const int LayerDefault = 0;
        const int LayerThermal = 22;
        const int LayerEcho    = 23;

        const string GenDir     = "Assets/_BLIND/Art/Meshes/Gimmick";
        const string GenMatDir  = "Assets/_BLIND/Art/Materials/Gimmick";
        const string EchoMatPath = "Assets/_BLIND/Art/Materials/EchoMaterial.mat";

        /// <summary>穴の深さ(m)。落ちたら自力では戻れない深さにする。</summary>
        const float PitDepth = 5.0f;

        /// <summary>
        /// 縦坑の側面をいくつの帯に割るか。
        ///
        /// エコロケは「UVの端」にしか線を引かないので、側面が1枚の面だと
        /// 縁にしか線が出ず、穴が「床のマス目」と全く同じ絵になる。
        /// 実際、修正前は真上から見ても目線から見ても穴と床が判別できなかった。
        /// 帯に割ると1帯ごとに輪が描かれ、穴の中に等間隔の線が下へ並ぶ＝井戸に見える。
        /// 深さ5mを6分割すると約0.8mごと。増える三角形は1穴あたり40程度で誤差。
        /// </summary>
        const int PitRings = 6;

        /// <summary>
        /// 縦坑をマスの内側へ寄せる量(m)。穴を「二重の四角」に見せるために使う。
        /// 当たり判定はマス全体なので、ここを変えても落ちる範囲は変わらない。
        /// </summary>
        const float PitInset = 0.12f;

        /// <summary>
        /// エコロケ層だけ踏み板を一回り小さく描く量(m)。
        ///
        /// エコロケは輪郭しか描かないので、踏み板をマスぴったりに置くと
        /// 隣同士の縁が重なって「一枚の格子」に見え、穴（中身の無いマス）と
        /// 床のマスがまったく同じ絵になる。内側に寄せると1マスが独立した四角になり、
        /// **穴は「四角が抜けている場所」** として確実に読める。
        /// 当たり判定は別メッシュなので、ここを変えても歩ける範囲は変わらない。
        /// </summary>
        const float DeckEchoInset = 0.10f;

        /// <summary>
        /// サーモ層で、穴の開口に張る「熱の蓋」の深さ(m)。
        ///
        /// 熱源(45℃)は縦坑の中にあるが、縦坑は床より下なので目線からは中が見えず、
        /// 実際サーモ視点で穴が一切分からなかった。開口のすぐ下に水平な面を張れば、
        /// 距離や角度に関係なく「床に空いた明るい四角」として見える。
        /// </summary>
        const float PitCapDepth = 0.05f;
        /// <summary>床板の厚み(m)。元の床(0.1)より少し厚くして縁が見えるようにする。</summary>
        const float DeckThickness = 0.15f;

        const string PitRootName    = "PitField_Generated";
        const string ButtonRootName = "Gimmicks_Generated";
        const string HazardName     = "Hazard_Generated";

        // ------------------------------------------------------------
        // 落とし穴フロアの設計値
        // ------------------------------------------------------------
        class PitSpec
        {
            public string room;
            public float fx0, fx1, fz0, fz1;   // 穴フィールドの範囲(world)
            public int nx, nz;                 // マス数
            public float rx0, rx1, rz0, rz1;   // 部屋の床全体(world)
            public int seed;
            public int entryCol, exitCol;      // 入口側(+Z)と出口側(-Z)の安全な列
            public float density;              // 安全な道以外を穴にする確率
            public string note;
        }

        static readonly PitSpec[] Pits =
        {
            // ⚠️ room 名は「どの GameObject の下に作るか」だけに使う。
            //    座標(fx0..rz1)はワールド絶対値なので、名前を間違えても
            //    生成物は同じ場所に出る＝別の部屋の下に二重にできてしまう。実際にそうなった。
            //    ここの名前は必ずシーンの実体と一致させること。

            // room4 : 北(z=-4.1, x≈-7.05)から入って南(z=-13.3, x≈-2.05)へ抜ける
            new PitSpec { room="room4",  fx0=-9.1f, fx1= 0.1f, fz0=-12.0f, fz1= -5.4f,
                          nx=6, nz=4, rx0=-9.1f, rx1=0.1f, rz0=-13.3f, rz1=-4.1f,
                          seed=5001, entryCol=1, exitCol=4, density=0.55f,
                          note="西ルート(赤)の入口。最初に出会う落とし穴なので小さめ・易しめ。" +
                               "3部屋で密度を変えて単調さを避ける（ここが一番薄い）" },

            // room9 : 北(z=-20.5, x≈-4.45)から入って南(z=-47.7, x≈-4.55)へ。
            //          西の扉(z≈-45.3)は穴フィールドの外側なので安全に回れる。
            new PitSpec { room="room9",  fx0=-9.1f, fx1= 0.1f, fz0=-42.0f, fz1=-26.0f,
                          nx=6, nz=10, rx0=-9.1f, rx1=0.1f, rz0=-47.7f, rz1=-20.5f,
                          seed=10007, entryCol=3, exitCol=3, density=0.80f,
                          note="南ルート(青)の長い廊下。16mぶん歩き通す最大の難所（密度も最大）" },

            // room14 : 南北とも扉は x≈17.95
            new PitSpec { room="room14", fx0=13.9f, fx1=22.1f, fz0=-35.6f, fz1=-24.6f,
                          nx=6, nz=7,  rx0=13.9f, rx1=22.1f, rz0=-37.7f, rz1=-22.5f,
                          seed=18013, entryCol=2, exitCol=3, density=0.70f,
                          note="東ルート(緑)の入口。room4 と room9 の中間の密度" },
        };

        // ------------------------------------------------------------
        // ゲートボタンの設計値
        // ------------------------------------------------------------
        class ButtonSpec
        {
            public string name;
            public string room;
            public int buttonId;        // MultiButtonDoor 側のビット
            public int checkpoint;      // CheckpointManager の復帰地点番号
            public Color color;
            public string note;
        }

        static readonly ButtonSpec[] Buttons =
        {
            new ButtonSpec { name="Btn_Red",   room="room7",  buttonId=0, checkpoint=1,
                             color=new Color(1.00f, 0.13f, 0.10f), note="西ルートの終点" },
            new ButtonSpec { name="Btn_Blue",  room="room11", buttonId=1, checkpoint=2,
                             color=new Color(0.15f, 0.40f, 1.00f), note="南ルートの終点" },
            new ButtonSpec { name="Btn_Green", room="room19", buttonId=2, checkpoint=3,
                             color=new Color(0.15f, 1.00f, 0.30f), note="東ルートの終点" },
        };

        // ============================================================
        // エントリポイント
        // ============================================================

        [MenuItem("BLIND/ギミック/1. 全ギミックを構築")]
        public static void Menu_BuildAll()
        {
            Debug.Log(BuildAll());
        }

        /// <summary>自動化から呼べる本体。ダイアログは出さない。</summary>
        public static string BuildAll()
        {
            var log = new System.Text.StringBuilder();

            EnsureFolders();
            log.AppendLine(BlindThermalTable.BuildMaterials().Split('\n')[0]);

            var cm = FindCheckpointManager();
            if (cm == null) return "CheckpointManager が見つからない。処理を中止した。";

            log.AppendLine(EnsureManagers());
            log.AppendLine(BuildPitFields(cm));
            log.AppendLine(BuildGateButtons(cm));
            log.AppendLine(BuildLaserHazards(cm));
            log.AppendLine(BuildBurningHazard(cm));
            log.AppendLine(ConfigureShutter());
            log.AppendLine(WireCheckpoints(cm));

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManagerMarkDirty();
            return log.ToString();
        }

        static void EditorSceneManagerMarkDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                SceneManager.GetActiveScene());
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_BLIND/Art/Meshes"))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art", "Meshes");
            if (!AssetDatabase.IsValidFolder(GenDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Meshes", "Gimmick");
            if (!AssetDatabase.IsValidFolder(GenMatDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Gimmick");
        }

        // ============================================================
        // 5. 管理用の空オブジェクト
        // ============================================================
        static string EnsureManagers()
        {
            var sys = GameObject.Find("=== SYSTEM ===");
            if (sys == null)
            {
                sys = new GameObject("=== SYSTEM ===");
                Undo.RegisterCreatedObjectUndo(sys, "SYSTEM");
            }

            var gm = Child(sys.transform, "GameManagement");
            var gimmicks = Child(gm, "Gimmicks");          // ボタン・ハザードの親
            Child(gimmicks, "GateButtons");
            Child(gimmicks, "Hazards");
            var resp = sys.transform.Find("resporn");
            if (resp == null) resp = Child(sys.transform, "resporn");
            Child(resp, "Checkpoints");

            return "管理オブジェクト: === SYSTEM ===/GameManagement/Gimmicks/{GateButtons,Hazards}, resporn/Checkpoints を用意";
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "create " + name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Component FindCheckpointManager()
        {
            var t = System.Type.GetType("CheckpointManager, Assembly-CSharp");
            if (t == null) return null;
            var all = UnityEngine.Object.FindObjectsOfType(t, true);
            return all.Length > 0 ? all[0] as Component : null;
        }

        // ============================================================
        // 2. 落とし穴フロア
        // ============================================================

        static string BuildPitFields(Component cm)
        {
            var log = new System.Text.StringBuilder("落とし穴フロア:\n");
            var echoMat = AssetDatabase.LoadAssetAtPath<Material>(EchoMatPath);
            var voidMat = MakeVoidMaterial();
            var tDeck = BlindThermalTable.Mat("PitDeck");
            var tVoid = BlindThermalTable.Mat("PitVoid");

            foreach (var s in Pits)
            {
                var room = GameObject.Find("=== ROOMS ===/" + s.room);
                if (room == null) { log.AppendLine("  " + s.room + " : 見つからない"); continue; }

                // 元の床を止める（消さずに非アクティブにして、いつでも戻せるようにする）
                int hidden = DisableOriginalFloor(room.transform, s);

                var old = room.transform.Find(PitRootName);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                var rootGo = new GameObject(PitRootName);
                Undo.RegisterCreatedObjectUndo(rootGo, "pit field");
                rootGo.transform.SetParent(room.transform, false);
                rootGo.transform.position = Vector3.zero;
                rootGo.layer = LayerDefault;

                // --- 穴の割り当て ---
                //  owner[x,z] : -1=普通の床 / 0=過去人にだけ見える穴
                //               1=サーモにだけ見える穴 / 2=エコロケにだけ見える穴
                var owner = Assign(s);

                float cw = (s.fx1 - s.fx0) / s.nx;
                float cd = (s.fz1 - s.fz0) / s.nz;

                var meshes = new List<Mesh>();
                int holes = 0, solid = 0;
                for (int x = 0; x < s.nx; x++)
                    for (int z = 0; z < s.nz; z++) { if (owner[x, z] < 0) solid++; else holes++; }

                // --- 3レイヤー分のデッキを行ごとに作る ---
                // 行ごとに分けるのはエコロケのため。EchoEmitter は受信機の
                // transform.position までの距離と角度でパルスの当たりを判定するので、
                // 16m の床を1オブジェクトにすると「部屋のどこを鳴らしても全部光る」
                // か「どこを鳴らしても光らない」のどちらかになってしまう。
                for (int pass = 0; pass < 3; pass++)
                {
                    int layer = pass == 0 ? LayerDefault : (pass == 1 ? LayerThermal : LayerEcho);
                    string tag = pass == 0 ? "D" : (pass == 1 ? "T" : "E");
                    bool isThermal = pass == 1;
                    bool isEcho    = pass == 2;
                    Material deckMat = pass == 0 ? FloorMaterialOf(room.transform) : (pass == 1 ? tDeck : echoMat);
                    Material pitMat  = pass == 0 ? deckMat : (pass == 1 ? tVoid : echoMat);

                    for (int z = 0; z < s.nz; z++)
                    {
                        var deck = new MeshBuild();
                        var pit  = new MeshBuild();
                        for (int x = 0; x < s.nx; x++)
                        {
                            float x0 = s.fx0 + cw * x, x1 = x0 + cw;
                            float z0 = s.fz0 + cd * z, z1 = z0 + cd;
                            int o = owner[x, z];

                            // その役に「床がある」なら踏み板を描く。
                            // o == pass の役だけ床が欠けて穴が見える。
                            //
                            // エコロケ層だけは一回り小さく描いて、1マスを独立した四角にする。
                            // 隣同士がくっついていると格子1枚に見えて、穴と床が区別できない。
                            if (o != pass)
                            {
                                float ins = isEcho ? DeckEchoInset : 0f;
                                deck.Box(new Vector3(x0 + ins, -DeckThickness, z0 + ins),
                                         new Vector3(x1 - ins, 0f, z1 - ins));
                            }

                            // 穴の縦坑は全レイヤーに置く。床がある役からは踏み板に隠れて見えない。
                            // 縦坑はマスより一回り小さく掘る。
                            //
                            // ぴったり同じ大きさだと、穴の縁の線と隣の踏み板の縁の線が
                            // 同じ位置に重なって1本に見え、遠くから穴と床が区別できない
                            // （エコロケは輪郭しか描かないので、中身の無いマスと
                            //   床のマスがまったく同じ長方形になってしまう）。
                            // 内側に寄せると穴だけ「二重の四角」になり、距離があっても
                            // 一目で分かる。深さは近づいたときに帯(PitRings)で伝わる。
                            if (o < 0) continue;

                            // エコロケ層に縦坑は置かない。穴は「完全な黒」にする。
                            // 輪郭しか描かない視点では、穴の中に何かを描くほど
                            // 床のマス目と紛らわしくなるため。
                            if (isEcho) continue;

                            pit.BoxOpenTop(new Vector3(x0 + PitInset, -PitDepth, z0 + PitInset),
                                           new Vector3(x1 - PitInset, 0f, z1 - PitInset), PitRings);

                            // サーモ層で、その役に見える穴には開口のすぐ下に「熱の蓋」を張る。
                            // 熱源は縦坑の中にあるが床より下なので、目線からは中が見えず
                            // 穴が一切分からなかった。水平な面を1枚張れば、
                            // 距離や角度に関係なく「床に空いた明るい四角」として見える。
                            if (isThermal && o == pass)
                            {
                                float qx0 = x0 + PitInset, qx1 = x1 - PitInset;
                                float qz0 = z0 + PitInset, qz1 = z1 - PitInset;
                                float qy = -PitCapDepth;
                                pit.Quad(new Vector3(qx0, qy, qz0), new Vector3(qx0, qy, qz1),
                                         new Vector3(qx1, qy, qz1), new Vector3(qx1, qy, qz0), Vector3.up);
                            }
                        }

                        if (deck.Count > 0)
                            meshes.Add(Emit(rootGo.transform, tag + "_Deck_" + z, layer, deck, deckMat, pass == 2));
                        if (pit.Count > 0)
                            meshes.Add(Emit(rootGo.transform, tag + "_Pit_" + z, layer, pit, pitMat, pass == 2));
                    }

                    // --- 穴フィールドの外側（扉まわりの安全地帯）---
                    var outer = new MeshBuild();
                    if (s.rz0 < s.fz0)
                        outer.Box(new Vector3(s.rx0, -DeckThickness, s.rz0), new Vector3(s.rx1, 0f, s.fz0));
                    if (s.rz1 > s.fz1)
                        outer.Box(new Vector3(s.rx0, -DeckThickness, s.fz1), new Vector3(s.rx1, 0f, s.rz1));
                    if (outer.Count > 0)
                        meshes.Add(Emit(rootGo.transform, tag + "_OuterFloor", layer, outer, deckMat, pass == 2));
                }

                // --- 当たり判定 ---
                // 見た目のデッキとは別に作る。Default レイヤーの板には
                // 「サーモにだけ見える穴」「エコロケにだけ見える穴」の板も含まれていて、
                // そこに当たり判定を付けてしまうと穴が穴でなくなるため。
                // 板が有るのは3役ぶん揃って穴でないマス(owner < 0)だけ。
                {
                    var col = new MeshBuild();
                    for (int x = 0; x < s.nx; x++)
                        for (int z = 0; z < s.nz; z++)
                        {
                            if (owner[x, z] >= 0) continue;
                            float x0 = s.fx0 + cw * x, x1 = x0 + cw;
                            float z0 = s.fz0 + cd * z, z1 = z0 + cd;
                            col.Box(new Vector3(x0, -DeckThickness, z0), new Vector3(x1, 0f, z1));
                        }
                    if (s.rz0 < s.fz0)
                        col.Box(new Vector3(s.rx0, -DeckThickness, s.rz0), new Vector3(s.rx1, 0f, s.fz0));
                    if (s.rz1 > s.fz1)
                        col.Box(new Vector3(s.rx0, -DeckThickness, s.fz1), new Vector3(s.rx1, 0f, s.rz1));
                    meshes.Add(EmitCollider(rootGo.transform, "Collision", col));
                }

                // --- 落下判定 ---
                var fall = new GameObject("FallZone");
                Undo.RegisterCreatedObjectUndo(fall, "fall zone");
                fall.transform.SetParent(rootGo.transform, false);
                fall.layer = LayerDefault;
                fall.transform.position = new Vector3((s.fx0 + s.fx1) * 0.5f, -PitDepth * 0.6f, (s.fz0 + s.fz1) * 0.5f);
                var bc = fall.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = new Vector3(s.fx1 - s.fx0, PitDepth * 0.8f, s.fz1 - s.fz0);
                var hz = AddUdon(fall, "HazardZone");
                if (hz != null) { SetObj(hz, "checkpointManager", cm); PushUdon(hz); }

                SaveMeshes(meshes, s.room);

                log.AppendLine("  " + s.room + " : " + s.nx + "x" + s.nz + "マス  穴" + holes
                               + "(過去人" + CountOwner(owner, 0) + "/サーモ" + CountOwner(owner, 1)
                               + "/エコロケ" + CountOwner(owner, 2) + ")  安全" + solid
                               + "  元の床を" + hidden + "個停止  — " + s.note);
            }
            return log.ToString().TrimEnd();
        }

        static int CountOwner(int[,] o, int who)
        {
            int n = 0;
            for (int x = 0; x < o.GetLength(0); x++)
                for (int z = 0; z < o.GetLength(1); z++) if (o[x, z] == who) n++;
            return n;
        }

        /// <summary>
        /// 穴の配置を決める。
        ///
        /// まず「全員にとって安全な一本道」を入口列から出口列まで彫る。
        /// これが無いと乱数次第で物理的に通れない床が出来てしまう。
        /// 残りのマスを一定確率で穴にし、3役に配る。
        /// 同じ seed なら必ず同じ配置になるので、作り直しても攻略手順が変わらない。
        ///
        /// ⚠️ 当たり判定は owner が付いたマス全部が本物の穴（誰の穴でも落ちる）。
        /// 各役は自分の色の穴しか「見えない」だけなので、3人で読み合わないと渡れない。
        /// 安全な一本道だけが全員にとって床なので、この道の作り方が難易度を決める。
        /// </summary>
        static int[,] Assign(PitSpec s)
        {
            var safe = new bool[s.nx, s.nz];
            var rnd = new System.Random(s.seed);

            // ------------------------------------------------------------
            // 1. 安全な道を「必ず左右に振らせて」彫る
            // ------------------------------------------------------------
            // 以前は1行ごとに ±1 列ずらすだけだったので、乱数次第で道が端に
            // 貼り付いたまま奥まで通れてしまった（room14 は左2列がほぼ素通りだった）。
            // 端を歩くだけで抜けられる＝他の2人と相談しなくても越えられる、
            // という状態はこの部屋の存在意義を消してしまう。
            //
            // そこで数行ごとに「左寄りの帯」「右寄りの帯」を交互に通る折り返し点を置き、
            // その間を繋ぐ。プレイヤーは必ず横断させられる。
            var wp = new List<Vector2Int>();                  // (列, 行)
            wp.Add(new Vector2Int(Mathf.Clamp(s.entryCol, 0, s.nx - 1), s.nz - 1));

            int step = Mathf.Max(2, s.nz / 3);
            bool leftSide = rnd.Next(2) == 0;
            int band = Mathf.Max(1, s.nx / 3);
            for (int z = s.nz - 1 - step; z > 0; z -= step)
            {
                int lo = leftSide ? 0 : s.nx - band;
                int hi = leftSide ? band - 1 : s.nx - 1;
                wp.Add(new Vector2Int(Mathf.Clamp(lo + rnd.Next(hi - lo + 1), 0, s.nx - 1), z));
                leftSide = !leftSide;
            }
            wp.Add(new Vector2Int(Mathf.Clamp(s.exitCol, 0, s.nx - 1), 0));

            for (int i = 0; i < wp.Count - 1; i++)
            {
                var a = wp[i];
                var b = wp[i + 1];
                int z0 = a.y, z1 = b.y;
                int prevCol = -1;
                for (int z = z0; z >= z1; z--)
                {
                    float t = (z0 == z1) ? 1f : (float)(z0 - z) / (z0 - z1);
                    int col = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), 0, s.nx - 1);
                    safe[col, z] = true;

                    // 列が変わる行は横にも繋ぐ。斜めに1マスずつずらすだけだと
                    // 角でしか接しておらず、実際には渡れない床になる。
                    if (prevCol >= 0 && prevCol != col)
                    {
                        int c0 = Mathf.Min(prevCol, col), c1 = Mathf.Max(prevCol, col);
                        for (int x = c0; x <= c1; x++) safe[x, z] = true;
                    }
                    prevCol = col;
                }
            }

            // ------------------------------------------------------------
            // 2. 穴にするマスを選ぶ
            // ------------------------------------------------------------
            var owner = new int[s.nx, s.nz];
            var holes = new List<Vector2Int>();
            for (int z = 0; z < s.nz; z++)
                for (int x = 0; x < s.nx; x++)
                {
                    owner[x, z] = -1;
                    if (!safe[x, z] && rnd.NextDouble() < s.density) holes.Add(new Vector2Int(x, z));
                }

            for (int i = holes.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var tmp = holes[i]; holes[i] = holes[j]; holes[j] = tmp;
            }

            // ------------------------------------------------------------
            // 3. 3役に「近くに同じ役を固めない」ように配る
            // ------------------------------------------------------------
            // 以前はシャッフルして i%3 で配っていた。個数は揃うが場所は完全な運任せで、
            // 実際に「過去人の穴が4連続」「1行まるごとエコロケ」という配置が出ていた。
            // 1行がまるごと同じ役だと、その行はその1人が読むだけで越えられてしまい、
            // 3人で擦り合わせる必要が消える。
            //
            // そこで、置くたびに周囲2マスを見て「同じ役がいちばん少ない役」を選ぶ。
            // 近いほど重い penalty を掛けるので、同じ役が隣り合いにくくなる。
            // 第2基準として全体の個数を見るので、3役の総数もほぼ揃う。
            var used = new int[3];
            foreach (var h in holes)
            {
                int best = 0;
                float bestScore = float.MaxValue;
                for (int r = 0; r < 3; r++)
                {
                    float near = 0f;
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dz = -2; dz <= 2; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = h.x + dx, nz = h.y + dz;
                            if (nx < 0 || nz < 0 || nx >= s.nx || nz >= s.nz) continue;
                            if (owner[nx, nz] != r) continue;
                            near += 1f / Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                        }
                    float score = near * 4f + used[r];
                    if (score < bestScore) { bestScore = score; best = r; }
                }
                owner[h.x, h.y] = best;
                used[best]++;
            }

            // ------------------------------------------------------------
            // 4. 「穴がゼロの列」を作らない
            // ------------------------------------------------------------
            // 穴の無い列が1本でもあると、そこをまっすぐ歩くだけで部屋を越えられてしまい、
            // 3人で読み合う必要が消える。安全な道を左右に振らせても、密度が低い部屋では
            // 端の列がまるごと空くことがある（room4 で実際に左2列が素通りだった）。
            // 乱数の出目に任せず、列ごとに最低1つは穴があることを保証する。
            for (int x = 0; x < s.nx; x++)
            {
                bool any = false;
                for (int z = 0; z < s.nz; z++) if (owner[x, z] >= 0) { any = true; break; }
                if (any) continue;

                // 安全な道から最も離れたマスを選ぶ。道のすぐ隣を塞ぐと
                // 「道を1マス外したら即死」になって理不尽なので。
                int pick = -1, bestD = -1;
                for (int z = 0; z < s.nz; z++)
                {
                    if (safe[x, z]) continue;
                    int d = int.MaxValue;
                    for (int x2 = 0; x2 < s.nx; x2++)
                        if (safe[x2, z]) d = Mathf.Min(d, Mathf.Abs(x2 - x));
                    if (d > bestD) { bestD = d; pick = z; }
                }
                // 列がまるごと安全な道＝ただの通路なので、そのままでよい
                if (pick < 0) continue;

                int least = 0;
                for (int r = 1; r < 3; r++) if (used[r] < used[least]) least = r;
                owner[x, pick] = least;
                used[least]++;
            }

            return owner;
        }

        /// <summary>元の床（Default / Thermal / Echo の3系統）を止める。</summary>
        /// <summary>
        /// 落とし穴フロアを置く前に、そこにあった床を止める。
        ///
        /// ⚠️ 名前で「Floor」を探すだけでは足りない。
        /// `BlindVisionBuilder` はサーモ/エコロケ層を**温度キーごとに結合**するので、
        /// 部屋のカーペット床は `T_Fabric (22.0C)` のような名前になり、
        /// 「Floor」を含まない。実際これが穴の上に残っていて、
        /// **サーモ視点でもエコロケ視点でも穴が床で塞がれて一切見えなかった。**
        /// （45℃の熱源は正しく作られていたのに、その上を22℃の床が覆っていた）
        ///
        /// なので名前だけでなく**形で**判定する：薄くて、上面が床の高さにあり、
        /// 穴フィールドのある部屋の矩形と重なっている物は床とみなす。
        /// 壁は厚み(size.y)で、天井は高さ(max.y)で除外される。
        /// </summary>
        static int DisableOriginalFloor(Transform room, PitSpec s)
        {
            int n = 0;
            var floor = room.Find("GeneratedRoom/Floor");
            if (floor != null && floor.gameObject.activeSelf)
            {
                Undo.RecordObject(floor.gameObject, "hide floor");
                floor.gameObject.SetActive(false); n++;
            }
            foreach (var vn in new[] { "Vision_Thermal", "Vision_Echo" })
            {
                var v = room.Find(vn);
                if (v == null) continue;
                for (int i = 0; i < v.childCount; i++)
                {
                    var c = v.GetChild(i);
                    if (!c.gameObject.activeSelf) continue;

                    bool floorLike = c.name.Contains("Floor");
                    if (!floorLike)
                    {
                        var r = c.GetComponent<Renderer>();
                        if (r != null)
                        {
                            var b = r.bounds;
                            floorLike =
                                b.size.y <= 0.5f &&                        // 薄い（壁ではない）
                                b.max.y <= 0.35f && b.max.y >= -0.60f &&   // 上面が床の高さ（天井ではない）
                                b.max.x > s.rx0 && b.min.x < s.rx1 &&      // 部屋の矩形と重なる
                                b.max.z > s.rz0 && b.min.z < s.rz1;
                        }
                    }
                    if (!floorLike) continue;

                    Undo.RecordObject(c.gameObject, "hide floor");
                    c.gameObject.SetActive(false); n++;
                }
            }
            return n;
        }

        static Material FloorMaterialOf(Transform room)
        {
            var floor = room.Find("GeneratedRoom/Floor");
            if (floor != null)
            {
                var r = floor.GetComponentInChildren<Renderer>(true);
                if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
            }
            return new Material(Shader.Find("Standard"));
        }

        static Material MakeVoidMaterial()
        {
            const string path = GenMatDir + "/Pit_Void.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = new Color(0.045f, 0.045f, 0.05f);
            m.SetFloat("_Glossiness", 0.05f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ============================================================
        // メッシュ生成
        // ============================================================

        class MeshBuild
        {
            public List<Vector3> v = new List<Vector3>();
            public List<Vector3> n = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();
            public int Count { get { return t.Count; } }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(nrm);
                // ワールド座標をそのまま UV に使う。床材のタイリングが部屋全体で揃う。
                Vector3[] p = { a, b, c, d };
                foreach (var q in p)
                {
                    if (Mathf.Abs(nrm.y) > 0.5f) uv.Add(new Vector2(q.x, q.z));
                    else if (Mathf.Abs(nrm.x) > 0.5f) uv.Add(new Vector2(q.z, q.y));
                    else uv.Add(new Vector2(q.x, q.y));
                }
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            /// <summary>外向きの箱。踏み板用。</summary>
            public void Box(Vector3 lo, Vector3 hi)
            {
                Quad(new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, hi.y, lo.z), Vector3.up);
                Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), Vector3.down);
                Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, lo.y, lo.z), Vector3.back);
                Quad(new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, lo.y, hi.z), Vector3.forward);
                Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, lo.y, lo.z), Vector3.left);
                Quad(new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, lo.y, hi.z), Vector3.right);
            }

            /// <summary>上面の無い内向きの箱。落とし穴の縦坑用。</summary>
            public void BoxOpenTop(Vector3 lo, Vector3 hi)
            {
                BoxOpenTop(lo, hi, 1);
            }

            /// <summary>
            /// 上面の無い内向きの箱を、側面を rings 段の帯に割って作る。
            ///
            /// エコロケは面のUVの端にしか線を引かないので、側面が1枚だと
            /// 穴の中に何も描かれず、床のマス目と見分けが付かない。
            /// 帯に割ると1段ごとに輪が出て、下へ向かって縮む線の列＝深さになる。
            /// </summary>
            public void BoxOpenTop(Vector3 lo, Vector3 hi, int rings)
            {
                // 底（上を向く）。穴の底がどこかを示す手掛かりになる。
                Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, lo.y, lo.z), Vector3.up);

                if (rings < 1) rings = 1;
                for (int k = 0; k < rings; k++)
                {
                    float y0 = Mathf.Lerp(lo.y, hi.y, (float)k / rings);
                    float y1 = Mathf.Lerp(lo.y, hi.y, (float)(k + 1) / rings);

                    // 側面は内側を向ける（穴の中から見える面）
                    Quad(new Vector3(hi.x, y0, lo.z), new Vector3(hi.x, y1, lo.z), new Vector3(lo.x, y1, lo.z), new Vector3(lo.x, y0, lo.z), Vector3.forward);
                    Quad(new Vector3(lo.x, y0, hi.z), new Vector3(lo.x, y1, hi.z), new Vector3(hi.x, y1, hi.z), new Vector3(hi.x, y0, hi.z), Vector3.back);
                    Quad(new Vector3(lo.x, y0, lo.z), new Vector3(lo.x, y1, lo.z), new Vector3(lo.x, y1, hi.z), new Vector3(lo.x, y0, hi.z), Vector3.right);
                    Quad(new Vector3(hi.x, y0, hi.z), new Vector3(hi.x, y1, hi.z), new Vector3(hi.x, y1, lo.z), new Vector3(hi.x, y0, lo.z), Vector3.left);
                }
            }
        }

        /// <summary>
        /// メッシュを GameObject にする。
        /// 頂点はワールド座標で組んであるので、重心を出して
        /// そこへ Transform を置き、頂点はその分だけ引く。
        /// （EchoEmitter が transform.position を見るため、原点のままだと
        ///   部屋の中の位置関係が失われて反響が正しく光らない）
        /// </summary>
        static Mesh Bake(MeshBuild mb, string name, out Vector3 center)
        {
            center = Vector3.zero;
            foreach (var p in mb.v) center += p;
            center /= mb.v.Count;

            var mesh = new Mesh();
            mesh.name = name;
            var verts = new Vector3[mb.v.Count];
            for (int i = 0; i < mb.v.Count; i++) verts[i] = mb.v[i] - center;
            mesh.SetVertices(new List<Vector3>(verts));
            mesh.SetNormals(mb.n);
            mesh.SetUVs(0, mb.uv);
            mesh.SetTriangles(mb.t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Emit(Transform parent, string name, int layer, MeshBuild mb, Material mat, bool echo)
        {
            Vector3 center;
            var mesh = Bake(mb, name, out center);

            if (echo)
            {
                // 面（Quad）ごとに 0〜1 の UV を貼り直す。
                //
                // EchoHighlight は min(uv, 1-uv) で「UVの端からの距離」を測って線を引く。
                // つまり UV が 0〜1 の外に出ていると min() が負になり、
                // saturate() が 0 に潰れて edge = 1.0 ＝ 面全体が塗りつぶしになる。
                // 落とし穴の床は Quad() がワールド座標をそのまま UV に入れていた
                // （床材のタイリングを部屋全体で揃えるため）ので UV が -12〜0 になり、
                // エコロケ視点で床が一面のベタ塗りになって穴が全く読めなかった。
                //
                // メッシュ全体のバウンズで 0〜1 に正規化する手もあるが、
                // 踏み板は 9.2m の行メッシュ1枚に何枚ものタイルが入っているので、
                // それだと「大きな長方形が1個だけ縁取られる」ことになりマス目が読めない。
                // マス目こそがこの部屋の情報（どこが穴か）なので、面ごとに割り当てる。
                //
                // MeshBuild は Quad() 以外から頂点を足さないので、頂点は必ず
                // 4個ずつ a,b,c,d の順に並んでいる。その前提でそのまま角に割り当てる。
                var v = mesh.vertices;
                var uv = new Vector2[v.Length];
                for (int i = 0; i + 3 < v.Length; i += 4)
                {
                    uv[i]     = new Vector2(0f, 0f);
                    uv[i + 1] = new Vector2(0f, 1f);
                    uv[i + 2] = new Vector2(1f, 1f);
                    uv[i + 3] = new Vector2(1f, 0f);
                }
                mesh.SetUVs(0, uv);
            }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "pit piece");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            if (echo)
            {
                var rec = AddUdon(go, "EchoReceiver");
                if (rec != null)
                {
                    var so = new SerializedObject(rec);
                    var arr = so.FindProperty("targetRenderers");
                    if (arr != null) { arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = mr; }
                    so.ApplyModifiedProperties();
                    PushUdon(rec);
                }
            }
            return mesh;
        }

        /// <summary>見えない当たり判定だけの板。Renderer は付けない。</summary>
        static Mesh EmitCollider(Transform parent, string name, MeshBuild mb)
        {
            Vector3 center;
            var mesh = Bake(mb, name, out center);

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "pit collision");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.layer = LayerDefault;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            return mesh;
        }

        static void SaveMeshes(List<Mesh> meshes, string room)
        {
            if (meshes.Count == 0) return;
            string path = GenDir + "/PitField_" + room + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], path);
            AssetDatabase.ImportAsset(path);
        }

        // ============================================================
        // 1 & 3. ゲートボタン ＋ 復帰地点
        // ============================================================

        static string BuildGateButtons(Component cm)
        {
            var log = new System.Text.StringBuilder("ゲートボタン:\n");
            var sys = GameObject.Find("=== SYSTEM ===");
            var holder = Child(Child(Child(sys.transform, "GameManagement"), "Gimmicks"), "GateButtons");
            var cpRoot = Child(sys.transform.Find("resporn"), "Checkpoints");

            var door = FindShutterDoor();
            var echoMat = AssetDatabase.LoadAssetAtPath<Material>(EchoMatPath);
            var tBtn = BlindThermalTable.Mat("Button");
            var tLit = BlindThermalTable.Mat("ButtonLit");

            // 既存の生成物を消す
            for (int i = holder.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(holder.GetChild(i).gameObject);

            foreach (var b in Buttons)
            {
                var room = GameObject.Find("=== ROOMS ===/" + b.room);
                if (room == null) { log.AppendLine("  " + b.room + " : 見つからない"); continue; }

                Vector3 p;
                if (!FindOpenSpot(room.transform, out p))
                {
                    log.AppendLine("  " + b.room + " : 置ける空きが見つからない");
                    continue;
                }

                var go = new GameObject(b.name);
                Undo.RegisterCreatedObjectUndo(go, "gate button");
                go.transform.SetParent(holder, false);
                go.transform.position = p;
                go.layer = LayerDefault;

                var baseMat = MakeButtonMaterial(b.name + "_Body", b.color * 0.25f, Color.black);
                var litMat  = MakeButtonMaterial(b.name + "_Lit", b.color, b.color * 2.2f);

                // 台座と押しボタンの頭。3レイヤー分をそのまま重ねる。
                var lit = new List<GameObject>();
                for (int pass = 0; pass < 3; pass++)
                {
                    int layer = pass == 0 ? LayerDefault : (pass == 1 ? LayerThermal : LayerEcho);
                    string tag = pass == 0 ? "D" : (pass == 1 ? "T" : "E");
                    Material bodyM = pass == 0 ? baseMat : (pass == 1 ? tBtn : echoMat);
                    Material litM  = pass == 0 ? litMat  : (pass == 1 ? tLit : echoMat);

                    MakeBox(go.transform, tag + "_Post", layer, new Vector3(0f, 0.50f, 0f), new Vector3(0.22f, 1.00f, 0.22f), bodyM, pass == 2);
                    MakeBox(go.transform, tag + "_Head", layer, new Vector3(0f, 1.06f, 0f), new Vector3(0.42f, 0.14f, 0.42f), bodyM, pass == 2);
                    var l = MakeBox(go.transform, tag + "_Lit", layer, new Vector3(0f, 1.15f, 0f), new Vector3(0.30f, 0.06f, 0.30f), litM, pass == 2);
                    l.SetActive(false);
                    lit.Add(l);
                }

                // Interact 用の当たり判定（頭のまわりを少し大きめに）
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 1.00f, 0f);
                col.size = new Vector3(0.60f, 0.45f, 0.60f);

                var beh = AddUdon(go, "ColorGateButton");
                if (beh != null)
                {
                    var so = new SerializedObject(beh);
                    so.FindProperty("checkpointManager").objectReferenceValue = cm;
                    so.FindProperty("doorManager").objectReferenceValue = door;
                    so.FindProperty("buttonId").intValue = b.buttonId;
                    so.FindProperty("checkpointIndex").intValue = b.checkpoint;
                    var arr = so.FindProperty("litVisuals");
                    arr.arraySize = lit.Count;
                    for (int i = 0; i < lit.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = lit[i];
                    so.ApplyModifiedProperties();
                    PushUdon(beh);

                    var ub = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(
                        beh as UdonSharp.UdonSharpBehaviour);
                    if (ub != null) ub.interactText = "押す";
                }

                // 復帰地点はボタンの少し手前。ボタンにめり込まないよう +Z へ 1m。
                var cp = Child(cpRoot, "CP_" + b.checkpoint + "_" + b.name.Replace("Btn_", ""));
                cp.position = p + new Vector3(0f, 0f, 1.0f);
                cp.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, -1f), Vector3.up);

                log.AppendLine("  " + b.name + " @ " + b.room + " " + p.ToString("F2")
                               + "  ボタンID=" + b.buttonId + " 復帰地点=" + b.checkpoint + "  — " + b.note);
            }
            return log.ToString().TrimEnd();
        }

        static Material MakeButtonMaterial(string name, Color c, Color emission)
        {
            string path = GenMatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = c;
            m.SetFloat("_Glossiness", 0.4f);
            if (emission.maxColorComponent > 0.01f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission);
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject MakeBox(Transform parent, string name, int layer, Vector3 localPos,
                                  Vector3 size, Material mat, bool echo)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(go, "box");
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.layer = layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (echo)
            {
                var rec = AddUdon(go, "EchoReceiver");
                if (rec != null)
                {
                    var so = new SerializedObject(rec);
                    var arr = so.FindProperty("targetRenderers");
                    if (arr != null) { arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = mr; }
                    so.ApplyModifiedProperties();
                    PushUdon(rec);
                }
            }
            return go;
        }

        /// <summary>
        /// 部屋の中で「一番広く空いている床」を探す。
        /// 部屋の中身は担当者ごとにバラバラなので座標を決め打ちにせず、
        /// 実際のコライダーを見て置き場所を選ぶ。
        /// </summary>
        static bool FindOpenSpot(Transform room, out Vector3 result)
        {
            result = Vector3.zero;
            Bounds b = new Bounds(); bool found = false;
            foreach (var r in room.GetComponentsInChildren<Renderer>(false))
            {
                if (r.gameObject.layer != LayerDefault) continue;
                if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
            }
            if (!found) return false;

            Physics.SyncTransforms();
            float best = -1f;
            for (float x = b.min.x + 0.8f; x <= b.max.x - 0.8f; x += 0.35f)
                for (float z = b.min.z + 0.8f; z <= b.max.z - 0.8f; z += 0.35f)
                {
                    RaycastHit hit;
                    if (!Physics.Raycast(new Vector3(x, b.max.y - 0.2f, z), Vector3.down, out hit,
                                         b.size.y, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float gy = hit.point.y;
                    var lo = new Vector3(x, gy + 0.62f, z);
                    var hi = new Vector3(x, gy + 1.60f, z);
                    if (Physics.CheckCapsule(lo, hi, 0.20f, ~0, QueryTriggerInteraction.Ignore)) continue;

                    // まわりが同じ高さの床で繋がっていること。
                    // room19 は床に穴が空いている部屋なので、これが無いと
                    // 穴を跨いだ細い梁の上にボタンが立つことがある。
                    bool flat = true;
                    for (int d = 0; d < 4 && flat; d++)
                    {
                        float ox = (d == 0 ? 0.6f : d == 1 ? -0.6f : 0f);
                        float oz = (d == 2 ? 0.6f : d == 3 ? -0.6f : 0f);
                        RaycastHit h2;
                        if (!Physics.Raycast(new Vector3(x + ox, b.max.y - 0.2f, z + oz), Vector3.down,
                                             out h2, b.size.y, ~0, QueryTriggerInteraction.Ignore)
                            || Mathf.Abs(h2.point.y - gy) > 0.12f) flat = false;
                    }
                    if (!flat) continue;

                    // 空きの広さ = 半径を広げて当たるまで
                    float clear = 0.2f;
                    for (float r = 0.4f; r <= 1.6f; r += 0.2f)
                    {
                        if (Physics.CheckCapsule(lo, hi, r, ~0, QueryTriggerInteraction.Ignore)) break;
                        clear = r;
                    }
                    // 部屋の中央寄りを少しだけ優遇（隅にポツンと置かれるのを避ける）
                    float score = clear - 0.05f * Vector3.Distance(new Vector3(x, 0, z), new Vector3(b.center.x, 0, b.center.z));
                    if (score > best) { best = score; result = new Vector3(x, gy, z); }
                }
            return best > 0f;
        }

        // ============================================================
        // 4. 死亡判定
        // ============================================================

        static string BuildLaserHazards(Component cm)
        {
            int n = 0;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name != "LaserBeam") continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;

                var old = t.Find(HazardName);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                var go = new GameObject(HazardName);
                Undo.RegisterCreatedObjectUndo(go, "laser hazard");
                go.transform.SetParent(t, false);
                go.layer = LayerDefault;
                go.transform.position = r.bounds.center;
                go.transform.rotation = Quaternion.identity;

                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                // ビームは太さ2cmしかないので、そのままだと歩いてすり抜ける。
                // 体が触れたと感じる太さ(20cm)まで膨らませる。
                var s = r.bounds.size;
                bc.size = new Vector3(Mathf.Max(s.x, 0.20f), Mathf.Max(s.y, 0.20f), Mathf.Max(s.z, 0.20f));

                var hz = AddUdon(go, "HazardZone");
                if (hz != null) { SetObj(hz, "checkpointManager", cm); PushUdon(hz); }
                n++;
            }
            return "レーザーの死亡判定: " + n + "本に設置 (room14)";
        }

        static string BuildBurningHazard(Component cm)
        {
            var man = GameObject.Find("=== ROOMS ===/room15/Prop_BurningMannequin/Mannequin");
            if (man == null) return "燃えている人: room15 に見つからない";

            var old = man.transform.Find(HazardName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var go = new GameObject(HazardName);
            Undo.RegisterCreatedObjectUndo(go, "burning hazard");
            go.transform.SetParent(man.transform, false);
            go.layer = LayerDefault;
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            go.transform.localRotation = Quaternion.identity;

            var cc = go.AddComponent<CapsuleCollider>();
            cc.isTrigger = true;
            cc.radius = 0.75f;   // 炎の届く範囲。触れる前に燃え移る
            cc.height = 2.4f;
            cc.direction = 1;

            var hz = AddUdon(go, "HazardZone");
            if (hz != null) { SetObj(hz, "checkpointManager", cm); PushUdon(hz); }

            return "燃えている人の死亡判定: room15 の Mannequin に設置（半径0.75m・本体と一緒に動く）";
        }

        // ============================================================
        // 1. room3 のシャッター
        // ============================================================

        static Component FindShutterDoor()
        {
            var t = System.Type.GetType("MultiButtonDoor, Assembly-CSharp");
            if (t == null) return null;
            var all = UnityEngine.Object.FindObjectsOfType(t, true);
            return all.Length > 0 ? all[0] as Component : null;
        }

        static string ConfigureShutter()
        {
            var door = FindShutterDoor();
            if (door == null) return "シャッター: MultiButtonDoor が見つからない";

            var shutter = door.transform;
            var r = shutter.GetComponentInChildren<Renderer>();
            float height = r != null ? r.bounds.size.y : 2.4f;
            float lift = height + 0.15f;   // 上端が開口の上に完全に抜けきる高さ

            Vector3 closed = shutter.localPosition;
            Vector3 up = shutter.parent != null
                ? shutter.parent.InverseTransformVector(Vector3.up * lift)
                : Vector3.up * lift;
            Vector3 open = closed + up;

            var so = new SerializedObject(door);
            so.FindProperty("targetDoor1").objectReferenceValue = shutter;
            so.FindProperty("door1ClosedPos").vector3Value = closed;
            so.FindProperty("door1OpenPos").vector3Value = open;
            so.ApplyModifiedProperties();
            PushUdon(door);

            // サーモ用・エコロケ用のコピーはシャッターの子ではないので、
            // このままだと開いても2人には閉まったまま見える。子に付け替えて一緒に動かす。
            int moved = 0;
            foreach (var name in new[] { "T_Garage_Shutter", "E_Garage_Shutter" })
            {
                var copy = FindByName(name);
                if (copy == null || copy.parent == shutter) continue;
                Undo.SetTransformParent(copy, shutter, "reparent shutter vision copy");
                moved++;
            }

            return "シャッター: 閉=" + closed.ToString("F2") + " 開=" + open.ToString("F2")
                 + " (" + lift.ToString("F2") + "m上昇)  視界コピー" + moved + "個をシャッターの子に移動";
        }

        static Transform FindByName(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ============================================================
        // 3. 復帰地点の登録
        // ============================================================

        static string WireCheckpoints(Component cm)
        {
            var sys = GameObject.Find("=== SYSTEM ===");
            var cpRoot = Child(sys.transform.Find("resporn"), "Checkpoints");

            // 0番＝スタート地点。ワールドのスポーン位置に合わせる。
            var start = Child(cpRoot, "CP_0_Start");
            var desc = UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null)
            {
                start.position = desc.spawns[0].position;
                start.rotation = desc.spawns[0].rotation;
            }

            var points = new Transform[4];
            points[0] = start;
            foreach (var b in Buttons)
            {
                var t = cpRoot.Find("CP_" + b.checkpoint + "_" + b.name.Replace("Btn_", ""));
                if (t != null) points[b.checkpoint] = t;
            }
            for (int i = 0; i < points.Length; i++) if (points[i] == null) points[i] = start;

            var so = new SerializedObject(cm);
            var arr = so.FindProperty("checkpoints");
            arr.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedProperties();
            PushUdon(cm);

            return "復帰地点: " + points.Length + "個を CheckpointManager に登録 (0=スタート, 1=赤, 2=青, 3=緑)";
        }

        // ============================================================
        // Udon ヘルパー
        // ============================================================

        /// <summary>
        /// U# の付与は必ず UdonSharpUndo.AddComponent を通す。
        /// 素の AddComponent だと C# 側のプロキシしか出来ず、実機で動く
        /// UdonBehaviour が作られない（エディタでは動いて見えるので気付きにくい）。
        /// </summary>
        static Component AddUdon(GameObject go, string typeName)
        {
            var t = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (t == null) { Debug.LogError("BLIND: 型が見つからない " + typeName); return null; }
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

        /// <summary>プロキシに書いた値を実体の UdonBehaviour へ流し込む。忘れると実機で全部 null。</summary>
        static void PushUdon(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour;
            if (usb == null) return;
            if (UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) == null) return;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
        }

        static void SetObj(Component c, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("BLIND: フィールドが無い " + field + " on " + c.GetType().Name); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
