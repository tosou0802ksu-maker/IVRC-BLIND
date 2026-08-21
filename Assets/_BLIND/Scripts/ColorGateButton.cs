
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 赤 / 青 / 緑 のゲートボタン。
//
// 役割は2つ。
//   1. 押すと MultiButtonDoor に「このIDが押された」と通知する。
//      3個すべてが押されると room3 のシャッターが開く。
//   2. 押した地点を CheckpointManager の復帰地点にする。
//
// マップは room5 / room10 / room18 の3方向に分岐していて、
// どの色から回っても良い設計なので、セーブ地点は「番号が大きい方」ではなく
// 「最後に押したボタン」になる (CheckpointManager.SetCheckpointDirect)。
//
// 見た目について:
//   本体は Default(0) レイヤーなので過去の人にしか見えない。
//   サーモ役・エコロケ役にも見せるため、Thermal(22) / Echo(23) の
//   コピーを子として重ねてある (BlindGimmickBuilder が生成)。
//   Interact 自体はレイヤーに関係なく全員が実行できる。
public class ColorGateButton : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private CheckpointManager checkpointManager;
    [SerializeField] private MultiButtonDoor doorManager;

    [Header("このボタンの番号 (赤=0, 青=1, 緑=2)")]
    [SerializeField] private int buttonId;

    [Header("復帰地点の番号 (0はスタート地点なので 1以上)")]
    [SerializeField] private int checkpointIndex = 1;

    [Header("押した後に点灯させる物(任意)")]
    [SerializeField] private GameObject[] litVisuals;

    [Header("効果音(任意)")]
    [SerializeField] private AudioSource pressSound;

    private bool pressed;

    void Start()
    {
        ApplyLit(IsPressedOnNetwork());
    }

    // 他人が押した時にも見た目を合わせる。
    // MultiButtonDoor 側の pressedFlags は同期されているので、
    // それを見に行くだけで途中参加でもズレない。
    void Update()
    {
        if (pressed)
        {
            return;
        }

        if (IsPressedOnNetwork())
        {
            pressed = true;
            ApplyLit(true);
        }
    }

    public override void Interact()
    {
        if (pressed)
        {
            return;
        }

        pressed = true;

        if (doorManager != null)
        {
            doorManager.OnButtonPressed(buttonId);
        }

        if (checkpointManager != null)
        {
            checkpointManager.SetCheckpointDirect(checkpointIndex);
        }

        ApplyLit(true);

        if (pressSound != null)
        {
            pressSound.Play();
        }
    }

    private bool IsPressedOnNetwork()
    {
        if (doorManager == null)
        {
            return false;
        }

        return doorManager.IsButtonPressed(buttonId);
    }

    private void ApplyLit(bool lit)
    {
        if (litVisuals == null)
        {
            return;
        }

        for (int i = 0; i < litVisuals.Length; i++)
        {
            if (litVisuals[i] != null)
            {
                litVisuals[i].SetActive(lit);
            }
        }
    }
}
