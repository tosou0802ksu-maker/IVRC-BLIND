
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// ドア選択式クイズ(1問分)。
//
// 仕様:
//   ・3枚のドアのうち1枚が正解
//   ・正解ドアをInteract → ドアが指定座標へ瞬間移動(開く)
//   ・不正解ドアをInteract → 全員チェックポイントへ戻される
//   ・正解するまで何度でも挑戦可能
//   ・3セット配置しても各インスタンスが独立しているため混線しない
//
// ドアの閉じた位置は Start() で自動記憶する。
// 開いた位置は doorOpenPositions で Inspector から指定する。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DoorQuizManager : UdonSharpBehaviour
{
    [Header("正解のドア番号(0始まり)")]
    [SerializeField] private int correctDoorIndex;

    [Header("ドア(3枚)")]
    [SerializeField] private Transform door0;
    [SerializeField] private Transform door1;
    [SerializeField] private Transform door2;

    [Header("ドアの開いた位置(ローカル座標・3つ分・正解ドアのみ使用)")]
    [SerializeField] private Vector3 door0OpenPosition;
    [SerializeField] private Vector3 door1OpenPosition;
    [SerializeField] private Vector3 door2OpenPosition;

    [Header("不正解時の復帰先")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("音声(任意)")]
    [SerializeField] private AudioSource correctSound;
    [SerializeField] private AudioSource wrongSound;

    // 0 = 未回答 / 1 = 正解済み
    [UdonSynced] private int solvedState;

    // 閉じた位置を自動記憶
    private Vector3 door0ClosedPosition;
    private Vector3 door1ClosedPosition;
    private Vector3 door2ClosedPosition;

    void Start()
    {
        if (door0 != null) door0ClosedPosition = door0.localPosition;
        if (door1 != null) door1ClosedPosition = door1.localPosition;
        if (door2 != null) door2ClosedPosition = door2.localPosition;

        ApplyState();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    // DoorQuizChoice から呼ばれる
    public void SubmitChoice(int doorIndex)
    {
        if (solvedState != 0) return;

        if (doorIndex == correctDoorIndex)
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            solvedState = 1;
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnCorrect));
        }
        else
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnWrong));
        }
    }

    public void OnCorrect()
    {
        ApplyState();

        if (correctSound != null)
        {
            correctSound.Play();
        }
    }

    public void OnWrong()
    {
        if (wrongSound != null)
        {
            wrongSound.Play();
        }

        if (checkpointManager != null)
        {
            checkpointManager.TriggerDeath();
        }
    }

    private void ApplyState()
    {
        if (solvedState == 1)
        {
            // 正解ドアだけを開いた位置へ移動
            MoveDoor(correctDoorIndex, true);
        }
        else
        {
            // 全ドアを閉じた位置に戻す
            MoveDoor(0, false);
            MoveDoor(1, false);
            MoveDoor(2, false);
        }
    }

    private void MoveDoor(int index, bool open)
    {
        switch (index)
        {
            case 0:
                if (door0 != null) door0.localPosition = open ? door0OpenPosition : door0ClosedPosition;
                break;
            case 1:
                if (door1 != null) door1.localPosition = open ? door1OpenPosition : door1ClosedPosition;
                break;
            case 2:
                if (door2 != null) door2.localPosition = open ? door2OpenPosition : door2ClosedPosition;
                break;
        }
    }
}
