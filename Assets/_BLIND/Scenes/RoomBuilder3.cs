using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 汎用の部屋生成スクリプト(RoomBuilder3)
/// 軸の定義: X = 縦(奥行き) / Z = 横(幅) / Y = 高さ
///
/// デフォルトでは「正方形・入り口なし・穴なし」の部屋になります。
/// そこから Inspector 上の各項目、またはスクリプトAPI経由で
/// 入り口の追加/削除/幅変更/位置ずらし、部屋サイズ変更、
/// 床タイルの密度変更(落とし穴)などをカスタマイズできます。
/// </summary>
public class RoomBuilder3 : MonoBehaviour
{
    // 壁の方角。North = +X側の壁, South = -X側, East = +Z側, West = -Z側
    // (コンパスの方角そのものではなく、説明のための便宜的な呼び名です)
    public enum WallSide { North, South, East, West }
    public enum SurfaceType { NorthSouth, EastWest, Floor }

    [System.Serializable]
    public class WallConfig
    {
        [Tooltip("この壁に入り口を作るか")]
        public bool hasDoor = false;

        [Tooltip("入り口の横幅(m)")]
        public float doorWidth = 1.2f;

        [Tooltip("壁の中心から入り口をどれだけ左右(または前後)にずらすか(m)。0で中央")]
        public float doorOffset = 0f;

        [Tooltip("入り口の高さ(m)。壁の高さ(Wall Height Y)より低い値にすると、入り口の上に壁(まぐさ)が残ったドア型の開口になります。壁の高さ以上にすると天井まで抜けた開口になります")]
        public float doorHeight = 2.0f;

    }

    [System.Serializable]
    public class HoleRect
    {
        [Tooltip("穴の範囲(最小のX,Z座標)")]
        public Vector2 min;
        [Tooltip("穴の範囲(最大のX,Z座標)")]
        public Vector2 max;
    }

    [Header("部屋全体のサイズ")]
    [Tooltip("縦(X方向)の長さ")]
    public float roomDepthX = 6f;
    [Tooltip("横(Z方向)の長さ")]
    public float roomWidthZ = 6f;
    [Tooltip("壁の高さ(Y方向)")]
    public float wallHeightY = 2.5f;
    [Tooltip("壁の厚み")]
    public float wallThickness = 0.2f;

    [Header("壁ごとの入り口設定")]
    [Tooltip("+X側の壁")]
    public WallConfig northWall = new WallConfig();
    [Tooltip("-X側の壁")]
    public WallConfig southWall = new WallConfig();
    [Tooltip("+Z側の壁")]
    public WallConfig eastWall = new WallConfig();
    [Tooltip("-Z側の壁")]
    public WallConfig westWall = new WallConfig();

    [Header("床タイル設定")]
    [Tooltip("オフにすると床を一切生成しません。" +
             "プールのように床へ穴を開けた自作メッシュを敷いている部屋で使います。" +
             "オンのままだと部屋を組み直すたびに床が再生成され、自作の床に重なって穴を塞いでしまいます。" +
             "オフにする場合、床の当たり判定も生成されないので自作メッシュ側にColliderを付けてください。")]
    public bool generateFloor = true;

    [Tooltip("オンにすると床をタイルに分割せず、部屋全体を覆う1枚の板として生成します。" +
             "マテリアル側(BLIND/RoomSurface)がワールド座標でタイル模様を描くため見た目は変わらず、" +
             "オブジェクト数が激減して軽くなります。" +
             "落とし穴(下のTile Fill RatioやManual Holes)を使う部屋ではオフにしてください。")]
    public bool singleSlabFloor = false;

    [Tooltip("タイル1枚の縦(X方向)の長さ")]
    public float tileDepthX = 0.5f;
    [Tooltip("タイル1枚の横(Z方向)の長さ")]
    public float tileWidthZ = 0.5f;
    [Tooltip("タイルの厚み")]
    public float tileThickness = 0.1f;
    [Range(0f, 1f)]
    [Tooltip("床全体に対してタイルを生成する割合。1=隙間なし、0=床が全く無い状態。1未満にするとランダムに間引かれて落とし穴になります")]
    public float tileFillRatio = 1f;
    [Tooltip("ランダム間引きのシード値。同じ値であれば毎回同じ配置になります")]
    public int randomSeed = 0;

