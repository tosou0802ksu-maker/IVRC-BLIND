using System.IO;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 「だるまさんがころんだ」の掛け声を、古い電子音（矩形波）＋壊れかけのラジオの音で作り、
    /// WAV ファイルに焼く。
    ///
    /// なぜエディタで焼くのか:
    ///   VRChat では MonoBehaviour はアップロード時に取り除かれ、Udon しか動かない。
    ///   さらに AudioClip.Create / SetData は Udon に公開されていないので、
    ///   実行中に音を作ることができない。作者が書いた生成スクリプトと同じ考え方で
    ///   エディタ上で波形を作り、ファイルにしておいて Udon から再生する。
    ///
    /// 作者の元案（DarumaRetroSound）からの変更点:
    ///   ・「ー」（伸ばす音）は前の音とつなげ、それ以外の音の間に短い切れ目を入れる。
    ///     元案は切れ目が無く、同じ高さの G4 が8つ続くので一本の長い「ピー」に聞こえた。
    ///   ・壊れかけのラジオ感: 帯域を 350Hz〜2.8kHz に絞る（AM ラジオ／電話の帯域）、
    ///     音程をわずかに揺らす（テープやラジオのふらつき）、ときどき音が途切れる、
    ///     パチパチという雑音を混ぜる。
    ///   ・雑音は種を固定した乱数で作る。何度焼き直しても同じ音になる。
    ///
    /// 振り向きとの合わせ方（DarumaWatcher 側）:
    ///   「だ」の直前までの長さ(Lead)を背を向けている時間、「だー」の長さ(Turn)を
    ///   振り向きの時間にする。速さは毎回ランダムに変え、再生速度(pitch)で合わせる。
    ///   ここで計算した Lead と Turn を DarumaWatcher に書き込む。
    /// </summary>
    public static class DarumaChantBaker
    {
        public const string OutPath = "Assets/_BLIND/SE/Daruma_Chant.wav";

        const float G4 = 392.00f, E4 = 329.63f, C4 = 261.63f;
        const float Note = 0.25f;     // 1音の基本の長さ(秒)
        const int Rate = 44100;

        // 音程・長さ（Note の倍数）・前の音とつなぐか（「ー」）
        static readonly float[] Freq = { G4, G4, G4, G4, G4, G4, G4, G4, E4, G4, E4, C4, C4 };
        static readonly float[] Len  = { 1f, 1.5f, 1f, 1f, 1f, 1f, 1f, 2f, 1f, 1.2f, 1f, 1f, 2.5f };
        //                               ダ  ー    ル  マ  さ  ん  が  ー  こ  ろ    ん  だ  ー
        static readonly bool[] Tie   = { false, true, false, false, false, false, false, true, false, false, false, false, true };
        /// <summary>振り向きが始まる音（「だ」）の番号</summary>
        const int TurnStart = 11;

        public static float LeadSeconds { get { float s = 0; for (int i = 0; i < TurnStart; i++) s += Len[i] * Note; return s; } }
        public static float TurnSeconds { get { float s = 0; for (int i = TurnStart; i < Len.Length; i++) s += Len[i] * Note; return s; } }

        [MenuItem("BLIND/だるま/1. 掛け声を焼く")]
        public static void Menu_Bake() { Debug.Log(Bake()); }

        public static string Bake()
        {
            float total = 0; foreach (var l in Len) total += l * Note;
            int n = Mathf.CeilToInt(total * Rate) + Rate / 10;   // 最後に少し余韻
            var s = new float[n];
            var rnd = new System.Random(7358);                   // 固定: 焼き直しても同じ音

            // --- 矩形波（ファミコン風）---
            int pos = 0; double phase = 0;
            for (int k = 0; k < Freq.Length; k++)
            {
                int len = Mathf.CeilToInt(Len[k] * Note * Rate);
                bool nextTied = k + 1 < Tie.Length && Tie[k + 1];
                int gap = nextTied ? 0 : Mathf.CeilToInt(0.035f * Rate);  // 音の間の切れ目
                for (int i = 0; i < len && pos < n; i++, pos++)
                {
                    float t = pos / (float)Rate;
                    // 音程のふらつき（5.3Hz と 0.7Hz を重ねる）。ラジオ・テープの揺れ
                    float wow = 1f + 0.012f * Mathf.Sin(t * 2f * Mathf.PI * 5.3f) + 0.006f * Mathf.Sin(t * 2f * Mathf.PI * 0.7f);
                    phase += Freq[k] * wow / Rate;
                    float sq = (phase % 1.0) < 0.5 ? 1f : -1f;
                    // 音の出だしと終わりを数msで丸める（プチッという音を消す）
                    float env = 1f;
                    int atk = (Tie[k] ? 0 : Mathf.CeilToInt(0.004f * Rate));
                    if (i < atk) env = i / (float)atk;
                    int rel = Mathf.CeilToInt(0.006f * Rate);
                    int soundLen = len - gap;
                    if (i >= soundLen) env = 0f;
                    else if (!nextTied && i > soundLen - rel) env = Mathf.Max(0f, (soundLen - i) / (float)rel);
                    s[pos] = sq * 0.22f * env;
                }
            }

            // --- ラジオの帯域（350Hz の高域通過 → 2.8kHz の低域通過を2段）---
            HighPass(s, 350f); LowPass(s, 2800f); LowPass(s, 2800f);

            // --- 壊れかけ: ときどき音がすっと遠のく ---
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = 0.78f + 0.22f * Mathf.Sin(t * 2f * Mathf.PI * 0.9f + 1.3f);
                // 1.7 秒付近と 2.9 秒付近で一瞬途切れる
                if ((t > 1.72f && t < 1.80f) || (t > 2.93f && t < 2.97f)) fade *= 0.15f;
                s[i] *= fade;
            }

            // --- 雑音: サーという砂嵐 ＋ ときどきパチッ ---
            var noise = new float[n];
            for (int i = 0; i < n; i++) noise[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.07f;
            HighPass(noise, 1200f);   // 高めのサー
            for (int i = 0; i < n; i++)
            {
                s[i] += noise[i];
                if (rnd.NextDouble() < 0.0006)                 // パチッ
                {
                    int w = 20 + rnd.Next(60);
                    float a = (float)(rnd.NextDouble() * 0.5 + 0.2) * (rnd.Next(2) == 0 ? 1 : -1);
                    for (int j = 0; j < w && i + j < n; j++) s[i + j] += a * (1f - j / (float)w);
                }
            }

            // --- 少し歪ませて全体をならす（安いスピーカーの潰れ）---
            float peak = 0;
            for (int i = 0; i < n; i++) { s[i] = (float)System.Math.Tanh(s[i] * 2.2f) * 0.55f; peak = Mathf.Max(peak, Mathf.Abs(s[i])); }
            if (peak > 0) for (int i = 0; i < n; i++) s[i] *= 0.89f / peak;

            WriteWav(OutPath, s, Rate);
            AssetDatabase.ImportAsset(OutPath);
            return "掛け声を焼いた: " + OutPath + "（" + (n / (float)Rate).ToString("F2") + "秒）"
                 + " / 背を向けている長さ(Lead) " + LeadSeconds.ToString("F3") + "秒 / 振り向き(Turn) " + TurnSeconds.ToString("F3") + "秒（どちらも速さ1のとき）";
        }

        static void HighPass(float[] x, float fc)
        {
            float rc = 1f / (2f * Mathf.PI * fc), dt = 1f / Rate, a = rc / (rc + dt);
            float prevX = 0, prevY = 0;
            for (int i = 0; i < x.Length; i++) { float y = a * (prevY + x[i] - prevX); prevX = x[i]; prevY = y; x[i] = y; }
        }

        static void LowPass(float[] x, float fc)
        {
            float rc = 1f / (2f * Mathf.PI * fc), dt = 1f / Rate, a = dt / (rc + dt);
            float y = 0;
            for (int i = 0; i < x.Length; i++) { y += a * (x[i] - y); x[i] = y; }
        }

        static void WriteWav(string path, float[] s, int rate)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int dataLen = s.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
                foreach (var v in s) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }
    }
}
