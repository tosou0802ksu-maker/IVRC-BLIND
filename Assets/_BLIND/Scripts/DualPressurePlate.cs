using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 2枚式感圧板の個別プレート。プレイヤーが乗った/降りたをDualPressurePlateDoorに通知する。
/// BoxColliderのIs TriggerをONにすること。
/// </summary>
public class DualPressurePlate : UdonSharpBehaviour
{
    [Header("接続先マネージャー")]
    [SerializeField] private DualPressurePlateDoor doorManager;

    [Header("この感圧板のID(0 または 1)")]
    [SerializeField] private int plateId;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.isLocal) return;
        if (doorManager == null) return;

        doorManager.OnPlateEnter(plateId);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.isLocal) return;
        if (doorManager == null) return;

        doorManager.OnPlateExit(plateId);
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.isLocal) return;
        if (doorManager == null) return;

        doorManager.OnPlateExit(plateId);
    }
}
