
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 人形の体温をゆっくり揺らす。
//
// なぜ必要か:
//   温度が固定だと、サーモ役の画面では人形が「止まった絵」にしかならない。
//   一度見てしまえば以降は情報が増えないので、二度目からはただの背景になる。
//   本物のサーモグラフィが不気味なのは、映っている物が生きていると温度が
//   絶えず動くからで、その「動いている」という手がかりが恐怖の源になっている。
//
// 何をするか:
//   対象それぞれに違う周期と位相を与えて _HeatIntensity をゆっくり動かす。
//   全部が同じ呼吸をすると機械的に見えるので、周期は個体ごとにばらす。
//   さらに1体だけを「暖かい個体」に指定でき、そいつは他より高い温度を保ったまま
//   ときどき脈打つ。サーモ役から見ると「16体のうち1体だけ体温がある」＝
//   どれかが生きている、という読み方ができる。
//
// 設計上の注意:
//   ・マテリアルは共有アセットなので直接書き換えてはいけない(全部屋に波及する)。
//     MaterialPropertyBlock でレンダラー単位に上書きする。
//   ・同期しない。各クライアントがローカルに計算する。Time.time は
//     クライアントごとにずれるが、見た目の揺らぎなので揃っている必要が無く、
//     同期を張るとその分だけ帯域を食うだけになる。
//   ・時間で動くノイズを画面全体に入れるのは禁止(VRで左右の目にずれたノイズが出て
//     立体視が壊れる)。これはピクセル単位ではなくオブジェクト単位の
//     ゆっくりした変化なので、その問題は起きない。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ThermalBodyDrift : UdonSharpBehaviour
{
    [Header("温度を揺らす対象(サーモ層のレンダラー)")]
    [SerializeField] private Renderer[] targets;

    [Header("揺らぎの深さ(_HeatIntensity の振れ幅)")]
    [Tooltip("0.06 なら体温34℃が概ね ±2℃ 揺れる。大きくすると生き物らしさが増すが騒がしくなる。")]
    [SerializeField] private float amplitude = 0.06f;

    [Header("一往復にかける秒数の範囲")]
    [Tooltip("個体ごとにこの範囲でばらつかせる。全部同じ周期だと機械的に見える。")]
    [SerializeField] private float minCycle = 7f;
    [SerializeField] private float maxCycle = 19f;

    [Header("「暖かい個体」の番号(-1で無し)")]
    [Tooltip("targets の何番目を他より高い体温にするか。1体だけ生きている、という読ませ方をする。")]
    [SerializeField] private int warmIndex = -1;

    [Header("暖かい個体の上乗せ")]
    [SerializeField] private float warmBoost = 0.14f;

    [Header("更新間隔(秒)")]
    [Tooltip("毎フレーム更新する必要は無い。0.1秒ごとで十分なめらかに見え、負荷が1/6になる。")]
    [SerializeField] private float updateInterval = 0.1f;

    private MaterialPropertyBlock block;
    private float[] cycles;
    private float[] phases;
    private float timer;

    void Start()
    {
        if (targets == null || targets.Length == 0)
        {
            enabled = false;
            return;
        }

        block = new MaterialPropertyBlock();
        cycles = new float[targets.Length];
        phases = new float[targets.Length];

        // 個体ごとの周期と位相。乱数を使わず番号から決めているので、
        // 何度入り直しても同じ個体が同じリズムで揺れる。
        // 「さっきと違う」と感じさせたいのは温度であって、個体の性格ではない。
        for (int i = 0; i < targets.Length; i++)
        {
            float t = (float)i / Mathf.Max(targets.Length - 1, 1);
            // 黄金比を足しながら剰余を取ると、少ない個数でも周期が綺麗にばらける
            float g = (i * 0.6180339f) % 1f;
            cycles[i] = Mathf.Lerp(minCycle, maxCycle, g);
            phases[i] = t * 6.2831853f + g * 3.1415926f;
        }
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < updateInterval)
        {
            return;
        }
        timer = 0f;

        float now = Time.time;

        for (int i = 0; i < targets.Length; i++)
        {
            Renderer r = targets[i];
            if (r == null)
            {
                continue;
            }

            float w = 6.2831853f / Mathf.Max(cycles[i], 0.1f);
            float wave = Mathf.Sin(now * w + phases[i]);

            float intensity = 1f + wave * amplitude;

            if (i == warmIndex)
            {
                // 暖かい個体は底上げしたうえで、少し速い脈を重ねる。
                // 心拍のつもりではなく「他とリズムが違う」ことが伝わればよい。
                intensity += warmBoost + Mathf.Sin(now * 1.7f) * amplitude * 0.5f;
            }

            r.GetPropertyBlock(block);
            block.SetFloat("_HeatIntensity", intensity);
            r.SetPropertyBlock(block);
        }
    }
}
