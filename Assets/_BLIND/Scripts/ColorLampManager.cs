
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 色ごとのランプ群の瞬間移動を管理する。
//
// 1色につき複数のランプを割り当てられるように、
// lamps / targetPositions / lampColorIds を同じ並びの並列配列として持つ
// (LampPuzzleManagerのlamps[]+correctOrder[]と同じ考え方)。
//
// ColorSignalButton から OnColorSelected(colorId) を呼ばれると、
// その色に属するランプだけを対応する座標へ瞬間移動させる。
// 一度移動したランプはそのまま残る(他の色が選ばれても戻らない)。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ColorLampManager : UdonSharpBehaviour
{
    [Header("ランプ一覧 (1色に複数可)")]
    [SerializeField] private Transform[] lamps;

    [Header("各ランプの移動先座標 (lampsと同じ並び)")]
    [SerializeField] private Vector3[] targetPositions;

    [Header("各ランプが属する色 (lampsと同じ並び。赤=0, 青=1, 緑=2)")]
    [SerializeField] private int[] lampColorIds;

    [UdonSynced] private int movedFlags;

    void Start()
    {
        ApplyState();
    }

    // ColorSignalButton から呼ばれる
    public void OnColorSelected(int colorId)
    {
        if (colorId < 0 || colorId > 31)
        {
            Debug.LogWarning("ColorLampManager: colorIdが範囲外です: " + colorId);
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
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null || i >= targetPositions.Length || i >= lampColorIds.Length)
            {
                continue;
            }

            if (IsMoved(lampColorIds[i]))
            {
                lamps[i].position = targetPositions[i];
            }
        }
    }
}
