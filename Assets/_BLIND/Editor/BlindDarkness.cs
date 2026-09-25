using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 過去の人の視界を暗くし、懐中電灯を持たせる。
    ///
    /// ------------------------------------------------------------------
    /// 暗くして困るのは過去の人だけ
    /// ------------------------------------------------------------------
    ///   サーモ(ThermalHeat/Cold/Surface)とエコロケ(EchoHighlight/ChessGrid)の
    ///   シェーダーはライトも環境光も一切参照しない（確認済み）。
    ///   つまりシーンのライトを落としても、変わるのは過去の人の見え方だけ。
    ///   役ごとに照明を切り替える仕組みは要らない。
    ///
    /// ------------------------------------------------------------------
    /// 「完全な暗闇ではない」の作り方
    /// ------------------------------------------------------------------
    ///   ・環境光を真っ黒(0,0,0)から、ごく暗い青みの灰に上げる。
    ///     これで光の当たらない所も輪郭だけは読める。
    ///     ⚠️ 環境光はシーン全体に効くので、Keep の部屋もわずかに明るくなる（暗くはならない）。
    ///   ・部屋のライトは消さずに残す。強さだけ落とし、影は切る。
    ///     消すと天井の灯具が光っているのに部屋が真っ暗という不整合が出るうえ、
    ///     「弱い灯りの溜まり」が暗闇に濃淡を作ってくれる。
    ///   ・影を切るのは負荷のため。点光源の影は1灯で6面ぶん描く。
    ///     暗くした灯りの影はほとんど見えないのに、負荷だけが残る。
    ///     影は懐中電灯の1本だけに任せる（そのほうが怖い）。
    ///
    /// ------------------------------------------------------------------
    /// 元に戻せること
    /// ------------------------------------------------------------------
    ///   各ライトの元の強さと影を Lighting_Originals.json に記録する。
    ///   キーは GlobalObjectId（シーン内の固有ID）なので、階層を整理して
    ///   親や名前が変わっても同じライトを指し続ける。
    ///   ⚠️ 記録は**初回だけ**。2回目以降は上書きしない。
    ///      上書きすると、暗くした後の値を「元の値」として覚えてしまい戻せなくなる。
    /// </summary>
    public static class BlindDarkness
    {
        public enum Level { Keep, Bright, Dim, Dark }

        /// <summary>
        /// 部屋ごとの暗さ。
        ///   Keep   : 触らない（元の明るさのまま）。未決定の部屋はこれ
        ///   Bright : 元の明るさ（Keep と同じだが「明るい部屋にすると決めた」印）
        ///   Dim    : 薄暗い。灯りの溜まりで歩ける
        ///   Dark   : 暗い。懐中電灯が主役
        ///
        /// 2026-09-25 に決定（表で合意）。
        ///   明るい 3 部屋（1・6・16）を挟んで「暗い→明るい→暗い」の緩急を作る。
        ///   ・room11 だるま : 暗い → 薄暗い（だるまを見せたいので）
        ///   ・落とし穴 4/9/14 : 3 部屋そろえて薄暗い（穴が見えないほど暗いと理不尽な即死になる）
        ///   ・room16 : 仲間が 2026-09-24 に意図して明るくした（強さ 1→10）。Bright で維持
        ///   ・room18 : 暗い。炎の輪と燃える人形の光だけ残して、炎で照らされる部屋にする
        ///   ・room13 : 「一旦触らない」指示なので Keep
        /// </summary>
        static readonly Dictionary<string, Level> RoomLevel = new Dictionary<string, Level>
        {
            { "room1",  Level.Bright }, { "room2",  Level.Dim  }, { "room3",  Level.Dim  },
            { "room4",  Level.Dim    }, { "room5",  Level.Dim  }, { "room6",  Level.Bright },
            { "room7",  Level.Dark   }, { "room8",  Level.Dark }, { "room9",  Level.Dim  },
            { "room10", Level.Dark   }, { "room11", Level.Dim  }, { "room12", Level.Dark },
            { "room13", Level.Keep   }, { "room14", Level.Dim  }, { "room15", Level.Dark },
            { "room16", Level.Bright }, { "room17", Level.Dim  }, { "room18", Level.Dark },
            { "room19", Level.Dim    },
        };

        /// <summary>
        /// 元の強さに掛ける倍率。
        /// ⚠️ 見た目の明るさはこの倍率どおりには落ちない。画面はガンマ補正されるので、
        ///    光を 10% にしても目には約 35% に見える（実測: 平均輝度 60.6 → 36.9）。
        ///    「暗い」と感じさせるには 2〜3% まで落とす必要がある。
        /// </summary>
        const float DimScale  = 0.15f;
        const float DarkScale = 0.03f;

        /// <summary>
        /// 照明器具の発光（天井パネル・電球）に掛ける倍率。
        /// ライトを落としても自発光の素材は白く光ったままなので、
        /// 「器具が煌々と光っているのに部屋が暗い」という嘘になる。
        /// 0 にはしない。器具がかすかに光っているほうが「電気が弱っている」と読めるし、
        /// 完全な暗闇にしないという要件にも効く。
        /// </summary>
        const float DimEmission  = 0.35f;
        const float DarkEmission = 0.12f;

        /// <summary>
        /// 暗くする照明器具の材質（名前の完全一致）。
        /// ⚠️ **材質そのものを書き換えてはいけない。** light.mat は 94 か所、
        ///    Room_Pool_LightPanel は room6（他メンバーの部屋）と共有している。
        ///    書き換えると Keep の部屋まで暗くなる。暗い版を別に作って、
        ///    暗くする部屋のレンダラーだけ差し替える。
        /// CRT の画面・鍵のランプ・赤ランプはギミックや物語上の光なので入れない。
        /// </summary>
        /// room3 の非常灯（T_Emergency_lights）は**わざと入れていない**。
        /// 停電しても非常灯だけは点いている、のほうが自然で、暗い部屋の目印にもなる。
        static readonly HashSet<string> LampMaterials = new HashSet<string>
        {
            "Room_Pool_LightPanel", "Room_Pool_LightPanel_Dim", "light",
            "Room12_LampLens", "Room16_LampGlass",
            // room2 の蛍光灯
            "mounted_fluorescent_lights_glass", "mounted_fluorescent_lights_glass 1",
            "mounted_fluorescent_lights_glass2",
        };
        const string VariantDir = "Assets/_BLIND/Art/Materials/Dark";

        /// <summary>
        /// 暗い部屋の環境光（ガンマ空間の色）。
        /// 真っ黒だと光の届かない所が完全に消える。ここを上げすぎると懐中電灯の意味が無くなる。
        /// 青みを入れるのは、暗所で人の目が青寄りに感じる（プルキンエ現象）のに合わせるため。
        /// </summary>
        static readonly Color AmbientDark = new Color(0.10f, 0.11f, 0.14f, 1f);

        /// <summary>
        /// 暗くしてはいけない灯り（名前の部分一致）。
        ///   Light_KeyShelf : room11 の鍵のヒント（FlickerLight が握っている）
        ///   RingLight/Fire : room18 の炎。物語上の光源なので暗くすると嘘になる
        ///   quiz           : room12 のクイズ板3枚を照らす演出灯（room12items/quiz の中）。
        ///                    暗い部屋に板だけが浮かぶ。
        ///   HoleLight      : room15 の巨大な手の穴から漏れる光（元 1.5・影あり）。
        ///                    暗くすると穴の向こうの「何か」が消える。
        ///   ※ room18 の燃える人形の FireLight は "Fire" で拾われる。
        /// ギミックの Udon が参照しているライトは名前に関係なく自動で除外する。
        ///
        /// ⚠️ 「Spot Light」のような**汎用の名前で指定してはいけない。**
        ///    一度そうしたら、room12 の壁の10灯と room16 の天井3灯まで同名で拾ってしまい、
        ///    2部屋がまるごと暗くならなかった。置き場所（親の名前）で絞ること。
        /// </summary>
        static readonly string[] KeepNames = { "Light_KeyShelf", "RingLight", "Fire", "quiz", "HoleLight" };

        /// <summary>
        /// 暗い部屋に残した明るい灯りのうち、**影を付けて壁で止める**もの（名前の部分一致）。
        ///
        /// ⚠️ 影の無いライトは壁を素通りする。周りを暗くしたことで、
        ///    元の明るさのまま残した灯りが壁の向こうの部屋を照らしているのが目立つようになった（実測）:
        ///      Light_KeyShelf(room11 鍵の緑の点滅灯) → room14 の壁に 2.9（落とし穴の部屋で緑に明滅する）
        ///      quiz(room12 クイズ板の3灯)           → room11 の壁に 0.5
        ///      FireLight(room18 燃える人形)          → room15 の壁に 0.5（人形の部屋に橙の光が浮く）
        ///    Spot の影は1枚で安い。FireLight は点光源で6面ぶんかかるが、
        ///    炎の揺らぎが人形の影として壁に映るので、見た目の得のほうが大きい。
        /// </summary>
        static readonly string[] ForceShadowNames = { "Light_KeyShelf", "quiz", "FireLight" };

        static bool NameHit(Transform t, string[] names)
        {
            for (; t != null; t = t.parent)
                foreach (var k in names)
                    if (t.name.Contains(k)) return true;
            return false;
        }

        const string OriginalsPath = "Assets/_BLIND/Editor/Lighting_Originals.json";

        // ------------------------------------------------------------
        // チカチカする灯り
        // ------------------------------------------------------------
        /// <summary>
        /// 明滅させる灯り。部屋ごとに1つだけ。
        ///
        /// ⚠️ **1部屋に1つまで。** 全部が明滅すると不気味さより故障感・うるささが勝つし、
        ///    部屋全体が明滅すると光過敏性発作の危険が出る。
        ///    「たくさんの普通の灯りの中で1つだけ壊れている」から目が行く。
        /// ⚠️ **room11 には入れない。** 鍵の場所を示す緑の点滅灯が
        ///    「この部屋で唯一明滅する灯り」であることがヒントの前提になっている。
        /// ⚠️ 落とし穴の部屋（4/9/14）にも入れない。暗くなった瞬間に踏み外すと理不尽になる。
        /// </summary>
        class FlickerSpec
        {
            public string room;
            public int style;            // 0 = 切れかけの蛍光灯 / 1 = 切れかけの電球
            public string newAt;         // null → 既存の灯りから選ぶ。名前 → その器具の下に灯りを新しく足す
            public Color color = Color.white;
            public float intensity;      // newAt のときだけ使う
            public float range;
        }

        static readonly FlickerSpec[] Flickers =
        {
            // 地下通路の蛍光灯。切れかけて鳴っている
            new FlickerSpec { room = "room2",  style = 0 },
            // ロッカー室の天井パネル
            new FlickerSpec { room = "room8",  style = 0 },
            // 人形の部屋の吊り下げ灯。消えている数秒の間に人形が動いたかもしれない、と思わせる
            new FlickerSpec { room = "room15", style = 1 },
            // シャワー室には灯りが1つも無い（器具はあるが Light が付いていない）。
            // 古びた天井灯 WornCeilingLight の1つだけを生かす
            new FlickerSpec { room = "room7",  style = 1, newAt = "WornCeilingLight (2)",
                              color = new Color(1.0f, 0.86f, 0.66f), intensity = 0.6f, range = 7f },
            // 電気室も Light が1つも無い。環境音の「電気バチバチ」と合わせて蛍光灯を1本だけ生かす。
            // ⚠️ 北の列（z=-49.7）の器具を使ってはいけない。北の壁の向こうが room9（落とし穴）で、
            //    一度 (8) に付けたら光が壁を抜けて room9 の壁が明滅した。南の列から選ぶ。
            new FlickerSpec { room = "room10", style = 0, newAt = "Fluorescent Lamp (3)",
                              color = new Color(0.86f, 0.95f, 1.0f), intensity = 0.6f, range = 7f },
        };
        const string FlickerRoot = "Flicker_Generated";

        /// <summary>
        /// 明滅させる灯りの強さ（元の値に対する倍率）。発光する器具もこの倍率。
        ///
        /// ⚠️ 暗くした後の値（3%）を基準にしてはいけない。明滅がほぼ見えなくなる。
        ///    暗い部屋で明滅している灯りは「まだ生き残っている唯一の灯り」なので、
        ///    周り（3〜15%）よりはっきり明るくないと、壊れかけているように見えない。
        /// </summary>
        const float FlickerLive = 0.6f;

        /// <summary>
        /// これより元の強さが弱い灯りは明滅に選ばない。
        /// room15 の吊り下げ灯には**わざと消してある灯り**（元の強さ 0）が混ざっている。
        /// 一度それを選んで、明滅が何も見えなかった。作者が消した灯りは消えたままにする。
        /// </summary>
        const float FlickerMinOriginal = 0.05f;

        // ============================================================
        // メニュー
        // ============================================================
        [MenuItem("BLIND/照明/1. 過去人の暗闇を適用")]
        public static void Menu_Apply() { Debug.Log(Apply()); }

        [MenuItem("BLIND/照明/2. 照明をすべて元に戻す")]
        public static void Menu_Restore() { Debug.Log(Restore()); }

        [MenuItem("BLIND/照明/3. 懐中電灯を設置")]
        public static void Menu_Flashlight() { Debug.Log(BuildFlashlight()); }

        // ============================================================
        // 記録
        // ============================================================
        [System.Serializable] class Orig { public string id; public string path; public float intensity; public int shadows; }
        /// <summary>照明器具の材質を差し替えたレンダラーの、元の材質（スロット単位）。</summary>
        [System.Serializable] class MatOrig { public string id; public int slot; public string path; public string mat; }
        [System.Serializable] class Store
        {
            public float[] ambient; public int ambientMode;
            public List<Orig> lights = new List<Orig>();
            public List<MatOrig> mats = new List<MatOrig>();
        }

        static Store Load()
        {
            if (!File.Exists(OriginalsPath)) return null;
            var s = JsonUtility.FromJson<Store>(File.ReadAllText(OriginalsPath));
            // 材質の記録を足す前に作った JSON には mats が無い
            if (s.mats == null) s.mats = new List<MatOrig>();
            if (s.lights == null) s.lights = new List<Orig>();
            return s;
        }

        static void Save(Store s)
        {
            File.WriteAllText(OriginalsPath, JsonUtility.ToJson(s, true));
            AssetDatabase.ImportAsset(OriginalsPath);
        }

        static string Id(Object o) { return GlobalObjectId.GetGlobalObjectIdSlow(o).ToString(); }

        static string PathOf(Transform t)
        {
            var p = t.name;
            for (var q = t.parent; q != null; q = q.parent) p = q.name + "/" + p;
            return p;
        }

        /// <summary>部屋の中のライトだけ（部屋外の無効な Directional などは触らない）。</summary>
        static IEnumerable<KeyValuePair<string, Light>> RoomLights()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            if (rooms == null) yield break;
            foreach (Transform r in rooms.transform)
                foreach (var l in r.GetComponentsInChildren<Light>(true))
                    if (l.type != LightType.Directional && !UnderFlicker(l.transform))
                        yield return new KeyValuePair<string, Light>(r.name, l);
        }

        /// <summary>明滅用に新しく足した灯り。暗さの倍率を掛けない（最初から暗い値で作ってある）。</summary>
        static bool UnderFlicker(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == FlickerRoot) return true;
            return false;
        }

        static HashSet<Light> GimmickLights()
        {
            var set = new HashSet<Light>();
            foreach (var usb in Resources.FindObjectsOfTypeAll<UdonSharp.UdonSharpBehaviour>())
            {
                if (!usb.gameObject.scene.IsValid()) continue;
                var it = new SerializedObject(usb).GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var o = it.objectReferenceValue;
                    var l = o as Light;
                    if (l == null && o is GameObject g) l = g.GetComponentInChildren<Light>(true);
                    if (l != null) set.Add(l);
                }
            }
            return set;
        }

        static bool IsKept(Light l, HashSet<Light> gimmick)
        {
            if (gimmick.Contains(l)) return true;
            for (var t = l.transform; t != null; t = t.parent)
                foreach (var k in KeepNames)
                    if (t.name.Contains(k)) return true;
            return false;
        }

        // ============================================================
        // 適用
        // ============================================================
        static string Apply()
        {
            var store = Load() ?? new Store
            {
                ambient = new[] { RenderSettings.ambientLight.r, RenderSettings.ambientLight.g,
                                  RenderSettings.ambientLight.b, RenderSettings.ambientLight.a },
                ambientMode = (int)RenderSettings.ambientMode,
            };
            var byId = new Dictionary<string, Orig>();
            foreach (var o in store.lights) byId[o.id] = o;

            // ⚠️ 明滅の仕掛けは**先に全部消してから**作り直す。
            //    残したままだと FlickerLight が握っている灯りが「ギミックの灯り」と判定され、
            //    暗くされずに元の明るさへ戻ってしまう。
            RemoveFlickers();
            var gimmick = GimmickLights();
            var count = new SortedDictionary<string, int[]>();   // 部屋 → [暗くした, 残した, 影を切った]
            var kept = new List<string>();
            int recorded = 0;

            foreach (var kv in RoomLights())
            {
                var room = kv.Key; var l = kv.Value;
                var id = Id(l);
                if (!byId.TryGetValue(id, out var orig))
                {
                    // ⚠️ 初めて見るライトだけ記録する。既存の記録は絶対に上書きしない。
                    orig = new Orig { id = id, path = PathOf(l.transform), intensity = l.intensity, shadows = (int)l.shadows };
                    store.lights.Add(orig); byId[id] = orig; recorded++;
                }

                if (!count.ContainsKey(room)) count[room] = new int[3];
                RoomLevel.TryGetValue(room, out var lv);

                Undo.RecordObject(l, "darkness");
                var origShadows = (LightShadows)orig.shadows;
                bool darkRoom = lv == Level.Dim || lv == Level.Dark;
                if (!darkRoom || IsKept(l, gimmick))
                {
                    l.intensity = orig.intensity;
                    l.shadows = origShadows;
                    // 暗い部屋に残した明るい灯りが壁を抜けて隣の部屋に漏れるのを止める
                    if (darkRoom && origShadows == LightShadows.None && NameHit(l.transform, ForceShadowNames))
                        l.shadows = LightShadows.Hard;
                    count[room][1]++;
                    if (darkRoom) kept.Add(PathOf(l.transform));
                }
                else
                {
                    l.intensity = orig.intensity * (lv == Level.Dim ? DimScale : DarkScale);
                    // 影を切るのは点光源と、Dark の部屋だけ。
                    // ⚠️ Dim の部屋の Spot の影は残す。影は1枚で安いうえ、切ったら壁を抜けて
                    //    隣の部屋に漏れ出した（room3 のスポット → room2・room19 の壁に 0.2〜0.3）。
                    bool keepShadow = lv == Level.Dim && l.type == LightType.Spot;
                    var want = keepShadow ? origShadows : LightShadows.None;
                    if (l.shadows != want && want == LightShadows.None) count[room][2]++;
                    l.shadows = want;
                    count[room][0]++;
                }
                EditorUtility.SetDirty(l);
            }

            int swapped = SwapLampMaterials(store, out var lampByRoom);
            // 明滅は最後。灯りの選び方と強さは、記録してある元の値を基準にする
            string flick = BuildFlickers(store);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientDark;
            Save(store);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            var sb = new System.Text.StringBuilder("== 過去人の暗闇を適用 ==\n");
            sb.AppendLine("環境光: 黒 → " + AmbientDark + "（ガンマ）");
            sb.AppendLine("照明器具の発光を暗い版へ: " + swapped + " スロット差し替え  " + lampByRoom);
            sb.AppendLine("記録: 新規 " + recorded + " 灯 / 記録済み合計 " + store.lights.Count + " 灯 → " + OriginalsPath);
            foreach (var c in count)
            {
                RoomLevel.TryGetValue(c.Key, out var lv);
                sb.AppendLine("  " + c.Key.PadRight(7) + " " + lv.ToString().PadRight(6)
                            + " 暗くした " + c.Value[0] + " / 元のまま " + c.Value[1] + " / 影を切った " + c.Value[2]);
            }
            if (kept.Count > 0) sb.AppendLine("暗い部屋の中で残した演出灯: " + string.Join(", ", kept));
            sb.Append(flick);
            return sb.ToString();
        }

        // ============================================================
        // 明滅
        // ============================================================
        static void RemoveFlickers()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            foreach (Transform r in rooms.transform)
            {
                var f = r.Find(FlickerRoot);
                if (f != null) Undo.DestroyObjectImmediate(f.gameObject);
            }
        }

        static Bounds RoomFloor(Transform room)
        {
            var b = new Bounds(room.position, Vector3.zero); bool any = false;
            var g = room.Find("GeneratedRoom");
            foreach (var mr in (g != null ? g : room).GetComponentsInChildren<MeshRenderer>())
            {
                if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds);
            }
            return b;
        }

        /// <summary>灯りの近く(1.2m以内)にある照明器具の発光部。明滅をそこにも掛けるため。</summary>
        static MeshRenderer FixtureNear(Transform room, Vector3 p)
        {
            MeshRenderer best = null; float bd = 1.2f;
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.gameObject.layer != 0) continue;
                bool lamp = false;
                foreach (var m in mr.sharedMaterials)
                    if (m != null && (LampMaterials.Contains(m.name) || IsVariant(m))) { lamp = true; break; }
                if (!lamp) continue;
                float d = Vector3.Distance(mr.bounds.ClosestPoint(p), p);
                if (d < bd) { bd = d; best = mr; }
            }
            return best;
        }

        static string BuildFlickers(Store store)
        {
            var origLight = new Dictionary<string, float>();
            foreach (var o in store.lights) origLight[o.id] = o.intensity;
            var origMat = new Dictionary<string, string>();
            foreach (var o in store.mats) origMat[o.id + "#" + o.slot] = o.mat;

            var rooms = GameObject.Find("=== ROOMS ===").transform;
            var sb = new System.Text.StringBuilder("チカチカする灯り:\n");
            for (int i = 0; i < Flickers.Length; i++)
            {
                var s = Flickers[i];
                var room = rooms.Find(s.room);
                if (room == null) { sb.AppendLine("  " + s.room + ": 部屋が無い"); continue; }

                Light target = null; MeshRenderer fixture = null; float baseI;
                var root = new GameObject(FlickerRoot);
                Undo.RegisterCreatedObjectUndo(root, "flicker");
                root.transform.SetParent(room, false);

                if (s.newAt != null)
                {
                    // 器具はあるが Light が無い部屋。器具の真下に灯りを足す
                    Transform at = null;
                    foreach (var t in room.GetComponentsInChildren<Transform>(true))
                        if (t.name == s.newAt) { at = t; break; }
                    if (at == null) { sb.AppendLine("  " + s.room + ": 器具 " + s.newAt + " が無い"); Undo.DestroyObjectImmediate(root); continue; }
                    var r0 = at.GetComponentInChildren<Renderer>();
                    var p = r0 != null ? new Vector3(r0.bounds.center.x, r0.bounds.min.y - 0.15f, r0.bounds.center.z)
                                       : at.position + Vector3.down * 0.3f;
                    var lg = new GameObject("Light_" + s.newAt);
                    Undo.RegisterCreatedObjectUndo(lg, "flicker light");
                    lg.transform.SetParent(root.transform, false);
                    lg.transform.position = p;
                    target = lg.AddComponent<Light>();
                    target.type = LightType.Point;
                    target.color = s.color;
                    target.intensity = s.intensity;
                    target.range = s.range;
                    target.shadows = LightShadows.None;     // 影は懐中電灯の1本だけ
                    target.lightmapBakeType = LightmapBakeType.Realtime;
                    var sample = FindRoomLight(room);
                    if (sample != null) target.cullingMask = sample.cullingMask;
                    else target.cullingMask = (1 << 0) | (1 << 24) | (1 << 9);
                    baseI = s.intensity;
                }
                else
                {
                    // 既存の灯りから、器具が付いていて部屋の中央に近いものを選ぶ（映像で目に入りやすい）
                    var center = RoomFloor(room).center;
                    float bd = float.MaxValue; bool bestHasFixture = false; float bestOrig = 0f;
                    foreach (var l in room.GetComponentsInChildren<Light>(true))
                    {
                        if (l.type == LightType.Directional || UnderFlicker(l.transform)) continue;
                        if (IsKept(l, new HashSet<Light>())) continue;          // 演出灯は明滅させない
                        if (!origLight.TryGetValue(Id(l), out var oi)) oi = l.intensity;
                        if (oi < FlickerMinOriginal) continue;                  // 作者が消した灯りは選ばない
                        var fx = FixtureNear(room, l.transform.position);
                        bool has = fx != null;
                        float d = Vector3.Distance(l.transform.position, center);
                        // 器具付きを優先。同じ条件なら中央に近いほう（映像で目に入りやすい）
                        if ((has && !bestHasFixture) || (has == bestHasFixture && d < bd))
                        { bd = d; target = l; fixture = fx; bestHasFixture = has; bestOrig = oi; }
                    }
                    if (target == null) { sb.AppendLine("  " + s.room + ": 明滅させる灯りが無い"); Undo.DestroyObjectImmediate(root); continue; }
                    baseI = bestOrig * FlickerLive;
                    // エディタ上（FlickerLight が動いていない状態）でも実行時と同じ明るさに見えるように
                    Undo.RecordObject(target, "flicker base");
                    target.intensity = baseI;
                    EditorUtility.SetDirty(target);
                }

                var holder = new GameObject("Flicker_" + target.name);
                Undo.RegisterCreatedObjectUndo(holder, "flicker holder");
                holder.transform.SetParent(root.transform, false);
                var fl = AddUdon(holder, "FlickerLight");
                if (fl == null) { sb.AppendLine("  " + s.room + ": FlickerLight を付けられない"); continue; }

                // 器具の発光も「元の材質の発光 × FlickerLive」。暗い版の発光(12%)を基準にすると明滅が見えない
                Color ec = Color.black;
                if (fixture != null)
                {
                    var mats = fixture.sharedMaterials;
                    string fid = null;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        var m = mats[k];
                        if (m == null) continue;
                        if (IsVariant(m))
                        {
                            if (fid == null) fid = Id(fixture);
                            if (origMat.TryGetValue(fid + "#" + k, out var op))
                            {
                                var om = AssetDatabase.LoadAssetAtPath<Material>(op);
                                if (om != null) m = om;
                            }
                        }
                        if (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > ec.maxColorComponent)
                            ec = m.GetColor("_EmissionColor");
                    }
                    ec *= FlickerLive;
                }

                var so = new SerializedObject(fl);
                so.FindProperty("target").objectReferenceValue = target;
                var em = so.FindProperty("emissive");
                em.arraySize = fixture != null ? 1 : 0;
                if (fixture != null) em.GetArrayElementAtIndex(0).objectReferenceValue = fixture;
                so.FindProperty("emissionColor").colorValue = ec;
                so.FindProperty("baseIntensity").floatValue = baseI;
                so.FindProperty("floorLevel").floatValue = s.style == 1 ? 0.0f : 0.12f;
                so.FindProperty("style").intValue = s.style;
                // 部屋ごとにずらす。同じ種類が同時に明滅すると制御された機械に見える
                so.FindProperty("phaseOffset").floatValue = 1.37f + i * 3.11f;
                so.ApplyModifiedProperties();
                SetSyncNone(fl);
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(fl as UdonSharp.UdonSharpBehaviour);
                EditorUtility.SetDirty(fl);

                sb.AppendLine("  " + s.room.PadRight(7) + (s.style == 1 ? "電球 " : "蛍光灯 ")
                            + (s.newAt != null ? "新設 " : "既存 ") + PathOf(target.transform)
                            + " 強さ " + baseI.ToString("F2")
                            + (fixture != null ? " / 器具 " + fixture.name + " も明滅" : " / 器具の発光なし"));
            }
            return sb.ToString();
        }

        static Light FindRoomLight(Transform room)
        {
            foreach (var l in room.GetComponentsInChildren<Light>(true))
                if (l.type == LightType.Point && !UnderFlicker(l.transform)) return l;
            return null;
        }

        static void SetSyncNone(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour;
            if (usb == null) return;
            var ub = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb);
            if (ub == null) return;
            var so = new SerializedObject(ub);
            var p = so.FindProperty("_syncMethod");
            if (p == null) return;
            int idx = System.Array.IndexOf(p.enumNames, "None");
            if (idx < 0) return;
            p.enumValueIndex = idx;
            so.ApplyModifiedProperties();
        }

        // ============================================================
        // 照明器具の発光
        // ============================================================
        static readonly Dictionary<string, Material> variantCache = new Dictionary<string, Material>();

        static bool IsVariant(Material m)
        {
            return m != null && AssetDatabase.GetAssetPath(m).StartsWith(VariantDir);
        }

        /// <summary>元の材質の「暗い版」。発光だけ弱めた複製を作る（無ければ作る、あれば値を合わせ直す）。</summary>
        static Material Variant(Material src, Level lv)
        {
            var key = AssetDatabase.GetAssetPath(src) + "|" + lv;
            if (variantCache.TryGetValue(key, out var hit) && hit != null) return hit;

            if (!AssetDatabase.IsValidFolder(VariantDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Dark");
            var path = VariantDir + "/" + src.name + "_" + lv + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.shader = src.shader;
            m.CopyPropertiesFromMaterial(src);
            m.shaderKeywords = src.shaderKeywords;
            m.globalIlluminationFlags = src.globalIlluminationFlags;
            if (src.HasProperty("_EmissionColor"))
                m.SetColor("_EmissionColor", src.GetColor("_EmissionColor") * (lv == Level.Dim ? DimEmission : DarkEmission));
            EditorUtility.SetDirty(m);
            variantCache[key] = m;
            return m;
        }

        /// <summary>
        /// 暗くする部屋の照明器具を暗い版に、それ以外の部屋は元の材質に揃える。
        /// 元の材質はスロット単位で記録する（1つのレンダラーに複数の材質が付くため）。
        /// </summary>
        static int SwapLampMaterials(Store store, out string summary)
        {
            variantCache.Clear();
            var byKey = new Dictionary<string, MatOrig>();
            foreach (var o in store.mats) byKey[o.id + "#" + o.slot] = o;

            var perRoom = new SortedDictionary<string, int>();
            int n = 0;
            var rooms = GameObject.Find("=== ROOMS ===");
            foreach (Transform r in rooms.transform)
            {
                RoomLevel.TryGetValue(r.name, out var lv);
                bool darken = lv == Level.Dim || lv == Level.Dark;

                foreach (var mr in r.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.gameObject.layer != 0) continue;   // サーモ/エコロケの複製は触らない
                    var mats = mr.sharedMaterials;
                    bool changed = false;
                    string id = null;

                    for (int i = 0; i < mats.Length; i++)
                    {
                        var cur = mats[i];
                        if (cur == null) continue;
                        bool lamp = LampMaterials.Contains(cur.name);
                        bool variant = IsVariant(cur);
                        if (!lamp && !variant) continue;       // GlobalObjectId は重いので、関係ない物では取らない

                        if (id == null) id = Id(mr);
                        var key = id + "#" + i;
                        Material orig;
                        if (byKey.TryGetValue(key, out var mo)) orig = AssetDatabase.LoadAssetAtPath<Material>(mo.mat);
                        else if (lamp)
                        {
                            orig = cur;
                            mo = new MatOrig { id = id, slot = i, path = PathOf(mr.transform), mat = AssetDatabase.GetAssetPath(cur) };
                            store.mats.Add(mo); byKey[key] = mo;
                        }
                        else continue;   // 記録の無い暗い版（手で貼った等）は触らない
                        if (orig == null) continue;

                        var want = darken ? Variant(orig, lv) : orig;
                        if (cur != want) { mats[i] = want; changed = true; n++; }
                    }

                    if (changed)
                    {
                        Undo.RecordObject(mr, "lamp material");
                        mr.sharedMaterials = mats;
                        EditorUtility.SetDirty(mr);
                        perRoom.TryGetValue(r.name, out var c);
                        perRoom[r.name] = c + 1;
                    }
                }
            }
            var parts = new List<string>();
            foreach (var kv in perRoom) parts.Add(kv.Key + "=" + kv.Value);
            summary = parts.Count > 0 ? "（" + string.Join(" ", parts) + "）" : "";
            return n;
        }

        static int RestoreLampMaterials(Store store)
        {
            var byKey = new Dictionary<string, MatOrig>();
            foreach (var o in store.mats) byKey[o.id + "#" + o.slot] = o;
            int n = 0;
            foreach (var mr in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (!mr.gameObject.scene.IsValid()) continue;
                var mats = mr.sharedMaterials;
                bool changed = false; string id = null;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!IsVariant(mats[i])) continue;
                    if (id == null) id = Id(mr);
                    if (!byKey.TryGetValue(id + "#" + i, out var mo)) continue;
                    var orig = AssetDatabase.LoadAssetAtPath<Material>(mo.mat);
                    if (orig == null) continue;
                    mats[i] = orig; changed = true; n++;
                }
                if (changed) { Undo.RecordObject(mr, "restore lamp"); mr.sharedMaterials = mats; EditorUtility.SetDirty(mr); }
            }
            return n;
        }

        // ============================================================
        // 元に戻す
        // ============================================================
        static string Restore()
        {
            var store = Load();
            if (store == null) return "記録が無い（まだ一度も暗くしていない）: " + OriginalsPath;
            var byId = new Dictionary<string, Orig>();
            foreach (var o in store.lights) byId[o.id] = o;

            int n = 0, missing = 0;
            foreach (var kv in RoomLights())
            {
                if (!byId.TryGetValue(Id(kv.Value), out var o)) { missing++; continue; }
                Undo.RecordObject(kv.Value, "restore light");
                kv.Value.intensity = o.intensity;
                kv.Value.shadows = (LightShadows)o.shadows;
                EditorUtility.SetDirty(kv.Value);
                n++;
            }
            // 明滅の仕掛けと、そのために足した灯りも消す（元の状態には無かった物）
            RemoveFlickers();
            int lamps = RestoreLampMaterials(store);
            RenderSettings.ambientMode = (UnityEngine.Rendering.AmbientMode)store.ambientMode;
            RenderSettings.ambientLight = new Color(store.ambient[0], store.ambient[1], store.ambient[2], store.ambient[3]);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "照明を元に戻した: " + n + " 灯 / 照明器具の材質 " + lamps + " スロット / 環境光も復元"
                 + (missing > 0 ? "（記録に無い新しいライト " + missing + " 灯は触っていない）" : "");
        }

        // ============================================================
        // 懐中電灯
        // ============================================================
        const string RigName = "MemoryFlashlight";
        const string ModelPrefab = "Assets/Flashlight/Model/Flashlight.prefab";
        const string CookiePath  = "Assets/Flashlight/Textures/Flashlight_Cookie.png";

        /// <summary>
        /// 光の届く距離(m)。「遠くまで届く」の要件。
        ///
        /// ⚠️ **range を「届かせたい距離」にしてはいけない。**
        ///    Built-in の Spot の減衰は 1/(1+25·(d/range)²) で、range の 7 割を過ぎると
        ///    ほぼ消える。range=35 にしたら、room11 の奥のだるま(27m)に当たる光が 6% しかなく、
        ///    撮影して確かめたら光の円がまったく見えなかった。
        ///    届かせたい距離の 2 倍以上を取る。70 なら 27m 先で約 21% 残る。
        ///    代わりに手元(2m)はほぼ 100% になり、近くの壁は白く飛ぶ。本物の懐中電灯もそうなる。
        /// </summary>
        const float BeamRange = 70f;
        const float BeamAngle = 50f;
        const float BeamIntensity = 4.5f;

        static string BuildFlashlight()
        {
            var gm = GameObject.Find("=== SYSTEM ===/GameManagement");
            if (gm == null) return "懐中電灯: GameManagement が無い";
            var pvc = gm.GetComponent("PlayerVisionController") as Component;
            if (pvc == null) return "懐中電灯: PlayerVisionController が無い";

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefab);
            if (src == null) return "懐中電灯: " + ModelPrefab + " が無い（アセットを取り込んだか確認）";
            var mf0 = src.GetComponent<MeshFilter>();
            var mr0 = src.GetComponent<MeshRenderer>();

            var old = gm.transform.Find(RigName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(root, "flashlight");
            root.transform.SetParent(gm.transform, false);

            // Body: 追従させる親。+Z が照らす向き。
            var body = new GameObject("Body");
            Undo.RegisterCreatedObjectUndo(body, "flashlight body");
            body.transform.SetParent(root.transform, false);

            // --- モデル ---
            // ⚠️ 元のモデルはレンズが +Y を向いている（金色版のライトの付き方から判明）。
            //    +X に 90° 回して +Z（照らす向き）に揃える。
            // ⚠️ プレハブの Animator はコントローラーが空で何もしないので持ち込まない。
            //    MeshFilter/MeshRenderer だけ写す。
            var model = new GameObject("Model");
            Undo.RegisterCreatedObjectUndo(model, "flashlight model");
            model.transform.SetParent(body.transform, false);
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            model.AddComponent<MeshFilter>().sharedMesh = mf0.sharedMesh;
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mr0.sharedMaterials;
            // ⚠️ 影を落とさせない。自分の光を自分で遮ってしまう。
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            model.layer = 0;

            // --- 光 ---
            var beamGo = new GameObject("Beam");
            Undo.RegisterCreatedObjectUndo(beamGo, "flashlight beam");
            beamGo.transform.SetParent(body.transform, false);
            beamGo.transform.localPosition = new Vector3(0f, 0f, 0.12f);   // レンズ(0.093)の少し先
            var li = beamGo.AddComponent<Light>();
            li.type = LightType.Spot;
            li.range = BeamRange;
            li.spotAngle = BeamAngle;
            li.intensity = BeamIntensity;
            li.color = new Color(1.0f, 0.95f, 0.86f);    // 白熱球寄りの白。部屋灯の青白(0.94,0.97,1.00)と区別がつく
            li.cookie = AssetDatabase.LoadAssetAtPath<Texture>(CookiePath);   // 反射板のムラ。均一な円だと作り物に見える
            // ⚠️ 必ず画素単位で描かせる。VRChat は画素単位のライト数を絞るので、
            //    Auto のままだと他の灯りに負けて頂点ライト落ちし、光の輪郭がぼやける。
            li.renderMode = LightRenderMode.ForcePixel;
            // 影はこの1本だけに持たせる（暗くした部屋灯の影は全部切ってある）
            li.shadows = LightShadows.Soft;
            li.shadowStrength = 0.85f;
            li.shadowNearPlane = 0.3f;                    // 手・袖で自分を影にしない
            li.lightmapBakeType = LightmapBakeType.Realtime;

            // 過去の人が見ているものだけ照らす。自分のアバター(PlayerLocal=10)と鏡(18)は外す。
            int mask = 0;
            var pso = new SerializedObject(pvc).FindProperty("memoryCullingMask");
            if (pso != null) mask = pso.intValue;
            if (mask == 0) mask = (1 << 0) | (1 << 24) | (1 << 9);
            mask &= ~(1 << 10);
            mask &= ~(1 << 18);
            li.cullingMask = mask;

            body.SetActive(false);   // エディタ上で原点(room1)を照らさないように。実行時はスクリプトが点ける

            // --- 追従と役判定 ---
            var fl = AddUdon(root, "MemoryFlashlight");
            if (fl == null) return "懐中電灯: MemoryFlashlight の U# プログラムが無い";
            var so = new SerializedObject(fl);
            so.FindProperty("visionController").objectReferenceValue = pvc;
            so.FindProperty("body").objectReferenceValue = body;
            so.ApplyModifiedProperties();
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(fl as UdonSharp.UdonSharpBehaviour);
            EditorUtility.SetDirty(fl);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "懐中電灯: " + PathOf(root.transform) + " に設置"
                 + "（Spot / range " + BeamRange + "m / " + BeamAngle + "° / 影あり / cookie "
                 + (li.cookie != null ? li.cookie.name : "なし") + " / cullingMask " + mask + "）";
        }

        static Component AddUdon(GameObject go, string typeName)
        {
            var t = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (t == null) return null;
            var undoType = System.Type.GetType("UdonSharpEditor.UdonSharpUndo, UdonSharp.Editor");
            Component c = null;
            if (undoType != null)
            {
                var mi = undoType.GetMethod("AddComponent", new[] { typeof(GameObject), typeof(System.Type) });
                if (mi != null) c = mi.Invoke(null, new object[] { go, t }) as Component;
            }
            return c ?? go.AddComponent(t);
        }
    }
}
