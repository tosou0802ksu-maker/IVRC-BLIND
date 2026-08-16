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

    private Transform root;

    void Start()
    {
        BuildRoom();
    }

    // ==================== 部屋の再構築 ====================

    [ContextMenu("Build Room")]
    public void BuildRoom()
    {
        Transform old = transform.Find("GeneratedRoom");
        if (old != null) DestroyImmediate(old.gameObject);

        GameObject rootObj = new GameObject("GeneratedRoom");
        rootObj.transform.SetParent(transform, false);
        root = rootObj.transform;

        BuildFloor();
        BuildWalls();

        if (hasCeiling)
        {
            BuildCeiling();
        }
    }

    // ==================== 床 ====================

    void BuildFloor()
    {
        GameObject floorRoot = new GameObject("Floor");
        floorRoot.transform.SetParent(root, false);

        int colsX = Mathf.CeilToInt(roomDepthX / tileDepthX);
        int colsZ = Mathf.CeilToInt(roomWidthZ / tileWidthZ);

        System.Random rng = new System.Random(randomSeed);

        for (int ix = 0; ix < colsX; ix++)
        {
            for (int iz = 0; iz < colsZ; iz++)
            {
                float x0 = ix * tileDepthX;
                float z0 = iz * tileWidthZ;
                float x1 = Mathf.Min(x0 + tileDepthX, roomDepthX);
                float z1 = Mathf.Min(z0 + tileWidthZ, roomWidthZ);

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
        tile.transform.localScale = new Vector3(w, tileThickness, d);
        tile.transform.localPosition = new Vector3(min.x + w / 2f, -tileThickness / 2f, min.y + d / 2f);
        if (floorMaterial != null)
            tile.GetComponent<Renderer>().sharedMaterial = floorMaterial;
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

        if (!cfg.hasDoor)
        {
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, wallLength / 2f),
                new Vector3(wallThickness, wallHeightY, wallLength + wallThickness));
            return;
        }

        float center = wallLength / 2f + cfg.doorOffset;
        float doorStart = Mathf.Clamp(center - cfg.doorWidth / 2f, 0f, wallLength);
        float doorEnd = Mathf.Clamp(center + cfg.doorWidth / 2f, 0f, wallLength);
        if (doorEnd < doorStart) doorEnd = doorStart;

        if (doorStart > 0f)
        {
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, doorStart / 2f),
                new Vector3(wallThickness, wallHeightY, doorStart));
        }
        if (doorEnd < wallLength)
        {
            float segLen = wallLength - doorEnd;
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, doorEnd + segLen / 2f),
                new Vector3(wallThickness, wallHeightY, segLen));
        }

        // 入り口の上部(まぐさ)。doorHeightが壁の高さより低い場合のみ生成し、
        // 入り口の上に壁が残った「ドア型」の開口にする
        float doorSpan = doorEnd - doorStart;
        if (doorSpan > 0f && cfg.doorHeight < wallHeightY)
        {
            float topHeight = wallHeightY - cfg.doorHeight;
            CreateWallSegment(parent,
                new Vector3(x, cfg.doorHeight + topHeight / 2f, (doorStart + doorEnd) / 2f),
                new Vector3(wallThickness, topHeight, doorSpan));
        }
    }

    // X方向に伸びる壁(East/West)。入り口があればギャップを開けて2分割
    void BuildWallAlongX(Transform parent, float z, WallConfig cfg)
    {
        float wallLength = roomDepthX;

        if (!cfg.hasDoor)
        {
            CreateWallSegment(parent,
                new Vector3(wallLength / 2f, wallHeightY / 2f, z),
                new Vector3(wallLength + wallThickness, wallHeightY, wallThickness));
            return;
        }

        float center = wallLength / 2f + cfg.doorOffset;
        float doorStart = Mathf.Clamp(center - cfg.doorWidth / 2f, 0f, wallLength);
        float doorEnd = Mathf.Clamp(center + cfg.doorWidth / 2f, 0f, wallLength);
        if (doorEnd < doorStart) doorEnd = doorStart;

        if (doorStart > 0f)
        {
            CreateWallSegment(parent,
                new Vector3(doorStart / 2f, wallHeightY / 2f, z),
                new Vector3(doorStart, wallHeightY, wallThickness));
        }
        if (doorEnd < wallLength)
        {
            float segLen = wallLength - doorEnd;
            CreateWallSegment(parent,
                new Vector3(doorEnd + segLen / 2f, wallHeightY / 2f, z),
                new Vector3(segLen, wallHeightY, wallThickness));
        }

        // 入り口の上部(まぐさ)
        float doorSpan = doorEnd - doorStart;
        if (doorSpan > 0f && cfg.doorHeight < wallHeightY)
        {
            float topHeight = wallHeightY - cfg.doorHeight;
            CreateWallSegment(parent,
                new Vector3((doorStart + doorEnd) / 2f, cfg.doorHeight + topHeight / 2f, z),
                new Vector3(doorSpan, topHeight, wallThickness));
        }
    }

    void CreateWallSegment(Transform parent, Vector3 localPos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallSegment";
        wall.transform.SetParent(parent, false);
        wall.transform.localScale = scale;
        wall.transform.localPosition = localPos;
        if (wallMaterial != null)
            wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
    }

    // ==================== 天井 ====================

    void BuildCeiling()
    {
        GameObject ceilingRoot = new GameObject("Ceiling");
        ceilingRoot.transform.SetParent(root, false);

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = "CeilingPanel";
        ceiling.transform.SetParent(ceilingRoot.transform, false);
        ceiling.transform.localScale = new Vector3(roomDepthX + wallThickness, ceilingThickness, roomWidthZ + wallThickness);
        ceiling.transform.localPosition = new Vector3(roomDepthX / 2f, wallHeightY + ceilingThickness / 2f, roomWidthZ / 2f);
        if (ceilingMaterial != null)
            ceiling.GetComponent<Renderer>().sharedMaterial = ceilingMaterial;
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
