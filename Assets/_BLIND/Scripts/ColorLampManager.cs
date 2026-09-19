
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 色ごとのランプの「旧→新」瞬間入れ替えを管理する。
//
// 色ごとに旧ランプ(見える位置)・新ランプ(隠れた位置)のペアを持ち、
// Start()時点でそれぞれの元位置を記憶しておく。
// ColorSignalButton から OnColorSelected(colorId) を呼ばれると、
// そのペアだけ位置を入れ替える(旧ランプは隠れ、新ランプが見える位置に出る
// = 光って見える)。一度入れ替えたペアはそのまま残る。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ColorLampManager : UdonSharpBehaviour
{
    [Header("赤 (0)")]
    [SerializeField] private Transform redOldLamp;
    [SerializeField] private Transform redNewLamp;

    [Header("青 (1)")]
    [SerializeField] private Transform blueOldLamp;
    [SerializeField] private Transform blueNewLamp;

    [Header("緑 (2)")]
    [SerializeField] private Transform greenOldLamp;
    [SerializeField] private Transform greenNewLamp;

    [UdonSynced] private int movedFlags;

    // Start()時点の元位置を記憶しておく(スワップを繰り返してもズレないように)
    private Vector3[] oldLampOrigin = new Vector3[3];
    private Vector3[] newLampOrigin = new Vector3[3];

    void Start()
    {
        for (int colorId = 0; colorId < 3; colorId++)
        {
            Transform oldLamp = GetOldLamp(colorId);
            Transform newLamp = GetNewLamp(colorId);

            if (oldLamp != null)
            {
                oldLampOrigin[colorId] = oldLamp.position;
            }

            if (newLamp != null)
            {
                newLampOrigin[colorId] = newLamp.position;
            }
        }

        ApplyState();
    }

    // ColorSignalButton から呼ばれる
    public void OnColorSelected(int colorId)
    {
        if (GetOldLamp(colorId) == null || GetNewLamp(colorId) == null)
        {
            Debug.LogWarning("ColorLampManager: colorIdが範囲外かランプ未設定です: " + colorId);
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

    private Transform GetOldLamp(int colorId)
    {
        switch (colorId)
        {
            case 0: return redOldLamp;
            case 1: return blueOldLamp;
            case 2: return greenOldLamp;
            default: return null;
        }
    }

    private Transform GetNewLamp(int colorId)
    {
        switch (colorId)
        {
            case 0: return redNewLamp;
            case 1: return blueNewLamp;
            case 2: return greenNewLamp;
            default: return null;
        }
    }

    private void ApplyState()
    {
        for (int colorId = 0; colorId < 3; colorId++)
        {
            Transform oldLamp = GetOldLamp(colorId);
            Transform newLamp = GetNewLamp(colorId);

            if (oldLamp == null || newLamp == null)
            {
                continue;
            }

            if (IsMoved(colorId))
            {
                oldLamp.position = newLampOrigin[colorId];
                newLamp.position = oldLampOrigin[colorId];
            }
            else
            {
                oldLamp.position = oldLampOrigin[colorId];
                newLamp.position = newLampOrigin[colorId];
            }
        }
    }
}
