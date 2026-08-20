using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 汎用の部屋生成スクリプト(RoomBuilder3) - 複数ドア対応版
/// 軸の定義: X = 縦(奥行き) / Z = 横(幅) / Y = 高さ
/// </summary>
public class RoomBuilder3 : MonoBehaviour
{
    public enum WallSide { North, South, East, West }
    public enum SurfaceType { NorthSouth, EastWest, Floor }

    // 個別のドアの設定データ
    [System.Serializable]
    public class DoorConfig
    {
        [Tooltip("入り口の横幅(m)")]
        public float doorWidth = 1.2f;

        [Tooltip("壁の中心から入り口をどれだけ左右(または前後)にずらすか(m)。0で中央")]
        public float doorOffset = 0f;

        [Tooltip("入り口の高さ(m)")]
        public float doorHeight = 2.0f;
    }

    // 壁ごとの設定データ（複数のドアを持てるように拡張）
    [System.Serializable]
    public class WallConfig
    {
        [Tooltip("この壁に入り口を作るか")]
        public bool hasDoor = false;

        [Tooltip("この壁に配置するドアのリスト（複数設定可能）")]
        public List<DoorConfig> doors = new List<DoorConfig>() { new DoorConfig() };
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
    public float roomDepthX = 6f;
    public float roomWidthZ = 6f;
    public float wallHeightY = 2.5f;
    public float wallThickness = 0.2f;

    [Header("壁ごとの入り口設定")]
    public WallConfig northWall = new WallConfig();
    public WallConfig southWall = new WallConfig();
    public WallConfig eastWall = new WallConfig();
    public WallConfig westWall = new WallConfig();

    [Header("床タイル設定")]
    public bool generateFloor = true;
    public bool singleSlabFloor = false;
    public float tileDepthX = 0.5f;
    public float tileWidthZ = 0.5f;
    public float tileThickness = 0.1f;
    [Range(0f, 1f)]
    public float tileFillRatio = 1f;
    public int randomSeed = 0;

    [Header("手動で指定する穴(任意・特定の形状の落とし穴用)")]
    public List<HoleRect> manualHoles = new List<HoleRect>();

    [Header("天井")]
    public bool hasCeiling = false;
    public float ceilingThickness = 0.1f;

