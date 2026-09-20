
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 色ごとの扉の瞬間移動を管理する。
//
// ColorSignalButton から OnColorSelected(colorId) を呼ばれると、
// その色に対応する扉だけを対応する座標へ瞬間移動させる。
// 一度移動した扉はそのまま残る(他の色が選ばれても戻らない)。
//
// finalDoor(任意): 赤/青/緑の3色すべてが選ばれたときだけ、
// 別枠で座標移動する第四の扉。一度開いたら閉じない。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ColorDoorManager : UdonSharpBehaviour
{
    [Header("赤 (0)")]
    [SerializeField] private Transform redDoor;
    [SerializeField] private Vector3 redTargetPosition;

    [Header("青 (1)")]
    [SerializeField] private Transform blueDoor;
    [SerializeField] private Vector3 blueTargetPosition;

    [Header("緑 (2)")]
    [SerializeField] private Transform greenDoor;
    [SerializeField] private Vector3 greenTargetPosition;

    [Header("全色そろった時に開く扉(任意)")]
    [SerializeField] private Transform finalDoor;
    [SerializeField] private Vector3 finalTargetPosition;

    [UdonSynced] private int movedFlags;

    void Start()
    {
        ApplyState();
    }

    // ColorSignalButton から呼ばれる
    public void OnColorSelected(int colorId)
    {
        if (GetDoor(colorId) == null)
        {
            Debug.LogWarning("ColorDoorManager: colorIdが範囲外か扉未設定です: " + colorId);
            return;
        }

        if (IsMoved(colorId))
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        movedFlags |= (1 << colorId);
        RequestSerialization();
        ApplyState();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    private bool IsMoved(int colorId)
    {
        return (movedFlags & (1 << colorId)) != 0;
    }

    private bool AreAllSelected()
    {
        return (movedFlags & 0b111) == 0b111;
    }

    private Transform GetDoor(int colorId)
    {
        switch (colorId)
        {
            case 0: return redDoor;
            case 1: return blueDoor;
            case 2: return greenDoor;
            default: return null;
        }
    }

    private Vector3 GetTargetPosition(int colorId)
    {
        switch (colorId)
        {
            case 0: return redTargetPosition;
            case 1: return blueTargetPosition;
            case 2: return greenTargetPosition;
            default: return Vector3.zero;
        }
    }

    private void ApplyState()
    {
        for (int colorId = 0; colorId < 3; colorId++)
        {
            if (!IsMoved(colorId))
            {
                continue;
            }

            Transform door = GetDoor(colorId);
            if (door != null)
            {
                door.position = GetTargetPosition(colorId);
            }
        }

        if (finalDoor != null && AreAllSelected())
        {
            finalDoor.position = finalTargetPosition;
        }
    }
}
