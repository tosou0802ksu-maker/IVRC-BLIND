using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room12「だるまの資料室」の躯体。8m(X) × 20m(Z) × 高さ4m。
    /// 扉は西壁 z=2（room11 から）と東壁 z=18（room13 へ）。長辺を端から端まで歩かされる。
    /// 昭和の公共建築の資料室：テラゾーの床、塗装壁＋腰壁、吊り天井グリッド＋直管蛍光灯、剥き出しのダクト。
    /// </summary>
    public static class Room12Shell
    {
        const float SizeX = 8f;
        /// <summary>
        /// 部屋の長さ(m)。だるまさんがころんだの助走距離そのものなので、
        /// ここを変えたら必ず動線を測り直すこと（BLIND/room12/3. Verify Circulation）。
        ///
        /// 20 → 33 に伸ばしてある。北へ伸ばす余地は z=-24.8 まで（その先は room18）。
        /// **扉はローカル座標で定義してあるので、伸ばしても位置は動かない**
        /// （西の入口 z=2 → world -55.8 / 東の出口 z=18 → world -39.8）。
        /// 結果、出口は部屋の途中になり、奥へ行って折り返す動線になる。
        /// </summary>
        const float SizeZ = 33f;
        const float CeilY = 2.95f;    // 吊り天井の高さ
        const float DadoY = 1.15f;    // 腰壁の高さ
        // 壁は境界線をまたいで立っている（厚さ0.2）ので、部屋の内側の面はここ
        const float InX0 = 0.10f, InX1 = 7.90f, InZ0 = 0.10f, InZ1 = SizeZ - 0.10f;
        // 扉：南壁(x=0)は z=2 で room11 へ、北壁(x=8)は z=18 で room13 へ。高さは隣室に合わせて 2.3
        const float DoorS = 2f, DoorN = 18f, DoorW = 1.2f, DoorH = 2.3f;
        const string MatDir = "Assets/_BLIND/Art/Materials/";
        const string TexDir = "Assets/_BLIND/Art/Textures/";

        /// <summary>
        /// だるまの部屋を探す。
        ///
        /// ⚠️ 以前は「ルート直下の room12」を名前で探していたが、
        ///    (1) 部屋が `=== ROOMS ===` の下へ移されてルート直下から消え、
        ///    (2) 部屋番号が振り直されて room12 → room11 になった
        ///    ため、**この関数は常に null を返し、Build Shell が何もしなくなっていた**。
        ///    名前と階層に依存しないよう、中身（Prop_Daruma を持つ部屋）で照合する。
        /// </summary>
        static GameObject Room()
        {
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.Find("Prop_Daruma") == null) continue;
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                return t.gameObject;
            }
            // 保険：昔の置き方でも見つかるようにしておく
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == "room12" && g.GetComponentsInChildren<Renderer>(true).Length > 0) return g;
            return null;
        }

        static Material Surface(string name, string texSet, Color tint, float texScale,
                                float smooth, float metallic, float saturation, float ambient)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("BLIND/RoomSurface");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            if (!string.IsNullOrEmpty(texSet))
            {
                var col = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + texSet + "/" + texSet + "_Color.jpg");
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + texSet + "/" + texSet + "_NormalGL.jpg");
                if (col != null) m.SetTexture("_MainTex", col);
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_NormalStrength", texSet == "OfficeCeiling001" ? 0.25f : 0.8f); }
            }
            m.SetColor("_Color", tint);
            m.SetFloat("_TextureScale", texScale);
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Saturation", saturation);
            m.SetFloat("_AmbientBoost", ambient);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Std(string name, Color c, float smooth, float metallic, Color emission)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Standard");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_Color", c);
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("BLIND/room12/Build Shell")]
        public static string Build()
        {
            var room = Room();
            if (room == null) return "room12（中身のある方）が見つからない";

            // ── マテリアル
            var mFloor = Surface("Room12_Floor", "Tiles110", new Color(0.62f, 0.60f, 0.56f), 3.6f, 0.30f, 0f, 0.55f, 0.03f);
            var mWall = Surface("Room12_Wall", "PaintedPlaster017", new Color(0.66f, 0.67f, 0.62f), 2.4f, 0.15f, 0f, 0.45f, 0.03f);
            var mDado = Surface("Room12_Dado", "PaintedPlaster017", new Color(0.245f, 0.285f, 0.265f), 2.4f, 0.26f, 0f, 0.55f, 0.02f);
            var mTrim = Std("Room12_Trim", new Color(0.30f, 0.31f, 0.30f), 0.35f, 0.25f, Color.black);
            var mCeil = Surface("Room12_CeilingTile", "OfficeCeiling001", new Color(0.66f, 0.66f, 0.63f), 3.6f, 0.06f, 0f, 0.30f, 0.05f);
            var mDuct = Std("Room12_Duct", new Color(0.42f, 0.43f, 0.44f), 0.42f, 0.55f, Color.black);
            var mHous = Std("Room12_LampHousing", new Color(0.52f, 0.52f, 0.50f), 0.30f, 0.35f, Color.black);
            var mLens = Std("Room12_LampLens", new Color(0.92f, 0.94f, 0.90f), 0.55f, 0f, new Color(1.00f, 0.98f, 0.90f) * 3.2f);
            AssetDatabase.SaveAssets();

            // ── RoomBuilder3 は 8×20 の方を使う（6×6 の重複コンポーネントは触らない）
            object rb = null;
            System.Type rt = null;
            foreach (var c in room.GetComponents<MonoBehaviour>())
            {
                var t = c.GetType();
                if (!t.Name.StartsWith("RoomBuilder")) continue;
                var f = t.GetField("roomDepthX");
                if (f == null) continue;
                if (Mathf.Approximately((float)System.Convert.ToDouble(f.GetValue(c)), SizeX)) { rb = c; rt = t; break; }
            }
            if (rb == null) return "8×20 の RoomBuilder3 が見つからない";

            // ⚠️ 長さは必ずここで入れ直すこと。SizeZ を変えただけでは
            //    RoomBuilder3 側は古い値のままで、床と壁が伸びない。
            var fz = rt.GetField("roomWidthZ");
            if (fz != null) fz.SetValue(rb, SizeZ);

            rt.GetField("floorMaterial").SetValue(rb, mFloor);
            rt.GetField("wallMaterial").SetValue(rb, mWall);
            rt.GetField("doorFrameMaterial").SetValue(rb, mTrim);
            rt.GetField("hasCeiling").SetValue(rb, false);

            // doorOffset は部屋の中心からの相対値。隣室に繋がる位置へ戻す
            SetDoor(rb, rt, "southWall", DoorS - SizeZ * 0.5f);
            SetDoor(rb, rt, "northWall", DoorN - SizeZ * 0.5f);

            rt.GetMethod("BuildRoom").Invoke(rb, null);

            var log = new System.Text.StringBuilder();
            log.AppendLine("躯体を再生成（床=テラゾー / 壁=塗装 / 枠=グレー）");

            // ── 腰壁と天井まわり
            Rebuild(room, "Room12_WallPatch", p => BuildWallPatch(p, mWall, mDado), log);
            Rebuild(room, "Room12_Dado", p => BuildDado(p, mDado, mTrim), log);
            Rebuild(room, "Room12_Ceiling", p => BuildCeiling(p, mCeil, mTrim), log);
            Rebuild(room, "Room12_Duct", p => BuildDuct(p, mDuct, mTrim), log);
            Rebuild(room, "Room12_Lights", p => BuildLights(p, mHous, mLens), log);

            EditorUtility.SetDirty(room);
            return log.ToString();
        }

        static void SetDoor(object rb, System.Type rt, string wall, float offset)
        {
            var f = rt.GetField(wall);
            var w = f.GetValue(rb);
            var wt = w.GetType();
            wt.GetField("hasDoor").SetValue(w, true);
            wt.GetField("doorWidth").SetValue(w, DoorW);
            wt.GetField("doorHeight").SetValue(w, DoorH);
            wt.GetField("doorOffset").SetValue(w, offset);
            f.SetValue(rb, w);
        }

        static void Rebuild(GameObject room, string name, System.Action<Transform> build, System.Text.StringBuilder log)
        {
            var old = room.transform.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(name);
            go.transform.SetParent(room.transform, false);
            build(go.transform);
            int n = go.GetComponentsInChildren<Renderer>(true).Length;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Renderer>() == null) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic);
            }
            log.AppendLine(name + " : " + n + " renderers");
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, float yaw = 0f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        /// <summary>腰壁：壁の下half を濃い色の板で覆い、上端に見切り縁を回す。扉の位置は避ける。</summary>
        /// <summary>
        /// 東壁の内側に貼る化粧板。
        ///
        /// ⚠️ 部屋を北へ伸ばした結果、**隣の room14 の西壁がこの部屋の内側に入り込んだ**。
        /// room14 の壁は x 13.9〜14.1、この部屋の壁は x 14.1〜14.3 なので、
        /// 伸ばした範囲だけ**手前に他人の部屋の壁面が見えてしまう**
        /// （実機で「隣の部屋の壁が出てきておかしい」と指摘された黄色い面がこれ）。
        ///
        /// room14 は他メンバーの部屋なので**あちらの壁は消さない**。
        /// こちら側から1枚かぶせて、見える面をこの部屋の材質に揃える。
        /// 貼るのは元の北端(ローカルZ=20.1)から新しい北端まで。
        /// </summary>
        static void BuildWallPatch(Transform p, Material mWall, Material mDado)
        {
            const float OldZ1 = 20.10f;              // 伸ばす前の北端
            if (SizeZ <= OldZ1 + 0.2f) return;       // 伸ばしていないなら不要

            float z0 = OldZ1, z1 = SizeZ - 0.10f;
            float zc = (z0 + z1) * 0.5f, zl = z1 - z0;
            float xf = 7.68f;                        // room14 の壁面(7.70)より 2cm 手前

            Patch(p, "PatchWall", mWall, new Vector3(xf, 2.00f, zc), new Vector3(0.03f, 4.00f, zl));
            Patch(p, "PatchDado", mDado, new Vector3(xf - 0.02f, DadoY * 0.5f, zc), new Vector3(0.03f, DadoY, zl));
        }

        static void Patch(Transform p, string name, Material m, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            go.transform.SetParent(p, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void BuildDado(Transform p, Material mDado, Material mTrim)
        {
            float half = DoorW * 0.5f + 0.06f;
            // 長辺（x=0 と x=8 の壁）。それぞれ扉の前後で分ける
            AddDadoRun(p, mDado, mTrim, true, InX0, +1f, InZ0, DoorS - half);
            AddDadoRun(p, mDado, mTrim, true, InX0, +1f, DoorS + half, InZ1);
            AddDadoRun(p, mDado, mTrim, true, InX1, -1f, InZ0, DoorN - half);
            AddDadoRun(p, mDado, mTrim, true, InX1, -1f, DoorN + half, InZ1);
            // 短辺（z=0 と z=20 の壁）。扉なし
            AddDadoRun(p, mDado, mTrim, false, InZ0, +1f, InX0, InX1);
            AddDadoRun(p, mDado, mTrim, false, InZ1, -1f, InX0, InX1);
        }

        static void AddDadoRun(Transform p, Material mDado, Material mTrim,
                               bool alongZ, float wallFace, float dir, float a, float b)
        {
            float len = b - a;
            if (len <= 0.05f) return;
            float mid = (a + b) * 0.5f;
            const float d = 0.035f;   // 腰壁の出

            if (alongZ)
            {
                Box(p, "Dado", new Vector3(wallFace + dir * d * 0.5f, DadoY * 0.5f, mid),
                    new Vector3(d, DadoY, len), mDado);
                Box(p, "DadoCap", new Vector3(wallFace + dir * d * 0.8f, DadoY + 0.03f, mid),
                    new Vector3(d * 1.7f, 0.06f, len), mTrim);
            }
            else
            {
                Box(p, "Dado", new Vector3(mid, DadoY * 0.5f, wallFace + dir * d * 0.5f),
                    new Vector3(len, DadoY, d), mDado);
                Box(p, "DadoCap", new Vector3(mid, DadoY + 0.03f, wallFace + dir * d * 0.8f),
                    new Vector3(len, 0.06f, d * 1.7f), mTrim);
            }
        }

        /// <summary>吊り天井。タイルは一枚のスラブにテクスチャで格子を出し、周囲に見切りを回す。</summary>
        static void BuildCeiling(Transform p, Material mCeil, Material mTrim)
        {
            Box(p, "Tiles", new Vector3(SizeX * 0.5f, CeilY + 0.025f, SizeZ * 0.5f),
                new Vector3(InX1 - InX0, 0.05f, InZ1 - InZ0), mCeil);
            // 天井より上は見せない蓋
            var cap = Box(p, "Plenum", new Vector3(SizeX * 0.5f, CeilY + 0.10f, SizeZ * 0.5f),
                new Vector3(InX1 - InX0, 0.06f, InZ1 - InZ0), mTrim);
            cap.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 周囲の見切り（アングル）
            Box(p, "EdgeW", new Vector3(InX0 + 0.04f, CeilY - 0.02f, SizeZ * 0.5f), new Vector3(0.08f, 0.09f, InZ1 - InZ0), mTrim);
            Box(p, "EdgeE", new Vector3(InX1 - 0.04f, CeilY - 0.02f, SizeZ * 0.5f), new Vector3(0.08f, 0.09f, InZ1 - InZ0), mTrim);
            Box(p, "EdgeS", new Vector3(SizeX * 0.5f, CeilY - 0.02f, InZ0 + 0.04f), new Vector3(InX1 - InX0, 0.09f, 0.08f), mTrim);
            Box(p, "EdgeN", new Vector3(SizeX * 0.5f, CeilY - 0.02f, InZ1 - 0.04f), new Vector3(InX1 - InX0, 0.09f, 0.08f), mTrim);
        }

        /// <summary>天井の下を通る空調ダクト。通路の真上を端から端まで。</summary>
        static void BuildDuct(Transform p, Material mDuct, Material mTrim)
        {
            float y = CeilY - 0.30f;
            Box(p, "DuctMain", new Vector3(SizeX * 0.5f, y, SizeZ * 0.5f), new Vector3(0.55f, 0.40f, SizeZ - 0.4f), mDuct);
            // フランジと吊りボルト
            for (float z = 1.2f; z < SizeZ - 0.5f; z += 2.0f)
            {
                Box(p, "DuctFlange", new Vector3(SizeX * 0.5f, y, z), new Vector3(0.60f, 0.45f, 0.05f), mTrim);
                Box(p, "Hanger_L", new Vector3(SizeX * 0.5f - 0.30f, y + 0.35f, z), new Vector3(0.03f, 0.42f, 0.03f), mTrim);
                Box(p, "Hanger_R", new Vector3(SizeX * 0.5f + 0.30f, y + 0.35f, z), new Vector3(0.03f, 0.42f, 0.03f), mTrim);
            }
            // 細い配管を1本添わせる
            Box(p, "Conduit", new Vector3(SizeX * 0.5f + 0.42f, y + 0.10f, SizeZ * 0.5f), new Vector3(0.07f, 0.07f, SizeZ - 0.6f), mTrim);
        }

        /// <summary>直管蛍光灯。通路の左右に2列、長辺に沿って並べる。</summary>
        static void BuildLights(Transform p, Material mHous, Material mLens)
        {
            float[] rows = { SizeX * 0.5f - 1.85f, SizeX * 0.5f + 1.85f };
            int n = 0;
            foreach (var x in rows)
                for (float z = 1.6f; z <= SizeZ - 1.2f; z += 2.4f)
                {
                    var g = new GameObject("Fluoro_" + n);
                    g.transform.SetParent(p, false);
                    g.transform.localPosition = new Vector3(x, CeilY, z);

                    var hous = Box(g.transform, "Housing", new Vector3(0f, -0.05f, 0f), new Vector3(0.28f, 0.10f, 1.25f), mHous);
                    // 器具が天井に自分の影を落とすと汚くなるだけなので切る
                    hous.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var lens = Box(g.transform, "Lens", new Vector3(0f, -0.105f, 0f), new Vector3(0.22f, 0.02f, 1.18f), mLens);
                    lens.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    var lg = new GameObject("Light");
                    lg.transform.SetParent(g.transform, false);
                    lg.transform.localPosition = new Vector3(0f, -0.22f, 0f);
                    var L = lg.AddComponent<Light>();
                    L.type = LightType.Point;
                    L.color = new Color(0.94f, 0.97f, 1.00f);   // 蛍光灯の白
                    L.intensity = 2.6f;
                    L.range = 7.5f;
                    L.shadows = LightShadows.Soft;
                    L.lightmapBakeType = LightmapBakeType.Baked;
                    n++;
                }
        }
    }
}
