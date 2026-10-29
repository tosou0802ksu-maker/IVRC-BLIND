using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 2枚式感圧板の個別プレート。乗っている人数を数えて DualPressurePlateDoor に知らせる。
/// BoxCollider の Is Trigger を ON にすること（OFF だと OnPlayerTriggerEnter が来ない）。
///
/// プレイヤーの出入りは、自分以外のプレイヤーについても各クライアントで起きる。
/// なので同期はせず、各クライアントが自分の画面で数える（扉の開閉も各自の画面で行う）。
/// 「乗っている/いない」の1つの印ではなく人数を数えるのは、交代で次の人が乗ってから
/// 前の人が降りたときに、まだ人がいるのに「降りた」扱いにしないため。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DualPressurePlate : UdonSharpBehaviour
{
    [Header("接続先マネージャー")]
    [SerializeField] private DualPressurePlateDoor doorManager;

    [Header("この感圧板のID(0 または 1)")]
    [SerializeField] private int plateId;

    // いま乗っている人の playerId
    private int[] insideIds = new int[16];
    private int insideCount;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;

        int id = player.playerId;
        if (IndexOf(id) >= 0) return;
        if (insideCount >= insideIds.Length) return;

        insideIds[insideCount] = id;
        insideCount++;
        Notify();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null) return;
        Remove(player.playerId);
    }

    // 乗ったまま抜けた人は OnPlayerTriggerExit が来ないことがあるので、ここでも外す
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null) return;
        Remove(player.playerId);
    }

    private void Remove(int id)
    {
        int i = IndexOf(id);
        if (i < 0) return;

        insideCount--;
        insideIds[i] = insideIds[insideCount];
        Notify();
    }

    private int IndexOf(int id)
    {
        for (int i = 0; i < insideCount; i++)
        {
            if (insideIds[i] == id) return i;
        }
        return -1;
    }

    private void Notify()
    {
        if (doorManager == null) return;
        doorManager.SetPlateOccupied(plateId, insideCount > 0);
    }
}
