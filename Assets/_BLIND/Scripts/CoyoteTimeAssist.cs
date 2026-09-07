using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

// コヨーテタイム。
//
// 落とし穴部屋は「他の2人の声を聞いてから跳ぶ」部屋なので、
// 入力が一瞬遅れるのが**普通の遊び方**になっている。
// 「跳べ！」と言われて反応した頃には、もう足場の端を踏み越えている。
// 素の VRChat は接地していない瞬間のジャンプ入力を捨てるので、
// この一瞬の遅れがそのまま落下＝全員リスポーンになっていた。
//
// そこで、地面を離れてから coyoteTime 秒のあいだはジャンプ入力を受け付け、
// 本来のジャンプと同じ初速を与える。プラットフォーマーで一般的な救済で、
// 「跳べたはずなのに落ちた」という理不尽だけを消す。
//
// ⚠️ 接地中の入力には**絶対に触らない**こと。
//    接地中は VRChat 本体がジャンプさせるので、こちらでも初速を足すと
//    二段ジャンプになり、穴を跳び越え放題になってこの部屋が成立しなくなる。
//    判定は本体と同じ VRCPlayerApi.IsPlayerGrounded() を使い、食い違いを避ける。
//
// ローカル専用。同期は一切しない（自分の足元の話なので他人に送る必要が無い）。
// === SYSTEM === の下に1つ置いておけばワールド全体で効く。
public class CoyoteTimeAssist : UdonSharpBehaviour
{
    [Header("地面を離れてからジャンプを受け付ける猶予(秒)")]
    [Tooltip("0.15〜0.25 が目安。長すぎると空中ジャンプに見えて、穴を跳び越え放題になる。")]
    [SerializeField] private float coyoteTime = 0.20f;

    [Header("与える初速(m/s)。0ならワールドのジャンプ力をそのまま使う")]
    [SerializeField] private float jumpSpeed = 0.0f;

    // 地面を離れた時刻。接地している間は更新し続ける。
    private float leftGroundAt = -99.0f;
    // 空中でジャンプが押された時刻。
    private float jumpPressedAt = -99.0f;
    private bool wasGrounded = true;
    // 1回空中に出るごとに1回だけ。これが無いと連打で浮き続けられる。
    private bool usedThisAirtime = false;

    public override void InputJump(bool value, UdonInputEventArgs args)
    {
        if (!value)
        {
            return;
        }

        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null)
        {
            return;
        }

        // 接地中の入力は VRChat 本体の仕事。ここで覚えると二段ジャンプになる。
        if (player.IsPlayerGrounded())
        {
            return;
        }

        jumpPressedAt = Time.timeSinceLevelLoad;
    }

    void Update()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null)
        {
            return;
        }

        float now = Time.timeSinceLevelLoad;

        if (player.IsPlayerGrounded())
        {
            // 接地中は猶予の起点を更新し続ける。着地したら次の空中でまた1回使える。
            leftGroundAt = now;
            wasGrounded = true;
            usedThisAirtime = false;
            return;
        }

        if (wasGrounded)
        {
            // 落ち始めた最初のフレーム
            leftGroundAt = now;
            wasGrounded = false;
        }

        if (usedThisAirtime)
        {
            return;
        }

        // 地面を離れてから猶予を過ぎていたら、もう普通の落下として扱う
        if (now - leftGroundAt > coyoteTime)
        {
            return;
        }

        // 押されたのが「地面を離れた後」でなければ無視する。
        // 接地中の入力は InputJump 側で捨てているので、ここに来るのは空中の入力だけ。
        if (jumpPressedAt < leftGroundAt)
        {
            return;
        }

        float speed = jumpSpeed;
        if (speed <= 0.0f)
        {
            speed = player.GetJumpImpulse();
        }
        if (speed <= 0.0f)
        {
            // ワールド設定でジャンプが無効。何もしない。
            return;
        }

        Vector3 v = player.GetVelocity();
        if (v.y >= speed)
        {
            // 既にそれ以上の速度で上昇中。上書きすると逆に減速してしまう。
            return;
        }

        v.y = speed;
        player.SetVelocity(v);

        usedThisAirtime = true;
        jumpPressedAt = -99.0f;
    }
}
