using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// ドア選択式クイズの「くぐった時の判定」担当。扉の先に置いた Trigger に付ける。
//
// 仕様:
//   ・正解の扉 → くぐったら全員に正解音を1回だけ鳴らし、扉を開きっぱなしにする
//   ・不正解の扉 → くぐったら全員に不正解音 → 少し待って全員セーブ地点へ → 扉を閉じる
//   ・3人が続けてくぐっても、音やテレポートは重ならない
//
// 扉の移動は DoorQuizManager の担当。ここは判定と音と戻すことだけ。
//
// ⚠️ GameObject に IsTrigger を有効にした Collider を付けること。
// ⚠️ 同期モードを None にしないこと。None だと SendCustomNetworkEvent が届かない。
// ⚠️ 戻すときに TriggerDeath を呼ばないこと。OnWrong 自体が全員で動いているので、
//    3人ぶん合図が飛んで3回戻される。自分だけ戻す RespawnAll を直接呼ぶ。
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class DoorPassJudge : UdonSharpBehaviour
{
    [Header("この扉は正解か")]
    [SerializeField] private bool isCorrect;

    [Header("扉。不正解のとき閉じる／正解のとき開きっぱなしにする")]
    [SerializeField] private DoorQuizManager doorQuizManager;
    [Tooltip("DoorQuizChoice の Door Index と同じ番号(0, 1, 2)")]
    [SerializeField] private int doorIndex;

    [Header("効果音")]
    [SerializeField] private AudioClip correctClip;
    [SerializeField] private AudioClip wrongClip;
    [Tooltip("鳴らすスピーカー(任意)。空ならこの箱の位置でそのまま鳴らす。")]
    [SerializeField] private AudioSource speaker;

    [Header("不正解の戻り先")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("不正解音からテレポートまでの待ち時間（秒）")]
    [SerializeField] private float wrongDelay = 0.8f;

    private bool correctPlayed;
    private bool wrongPending;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;

        if (isCorrect)
        {
            if (correctPlayed) return;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnCorrect));
            if (doorQuizManager != null) doorQuizManager.MarkSolved(doorIndex);
            return;
        }

        if (wrongPending) return;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnWrong));
        // 扉を閉じるのはくぐった本人だけ（全員で閉じると所有権の取り合いになる）
        SendCustomEventDelayedSeconds(nameof(CloseMyDoor), wrongDelay);
    }

    public void OnCorrect()
    {
        if (correctPlayed) return;
        correctPlayed = true;
        PlaySound(correctClip);
    }

    public void OnWrong()
    {
        if (wrongPending) return;
        wrongPending = true;

        PlaySound(wrongClip);
        SendCustomEventDelayedSeconds(nameof(RespawnAfterWrong), wrongDelay);
    }

    public void RespawnAfterWrong()
    {
        wrongPending = false;

        if (checkpointManager != null)
        {
            checkpointManager.RespawnAll();
        }
    }

    public void CloseMyDoor()
    {
        if (doorQuizManager != null)
        {
            doorQuizManager.CloseDoor(doorIndex);
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;

        if (speaker != null)
        {
            speaker.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
