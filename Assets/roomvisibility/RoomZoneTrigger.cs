using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 部屋の中に置くトリガー(Is Triggerを付けたCollider)用スクリプト。
/// 自分(ローカルプレイヤー)がこのトリガーに入ったら、visibleRoomsの部屋だけを表示する。
/// visibleRoomsには「今いる部屋 + 扉でつながっている部屋」を全部入れる。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoomZoneTrigger : UdonSharpBehaviour
{
    public RoomVisibilityManager manager;

    [Tooltip("このゾーンに入った時に表示する部屋(今の部屋 + 扉でつながっている部屋)")]
    public GameObject[] visibleRooms;

    [Tooltip("オンにすると、ゾーンに入った時にConsoleへログを出す(動作確認用)")]
    public bool debugLog = true;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;

        if (debugLog)
        {
            Debug.Log("[RoomZone] 入った: " + gameObject.name);
        }

        if (manager == null)
        {
            Debug.LogError("[RoomZone] managerが空です: " + gameObject.name);
            return;
        }

        if (visibleRooms == null || visibleRooms.Length == 0)
        {
            Debug.LogWarning("[RoomZone] visibleRoomsが空です: " + gameObject.name);
            return;
        }

        manager.ShowOnly(visibleRooms);
    }
}
