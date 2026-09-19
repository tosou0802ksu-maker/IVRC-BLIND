
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 色ごとの扉の瞬間移動を管理する。
//
// ColorSignalButton から OnColorSelected(colorId) を呼ばれると、
// その色に対応する扉だけを targetPositions の座標へ瞬間移動させる。
// 一度移動した扉はそのまま残る(他の色が選ばれても戻らない)。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ColorDoorManager : UdonSharpBehaviour
{
    [Header("色ごとの扉 (index = 色番号: 赤=0, 青=1, 緑=2)")]
    [SerializeField] private Transform[] doors;

    [Header("色ごとの移動先座標 (doorsと同じ並び)")]
    [SerializeField] private Vector3[] targetPositions;

    [UdonSynced] private int movedFlags;

    void Start()
    {
        ApplyState();
    }

    // ColorSignalButton から呼ばれる
    public void OnColorSelected(int colorId)
    {
        if (colorId < 0 || colorId >= doors.Length || colorId >= targetPositions.Length)
        {
            Debug.LogWarning("ColorDoorManager: colorIdが範囲外です: " + colorId);
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

    private void ApplyState()
    {
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null || i >= targetPositions.Length)
            {
                continue;
            }

            if (IsMoved(i))
            {
                doors[i].position = targetPositions[i];
            }
        }
    }
}
