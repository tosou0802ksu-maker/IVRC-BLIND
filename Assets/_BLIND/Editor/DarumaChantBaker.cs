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
    /// 節回し（2026-09-30 作者の指定「ダーるまさんがこーろんだ」）:
    ///   ダー る ま さ ん が （息つぎ） こー ろ ん だー
    ///   前半はソで平らに、後半は ソ→ミ→レ→ド と下がって終わる。
    ///   ⚠️ 初版は後半が「ミ・ソ↑・ミ・ド」で途中に一度上がり、「がー」を伸ばして「こー」を伸ばさず、
    ///      作者に「音程がおかしい」と言われた。
    ///
    /// 速さ違いを別ファイルで焼く:
    ///   ⚠️ 初版は1本だけ焼いて、ゲーム中は再生速度(pitch)で速さを変えていた。
    ///      pitch を上げると音程まで上がるので、速い回ほど甲高く聞こえた。
    ///      速さごとに別の波形を作り、音程は常に同じにする。
    ///
    /// ラジオらしさ（帯域を絞る・音程のふらつき・途切れ・パチパチ）:
    ///   ⚠️ 初版は 350Hz 以下を削っていて、ド(262Hz)・ミ(330Hz)の基音が消え、
    ///      低い音ほど音程がぼやけていた。削る境目を 200Hz に下げた。
    /// </summary>
    public static class DarumaChantBaker
    {
        public const string OutDir = "Assets/_BLIND/SE";
        public const string BaseName = "Daruma_Chant";

        const float G4 = 392.00f, E4 = 329.63f, D4 = 293.66f, C4 = 261.63f;
        const float Rest = 0f;
        const float Unit = 0.25f;     // 1拍の長さ(秒, 速さ1のとき)
        const int Rate = 44100;

        /// <summary>唱える速さ。DarumaWatcher はこの中から毎回1つ選ぶ。</summary>
        public static readonly float[] Speeds = { 0.85f, 1.0f, 1.15f, 1.3f, 1.5f };

        // 音節ごとの 音程・長さ(拍)・前の音とつなぐか（「ー」）
        static readonly string[] Syl = { "ダ", "ー", "る", "ま", "さ", "ん", "が", "（息）", "こ", "ー", "ろ", "ん", "だ", "ー" };
        static readonly float[] Freq = {  G4,  G4,   G4,   G4,   G4,   G4,   G4,   Rest,    G4,  G4,   E4,   D4,   C4,  C4 };
        static readonly float[] Len  = { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.3f, 0.6f,    1.0f, 0.9f, 1.0f, 1.0f, 1.0f, 1.8f };
        static readonly bool[] Tie   = { false, true, false, false, false, false, false, false, false, true, false, false, false, true };
        /// <summary>振り向きが始まる音節（「だ」）の番号</summary>
        const int TurnStart = 12;

        public static float LeadSeconds { get { float s = 0; for (int i = 0; i < TurnStart; i++) s += Len[i] * Unit; return s; } }
        public static float TurnSeconds { get { float s = 0; for (int i = TurnStart; i < Len.Length; i++) s += Len[i] * Unit; return s; } }
        public static string PathFor(float speed) { return OutDir + "/" + BaseName + "_x" + Mathf.RoundToInt(speed * 100) + ".wav"; }

        [MenuItem("BLIND/だるま/1. 掛け声を焼く")]
        public static void Menu_Bake() { Debug.Log(BakeAll()); }

        public static string BakeAll()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var sp in Speeds) { Bake(sp); sb.AppendLine("  " + PathFor(sp)); }
            AssetDatabase.Refresh();
            sb.Insert(0, "掛け声を " + Speeds.Length + " 本焼いた（Lead " + LeadSeconds.ToString("F3") + "秒 / Turn " + TurnSeconds.ToString("F3") + "秒, 速さ1のとき）\n");
            sb.Append(Analyze(1.0f));
            return sb.ToString();
        }

        /// <summary>1本分の波形を作る（速さ speed）。解析にも使う。</summary>
        public static float[] Synthesize(float speed, bool withNoise)
        {
            float total = 0; foreach (var l in Len) total += l * Unit / speed;
            int n = Mathf.CeilToInt(total * Rate) + Rate / 10;
            var s = new float[n];
            var rnd = new System.Random(7358);

            int pos = 0; double phase = 0;
            for (int k = 0; k < Freq.Length; k++)
            {
                int len = Mathf.CeilToInt(Len[k] * Unit / speed * Rate);
                bool nextTied = k + 1 < Tie.Length && Tie[k + 1];
                // 音節の間の切れ目。30ms では同じ高さの「る・ま」がつながって聞こえた（実測 5.7dB）ので 45ms
                int gap = (nextTied || Freq[k] == Rest) ? 0 : Mathf.CeilToInt(0.045f * Rate);
                int atk = Tie[k] ? 0 : Mathf.CeilToInt(0.004f * Rate);
                int rel = Mathf.CeilToInt(0.008f * Rate);
                int soundLen = len - gap;
                for (int i = 0; i < len && pos < n; i++, pos++)
                {
                    if (Freq[k] == Rest) { s[pos] = 0; continue; }
                    float t = pos / (float)Rate;
                    // 音程のふらつき（ラジオ・テープの揺れ）。±0.8% に抑える（大きいと音程が狂って聞こえる）
                    float wow = 1f + 0.006f * Mathf.Sin(t * 2f * Mathf.PI * 5.3f) + 0.002f * Mathf.Sin(t * 2f * Mathf.PI * 0.7f);
                    phase += Freq[k] * wow / Rate;
                    float sq = (phase % 1.0) < 0.5 ? 1f : -1f;
                    float env = 1f;
                    if (i < atk) env = i / (float)atk;
                    if (i >= soundLen) env = 0f;
                    else if (!nextTied && i > soundLen - rel) env = Mathf.Max(0f, (soundLen - i) / (float)rel);
                    s[pos] = sq * 0.22f * env;
                }
            }

            if (!withNoise) return s;

            // 音が鳴っている所だけ覚えておく（パチッを切れ目に落とさないため）
            var sounding = new bool[n];
            for (int i = 0; i < n; i++) sounding[i] = Mathf.Abs(s[i]) > 1e-4f;

            // ラジオの帯域: 200Hz 高域通過 → 3kHz 低域通過×2
            HighPass(s, 200f); LowPass(s, 3000f); LowPass(s, 3000f);

            // 壊れかけ: ゆっくり遠のく揺れ（音程には触らない）
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] *= 0.85f + 0.15f * Mathf.Sin(t * 2f * Mathf.PI * 0.9f + 1.3f);
            }

            // 砂嵐 ＋ パチッ
            var noise = new float[n];
            // 砂嵐の大きさ。0.06 だと音節の切れ目を埋めて「る・ま」がつながった（切れ目 7dB）。
            // 切れ目は砂嵐より静かにはならないので、砂嵐のほうを下げる。
            for (int i = 0; i < n; i++) noise[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.032f;
            HighPass(noise, 1200f);
            for (int i = 0; i < n; i++)
            {
                s[i] += noise[i];
                // パチッは1秒に3〜4回。多すぎると（初版は毎秒22回）音節の切れ目を埋めて節が崩れる
                // ⚠️ 音節の切れ目には落とさない。落ちるとその切れ目だけ埋まり、2音が1音に聞こえる（実測 8dB）
                if (rnd.NextDouble() < 0.00008 && sounding[i])
                {
                    int w = 20 + rnd.Next(60);
                    float a = (float)(rnd.NextDouble() * 0.4 + 0.15) * (rnd.Next(2) == 0 ? 1 : -1);
                    for (int j = 0; j < w && i + j < n; j++) s[i + j] += a * (1f - j / (float)w);
                }
            }

            // 安いスピーカーの潰れ → 音量をそろえる
            float peak = 0;
            for (int i = 0; i < n; i++) { s[i] = (float)System.Math.Tanh(s[i] * 2.0f) * 0.55f; peak = Mathf.Max(peak, Mathf.Abs(s[i])); }
            if (peak > 0) for (int i = 0; i < n; i++) s[i] *= 0.89f / peak;
            return s;
        }

        static void Bake(float speed)
        {
            var s = Synthesize(speed, true);
            WriteWav(PathFor(speed), s, Rate);
        }

        /// <summary>
        /// 焼いた音（雑音入り）の各音節の真ん中で基音を測り、狙いの音程と比べる。
        /// 自己相関で 200〜500Hz の周期を探す。
        /// </summary>
        public static string Analyze(float speed)
        {
            var s = Synthesize(speed, true);
            var sb = new System.Text.StringBuilder("音節ごとの音程（狙い → 実測）:\n");
            float t0 = 0;
            for (int k = 0; k < Freq.Length; k++)
            {
                float dur = Len[k] * Unit / speed;
                if (Freq[k] > 0)
                {
                    int mid = Mathf.RoundToInt((t0 + dur * 0.45f) * Rate);
                    int win = Mathf.RoundToInt(0.06f * Rate);
                    int a = Mathf.Max(0, mid - win / 2);
                    float best = 0; int bestLag = 0;
                    for (int lag = Rate / 500; lag <= Rate / 200; lag++)
                    {
                        double c = 0, e1 = 0, e2 = 0;
                        for (int i = a; i < a + win && i + lag < s.Length; i++) { c += s[i] * s[i + lag]; e1 += s[i] * s[i]; e2 += s[i + lag] * s[i + lag]; }
                        float r = (float)(c / System.Math.Sqrt(e1 * e2 + 1e-12));
                        if (r > best) { best = r; bestLag = lag; }
                    }
                    float hz = bestLag > 0 ? Rate / (float)bestLag : 0;
                    float cents = hz > 0 ? 1200f * Mathf.Log(hz / Freq[k], 2f) : 0;
                    sb.AppendLine("  " + Syl[k] + " " + NoteName(Freq[k]) + " " + Freq[k].ToString("F0") + "Hz → " + hz.ToString("F0") + "Hz（ずれ " + cents.ToString("+0;-0") + "セント, 相関 " + best.ToString("F2") + "）");
                }
                t0 += dur;
            }
            return sb.ToString();
        }

        static string NoteName(float f)
        {
            if (Mathf.Abs(f - G4) < 1) return "ソ";
            if (Mathf.Abs(f - E4) < 1) return "ミ";
            if (Mathf.Abs(f - D4) < 1) return "レ";
            if (Mathf.Abs(f - C4) < 1) return "ド";
            return "?";
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