    [Header("マテリアル(任意)")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;

    [Header("ドアフレーム設定")]
    public float doorFrameThickness = 0.05f;
    public Material doorFrameMaterial;

    [Header("レンダリング設定")]
    public bool useUnlitShader = false;

    private Transform root;
    private Dictionary<Material, Material> materialCache = new Dictionary<Material, Material>();

    void Start()
    {
        if (transform.Find("GeneratedRoom") == null)
        {
            BuildRoom();
        }
    }

    void OnValidate()
    {
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

    [ContextMenu("Build Room")]
    public void BuildRoom()
    {
        CleanUp();

        GameObject rootObj = new GameObject("GeneratedRoom");
        rootObj.transform.SetParent(transform, false);
        root = rootObj.transform;

        materialCache.Clear();

        BuildFloor();
        BuildWalls();

        if (hasCeiling)
        {
            BuildCeiling();
        }
    }

    void CleanUp()
    {
        Transform old = transform.Find("GeneratedRoom");
        if (old != null)
        {
            foreach (var mf in old.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null && !mf.sharedMesh.name.StartsWith("Cube"))
                {
                    DestroyImmediate(mf.sharedMesh);
                }
            }
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

        foreach (var kvp in materialCache)
        {
            if (kvp.Value != null) DestroyImmediate(kvp.Value);
        }
        materialCache.Clear();
    }

    // ==================== 床 ====================

    void BuildFloor()
    {
        if (!generateFloor) return;

        GameObject floorRoot = new GameObject("Floor");
        floorRoot.transform.SetParent(root, false);

        float halfWall = wallThickness * 0.5f;
        float originX = -halfWall;
        float originZ = -halfWall;
        float spanX = roomDepthX + wallThickness;
        float spanZ = roomWidthZ + wallThickness;

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

                bool inManualHole = false;
                foreach (var h in manualHoles)
                {
                    if (IsInsideRect(center, h.min, h.max)) { inManualHole = true; break; }
                }
                if (inManualHole) continue;

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
        float w = max.x - min.x;
        float d = max.y - min.y;
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

        BuildWallAlongZ(wallRoot.transform, roomDepthX, northWall);
        BuildWallAlongZ(wallRoot.transform, 0f, southWall);
        BuildWallAlongX(wallRoot.transform, roomWidthZ, eastWall);
        BuildWallAlongX(wallRoot.transform, 0f, westWall);
    }

    // ドア開口部の範囲（開始・終了・高さ）を保持する構造体
    struct DoorSpan
    {
        public float start;
        public float end;
        public float height;
    }

    // Z方向に伸びる壁(North/South) - 複数ドア対応
    void BuildWallAlongZ(Transform parent, float x, WallConfig cfg)
    {
        float wallLength = roomWidthZ;
        float halfWall = wallThickness * 0.5f;

        if (!cfg.hasDoor || cfg.doors == null || cfg.doors.Count == 0)
        {
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, wallLength / 2f),
                new Vector3(wallThickness, wallHeightY, wallLength + wallThickness),
                SurfaceType.NorthSouth);
            return;
        }

        // 開口部リストを作成してソート
        List<DoorSpan> spans = new List<DoorSpan>();
        float centerOffset = wallLength / 2f;

        foreach (var door in cfg.doors)
        {
            float c = centerOffset + door.doorOffset;
            float s = Mathf.Clamp(c - door.doorWidth / 2f, 0f, wallLength);
            float e = Mathf.Clamp(c + door.doorWidth / 2f, 0f, wallLength);
            if (e > s)
            {
                spans.Add(new DoorSpan { start = s, end = e, height = door.doorHeight });
            }
        }

        spans.Sort((a, b) => a.start.CompareTo(b.start));

        // 壁セグメントの生成（ドアとドアの間を壁で埋める）
        float currentPos = -halfWall;
        float zMax = wallLength + halfWall;

        foreach (var span in spans)
        {
            if (span.start > currentPos)
            {
                float segLen = span.start - currentPos;
                CreateWallSegment(parent,
                    new Vector3(x, wallHeightY / 2f, currentPos + segLen / 2f),
                    new Vector3(wallThickness, wallHeightY, segLen),
                    SurfaceType.NorthSouth);
            }

            // まぐさ（ドアの上の壁）
            if (span.height < wallHeightY)
            {
                float topHeight = wallHeightY - span.height;
                float doorLen = span.end - span.start;
                CreateWallSegment(parent,
                    new Vector3(x, span.height + topHeight / 2f, (span.start + span.end) / 2f),
                    new Vector3(wallThickness, topHeight, doorLen),
                    SurfaceType.NorthSouth);
            }

            // ドアフレーム生成
            CreateDoorFrameZ(parent, x, span.start, span.end, span.height);

            currentPos = Mathf.Max(currentPos, span.end);
        }

        // 最後のドアから端までの壁
        if (currentPos < zMax)
        {
            float segLen = zMax - currentPos;
            CreateWallSegment(parent,
                new Vector3(x, wallHeightY / 2f, currentPos + segLen / 2f),
                new Vector3(wallThickness, wallHeightY, segLen),
                SurfaceType.NorthSouth);
        }
    }

