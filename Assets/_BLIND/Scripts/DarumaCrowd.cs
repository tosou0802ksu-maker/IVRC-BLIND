using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 棚や机の上のだるまが、こちらを向く。
//
// 追いかけては来ない。置いてある場所からは一歩も動かず、**首だけがこちらを向く**。
// 部屋を歩くと視界の端で何十体もの向きが揃っていく、という圧を作るためのもの。
// 中央の監視者(DarumaWatcher)とは別物で、こちらは捕まえたりはしない。
//
// ⚠️ 同期しない。**各クライアントが自分のプレイヤーの方を向かせる。**
//    3人はそれぞれ別の世界を見ているので、他人から見た向きを合わせる必要が無い。
//    97体ぶんの回転を同期すると帯域を使い切るし、遅延でガクガクになる。
//    「自分を見ている」ことが伝わればよいので、ローカル計算が正解。
//
// ⚠️ 回すのは Default レイヤーの本体だけでよい……ようには**なっていない**。
//    サーモ・エコロケ用の複製は BlindGimmickBuilder が本体の子として作るので、
//    本体を回せば一緒に回る。複製を Vision_Thermal 側（部屋直下のバケツ）に
//    作らせると取り残されて、過去人だけが「こっち向いた」と言う嘘になる。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DarumaCrowd : UdonSharpBehaviour
{
    [Header("向きを変えるだるま（本体の Transform）")]
    [SerializeField] private Transform[] darumas;

    [Tooltip("↑と同じ並びのエコロケ受信機。動いた瞬間に光らせる。")]
    [SerializeField] private EchoReceiver[] echoes;

    [Header("1回でカクッと回る角度(度)")]
    // ⚠️ なめらかに回してはいけない。
    //    ゆっくり連続で回すと「気付いたら向きが変わっていた」になり、
    //    **動いた瞬間がどの役にも見えない**（サーモは面の変化が小さすぎ、
    //    エコロケはそもそもパルスが当たっていないと何も見えない）。
    //    止まっている物が一瞬でカクッと動くほうが、必ず目に留まる。
    [SerializeField] private float snapAngle = 32.0f;

    [Header("次に動くまでの間隔(秒)。体ごとにこの範囲でばらつく")]
    // 短くすると一斉にガチャガチャ動いて、どれが動いたのか読めなくなる。
    // 1体ずつ「今そこが動いた」と分かる程度に間を空ける。
    [SerializeField] private float intervalMin = 1.4f;
    [SerializeField] private float intervalMax = 3.5f;

    [Header("この距離まで近づくと向き始める(m)")]
    [Tooltip("部屋の外にいる間まで回すと、入った瞬間に全部こちらを向いていて驚きが無い。")]
    [SerializeField] private float wakeRange = 14.0f;

    [Header("向き終わったとみなす角度(度)")]
    [Tooltip("これ以内なら動かない。0にすると永久にカクカクし続ける。")]
    [SerializeField] private float deadZone = 8.0f;

    [Header("1フレームに見る体数")]
    [Tooltip("97体を毎フレーム見ると重い。順番に少しずつ見る。")]
    [SerializeField] private int perFrame = 12;

    private int cursor;
    private float[] nextMove;

    void Start()
    {
        if (darumas == null)
        {
            return;
        }

        // 全部が同じ拍で動くと機械に見える。体ごとに開始をばらす。
        nextMove = new float[darumas.Length];
        for (int i = 0; i < nextMove.Length; i++)
        {
            nextMove[i] = Time.timeSinceLevelLoad + Random.Range(0.0f, intervalMax);
        }
    }

    void Update()
    {
        if (darumas == null || darumas.Length == 0 || nextMove == null)
        {
            return;
        }

        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null)
        {
            return;
        }

        Vector3 eye = player.GetPosition();
        float now = Time.timeSinceLevelLoad;

        int n = Mathf.Min(perFrame, darumas.Length);
        for (int i = 0; i < n; i++)
        {
            cursor++;
            if (cursor >= darumas.Length)
            {
                cursor = 0;
            }

            Transform t = darumas[cursor];
            if (t == null)
            {
                continue;
            }

            if (now < nextMove[cursor])
            {
                continue;
            }

            Vector3 d = eye - t.position;
            d.y = 0.0f;
            float sq = d.sqrMagnitude;
            if (sq < 0.0004f || sq > wakeRange * wakeRange)
            {
                continue;
            }

            Vector3 fwd = t.forward;
            fwd.y = 0.0f;
            if (fwd.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            float cur = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(cur, want);

            // もう向いている。動かさない（動かすと永久にカクカクし続ける）。
            if (Mathf.Abs(diff) < deadZone)
            {
                nextMove[cursor] = now + Random.Range(intervalMin, intervalMax);
                continue;
            }

            float delta = Mathf.Clamp(diff, -snapAngle, snapAngle);

            // ⚠️ rotation を組み直さないこと。
            //    棚の上のだるまは少し傾けて置いてあり、Quaternion.Euler(0,yaw,0) を
            //    代入すると全部が直立に揃ってしまって「置いてある感」が消える。
            //    ワールドのY軸まわりに差分だけ回せば、傾きはそのまま残る。
            t.rotation = Quaternion.AngleAxis(delta, Vector3.up) * t.rotation;

            // 動いた瞬間だけエコロケを光らせる。
            // ⚠️ エコロケ役はパルスが当たっている間しか物が見えないので、
            //    これが無いと**動いたこと自体が伝わらない**。
            //    向き終われば光らなくなるので、部屋が光り続けることもない。
            if (echoes != null && cursor < echoes.Length && echoes[cursor] != null)
            {
                echoes[cursor].TriggerGlowWithDelay(0.0f, 0.15f);
            }

            nextMove[cursor] = now + Random.Range(intervalMin, intervalMax);
        }
    }
}
