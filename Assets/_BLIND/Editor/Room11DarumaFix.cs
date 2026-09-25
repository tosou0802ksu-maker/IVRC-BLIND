using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room11（だるま部屋）のテストプレイで出た3件を直す。
    ///
    /// ------------------------------------------------------------------
    /// 1. デカだるま（Boss）がサーモ・エコロケから見えない
    /// ------------------------------------------------------------------
    ///   生成漏れではない。BlindGimmickBuilder は Boss の T_Body / E_Body /
    ///   E_Face をちゃんと作っている。**後から BlindVisionBuilder の
    ///   「動く物に残っていた旧生成物」削除に消された**（room6 の T_Leaf /
    ///   E_Leaf が毎回消えていたのと同じ事故）。
    ///
    ///   ⚠️ 消えていたのは見た目だけではない。DarumaWatcher の
    ///      thermalBody と echoParts の参照も道連れに null になっていて、
    ///      **「振り向いた」合図がサーモ役とエコロケ役に一切届いていなかった**。
    ///      この部屋のギミックそのものが2役ぶん壊れていた。
    ///      同じ理由で KeyGate の扉も D_Leaf しか残っていない。
    ///
    ///   削除を防ぐガード（BlindVisionBuilder.IsGimmickOwned）は既に入っているので、
    ///   ここで戻せば再発しない。
    ///
    /// ------------------------------------------------------------------
    /// 2. 鍵が空中に浮いて見える
    /// ------------------------------------------------------------------
    ///   棚の当たり判定が「棚1台まるごと1個の箱」で棚板のあいだに手が入らないため、
    ///   鍵は箱の外・棚板の 9cm 手前に置いてある。その受けが Ledge だが、
    ///   これは BoxCollider だけで MeshRenderer が無い。つまり
    ///   26cm×2cm の**透明な板**の上に鍵が乗っている。だから浮いて見える。
    ///   → 見える棚受けを足して、棚の面まで届かせる。
    ///
    /// ------------------------------------------------------------------
    /// 3. 鍵の位置が分からない
    /// ------------------------------------------------------------------
    ///   スタート(z=-56.9)から鍵(z=-28.5)まで 28m。鍵は 17cm・厚さ 1cm。
    ///   照明は色も強さも同一の点光源が 2列×13個。棚 FarRack_1_7〜11 も全部同じ見た目。
    ///   手がかりが画面上に一つも無い。役ごとに1つずつ配る:
    ///
    ///     過去人   : 棚の真上に1灯だけ色の違う点滅灯（FlickerLight）
    ///     サーモ   : 鍵に熱の暈（T_KeyGlow）。鍵と一緒に動くので運搬中も追える
    ///     エコロケ : 今回は入れない（別途 E_ リブを足すか要相談）
    ///
    ///   ⚠️ 点滅灯は range を絞った局所光にすること。部屋全体が明滅すると
    ///      光過敏性発作の危険がある。強さは FlickerLight 側でも下限を持たせている。
    ///
    ///   ⚠️ 「今後 過去人用に全体を暗くする」前提で置いている。
    ///      周りの26灯を落としても、この灯りだけは残すこと。落とすと手がかりが消える。
    /// </summary>
    public static class Room11DarumaFix
    {
        const string RoomName = "room11";

        const int LayerDefault = 0;
        const int LayerThermal = 22;
        const int LayerEcho    = 23;

        const string EchoPropMat = "Assets/_BLIND/Art/Materials/Echo/EchoMaterial_Prop.mat";
        const string EchoMatAlt  = "Assets/_BLIND/Art/Materials/EchoMaterial.mat";
        const string BossThermal = "Assets/_BLIND/Art/Materials/Gimmick/Thermal_Watcher_Material_002_baseColor.mat";
        const string DarumaMesh  = "Assets/_BLIND/Art/Models/Room12Daruma/DarumaA.asset";

        /// <summary>鍵の棚の真上に吊る点滅灯。既存の灯具の並び(x=12.05)ではなく棚の真上に置く。</summary>
        const float LampY = 2.88f;

        [MenuItem("BLIND/部屋修正/11. だるま部屋を直す（Boss視界・鍵の浮き・鍵のヒント）")]
        public static void Menu_Fix()
        {
            var room = GameObject.Find("=== ROOMS ===/" + RoomName);
            if (room == null) { Debug.LogError(RoomName + " が見つからない。"); return; }

            var log = new System.Text.StringBuilder();
            log.AppendLine("== room11 修正 ==");
            log.AppendLine(RestoreBossVision(room.transform));
            log.AppendLine(RestoreGateVision(room.transform));
            log.AppendLine(FixLedge(room.transform));
            log.AppendLine(KeyThermalBeacon(room.transform));
            log.AppendLine(KeyLamp(room.transform));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log(log.ToString());
        }

        // ============================================================
        // 1. Boss の視界コピーを戻し、DarumaWatcher を再配線する
        // ============================================================
        static string RestoreBossVision(Transform room)
        {
            var dw = room.Find("DarumaWatcher_Generated");
            if (dw == null) return "Boss: DarumaWatcher_Generated が無い。先に BLIND/ギミック/1";
            var boss = dw.Find("Boss");
            if (boss == null) return "Boss: Boss が無い";

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DarumaMesh);
            if (mesh == null) return "Boss: " + DarumaMesh + " が無い";
            var tMat = AssetDatabase.LoadAssetAtPath<Material>(BossThermal);
            if (tMat == null) tMat = BlindThermalTable.Mat("Watcher");
            if (tMat == null) return "Boss: サーモ材質が無い。先に BLIND/vision/1";
            var eMat = EchoMat();
            if (eMat == null) return "Boss: エコロケ材質が無い";

            int made = 0;
            var tBody = boss.Find("T_Body");
            if (tBody == null) { tBody = MakeMeshChild(boss, "T_Body", LayerThermal, mesh, tMat, false).transform; made++; }

            var eBody = boss.Find("E_Body");
            if (eBody == null) { eBody = MakeMeshChild(boss, "E_Body", LayerEcho, mesh, eMat, true).transform; made++; }

            // 顔の板（エコロケ用だけ）。
            // ⚠️ だるまはシルエットが前後でほぼ同じなので、輪郭しか見えないエコロケ役には
            //    **どちらを向いているか判別できない**。向きが全てのギミックなので致命的。
            //    サーモ側には貼らない（Thermal_Watcher が元の絵から温度差を作るので潰れる）。
            var eFace = boss.Find("E_Face");
            if (eFace == null)
            {
                eFace = MakeBox(boss, "E_Face", LayerEcho,
                                new Vector3(0f, 0.56f, 0.44f),
                                new Vector3(0.52f, 0.40f, 0.10f), eMat, true).transform;
                made++;
            }

            // --- 再配線 ---
            // ここが本題。見た目より、この参照が null だったことのほうが重い。
            var watcher = dw.GetComponent("DarumaWatcher") as Component;
            if (watcher == null) return "Boss: 視界コピーを " + made + " 個戻したが DarumaWatcher が無く再配線できない";

            SetObj(watcher, "thermalBody", tBody.GetComponent<MeshRenderer>());
            SetObjArray(watcher, "echoParts", new Object[] { EchoRec(eBody.gameObject), EchoRec(eFace.gameObject) });
            PushUdon(watcher);

            return "Boss: 視界コピーを " + made + " 個復元し、DarumaWatcher の thermalBody / echoParts を再配線"
                 + "（振り向き合図がサーモ・エコロケに戻った）";
        }

        // ============================================================
        // 2. 施錠扉もサーモ・エコロケから見えるようにする
        // ============================================================
        static string RestoreGateVision(Transform room)
        {
            var leaf = room.Find("KeyGate_Generated/Leaf");
            if (leaf == null) return "施錠扉: Leaf が無い";
            var d = leaf.Find("D_Leaf");
            if (d == null) return "施錠扉: D_Leaf が無い";

            var tMat = BlindThermalTable.Mat("Gate");
            var eMat = EchoMat();
            if (tMat == null) return "施錠扉: Thermal_Gate が無い。先に BLIND/vision/1";

            int made = 0;
            if (leaf.Find("T_Leaf") == null) { CloneAs(d, "T_Leaf", LayerThermal, tMat, false); made++; }
            if (leaf.Find("E_Leaf") == null) { CloneAs(d, "E_Leaf", LayerEcho,    eMat, true);  made++; }
            return "施錠扉: 視界コピーを " + made + " 個復元（壁と区別できる板として読める）";
        }

        // ============================================================
        // 3. 鍵の受け皿を見えるようにする
        // ============================================================
        static string FixLedge(Transform room)
        {
            var ledge = room.Find("KeyProp_Generated/Ledge");
            if (ledge == null) return "受け皿: Ledge が無い";
            if (ledge.Find("D_Ledge") != null) return "受け皿: 既に見えるようになっている";

            var lc = ledge.GetComponent<BoxCollider>();
            float thick = lc != null ? lc.size.y : 0.02f;

            // ⚠️ 当たり判定は動かさない。鍵はこの板の上に初期配置されているので、
            //    動かすと開始直後に床へ落ちる。
            //    見た目だけ棚側（+X）へ伸ばして、棚から突き出した受けに見せる。
            //    棚の前面は x=13.455、Ledge の中心は x=13.36 なので 0.145m 重ねれば繋がる。
            var plateMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_BLIND/Art/Materials/Gimmick/KeyPanel_Plate.mat");

            MakeBox(ledge, "D_Ledge", LayerDefault,
                    new Vector3(0.07f, 0f, 0f), new Vector3(0.34f, thick, 0.26f), plateMat, false);
            MakeBox(ledge, "T_Ledge", LayerThermal,
                    new Vector3(0.07f, 0f, 0f), new Vector3(0.34f, thick, 0.26f),
                    BlindThermalTable.Mat("Metal"), false);
            MakeBox(ledge, "E_Ledge", LayerEcho,
                    new Vector3(0.07f, 0f, 0f), new Vector3(0.34f, thick, 0.26f), EchoMat(), true);

            return "受け皿: 見える棚受け（0.34×0.26m）を追加。棚の面まで届かせたので鍵が浮かなくなった";
        }

        // ============================================================
        // 4. サーモ役のヒント: 鍵に熱の暈をつける
        // ============================================================
        static string KeyThermalBeacon(Transform room)
        {
            var key = room.Find("KeyProp_Generated/Key");
            if (key == null) return "熱の暈: Key が無い";

            var tMat = BlindThermalTable.Mat("Key");
            if (tMat == null) return "熱の暈: Thermal_Key が無い。先に BLIND/vision/1";

            // ⚠️ 棚ではなく**鍵の子**にする。棚に付けると鍵を取った後も光り続けて嘘になるし、
            //    鍵に付ければ運搬中もサーモ役が「今どこにあるか」を追える。
            var glow = key.Find("T_KeyGlow");
            if (glow == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Undo.RegisterCreatedObjectUndo(go, "key glow");
                go.name = "T_KeyGlow";
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);   // 掴み判定を邪魔しない
                go.transform.SetParent(key, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one * 0.26f;
                go.layer = LayerThermal;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = tMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                glow = go.transform;
            }

            // 脈打たせる。66℃で固定だと「白い点」が止まったまま＝一度見たら情報が増えない。
            // 周期を 1.2〜1.8 秒と短くしてあるのは、この部屋の他の熱源（人 7〜19秒）と
            // リズムで区別させるため。サーモ役は色ではなく**拍**で鍵を見分けられる。
            var root = room.Find("KeyProp_Generated");
            var drift = root.GetComponent("ThermalBodyDrift") as Component;
            if (drift == null) drift = AddUdon(root.gameObject, "ThermalBodyDrift");
            if (drift != null)
            {
                SetObjArray(drift, "targets", new Object[] { glow.GetComponent<MeshRenderer>() });
                SetFloat(drift, "amplitude", 0.30f);
                SetFloat(drift, "minCycle", 1.2f);
                SetFloat(drift, "maxCycle", 1.8f);
                SetInt(drift, "warmIndex", -1);
                SetSyncMode(drift, "None");
                PushUdon(drift);
            }

            return "熱の暈: 鍵に 0.26m の熱源（66℃）を追加し、1.2〜1.8秒で脈動させた"
                 + "（鍵の子なので運搬中も追える）";
        }

        // ============================================================
        // 5. 過去人のヒント: 鍵の棚の真上に点滅灯
        // ============================================================
        static string KeyLamp(Transform room)
        {
            var key = room.Find("KeyProp_Generated/Key");
            if (key == null) return "点滅灯: Key が無い";
            var root = room.Find("KeyProp_Generated");

            var old = root.Find("KeyLamp_Generated");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var lampRoot = new GameObject("KeyLamp_Generated");
            Undo.RegisterCreatedObjectUndo(lampRoot, "key lamp");
            lampRoot.transform.SetParent(root, false);
            lampRoot.transform.position = new Vector3(key.position.x, LampY, key.position.z);
            lampRoot.layer = LayerDefault;

            // --- 灯具 ---
            // 既存の灯具（Housing 0.28×0.10×1.25 / Lens 0.22×0.02×1.18）に合わせる。
            // 同じ形で色だけ違うほうが「同じ器具の1つが壊れている」と読めて、
            // 見慣れない物を置くより自然に目が行く。
            var housingMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_BLIND/Art/Materials/Gimmick/KeyPanel_Plate.mat");
            MakeBox(lampRoot.transform, "D_Housing", LayerDefault,
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.28f, 0.10f, 1.25f), housingMat, false);
            MakeBox(lampRoot.transform, "T_Housing", LayerThermal,
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.28f, 0.10f, 1.25f),
                    BlindThermalTable.Mat("Metal"), false);
            MakeBox(lampRoot.transform, "E_Housing", LayerEcho,
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.28f, 0.10f, 1.25f), EchoMat(), true);

            var lensGo = MakeBox(lampRoot.transform, "D_Lens", LayerDefault,
                                 new Vector3(0f, -0.03f, 0f), new Vector3(0.22f, 0.02f, 1.18f),
                                 LensMaterial(), false);

            // サーモ側は LampDim（32℃）。
            // ⚠️ Lamp(52℃) や Ballast(68℃) を使ってはいけない。鍵が 66℃ なので、
            //    真上に同じ温度の物を置くと**どちらが鍵か分からなくなる**。
            //    ちらついて出力の落ちた灯り、という設定にも LampDim がそのまま合う。
            MakeBox(lampRoot.transform, "T_Lens", LayerThermal,
                    new Vector3(0f, -0.03f, 0f), new Vector3(0.22f, 0.02f, 1.18f),
                    BlindThermalTable.Mat("LampDim"), false);

            // --- 光源 ---
            // ⚠️ Spot にして range を絞る。Point で range を広げると部屋全体が明滅し、
            //    光過敏性発作の危険が出る。ここは棚の周りだけを照らす。
            var lightGo = new GameObject("Light_KeyShelf");
            Undo.RegisterCreatedObjectUndo(lightGo, "key lamp light");
            lightGo.transform.SetParent(lampRoot.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            lightGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 真下を向く
            var li = lightGo.AddComponent<Light>();
            li.type = LightType.Spot;
            li.spotAngle = 46f;
            li.range = 4.5f;                 // 棚と足元まで。部屋(33m)には届かない
            li.intensity = 4.0f;
            li.color = new Color(0.45f, 1.0f, 0.55f);   // 非常灯の緑。周りの26灯は白(0.94,0.97,1.00)
            li.shadows = LightShadows.None;  // 12,407 個のレンダラーがある部屋で影は焚かない

            // 周りの灯りと同じ物だけ照らす。層の設定は既存灯から写す
            // （決め打ちにすると、あとで層構成を変えたときにここだけ取り残される）。
            var sample = FindRoomLight(room);
            if (sample != null) li.cullingMask = sample.cullingMask;

            // --- 点滅 ---
            var fl = AddUdon(lampRoot, "FlickerLight");
            if (fl != null)
            {
                SetObj(fl, "target", li);
                SetObjArray(fl, "emissive", new Object[] { lensGo.GetComponent<MeshRenderer>() });
                SetColor(fl, "emissionColor", new Color(0.45f, 1.0f, 0.55f));
                SetFloat(fl, "baseIntensity", 4.0f);
                SetFloat(fl, "floorLevel", 0.12f);
                SetSyncMode(fl, "None");
                PushUdon(fl);
            }

            return "点滅灯: 鍵の棚の真上 y=" + LampY.ToString("F2")
                 + " に緑の点滅灯（Spot / range 4.5 / 影なし）を設置。"
                 + (fl != null ? "FlickerLight 配線済み" : "⚠️ FlickerLight が付けられなかった");
        }

        /// <summary>既存の部屋灯を1つ拾う（層マスクを写すため）。生成した点滅灯自身は除く。</summary>
        static Light FindRoomLight(Transform room)
        {
            foreach (var l in room.GetComponentsInChildren<Light>(true))
                if (l.type == LightType.Point) return l;
            return null;
        }

        static Material LensMaterial()
        {
            const string dir = "Assets/_BLIND/Art/Materials/Gimmick";
            const string path = dir + "/Room11_KeyLamp_Lens.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find("Standard"));
            m.color = new Color(0.80f, 0.92f, 0.82f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", new Color(0.45f, 1.0f, 0.55f));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ============================================================
        // 小物
        // ============================================================
        static Material EchoMat()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(EchoPropMat);
            return m != null ? m : AssetDatabase.LoadAssetAtPath<Material>(EchoMatAlt);
        }

        /// <summary>D_ の板をそのまま複製して層と材質だけ差し替える。位置と大きさを写し間違えない。</summary>
        static GameObject CloneAs(Transform src, string name, int layer, Material mat, bool echo)
        {
            var go = Object.Instantiate(src.gameObject, src.parent);
            Undo.RegisterCreatedObjectUndo(go, "clone vision");
            go.name = name;
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;
            go.layer = layer;
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (echo) WireEcho(go, mr);
            }
            return go;
        }

        static GameObject MakeMeshChild(Transform parent, string name, int layer,
                                        Mesh mesh, Material mat, bool echo)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "mesh child");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (echo) WireEcho(go, mr);
            return go;
        }

        static GameObject MakeBox(Transform parent, string name, int layer, Vector3 localPos,
                                  Vector3 size, Material mat, bool echo)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(go, "box");
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.layer = layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (echo) WireEcho(go, mr);
            return go;
        }

        static void WireEcho(GameObject go, MeshRenderer mr)
        {
            var rec = AddUdon(go, "EchoReceiver");
            if (rec == null) return;
            SetObjArray(rec, "targetRenderers", new Object[] { mr });
            PushUdon(rec);
        }

        static Object EchoRec(GameObject go)
        {
            if (go == null) return null;
            var t = System.Type.GetType("EchoReceiver, Assembly-CSharp");
            return t == null ? null : go.GetComponent(t);
        }

        static Component AddUdon(GameObject go, string typeName)
        {
            var t = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (t == null) { Debug.LogError("BLIND: 型が見つからない " + typeName); return null; }
            var existing = go.GetComponent(t);
            if (existing != null) return existing;

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

        /// <summary>
        /// 実体の UdonBehaviour の同期方式を指定する。
        /// ⚠️ スクリプトに属性を書いても AddComponent した実体には反映されない。
        /// </summary>
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
            if (idx < 0) return;
            p.enumValueIndex = idx;
            so.ApplyModifiedProperties();
        }

        /// <summary>プロキシに書いた値を実体の UdonBehaviour へ流し込む。忘れると実機で全部 null。</summary>
        static void PushUdon(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour;
            if (usb == null) return;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
            EditorUtility.SetDirty(usb);
        }

        static void SetObj(Component c, string field, Object value)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("BLIND: フィールドが無い " + field + " on " + c.GetType().Name); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        static void SetObjArray(Component c, string field, Object[] values)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("BLIND: フィールドが無い " + field + " on " + c.GetType().Name); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedProperties();
        }

        static void SetFloat(Component c, string field, float v)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.floatValue = v;
            so.ApplyModifiedProperties();
        }

        static void SetInt(Component c, string field, int v)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.intValue = v;
            so.ApplyModifiedProperties();
        }

        static void SetColor(Component c, string field, Color v)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.colorValue = v;
            so.ApplyModifiedProperties();
        }
    }
}
