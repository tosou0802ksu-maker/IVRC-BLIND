using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 壁1枚(入り口を最大3つ)を生成する UdonSharp 対応版。
///
/// 元の RoomBuilder3 と違う点(UdonSharpの制約に合わせたもの):
/// ・MonoBehaviour ではなく UdonSharpBehaviour を継承
/// ・List&lt;T&gt; / Dictionary&lt;K,V&gt; / LINQ は使わず、配列とforループだけで実装
///   (Udonは配列[]コレクションのみサポートで、ジェネリックコレクションやLINQは非対応)
/// ・DoorConfigのようなネストしたクラスは使わず、door1〜door3の項目をフラットな
///   public フィールドに展開(Udonの独自クラスのシリアライズ絡みの不確実性を避けるため)
/// ・OnValidate や UnityEditor 名前空間、DestroyImmediate は削除
///   (Udonはエディタの編集モードでは動作せず、Playモード/VRChat実行時にのみ動くため、
///   「Inspectorを触ると即座に組み直る」という元のプレビュー機構はそもそも成立しません。
///   壁は Start() で1回組み立てられ、以後は BuildWall() を明示的に呼んだ時だけ組み直します)
/// ・DestroyImmediate → Destroy に変更(ランタイムでのみ動作するため)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WallBuilder3_UdonSharp : UdonSharpBehaviour
{
    // 壁は Z方向(横)に伸び、厚みはX方向、高さはY方向。

    [Header("壁のサイズ")]
    public float wallLength = 6f;
    public float wallHeightY = 2.5f;
    public float wallThickness = 0.2f;

    [Tooltip("オンの場合、壁の両端を厚みの半分だけ外側(コーナー側)に伸ばします。" +
             "部屋の角に組み込む壁ではオンのままにしてください。" +
             "単独で置く壁ではオフにすると wallLength ぴったりの長さになります。")]
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

    [Header("マテリアル(任意)")]
    public Material wallMaterial;

    [Header("ドアフレーム設定")]
    public float doorFrameThickness = 0.05f;
    public Material doorFrameMaterial;
    public bool generateDoorFrame = true;

    [Header("レンダリング設定")]
    [Tooltip("Unlitシェーダーを使用（ライティングの影響を受けず、継ぎ目のアーティファクトを防止）")]
    public bool useUnlitShader = false;

    private Transform root;

    // Unlit用マテリアルのインスタンスキャッシュ(Dictionary非対応のため、
    // 使用する元マテリアルが最大2種類(壁用・フレーム用)である前提で個別フィールドに保持)
    private Material unlitWallMaterial;
    private Material unlitFrameMaterial;

    void Start()
    {
        BuildWall();
    }

    // ==================== 壁の再構築 ====================
    // Inspectorの値を変えても自動では組み直りません(Udonは編集モードで動かないため)。
    // Playモード/VRChat実行中に値を変えたら、この BuildWall() を呼び直してください。
    public void BuildWall()
    {
        CleanUp();

        GameObject rootObj = new GameObject("GeneratedWall");
        rootObj.transform.SetParent(transform, false);
        root = rootObj.transform;

        BuildWallWithDoors();
    }

    // 既存の生成物と、生成しておいたUnlitマテリアルインスタンスを破棄
    void CleanUp()
    {
        Transform old = transform.Find("GeneratedWall");
        if (old != null)
        {
            MeshFilter[] mfs = old.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < mfs.Length; i++)
            {
                if (mfs[i].sharedMesh != null)
                {
                    Destroy(mfs[i].sharedMesh);
                }
            }
            Destroy(old.gameObject);
        }

        if (unlitWallMaterial != null)
        {
            Destroy(unlitWallMaterial);
            unlitWallMaterial = null;
        }
        if (unlitFrameMaterial != null)
        {
            Destroy(unlitFrameMaterial);
            unlitFrameMaterial = null;
        }
    }

    // ==================== 壁 + 複数ドア ====================

    void BuildWallWithDoors()
    {
        float halfWall = wallThickness * 0.5f;

        // 有効な入り口を配列に集める(最大3件)
        float[] rawStart = new float[3];
        float[] rawEnd = new float[3];
        float[] rawHeight = new float[3];
        int rawCount = 0;

        rawCount = CollectDoor(door1Enabled, door1Width, door1Offset, door1Height,
            rawStart, rawEnd, rawHeight, rawCount);
        rawCount = CollectDoor(door2Enabled, door2Width, door2Offset, door2Height,
            rawStart, rawEnd, rawHeight, rawCount);
        rawCount = CollectDoor(door3Enabled, door3Width, door3Offset, door3Height,
            rawStart, rawEnd, rawHeight, rawCount);

        // start位置で昇順ソート(要素数が最大3なので単純なバブルソートで十分)
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

        // 重なっている(または隣接している)区間は1つの開口にまとめる。
        // まとめた開口のまぐさの高さは、重なった中で最も低いdoorHeightに合わせる。
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

        // 壁の両端(部屋の角に合わせて伸ばすかどうか)
        float zMin = extendEndsHalfThickness ? -halfWall : 0f;
        float zMax = extendEndsHalfThickness ? wallLength + halfWall : wallLength;

        float cursor = zMin;
        for (int i = 0; i < mergedCount; i++)
        {
            float openStart = mergedStart[i];
            float openEnd = mergedEnd[i];
            float openHeight = mergedHeight[i];

            // 開口の手前までを壁として埋める
            if (openStart > cursor)
            {
                float segLen = openStart - cursor;
                CreateWallSegment(
                    new Vector3(0f, wallHeightY / 2f, cursor + segLen / 2f),
                    new Vector3(wallThickness, wallHeightY, segLen));
            }

            float doorSpan = openEnd - openStart;

            // まぐさ(入り口の上に残る壁)
            if (doorSpan > 0f && openHeight < wallHeightY)
            {
                float topHeight = wallHeightY - openHeight;
                CreateWallSegment(
                    new Vector3(0f, openHeight + topHeight / 2f, (openStart + openEnd) / 2f),
                    new Vector3(wallThickness, topHeight, doorSpan));
            }

            // ドアフレーム
            if (doorSpan > 0f && generateDoorFrame)
            {
                CreateDoorFrame(openStart, openEnd, openHeight);
            }

            cursor = openEnd;
        }

        // 最後の開口から壁の端まで(開口が1つも無ければ、これで壁全体が1枚できる)
        if (cursor < zMax)
        {
            float segLen = zMax - cursor;
            CreateWallSegment(
                new Vector3(0f, wallHeightY / 2f, cursor + segLen / 2f),
                new Vector3(wallThickness, wallHeightY, segLen));
        }
    }

    // 入り口1件分を区間(start, end)に変換して配列に積む。積んだ後の件数を返す
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
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallSegment";
        wall.transform.SetParent(root, false);
        wall.transform.localScale = scale;
        wall.transform.localPosition = localPos;
        ApplyMaterial(wall, wallMaterial, scale, localPos);
    }

    // ==================== ドアフレーム ====================
    // 「下枠 → 柱 → 上枠」を縦に積んで互いに重ならないようにしている。

    void CreateDoorFrame(float doorStart, float doorEnd, float doorHeight)
    {
        float doorSpan = doorEnd - doorStart;
        float ft = doorFrameThickness;
        Material frameMat = doorFrameMaterial != null ? doorFrameMaterial : wallMaterial;

        GameObject frame = new GameObject("DoorFrame");
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
    // ワールド座標ベースでUVを直接書き換え、キャッシュ済みマテリアルを適用。
    // マテリアルは複製せずアセットをそのまま共有する(useUnlitShaderがオフの場合)。

    void ApplyMaterial(GameObject obj, Material sourceMat, Vector3 scale, Vector3 localPos)
    {
        if (sourceMat == null) return;

        SetWorldSpaceUVs(obj, scale, localPos);
        obj.GetComponent<Renderer>().sharedMaterial = GetCachedMaterial(sourceMat);
    }

    // Dictionary非対応のため、壁用/フレーム用の最大2種類の元マテリアルを
    // 参照の一致で判定して個別フィールドにキャッシュする
    Material GetCachedMaterial(Material sourceMat)
    {
        if (!useUnlitShader) return sourceMat;

        if (sourceMat == wallMaterial)
        {
            if (unlitWallMaterial == null)
            {
                unlitWallMaterial = new Material(Shader.Find("Unlit/Texture"));
                if (sourceMat.mainTexture != null) unlitWallMaterial.mainTexture = sourceMat.mainTexture;
            }
            return unlitWallMaterial;
        }
        else
        {
            if (unlitFrameMaterial == null)
            {
                unlitFrameMaterial = new Material(Shader.Find("Unlit/Texture"));
                if (sourceMat.mainTexture != null) unlitFrameMaterial.mainTexture = sourceMat.mainTexture;
            }
            return unlitFrameMaterial;
        }
    }

    // Cubeメッシュの全頂点UVをワールド座標(U=Y, V=Z)で設定
    void SetWorldSpaceUVs(GameObject obj, Vector3 scale, Vector3 localPos)
    {
        MeshFilter mf = obj.GetComponent<MeshFilter>();
        Mesh mesh = mf.mesh; // コピーが作られる。破棄はCleanUpで行う
        Vector3[] verts = mesh.vertices;
        Vector2[] uvs = new Vector2[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            float wy = localPos.y + verts[i].y * scale.y;
            float wz = localPos.z + verts[i].z * scale.z;
            uvs[i] = new Vector2(wy, wz);
        }

        mesh.uv = uvs;
    }

    // ==================== 他スクリプトから呼べる操作API ====================

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