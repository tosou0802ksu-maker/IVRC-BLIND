using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// レーザー部屋（room13）のレーザーを、ボタンで全員分まとめて止める。
//
// なぜ必要か:
//   部屋の突き当たりに青いボタンがあるが、色扉の通知にしかつながっておらず、
//   押してもレーザーが消えなかった（2026-09-29 テストプレイ）。
//   1人がレーザーを抜けてボタンを押せば、残りの2人は安全に通れる、という形にする。
//
// 何をするか:
//   TurnOff を呼ばれたら lasersRoot（レーザー14本・死亡判定・レーザー音をまとめた親）を
//   全員の画面で非表示にする。一度止めたら戻さない。
//
// 設計上の注意:
//   ・**このスクリプトは部屋の外（常に有効な場所）に置くこと。**
//     部屋の表示切替（RoomVisibilityManager）は部屋を丸ごと SetActive(false) にする。
//     部屋の中に置くと、隠れている間に届いた同期を取りこぼし、
//     後からその部屋に入った人にはレーザーが点いたまま見える。
//   ・状態は UdonSynced で持つ。イベントだけ飛ばすと、後から入ったプレイヤーには
//     「止まった」という事実が届かない。
//   ・死亡判定もレーザーと一緒に消える（lasersRoot の中にある）ので、
//     見えないレーザーに当たって死ぬ、ということは起きない。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class LaserSwitch : UdonSharpBehaviour
{
    [Header("レーザー一式の親（止めると丸ごと非表示）")]
    [SerializeField] private GameObject lasersRoot;

    [Header("止めた瞬間に鳴らす音（任意）")]
    [SerializeField] private AudioSource offSound;

    [UdonSynced] private bool off;

    // この画面で「止まった」をもう表示したか。
    // 後から入ったプレイヤーは最初の同期で off=true を受け取るが、
    // その時に止まる音を鳴らすのはおかしいので、Start 前後で区別する。
    private bool shownOff;
    private bool started;

    void Start()
    {
        Apply(false);
        started = true;
    }

    // ボタンから呼ばれる。押した本人のクライアントで実行される。
    public void TurnOff()
    {
        if (off) return;
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        off = true;
        RequestSerialization();
        Apply(true);
    }

    public override void OnDeserialization()
    {
        Apply(started);
    }

    private void Apply(bool withSound)
    {
        if (lasersRoot != null) lasersRoot.SetActive(!off);
        if (off && !shownOff)
        {
            shownOff = true;
            if (withSound && offSound != null) offSound.Play();
        }
    }
}