    // X方向に伸びる壁(East/West) - 複数ドア対応
    void BuildWallAlongX(Transform parent, float z, WallConfig cfg)
    {
        float wallLength = roomDepthX;
        float halfWall = wallThickness * 0.5f;

        if (!cfg.hasDoor || cfg.doors == null || cfg.doors.Count == 0)
        {
            CreateWallSegment(parent,
                new Vector3(wallLength / 2f, wallHeightY / 2f, z),
                new Vector3(wallLength + wallThickness, wallHeightY, wallThickness),
                SurfaceType.EastWest);
            return;
        }

        List<DoorSpan> spans = new List<DoorSpan>();
        float centerOffset = wallLength / 2f;

        foreach (var door in cfg.doors)
        {
            float c = centerOffset + door.doorOffset;
            float s = Mathf.Clamp(c - door.doorWidth / 2f, 0f, wallLength);
            float e = Mathf.Clamp(c + door.doorWidth / 2f, 0f, wallLength);
            if (e > s)
            {
                spans.Add(new DoorSpan { start = s, end = e, height = door.doorHeight });
            }
        }

        spans.Sort((a, b) => a.start.CompareTo(b.start));

        float currentPos = -halfWall;
        float xMax = wallLength + halfWall;

        foreach (var span in spans)
        {
            if (span.start > currentPos)
            {
                float segLen = span.start - currentPos;
                CreateWallSegment(parent,
                    new Vector3(currentPos + segLen / 2f, wallHeightY / 2f, z),
                    new Vector3(segLen, wallHeightY, wallThickness),
                    SurfaceType.EastWest);
            }

            if (span.height < wallHeightY)
            {
                float topHeight = wallHeightY - span.height;
                float doorLen = span.end - span.start;
                CreateWallSegment(parent,
                    new Vector3((span.start + span.end) / 2f, span.height + topHeight / 2f, z),
                    new Vector3(doorLen, topHeight, wallThickness),
                    SurfaceType.EastWest);
            }

            CreateDoorFrameX(parent, z, span.start, span.end, span.height);

            currentPos = Mathf.Max(currentPos, span.end);
        }

        if (currentPos < xMax)
        {
            float segLen = xMax - currentPos;
            CreateWallSegment(parent,
                new Vector3(currentPos + segLen / 2f, wallHeightY / 2f, z),
                new Vector3(segLen, wallHeightY, wallThickness),
                SurfaceType.EastWest);
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

    void CreateDoorFrameZ(Transform parent, float x, float doorStart, float doorEnd, float doorHeight)
    {
        float doorSpan = doorEnd - doorStart;
        float ft = doorFrameThickness;
        Material frameMat = doorFrameMaterial != null ? doorFrameMaterial : wallMaterial;

        GameObject frame = new GameObject("DoorFrame");
        frame.transform.SetParent(parent, false);

        float postHeight = doorHeight - 2f * ft;
        float postCenterY = ft + postHeight / 2f;

        CreateFramePiece(frame.transform, frameMat,
            new Vector3(x, ft / 2f, (doorStart + doorEnd) / 2f),
            new Vector3(wallThickness, ft, doorSpan));

        CreateFramePiece(frame.transform, frameMat,
            new Vector3(x, doorHeight - ft / 2f, (doorStart + doorEnd) / 2f),
            new Vector3(wallThickness, ft, doorSpan));

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

    void CreateDoorFrameX(Transform parent, float z, float doorStart, float doorEnd, float doorHeight)
    {
        float doorSpan = doorEnd - doorStart;
        float ft = doorFrameThickness;
        Material frameMat = doorFrameMaterial != null ? doorFrameMaterial : wallMaterial;

        GameObject frame = new GameObject("DoorFrame");
        frame.transform.SetParent(parent, false);

        float postHeight = doorHeight - 2f * ft;
        float postCenterY = ft + postHeight / 2f;

        CreateFramePiece(frame.transform, frameMat,
            new Vector3((doorStart + doorEnd) / 2f, ft / 2f, z),
            new Vector3(doorSpan, ft, wallThickness));

        CreateFramePiece(frame.transform, frameMat,
            new Vector3((doorStart + doorEnd) / 2f, doorHeight - ft / 2f, z),
            new Vector3(doorSpan, ft, wallThickness));

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

    void ApplyMaterial(GameObject obj, Material sourceMat, Vector3 scale, Vector3 localPos, SurfaceType surfaceType)
    {
        if (sourceMat == null) return;
        SetWorldSpaceUVs(obj, scale, localPos, surfaceType);
        obj.GetComponent<Renderer>().sharedMaterial = GetCachedMaterial(sourceMat);
    }

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

    void SetWorldSpaceUVs(GameObject obj, Vector3 scale, Vector3 localPos, SurfaceType surfaceType)
    {
        MeshFilter mf = obj.GetComponent<MeshFilter>();
        Mesh mesh = mf.mesh;
        Vector3[] verts = mesh.vertices;
        Vector2[] uvs = new Vector2[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            float wx = localPos.x + verts[i].x * scale.x;
            float wy = localPos.y + verts[i].y * scale.y;
            float wz = localPos.z + verts[i].z * scale.z;

            switch (surfaceType)
            {
                case SurfaceType.NorthSouth:
                    uvs[i] = new Vector2(wy, wz);
                    break;
                case SurfaceType.EastWest:
                    uvs[i] = new Vector2(wx, wy);
                    break;
                default:
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

    public void SetCeiling(bool enabled)
    {
        hasCeiling = enabled;
        BuildRoom();
    }
}