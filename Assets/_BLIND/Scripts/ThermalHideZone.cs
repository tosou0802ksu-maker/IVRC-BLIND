
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// サーマル視覚で味方プロキシが見えなくなるゾーン。
//
// 仕様:
//   ・このオブジェクトのTransform.positionを中心、zoneSizeで範囲を定義
//   ・ゾーン内にいるリモートプレイヤーのサーマル表示を非表示にする
//   ・Echo/Memory役には影響しない(サーマルのRendererだけを制御)
//
// RemotePlayerProxyManager の thermalHideZones 配列にこのオブジェクトを登録して使う。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ThermalHideZone : UdonSharpBehaviour
{
    [Header("ゾーンの大きさ(このオブジェクトの位置が中心)")]
    [SerializeField] private Vector3 zoneSize = new Vector3(10f, 5f, 10f);

    // 指定座標がゾーン内にいるか判定
    public bool IsInside(Vector3 position)
    {
        Vector3 center = transform.position;
        Vector3 half = zoneSize * 0.5f;

        return position.x >= center.x - half.x && position.x <= center.x + half.x
            && position.y >= center.y - half.y && position.y <= center.y + half.y
            && position.z >= center.z - half.z && position.z <= center.z + half.z;
    }

    // エディタ上でゾーン範囲を可視化
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawCube(transform.position, zoneSize);
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        Gizmos.DrawWireCube(transform.position, zoneSize);
    }
}
