using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// だるまさんがころんだ（room11）で捕まったときの死亡演出。
// 棚のミニだるまが、捕まった人の顔めがけて一斉に飛んでくる。
//
// なぜこうするか:
//   以前は捕まっても大だるまが振り向くだけで、何も起きなかった。
//   死亡判定を付けるにあたって「何に殺されたか」が一目で分かる演出が要る。
//   部屋じゅうに並べたミニだるま（DarumaCrowd で首だけこちらを向いていた群れ）が
//   一斉に飛びかかってくれば、「ずっと見られていた」の答え合わせになる。
//
// 何をするか:
//   Launch(playerId, duration) で、その人に近い順に count 体を選び、
//   少しずつ時間をずらして顔へ飛ばす。弧を描き、回転しながら加速して当たる。
//   duration（名指しの長さ）が終わる頃に全員が当たり、そこで死亡・復帰になる。
//   復帰後に元の棚の位置へ戻す。
//
// 設計上の注意:
//   ・**同期しない。** 位相（名指し）は DarumaWatcher が同期しているので、
//     全員のクライアントがそれを見て、それぞれ自分の画面で同じ演出を再生する。
//     誰を狙うかは playerId で決まるので、全員が同じ答えになる。
//   ・ミニだるまは**静的バッチングを外しておくこと。**
//     BatchingStatic のままだと、実機では Transform を動かしても見た目が止まったまま。
//   ・サーモ・エコロケの複製は本体の子なので、本体を動かせば一緒に飛ぶ。
//   ・当たる直前に狙いを固定する。固定しないと、死亡で復帰地点へ飛ばされた
//     プレイヤーをだるまが追いかけて、復帰地点まで飛んでいってしまう。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DarumaSwarm : UdonSharpBehaviour
{
    [Header("飛ばす候補（棚のミニだるま全部）")]
    [SerializeField] private Transform[] minis;

    [Header("一度に飛んでくる数")]
    [SerializeField] private int count = 12;

    [Header("1体が飛ぶ時間(秒)")]
    [SerializeField] private float flyTime = 0.85f;

    [Header("1体ごとの飛び出しのずれ(秒)")]
    [SerializeField] private float stagger = 0.035f;

    [Header("弧の高さ(m)")]
    [SerializeField] private float arcHeight = 1.2f;

    [Header("顔のまわりに散らばる半径(m)")]
    [SerializeField] private float scatter = 0.22f;

    [Header("飛んでくる距離の下限(m)")]
    [Tooltip("近すぎるだるまは選ばない。すぐ隣の棚から跳ねるだけだと「飛んでくる」ように見えない" +
             "（撮影で確認: 近い順に選ぶと12体とも2m以内で、その場で固まって見えた）。")]
    [SerializeField] private float minDistance = 3.0f;

    [Header("当たってから棚へ戻すまで(秒)")]
    [SerializeField] private float holdAfter = 0.6f;

    private Vector3[] homePos;
    private Quaternion[] homeRot;
    private int[] chosen;
    private Vector3[] startPos;
    private Vector3[] offset;
    private bool flying;
    private float timer;
    private float duration;
    private int targetId;
    private Vector3 target;

    void Start()
    {
        int n = minis != null ? minis.Length : 0;
        homePos = new Vector3[n];
        homeRot = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            if (minis[i] == null) continue;
            homePos[i] = minis[i].position;
            homeRot[i] = minis[i].rotation;
        }
        chosen = new int[count];
        startPos = new Vector3[count];
        offset = new Vector3[count];
    }

    // DarumaWatcher が「名指し」を始めた瞬間に、全員のクライアントで呼ぶ。
    public void Launch(int playerId, float accuseSeconds)
    {
        if (minis == null || minis.Length == 0) return;
        if (flying) ResetHome();   // 前の演出が残っていたら戻してから

        targetId = playerId;
        duration = accuseSeconds;
        timer = 0.0f;
        target = HeadOf(playerId, transform.position);

        // 狙う人から minDistance 以上離れたものを、近い順に count 体選ぶ。
        // 足りなければ近すぎるものでも選ぶ（部屋の端で捕まった場合など）。
        int n = minis.Length;
        int k = Mathf.Min(count, n);
        float minSq = minDistance * minDistance;
        for (int c = 0; c < k; c++)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (minis[i] == null) continue;
                    bool used = false;
                    for (int u = 0; u < c; u++) if (chosen[u] == i) { used = true; break; }
                    if (used) continue;
                    float d = (homePos[i] - target).sqrMagnitude;
                    if (pass == 0 && d < minSq) continue;
                    if (d < bd) { bd = d; best = i; }
                }
            }
            chosen[c] = best;
            startPos[c] = best >= 0 ? minis[best].position : target;
            // 顔のまわりに少し散らす（全部が1点に重なると1体にしか見えない）
            float a = c * 2.399963f;   // 黄金角で散らす
            float r = scatter * Mathf.Sqrt((c + 0.5f) / k);
            offset[c] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.8f, Mathf.Sin(a) * r);
        }
        for (int c = k; c < count; c++) chosen[c] = -1;
        flying = true;
    }

    void Update()
    {
        if (!flying) return;
        timer += Time.deltaTime;

        // 当たる直前まで顔を追い、そこで固定する（復帰地点まで追いかけないように）
        if (timer < duration - 0.05f)
        {
            target = HeadOf(targetId, target);
        }

        for (int c = 0; c < count; c++)
        {
            int i = chosen[c];
            if (i < 0 || minis[i] == null) continue;

            float local = Mathf.Clamp01((timer - c * stagger) / flyTime);
            float e = local * local;   // 加速しながら当たる
            Vector3 end = target + offset[c];
            Vector3 p = Vector3.Lerp(startPos[c], end, e);
            p += Vector3.up * (arcHeight * Mathf.Sin(local * Mathf.PI) * (1.0f - local));
            minis[i].position = p;
            minis[i].rotation = homeRot[i] * Quaternion.Euler(local * 720.0f, local * 540.0f, 0.0f);
        }

        if (timer > duration + holdAfter)
        {
            ResetHome();
        }
    }

    private void ResetHome()
    {
        for (int c = 0; c < count; c++)
        {
            int i = chosen[c];
            if (i < 0 || minis[i] == null) continue;
            minis[i].position = homePos[i];
            minis[i].rotation = homeRot[i];
        }
        flying = false;
    }

    private Vector3 HeadOf(int playerId, Vector3 fallback)
    {
        VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
        if (!Utilities.IsValid(p)) return fallback;
        return p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
    }
}