    [Header("手動で指定する穴(任意・特定の形状の落とし穴用)")]
    public List<HoleRect> manualHoles = new List<HoleRect>();

    [Header("天井")]
    [Tooltip("天井を生成するかどうか")]
    public bool hasCeiling = false;
    [Tooltip("天井の厚み。床タイルのような分割はせず、部屋全体を覆う1枚板として生成します")]
    public float ceilingThickness = 0.1f;

    [Header("マテリアル(任意)")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;

    [Header("ドアフレーム設定")]
    [Tooltip("ドアフレームの太さ")]
    public float doorFrameThickness = 0.05f;
    [Tooltip("ドアフレームのマテリアル(未指定時は壁マテリアルを使用)")]
    public Material doorFrameMaterial;

    [Header("レンダリング設定")]
    [Tooltip("Unlitシェーダーを使用（ライティングの影響を受けず、継ぎ目のアーティファクトを防止）")]
    public bool useUnlitShader = false;

    private Transform root;

    // マテリアルキャッシュ（同一ソースマテリアルからは1つだけインスタンスを作る）
    private Dictionary<Material, Material> materialCache = new Dictionary<Material, Material>();

    void Start()
    {
        // シーンに生成済みのジオメトリが保存されていれば、実行時に作り直さない。
        //
        // 毎回作り直していると、部屋に後から加えた変更(マテリアルの差し替え、
        // ベイクした結果、手で置いた小物との位置関係)が再生のたびに失われ、
        // 「再生すると見た目が変わる」状態になる。
        // VRChatのワールドでも、実行時生成のオブジェクトはライトマップが焼けず、
        // 入室のたびに全部屋を組み直すことになる。
        if (transform.Find("GeneratedRoom") == null)
        {
            BuildRoom();
        }
    }

    void OnValidate()
    {
        // OnValidate内でDestroyImmediate+オブジェクト生成するとUnityの内部状態が壊れるため、
        // 次のフレームに遅延して再ビルドする
#if UNITY_EDITOR
        if (root != null && !Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) BuildRoom();
            };
        }
#endif
    }

    // ==================== 部屋の再構築 ====================

    [ContextMenu("Build Room")]
    public void BuildRoom()
    {
        CleanUp();

        GameObject rootObj = new GameObject("GeneratedRoom");
        rootObj.transform.SetParent(transform, false);
        root = rootObj.transform;

        // マテリアルキャッシュをクリア（再ビルド時に新しいインスタンスを作り直す）
        materialCache.Clear();

        BuildFloor();
        BuildWalls();

        if (hasCeiling)
        {
            BuildCeiling();
        }
    }

    // 既存の生成物とリークしたリソースを破棄
    void CleanUp()
    {
        Transform old = transform.Find("GeneratedRoom");
        if (old != null)
        {
            // 子オブジェクトのメッシュを破棄（.meshアクセスで生成されたコピー）
            foreach (var mf in old.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null && !mf.sharedMesh.name.StartsWith("Cube"))
                {
                    DestroyImmediate(mf.sharedMesh);
                }
            }
            // 子オブジェクトのマテリアルインスタンスを破棄
            foreach (var r in old.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.name.EndsWith("(Instance)"))
                    {
                        DestroyImmediate(m);
                    }
                }
            }
            DestroyImmediate(old.gameObject);
        }

        // キャッシュ内のマテリアルも念のため破棄
        foreach (var kvp in materialCache)
        {
            if (kvp.Value != null) DestroyImmediate(kvp.Value);
        }
        materialCache.Clear();
    }

    // ==================== 床 ====================

    void BuildFloor()
    {
        // 床を自前のメッシュで用意している部屋(プールなど)では、
        // ここで床を作ると自作メッシュの上に重なって穴を塞いでしまう。
        if (!generateFloor) return;

        GameObject floorRoot = new GameObject("Floor");
        floorRoot.transform.SetParent(root, false);

        // 床は壁の「外側の面」まで伸ばす。
        // ここを部屋の内寸ぴったり(0〜roomDepthX)にすると、壁は中心線上に建つため
        // 壁の厚みの外半分には床が存在しないことになり、
        // 入り口をくぐる瞬間に足元が抜けて見える(=「扉の下に床が生成されない」)。
        float halfWall = wallThickness * 0.5f;
        float originX = -halfWall;
        float originZ = -halfWall;
        float spanX = roomDepthX + wallThickness;
        float spanZ = roomWidthZ + wallThickness;

        // 1枚板モード。
        // 床タイルを1枚ずつ並べるとオブジェクト数が跳ね上がり、部屋を組み直すたびに重くなる
        // (12m×7mの部屋でも数十枚、大部屋では数百枚に達する)。
        // マテリアル(BLIND/RoomSurface)がワールド座標基準でタイル模様を描くため、
        // 板1枚にしても見た目はタイル床のまま。目地の位置も変わらない。
        // ※ 落とし穴(tileFillRatio や manualHoles)を使う部屋ではオフにすること。
        if (singleSlabFloor)
        {
            CreateFloorTile(floorRoot.transform,
                new Vector2(originX, originZ),
                new Vector2(originX + spanX, originZ + spanZ));
            return;
        }

        int colsX = Mathf.CeilToInt(spanX / tileDepthX);
        int colsZ = Mathf.CeilToInt(spanZ / tileWidthZ);

        System.Random rng = new System.Random(randomSeed);

        for (int ix = 0; ix < colsX; ix++)
        {
            for (int iz = 0; iz < colsZ; iz++)
            {
                float x0 = originX + ix * tileDepthX;
                float z0 = originZ + iz * tileWidthZ;
                float x1 = Mathf.Min(x0 + tileDepthX, originX + spanX);
                float z1 = Mathf.Min(z0 + tileWidthZ, originZ + spanZ);

                Vector2 min = new Vector2(x0, z0);
                Vector2 max = new Vector2(x1, z1);
                Vector2 center = (min + max) * 0.5f;

                // 手動指定の穴に入っていればスキップ
                bool inManualHole = false;
                foreach (var h in manualHoles)
                {
                    if (IsInsideRect(center, h.min, h.max)) { inManualHole = true; break; }
                }
                if (inManualHole) continue;

                // 生成率に応じてランダムに間引く(落とし穴)
                if (rng.NextDouble() > tileFillRatio) continue;

                CreateFloorTile(floorRoot.transform, min, max);
            }
        }
    }

    bool IsInsideRect(Vector2 p, Vector2 min, Vector2 max)
    {
        return p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;
    }

    void CreateFloorTile(Transform parent, Vector2 min, Vector2 max)
    {
        float w = max.x - min.x; // X方向(縦)
        float d = max.y - min.y; // Z方向(横) ※Vector2のyをZとして扱っている
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = "FloorTile";
        tile.transform.SetParent(parent, false);
        Vector3 scale = new Vector3(w, tileThickness, d);
        tile.transform.localScale = scale;
        Vector3 pos = new Vector3(min.x + w / 2f, -tileThickness / 2f, min.y + d / 2f);
        tile.transform.localPosition = pos;
        ApplyMaterial(tile, floorMaterial, scale, pos, SurfaceType.Floor);
    }

    // ==================== 壁 ====================

    void BuildWalls()
    {
        GameObject wallRoot = new GameObject("Walls");
        wallRoot.transform.SetParent(root, false);

        // North: X = roomDepthX の位置、Z方向に伸びる壁
        BuildWallAlongZ(wallRoot.transform, roomDepthX, northWall);
        // South: X = 0 の位置
        BuildWallAlongZ(wallRoot.transform, 0f, southWall);
        // East: Z = roomWidthZ の位置、X方向に伸びる壁
        BuildWallAlongX(wallRoot.transform, roomWidthZ, eastWall);
        // West: Z = 0 の位置
        BuildWallAlongX(wallRoot.transform, 0f, westWall);
    }

    // Z方向に伸びる壁(North/South)。入り口があればギャップを開けて2分割
    void BuildWallAlongZ(Transform parent, float x, WallConfig cfg)
    {
        float wallLength = roomWidthZ;
        float halfWall = wallThickness * 0.5f;

        if (!cfg.hasDoor)
        {
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, wallLength / 2f),
                new Vector3(wallThickness, wallHeightY, wallLength + wallThickness),
                SurfaceType.NorthSouth);
            return;
        }

        float center = wallLength / 2f + cfg.doorOffset;
        float doorStart = Mathf.Clamp(center - cfg.doorWidth / 2f, 0f, wallLength);
        float doorEnd = Mathf.Clamp(center + cfg.doorWidth / 2f, 0f, wallLength);
        if (doorEnd < doorStart) doorEnd = doorStart;

        // 入り口が無い場合の壁は「wallLength + wallThickness」で角まで伸ばしているのに対し、
        // 入り口がある場合は 0〜wallLength ちょうどで作られていたため、
        // 部屋の四隅に壁の厚みの半分だけ隙間が空き、扉をくぐる時にそこから
        // 隣の部屋の壁紙が覗いて見えていた(=「壁紙の境目が見える」)。
        // 両端を角(-wallThickness/2 と wallLength+wallThickness/2)まで伸ばして塞ぐ。
        float zMin = -halfWall;
        float zMax = wallLength + halfWall;

        if (doorStart > zMin)
        {
            float segLen = doorStart - zMin;
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, zMin + segLen / 2f),
                new Vector3(wallThickness, wallHeightY, segLen),
                SurfaceType.NorthSouth);
        }
        if (doorEnd < zMax)
        {
            float segLen = zMax - doorEnd;
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, doorEnd + segLen / 2f),
                new Vector3(wallThickness, wallHeightY, segLen),
                SurfaceType.NorthSouth);
        }

        // 入り口の上部(まぐさ)。doorHeightが壁の高さより低い場合のみ生成し、
        // 入り口の上に壁が残った「ドア型」の開口にする
        float doorSpan = doorEnd - doorStart;
        if (doorSpan > 0f && cfg.doorHeight < wallHeightY)
        {
            float topHeight = wallHeightY - cfg.doorHeight;
            CreateWallSegment(parent,
                new Vector3(x, cfg.doorHeight + topHeight / 2f, (doorStart + doorEnd) / 2f),
                new Vector3(wallThickness, topHeight, doorSpan),
                SurfaceType.NorthSouth);
        }

        // ドアフレーム
        if (doorSpan > 0f)
        {
            CreateDoorFrameZ(parent, x, doorStart, doorEnd, cfg.doorHeight);
        }
    }

    // X方向に伸びる壁(East/West)。入り口があればギャップを開けて2分割
    void BuildWallAlongX(Transform parent, float z, WallConfig cfg)
    {
        float wallLength = roomDepthX;
        float halfWall = wallThickness * 0.5f;

        if (!cfg.hasDoor)
        {
            CreateWallSegment(parent,
                new Vector3(wallLength / 2f, wallHeightY / 2f, z),
                new Vector3(wallLength + wallThickness, wallHeightY, wallThickness),
                SurfaceType.EastWest);
            return;
        }

        float center = wallLength / 2f + cfg.doorOffset;
        float doorStart = Mathf.Clamp(center - cfg.doorWidth / 2f, 0f, wallLength);
        float doorEnd = Mathf.Clamp(center + cfg.doorWidth / 2f, 0f, wallLength);
        if (doorEnd < doorStart) doorEnd = doorStart;

        // BuildWallAlongZ と同じく、両端を部屋の角まで伸ばして四隅の隙間を塞ぐ
        float xMin = -halfWall;
        float xMax = wallLength + halfWall;

        if (doorStart > xMin)
        {
            float segLen = doorStart - xMin;
            CreateWallSegment(parent,
                new Vector3(xMin + segLen / 2f, wallHeightY / 2f, z),
                new Vector3(segLen, wallHeightY, wallThickness),
                SurfaceType.EastWest);
        }
        if (doorEnd < xMax)
        {
            float segLen = xMax - doorEnd;
            CreateWallSegment(parent,
                new Vector3(doorEnd + segLen / 2f, wallHeightY / 2f, z),
                new Vector3(segLen, wallHeightY, wallThickness),
                SurfaceType.EastWest);
        }

        // 入り口の上部(まぐさ)
        float doorSpan = doorEnd - doorStart;
        if (doorSpan > 0f && cfg.doorHeight < wallHeightY)
        {
            float topHeight = wallHeightY - cfg.doorHeight;
            CreateWallSegment(parent,
                new Vector3((doorStart + doorEnd) / 2f, cfg.doorHeight + topHeight / 2f, z),
                new Vector3(doorSpan, topHeight, wallThickness),
                SurfaceType.EastWest);
        }

        // ドアフレーム
        if (doorSpan > 0f)
        {
            CreateDoorFrameX(parent, z, doorStart, doorEnd, cfg.doorHeight);
        }
    }

    void CreateWallSegment(Transform parent, Vector3 localPos, Vector3 scale, SurfaceType surfaceType)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallSegment";
        wall.transform.SetParent(parent, false);
        wall.transform.localScale = scale;
        wall.transform.localPosition = localPos;
        ApplyMaterial(wall, wallMaterial, scale, localPos, surfaceType);
    }

    // ==================== ドアフレーム ====================

    // Z方向の壁(North/South)用ドアフレーム ― 全て開口部の内側に配置
    void CreateDoorFrameZ(Transform parent, float x, float doorStart, float doorEnd, float doorHeight)
    {
        float doorSpan = doorEnd - doorStart;
        float ft = doorFrameThickness;
        Material frameMat = doorFrameMaterial != null ? doorFrameMaterial : wallMaterial;

        GameObject frame = new GameObject("DoorFrame");
        frame.transform.SetParent(parent, false);

        // 【枠の重なり対策】
        // 以前は柱が Y=0〜doorHeight の全高で作られていたため、
        // 下枠(Y=0〜ft)・上枠と同じ場所を奪い合って重なり、
        // 継ぎ目にちらつき(Zファイティング)が出ていた。
        // 「下枠 → 柱 → 上枠」を縦に積んで、互いに一切重ならないようにする。
        float postHeight = doorHeight - 2f * ft;
        float postCenterY = ft + postHeight / 2f;

        // 下枠（Y = 0 〜 ft、開口の幅いっぱい）
        CreateFramePiece(frame.transform, frameMat,
            new Vector3(x, ft / 2f, (doorStart + doorEnd) / 2f),
            new Vector3(wallThickness, ft, doorSpan));

        // 上枠（Y = doorHeight-ft 〜 doorHeight、開口の幅いっぱい）
        CreateFramePiece(frame.transform, frameMat,
            new Vector3(x, doorHeight - ft / 2f, (doorStart + doorEnd) / 2f),
            new Vector3(wallThickness, ft, doorSpan));

        // 左右の柱（Y = ft 〜 doorHeight-ft。上下の枠の間だけを埋める）
        if (postHeight > 0f)
        {
            CreateFramePiece(frame.transform, frameMat,
                new Vector3(x, postCenterY, doorStart + ft / 2f),
                new Vector3(wallThickness, postHeight, ft));

            CreateFramePiece(frame.transform, frameMat,
                new Vector3(x, postCenterY, doorEnd - ft / 2f),
                new Vector3(wallThickness, postHeight, ft));
        }
    }

    // X方向の壁(East/West)用ドアフレーム ― 全て開口部の内側に配置
    void CreateDoorFrameX(Transform parent, float z, float doorStart, float doorEnd, float doorHeight)
    {
        float doorSpan = doorEnd - doorStart;
        float ft = doorFrameThickness;
        Material frameMat = doorFrameMaterial != null ? doorFrameMaterial : wallMaterial;

        GameObject frame = new GameObject("DoorFrame");
        frame.transform.SetParent(parent, false);

        // CreateDoorFrameZ と同じく「下枠 → 柱 → 上枠」を縦に積んで重なりを無くす
        float postHeight = doorHeight - 2f * ft;
        float postCenterY = ft + postHeight / 2f;

        // 下枠（Y = 0 〜 ft、開口の幅いっぱい）
        CreateFramePiece(frame.transform, frameMat,
            new Vector3((doorStart + doorEnd) / 2f, ft / 2f, z),
            new Vector3(doorSpan, ft, wallThickness));

        // 上枠（Y = doorHeight-ft 〜 doorHeight、開口の幅いっぱい）
        CreateFramePiece(frame.transform, frameMat,
            new Vector3((doorStart + doorEnd) / 2f, doorHeight - ft / 2f, z),
            new Vector3(doorSpan, ft, wallThickness));

        // 左右の柱（Y = ft 〜 doorHeight-ft。上下の枠の間だけを埋める）
        if (postHeight > 0f)
        {
            CreateFramePiece(frame.transform, frameMat,
                new Vector3(doorStart + ft / 2f, postCenterY, z),
                new Vector3(ft, postHeight, wallThickness));

            CreateFramePiece(frame.transform, frameMat,
                new Vector3(doorEnd - ft / 2f, postCenterY, z),
                new Vector3(ft, postHeight, wallThickness));
        }
    }

    void CreateFramePiece(Transform parent, Material sourceMat, Vector3 localPos, Vector3 scale)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = "FramePiece";
        piece.transform.SetParent(parent, false);
        piece.transform.localScale = scale;
        piece.transform.localPosition = localPos;
        if (sourceMat != null)
        {
            piece.GetComponent<Renderer>().sharedMaterial = GetCachedMaterial(sourceMat);
        }
    }

    // ==================== マテリアル適用 ====================

    // ワールド座標ベースでメッシュUVを直接書き換え、キャッシュ済みマテリアルを適用
    void ApplyMaterial(GameObject obj, Material sourceMat, Vector3 scale, Vector3 localPos, SurfaceType surfaceType)
    {
        if (sourceMat == null) return;

        // 全頂点のUVをワールド座標に基づいて設定
        SetWorldSpaceUVs(obj, scale, localPos, surfaceType);

        // キャッシュ済みマテリアルをsharedMaterialで適用（インスタンス生成を回避）
        obj.GetComponent<Renderer>().sharedMaterial = GetCachedMaterial(sourceMat);
    }

    // マテリアルはアセットをそのまま共有する。
    //
    // 【なぜ複製を作らないか】
    // 以前はここで new Material(sourceMat) の複製を作っていたが、この複製は
    // 生成物と一緒にシーンへ保存されてしまう。するとマテリアル資産を後から
    // 編集しても複製側は古いままになり、
    //   ・編集モード → シーンに保存された古い複製が見える（テクスチャ無しなど）
    //   ・再生       → Startのビルドで作り直され、正しい見た目になる
    // という食い違いが起きる。CleanUpは "(Instance)" で終わる名前しか破棄しない
    // ため、この複製は破棄されず溜まり続けていた。
    //
    // また複製時に色とtiling/offsetを初期化していたが、BLIND/RoomSurface は
    // ワールド座標でUVを決めるのでtilingは不要で、_Colorはむしろ意図した色味
    // (壁の薄いグレー等)なので白に潰してはいけない。
    Material GetCachedMaterial(Material sourceMat)
    {
        if (!useUnlitShader) return sourceMat;

        if (materialCache.TryGetValue(sourceMat, out Material cached) && cached != null)
        {
            return cached;
        }

        Material mat = new Material(Shader.Find("Unlit/Texture"));
        if (sourceMat.mainTexture != null)
            mat.mainTexture = sourceMat.mainTexture;

        materialCache[sourceMat] = mat;
        return mat;
    }

    // Cubeメッシュの全頂点UVをワールド座標で設定
    // 全オブジェクトが同一のワールド座標系を参照するため、タイル目地が自動で揃う
    void SetWorldSpaceUVs(GameObject obj, Vector3 scale, Vector3 localPos, SurfaceType surfaceType)
    {
        MeshFilter mf = obj.GetComponent<MeshFilter>();
        Mesh mesh = mf.mesh; // コピーが作られるが、CleanUpで破棄される
        Vector3[] verts = mesh.vertices;
        Vector2[] uvs = new Vector2[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            // Cube頂点(±0.5)からワールド座標を算出
            float wx = localPos.x + verts[i].x * scale.x;
            float wy = localPos.y + verts[i].y * scale.y;
            float wz = localPos.z + verts[i].z * scale.z;

            switch (surfaceType)
            {
                case SurfaceType.NorthSouth: // N/S壁: U=Y, V=Z（床のV=Zと一致）
                    uvs[i] = new Vector2(wy, wz);
                    break;
                case SurfaceType.EastWest: // E/W壁: U=X, V=Y
                    uvs[i] = new Vector2(wx, wy);
                    break;
                default: // 床・天井: U=X, V=Z
                    uvs[i] = new Vector2(wx, wz);
                    break;
            }
        }

        mesh.uv = uvs;
    }

    // ==================== 天井 ====================

    void BuildCeiling()
    {
        GameObject ceilingRoot = new GameObject("Ceiling");
        ceilingRoot.transform.SetParent(root, false);

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = "CeilingPanel";
        ceiling.transform.SetParent(ceilingRoot.transform, false);
        Vector3 ceilingScale = new Vector3(roomDepthX + wallThickness, ceilingThickness, roomWidthZ + wallThickness);
        ceiling.transform.localScale = ceilingScale;
        Vector3 ceilingPos = new Vector3(roomDepthX / 2f, wallHeightY + ceilingThickness / 2f, roomWidthZ / 2f);
        ceiling.transform.localPosition = ceilingPos;
        ApplyMaterial(ceiling, ceilingMaterial, ceilingScale, ceilingPos, SurfaceType.Floor);
    }

    /// <summary>天井の表示/非表示を切り替える</summary>
    public void SetCeiling(bool enabled)
    {
        hasCeiling = enabled;
        BuildRoom();
    }

    // ==================== スクリプトから呼べる操作API ====================
    // Inspectorで直接値をいじる代わりに、他のスクリプトから
    // 動的に部屋を編集したい場合はこれらを使ってください。
    // 呼び出し後は自動でBuildRoom()が実行され、即座に反映されます。

    public WallConfig GetWall(WallSide side)
    {
        switch (side)
        {
            case WallSide.North: return northWall;
            case WallSide.South: return southWall;
            case WallSide.East: return eastWall;
            case WallSide.West: return westWall;
            default: return null;
        }
    }

    /// <summary>指定した壁に入り口を追加(既にあれば設定を上書き)</summary>
    public void AddDoor(WallSide side, float width, float offset = 0f)
    {
        var w = GetWall(side);
        w.hasDoor = true;
        w.doorWidth = width;
        w.doorOffset = offset;
        BuildRoom();
    }

    /// <summary>指定した壁から入り口を削除</summary>
    public void RemoveDoor(WallSide side)
    {
        GetWall(side).hasDoor = false;
        BuildRoom();
    }

    /// <summary>入り口の横幅を変更</summary>
    public void SetDoorWidth(WallSide side, float width)
    {
        GetWall(side).doorWidth = width;
        BuildRoom();
    }

    /// <summary>入り口の位置を左右(または前後)にずらす</summary>
    public void SetDoorOffset(WallSide side, float offset)
    {
        GetWall(side).doorOffset = offset;
        BuildRoom();
    }

    /// <summary>部屋全体のサイズを変更(縦・横)</summary>
    public void SetRoomSize(float depthX, float widthZ)
    {
        roomDepthX = depthX;
        roomWidthZ = widthZ;
        BuildRoom();
    }

    /// <summary>床タイルの生成率を変更(0〜1、低いほど穴だらけになる)</summary>
    public void SetTileFillRatio(float ratio)
    {
        tileFillRatio = Mathf.Clamp01(ratio);
        BuildRoom();
    }
}
