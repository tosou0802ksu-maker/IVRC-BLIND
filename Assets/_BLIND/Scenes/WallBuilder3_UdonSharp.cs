using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// UdonSharpの型制約をクリアした壁生成スクリプト
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WallBuilder3_UdonSharp : UdonSharpBehaviour
{
    [Header("必須アセット設定")]
    [Tooltip("壁のベースとなるCubeのPrefab。未指定の場合はこのオブジェクト自体を複製して使います。")]
    public GameObject cubePrefab;

    [Header("壁のサイズ")]
    public float wallLength = 6f;
    public float wallHeightY = 2.5f;
    public float wallThickness = 0.2f;

    [Tooltip("オンの場合、壁の両端を厚みの半分だけ外側に伸ばします。")]
    public bool extendEndsHalfThickness = true;

    [Header("入り口1")]
    public bool door1Enabled = false;
    public float door1Width = 1.2f;
    public float door1Offset = 0f;
    public float door1Height = 2.0f;

    [Header("入り口2")]
    public bool door2Enabled = false;
    public float door2Width = 1.2f;
    public float door2Offset = 0f;
    public float door2Height = 2.0f;

    [Header("入り口3")]
    public bool door3Enabled = false;
    public float door3Width = 1.2f;
    public float door3Offset = 0f;
    public float door3Height = 2.0f;

    [Header("マテリアル設定")]
    public Material wallMaterial;

    [Header("ドアフレーム設定")]
    public float doorFrameThickness = 0.05f;
    public Material doorFrameMaterial;
    public bool generateDoorFrame = true;

    private Transform root;

    void Start()
    {
        BuildWall();
    }

    // UdonSharp完全対応の安全な複製処理
    GameObject SpawnCube()
    {
        GameObject go;
        if (cubePrefab != null)
        {
            go = Instantiate(cubePrefab);
        }
        else
        {
            go = Instantiate(gameObject);
            
            // 無限ループ・誤作動防止のために不要なスクリプトを除去
            WallBuilder3_UdonSharp script = go.GetComponent<WallBuilder3_UdonSharp>();
            if (script != null) Destroy(script);
            
            UdonBehaviour udon = go.GetComponent<UdonBehaviour>();
            if (udon != null) Destroy(udon);
        }
        return go;
    }

    // メッシュ・コライダーを外して空のコンテナを作成
    GameObject CreateContainer(string objectName)
    {
        GameObject go = SpawnCube();
        go.name = objectName;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) Destroy(mr);

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf != null) Destroy(mf);
        
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        return go;
    }

    // ==================== 壁の再構築 ====================
    public void BuildWall()
    {
        CleanUp();

        GameObject rootObj = CreateContainer("GeneratedWall");
        rootObj.transform.SetParent(transform, false);
        root = rootObj.transform;

        BuildWallWithDoors();
    }

    void CleanUp()
    {
        Transform old = transform.Find("GeneratedWall");
        if (old != null)
        {
            Destroy(old.gameObject);
        }
    }

    // ==================== 壁 + 複数ドア ====================
    void BuildWallWithDoors()
    {
        float halfWall = wallThickness * 0.5f;

        float[] rawStart = new float[3];
        float[] rawEnd = new float[3];
        float[] rawHeight = new float[3];
        int rawCount = 0;

        rawCount = CollectDoor(door1Enabled, door1Width, door1Offset, door1Height, rawStart, rawEnd, rawHeight, rawCount);
        rawCount = CollectDoor(door2Enabled, door2Width, door2Offset, door2Height, rawStart, rawEnd, rawHeight, rawCount);
        rawCount = CollectDoor(door3Enabled, door3Width, door3Offset, door3Height, rawStart, rawEnd, rawHeight, rawCount);

        for (int i = 0; i < rawCount - 1; i++)
        {
            for (int j = 0; j < rawCount - 1 - i; j++)
            {
                if (rawStart[j] > rawStart[j + 1])
                {
                    float ts = rawStart[j]; rawStart[j] = rawStart[j + 1]; rawStart[j + 1] = ts;
                    float te = rawEnd[j]; rawEnd[j] = rawEnd[j + 1]; rawEnd[j + 1] = te;
                    float th = rawHeight[j]; rawHeight[j] = rawHeight[j + 1]; rawHeight[j + 1] = th;
                }
            }
        }

        float[] mergedStart = new float[3];
        float[] mergedEnd = new float[3];
        float[] mergedHeight = new float[3];
        int mergedCount = 0;

        for (int i = 0; i < rawCount; i++)
        {
            if (mergedCount > 0 && rawStart[i] <= mergedEnd[mergedCount - 1])
            {
                if (rawEnd[i] > mergedEnd[mergedCount - 1]) mergedEnd[mergedCount - 1] = rawEnd[i];
                if (rawHeight[i] < mergedHeight[mergedCount - 1]) mergedHeight[mergedCount - 1] = rawHeight[i];
            }
            else
            {
                mergedStart[mergedCount] = rawStart[i];
                mergedEnd[mergedCount] = rawEnd[i];
                mergedHeight[mergedCount] = rawHeight[i];
                mergedCount++;
            }
        }

        float zMin = extendEndsHalfThickness ? -halfWall : 0f;
        float zMax = extendEndsHalfThickness ? wallLength + halfWall : wallLength;

        float cursor = zMin;
        for (int i = 0; i < mergedCount; i++)
        {
            float openStart = mergedStart[i];
            float openEnd = mergedEnd[i];
            float openHeight = mergedHeight[i];

            if (openStart > cursor)
            {
                float segLen = openStart - cursor;
                CreateWallSegment(
                    new Vector3(0f, wallHeightY / 2f, cursor + segLen / 2f),
                    new Vector3(wallThickness, wallHeightY, segLen));
            }

            float doorSpan = openEnd - openStart;

            if (doorSpan > 0f && openHeight < wallHeightY)
            {
                float topHeight = wallHeightY - openHeight;
                CreateWallSegment(
                    new Vector3(0f, openHeight + topHeight / 2f, (openStart + openEnd) / 2f),
                    new Vector3(wallThickness, topHeight, doorSpan));
            }

            if (doorSpan > 0f && generateDoorFrame)
            {
                CreateDoorFrame(openStart, openEnd, openHeight);
            }

            cursor = openEnd;
        }

        if (cursor < zMax)
        {
            float segLen = zMax - cursor;
            CreateWallSegment(
                new Vector3(0f, wallHeightY / 2f, cursor + segLen / 2f),
                new Vector3(wallThickness, wallHeightY, segLen));
        }
    }

    int CollectDoor(bool enabled, float width, float offset, float height,
        float[] outStart, float[] outEnd, float[] outHeight, int count)
    {
        if (!enabled || width <= 0f || count >= outStart.Length) return count;

        float center = wallLength / 2f + offset;
        float start = Mathf.Clamp(center - width / 2f, 0f, wallLength);
        float end = Mathf.Clamp(center + width / 2f, 0f, wallLength);
        if (end <= start) return count;

        outStart[count] = start;
        outEnd[count] = end;
        outHeight[count] = height;
        return count + 1;
    }

    void CreateWallSegment(Vector3 localPos, Vector3 scale)
    {
        GameObject wall = SpawnCube();
        wall.name = "WallSegment";
        wall.transform.SetParent(root, false);
        wall.transform.localScale = scale;
        wall.transform.localPosition = localPos;
        
        if (wallMaterial != null)
        {
            MeshRenderer mr = wall.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = wallMaterial;
        }
    }

    // ==================== ドアフレーム ====================
    void CreateDoorFrame(float doorStart, float doorEnd, float doorHeight)
    {
        float doorSpan = doorEnd - doorStart;
        float ft = doorFrameThickness;
        Material frameMat = doorFrameMaterial != null ? doorFrameMaterial : wallMaterial;

        GameObject frame = CreateContainer("DoorFrame");
        frame.transform.SetParent(root, false);

        float postHeight = doorHeight - 2f * ft;
        float postCenterY = ft + postHeight / 2f;

        // 下枠
        CreateFramePiece(frame.transform, frameMat,
            new Vector3(0f, ft / 2f, (doorStart + doorEnd) / 2f),
            new Vector3(wallThickness, ft, doorSpan));

        // 上枠
        CreateFramePiece(frame.transform, frameMat,
            new Vector3(0f, doorHeight - ft / 2f, (doorStart + doorEnd) / 2f),
            new Vector3(wallThickness, ft, doorSpan));

        // 左右の柱
        if (postHeight > 0f)
        {
            CreateFramePiece(frame.transform, frameMat,
                new Vector3(0f, postCenterY, doorStart + ft / 2f),
                new Vector3(wallThickness, postHeight, ft));

            CreateFramePiece(frame.transform, frameMat,
                new Vector3(0f, postCenterY, doorEnd - ft / 2f),
                new Vector3(wallThickness, postHeight, ft));
        }
    }

    void CreateFramePiece(Transform parent, Material sourceMat, Vector3 localPos, Vector3 scale)
    {
        GameObject piece = SpawnCube();
        piece.name = "FramePiece";
        piece.transform.SetParent(parent, false);
        piece.transform.localScale = scale;
        piece.transform.localPosition = localPos;
        
        if (sourceMat != null)
        {
            MeshRenderer mr = piece.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = sourceMat;
        }
    }

    // ==================== 外部操作API ====================
    public void SetDoor1(bool enabled, float width, float offset, float height)
    {
        door1Enabled = enabled; door1Width = width; door1Offset = offset; door1Height = height;
        BuildWall();
    }

    public void SetDoor2(bool enabled, float width, float offset, float height)
    {
        door2Enabled = enabled; door2Width = width; door2Offset = offset; door2Height = height;
        BuildWall();
    }

    public void SetDoor3(bool enabled, float width, float offset, float height)
    {
        door3Enabled = enabled; door3Width = width; door3Offset = offset; door3Height = height;
        BuildWall();
    }

    public void RemoveDoor1() { door1Enabled = false; BuildWall(); }
    public void RemoveDoor2() { door2Enabled = false; BuildWall(); }
    public void RemoveDoor3() { door3Enabled = false; BuildWall(); }

    public void SetWallLength(float length)
    {
        wallLength = length;
        BuildWall();
    }
}