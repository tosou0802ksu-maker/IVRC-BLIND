
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// 押したら「間違い」になるボタン。ボタンにこれ1つを付けるだけで動く。
//
// 仕様:
//   ・誰かが Interact → 全員に間違いの効果音 → 少し待って全員をテレポート
//   ・戻る先は Respawn Point。空ならセーブ地点（CheckpointManager）
//   ・待っている間の連打は無視する
//
// ⚠️ テレポートを遅らせているのは、音がボタンの位置から鳴るため。
//    鳴った瞬間に飛ばすと音源から離れてしまい、効果音が聞こえない。
//
// ⚠️ 同期モードを None にしないこと。None だと SendCustomNetworkEvent が届かず、
//    押した本人しか戻らない。変数は同期しないので NoVariableSync にしている。
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class WrongButton : UdonSharpBehaviour
{
    [Header("間違いの効果音")]
    [SerializeField] private AudioClip wrongClip;
    [Tooltip("鳴らすスピーカー(任意)。空ならボタンの位置でそのまま鳴らす。")]
    [SerializeField] private AudioSource wrongSound;

    [Header("戻る先。Respawn Point が空ならセーブ地点へ戻る")]
    [SerializeField] private CheckpointManager checkpointManager;
    [Tooltip("入れた時だけ、セーブ地点ではなくこの場所へ戻す（位置と向きを使う）。")]
    [SerializeField] private Transform respawnPoint;

    [Header("効果音からテレポートまでの待ち時間（秒）")]
    [SerializeField] private float teleportDelay = 0.8f;

    private bool busy;

    public override void Interact()
    {
        if (busy) return;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnWrong));
    }

    public void OnWrong()
    {
        if (busy) return;
        busy = true;

        if (wrongClip != null)
        {
            if (wrongSound != null)
            {
                wrongSound.PlayOneShot(wrongClip);
            }
            else
            {
                AudioSource.PlayClipAtPoint(wrongClip, transform.position);
            }
        }

        SendCustomEventDelayedSeconds(nameof(RespawnLocal), teleportDelay);
    }

    public void RespawnLocal()
    {
        busy = false;

        if (respawnPoint == null)
        {
            // OnWrong は全員のクライアントで動いているので、ここでは自分だけ戻せばよい。
            // TriggerDeath を呼ぶと3人ぶん合図が飛んで3回戻されるので使わない。
            if (checkpointManager != null)
            {
                checkpointManager.RespawnAll();
            }
            return;
        }

        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        local.TeleportTo(respawnPoint.position, respawnPoint.rotation);
    }
}
