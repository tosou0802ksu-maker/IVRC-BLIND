using UdonSharp;
using UnityEngine;

// 切れかけの灯りを1つだけ点滅させる。
//
// なぜ必要か:
//   room11 の照明は 2列×13個、色も強さも完全に同じ点光源が並んでいる。
//   均一なので、28m 先の棚に置いてある 17cm の鍵を「あの棚だ」と特定する
//   手がかりが画面上に一つも無かった（テストプレイで実際に迷った）。
//   同じ光が26個並ぶ中に1つだけ色と明滅の違う灯りがあれば、
//   遠くからでも一点だけ目に付く。文字や矢印を置かずに場所を示せる。
//
// 何をするか:
//   Light の強さと、灯具のレンズの発光を、不規則に揺らす。
//   たまに短く消える。切れかけの蛍光灯の挙動。
//
// 設計上の注意:
//   ・**全画面が明滅するような使い方をしてはいけない。** 光過敏性発作の危険がある。
//     この灯りは range が狭く、部屋全体の明るさには影響しない局所光であること。
//     さらに下限を 0 にせず、落ちる頻度も 1 秒に 1 回程度までに抑えている。
//   ・同期しない。各クライアントがローカルに計算する。
//     明滅のタイミングが3人でずれていても遊びに影響せず、
//     同期を張るとその分だけ帯域を食うだけになる。
//   ・マテリアルは共有アセットなので直接書き換えない（他の灯具に波及する）。
//     MaterialPropertyBlock でレンダラー単位に上書きする。
//   ・乱数を使わず時間から決めているので、入り直しても同じリズムで点滅する。
//     「さっきと違う」と感じさせたいのは部屋であって、灯りの性格ではない。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FlickerLight : UdonSharpBehaviour
{
    [Header("明滅させる光源")]
    [SerializeField] private Light target;

    [Header("一緒に発光させるレンズなどのレンダラー")]
    [SerializeField] private Renderer[] emissive;

    [Header("発光色（レンダラー側の _EmissionColor に入れる基準色）")]
    [SerializeField] private Color emissionColor = new Color(0.45f, 1.0f, 0.55f, 1f);

    [Header("光源の強さ（この値に倍率が掛かる）")]
    [SerializeField] private float baseIntensity = 4.0f;

    [Header("明滅の下限倍率")]
    [Tooltip("0 にすると完全に消える。0.12 くらい残すと「消えかけ」に見えて、暗所でも位置を見失わない。")]
    [SerializeField] private float floorLevel = 0.12f;

    [Header("更新間隔(秒)")]
    [Tooltip("毎フレーム更新する必要は無い。0.033 秒ごとで十分ちらついて見える。")]
    [SerializeField] private float updateInterval = 0.033f;

    private MaterialPropertyBlock block;
    private float timer;

    void Start()
    {
        if (target == null && (emissive == null || emissive.Length == 0))
        {
            enabled = false;
            return;
        }
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        float level = Level(Time.time);

        if (target != null) target.intensity = baseIntensity * level;

        if (emissive != null)
        {
            var c = emissionColor * level;
            for (int i = 0; i < emissive.Length; i++)
            {
                var r = emissive[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", c);
                r.SetPropertyBlock(block);
            }
        }
    }

    /// <summary>
    /// 0..1 の明るさ倍率。
    ///
    /// 3つの正弦波を足して「だいたい点いているが落ち着かない」状態を作り、
    /// そこへ周期的に短い脱落を重ねる。周期が互いに割り切れない数なので、
    /// 見ていても繰り返しに気付かない。
    /// </summary>
    private float Level(float t)
    {
        // ゆっくりした揺れ。これだけだと「呼吸」に見えて故障感が出ない
        float slow = 0.5f + 0.5f * Mathf.Sin(t * 1.7f);
        // 速い震え。蛍光灯の安定器が鳴っている感じ
        float fast = 0.5f + 0.5f * Mathf.Sin(t * 13.1f + 1.3f);
        // 中間。上2つの拍が揃うのを防ぐ
        float mid = 0.5f + 0.5f * Mathf.Sin(t * 4.3f + 2.7f);

        float v = 0.62f + 0.22f * slow + 0.10f * fast + 0.06f * mid;

        // --- 短い脱落 ---
        // 1.9 秒ごとに窓を開け、その中の 0.10 秒だけ落とす。
        // ⚠️ ここを速くしすぎると光過敏性発作の危険が出る。1秒に1回より速くしない。
        float phase = Mathf.Repeat(t, 1.9f);
        if (phase < 0.10f) v *= 0.18f;
        // まれに二度落ちさせる（完全な周期に聞こえないように）
        float phase2 = Mathf.Repeat(t, 7.3f);
        if (phase2 < 0.06f) v *= 0.25f;

        return Mathf.Clamp(v, floorLevel, 1f);
    }
}
