
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 赤/青/緑のボタンで開閉する扉。
//
// 計画書の「赤開くドア」「赤閉じるドア」に対応する。
// ボタンを押すとルートが組み替わり、最短ルートが塞がって迂回が必要になる、
// という導線制御に使う。
//
// 状態はCheckpointManagerのcurrentIndex(同期済み)から導出するので、
// 途中参加のプレイヤーでも扉の状態がズレない。
//   openWhenReached = true  → requiredCheckpoint に到達したら「開く」ドア
//   openWhenReached = false → requiredCheckpoint に到達したら「閉じる」ドア
public class ToggleDoor : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("このドアが反応するセーブポイント番号(赤=1, 青=2, 緑=3)")]
    [SerializeField] private int requiredCheckpoint = 1;

    [Header("到達時に開くドアなら true / 閉じるドアなら false")]
    [SerializeField] private bool openWhenReached = true;

    [Header("扉の実体(閉じている時に表示・当たり判定を持つもの)")]
    [SerializeField] private GameObject doorBody;

    [Header("開閉音(任意)")]
    [SerializeField] private AudioSource moveSound;

    private bool lastOpenState;
    private bool initialized;

    void Start()
    {
        ApplyState();
    }

    // CheckpointManager から呼ばれる
    public void ApplyState()
    {
        bool reached = false;
        if (checkpointManager != null)
        {
            reached = checkpointManager.currentIndex >= requiredCheckpoint;
        }

        // 到達前は openWhenReached の逆の状態でいる
        bool isOpen = reached ? openWhenReached : !openWhenReached;

        if (initialized && isOpen == lastOpenState)
        {
            return;
        }

        if (doorBody != null)
        {
            doorBody.SetActive(!isOpen);
        }

        if (initialized && moveSound != null)
        {
            moveSound.Play();
        }

        lastOpenState = isOpen;
        initialized = true;
    }
}
