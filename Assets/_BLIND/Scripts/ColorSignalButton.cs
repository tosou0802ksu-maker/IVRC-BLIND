
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// 赤/青/緑のいずれかを表す通知ボタン。
//
// 押すと ColorDoorManager と ColorLampManager の両方へ「この色が選ばれた」と
// 直接通知する。実際の移動や同期は各マネージャー側が担当するので、
// このスクリプトはただの送信機。
//
// 効果音は全プレイヤーに聞こえるように SendCustomNetworkEvent で鳴らす。
public class ColorSignalButton : UdonSharpBehaviour
{
    [Header("色 (赤=0, 青=1, 緑=2)")]
    [SerializeField] private int colorId;

    [Header("接続先")]
    [SerializeField] private ColorDoorManager doorManager;
    [SerializeField] private ColorLampManager lampManager;

    [Header("セーブ地点。押すと 色番号+1 番(赤=CP_1 / 青=CP_2 / 緑=CP_3)が復帰地点になる")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip pressClip;
    [Tooltip("鳴らすスピーカー(任意)。空ならボタンの位置でそのまま鳴らす。")]
    [SerializeField] private AudioSource pressSound;

    private bool pressed;

    public override void Interact()
    {
        if (pressed)
        {
            return;
        }

        pressed = true;

        if (doorManager != null)
        {
            doorManager.OnColorSelected(colorId);
        }

        if (lampManager != null)
        {
            lampManager.OnColorSelected(colorId);
        }

        // checkpoints[0] はスタート地点なので +1 する
        if (checkpointManager != null)
        {
            checkpointManager.SetCheckpointDirect(colorId + 1);
        }

        if (pressClip != null || pressSound != null)
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlayPressSoundGlobal));
        }
    }

    // ネットワーク越しに全員のクライアントで実行される
    public void PlayPressSoundGlobal()
    {
        if (pressClip == null)
        {
            // 以前の設定(AudioSource に音を入れてある)もそのまま鳴らす
            if (pressSound != null)
            {
                pressSound.Play();
            }
            return;
        }

        if (pressSound != null)
        {
            pressSound.PlayOneShot(pressClip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(pressClip, transform.position);
        }
    }
}
