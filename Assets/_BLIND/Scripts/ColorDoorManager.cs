
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
//
// allSelectedClip(任意): 全色そろった瞬間から allSelectedDelay 秒後に、全員の耳元で1回だけ鳴らす。
// 途中参加の人には鳴らさない。
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

    [Header("全色そろった時のSE(任意)")]
    [SerializeField] private AudioClip allSelectedClip;
    [Tooltip("全色そろってから鳴らすまでの秒数")]
    [SerializeField] private float allSelectedDelay = 1.0f;
    [Tooltip("鳴らすスピーカー(任意)。空なら各プレイヤーの耳元で鳴らす（どこにいても同じ音量）。")]
    [SerializeField] private AudioSource allSelectedSpeaker;

    [UdonSynced] private int movedFlags;

    // 全色そろった SE を、このクライアントで鳴らした(または鳴らさないと決めた)か
    private bool allSelectedHandled;
    private bool receivedOnce;

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
        // 途中参加で、最初に受け取った時点ですでに全色そろっていたら SE は鳴らさない
        if (!receivedOnce)
        {
            receivedOnce = true;
            if (AreAllSelected()) allSelectedHandled = true;
        }
        ApplyState();
    }

    // 全員のクライアントで、同期値から「そろった瞬間」を見つけて1回だけ予約する。
    // ネットワークイベントを使わないので、音が二重に鳴らない。
    private void CheckAllSelectedSound()
    {
        if (allSelectedHandled || !AreAllSelected()) return;
        allSelectedHandled = true;
        if (allSelectedClip == null) return;
        SendCustomEventDelayedSeconds(nameof(PlayAllSelectedSound), allSelectedDelay);
    }

    public void PlayAllSelectedSound()
    {
        if (allSelectedClip == null) return;

        if (allSelectedSpeaker != null)
        {
            allSelectedSpeaker.spatialBlend = 0f;   // 距離で小さくならない音にする
            allSelectedSpeaker.PlayOneShot(allSelectedClip);
            return;
        }

        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        AudioSource.PlayClipAtPoint(allSelectedClip, local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position);
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

        CheckAllSelectedSound();
    }
}
