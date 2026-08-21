using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 実機テスト用の数値を一括適用する。
    ///
    /// UdonSharp のコンポーネントは「C#のプロキシ」と「UdonBehaviour」の2つで1組で、
    /// インスペクタやスクリプトからプロキシ側の値を書いただけでは UdonBehaviour に伝わらない。
    /// エディタ上は正しい値に見えるのに、アップロードすると既定値のまま動く。
    /// 必ず CopyProxyToUdon まで通すこと。このツールはそこまでやる。
    /// </summary>
    public static class BlindTuning1200
    {
        // --- タスクA: エコロケのテンポ ---
        //
        // 1回のパルスで輪郭が点いている時間は
        //   distance*delayPerMeter (届くまで) + distance*delayPerMeter (保持) + glowDuration (減衰)
        // 最遠(12m)で 0.54 + 0.54 + 0.6 = 1.68秒。間隔2.2秒なので暗闇が約0.5秒残る。
        // 近く(4m)なら 0.18 + 0.18 + 0.6 = 0.96秒で、1.2秒以上暗い。
        //
        // 「間隔を縮める」だけをやると光が途切れなくなり、
        // 暗闇と一瞬の安心感というこのゲームで一番効いている対比が消える。
        // 必ず glowDuration と delayPerMeter を同じ比率で削ること。
        const float PulseRange = 12f;
        const float PulseAngle = 55f;
        const float PulseInterval = 2.2f;
        const float DelayPerMeter = 0.045f;
        const float GlowDuration = 0.6f;
        const bool AllowManualPulse = true;
        const float ManualCooldown = 1.1f;

        [MenuItem("BLIND/vision/0. 実機テスト用の数値を一括適用", priority = 0)]
        public static void ApplyAll()
        {
            var msg = Apply();
            Debug.Log(msg);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        /// <summary>ダイアログを出さない版。自動化からはこちらを呼ぶこと。</summary>
        public static string Apply()
        {
            var log = new StringBuilder();

            // 空のシーンに対して SaveScene を走らせると、32MBの MainWorld を
            // 空ファイルで上書きしてワールドが消える。
            // 実際に、シーンがエディタ上で空になっている状態を踏んだことがあるので、
            // 保存する処理を持つツールは必ずここを通すこと。
            // ルート数では判定しないこと。MainWorld のルートは "=== ROOMS ===" など
            // 4個しかなく、正常な状態でも少ない。中身の実数で見る。
            var scene = EditorSceneManager.GetActiveScene();
            int renderers = Object.FindObjectsOfType<Renderer>(true).Length;
            if (renderers < 100)
            {
                return "中止しました。\n開いているシーン「" + scene.name + "」の Renderer が "
                     + renderers + "個 しかありません。\n"
                     + "シーンが正しく読み込まれていない状態です。この状態で保存すると\n"
                     + "MainWorld.unity が空で上書きされ、ワールドが失われます。\n\n"
                     + "シーンファイルにマージコンフリクト(<<<<<<<)が混入していないか確認し、\n"
                     + "Unity で " + scene.path + " を開き直してから、もう一度実行してください。";
            }

            // --- タスクB: サーマル材質を作り直す（_Dim の変更を反映） ---
            log.AppendLine("[B] " + BlindThermalTable.BuildMaterials().Split('\n')[0]);
            log.AppendLine("    サーマル材質を再生成しました（_Dim 変更を反映）");

            // --- タスクA-1: EchoEmitter ---
            int emitters = 0;
            foreach (var em in Object.FindObjectsOfType<EchoEmitter>(true))
            {
                var so = new SerializedObject(em);
                SetF(so, "pulseRange", PulseRange);
                SetF(so, "pulseAngle", PulseAngle);
                SetF(so, "pulseInterval", PulseInterval);
                SetF(so, "delayPerMeter", DelayPerMeter);
                SetF(so, "manualCooldown", ManualCooldown);
                SetB(so, "allowManualPulse", AllowManualPulse);
                SetB(so, "occlusionCheck", false);
                so.ApplyModifiedPropertiesWithoutUndo();
                PushToUdon(em);
                emitters++;
            }
            log.AppendLine("[A] EchoEmitter " + emitters + "個: 射程" + PulseRange + "m / " + PulseAngle
                         + "度 / 間隔" + PulseInterval + "秒 / 手動パルス"
                         + (AllowManualPulse ? "有効(CD " + ManualCooldown + "秒)" : "無効"));

            // --- タスクA-2: EchoReceiver ---
            int receivers = 0, noUdon = 0;
            foreach (var rc in Object.FindObjectsOfType<EchoReceiver>(true))
            {
                var so = new SerializedObject(rc);
                SetF(so, "glowDuration", GlowDuration);
                so.ApplyModifiedPropertiesWithoutUndo();
                if (!PushToUdon(rc)) noUdon++;
                receivers++;
            }
            log.AppendLine("[A] EchoReceiver " + receivers + "個: 点灯" + GlowDuration + "秒");
            if (noUdon > 0)
                log.AppendLine("    ⚠ UdonBehaviour が無い受信機が " + noUdon
                             + "個。実機で光りません。vision/2 を実行し直してください。");

            // --- タスクC: NowOnly の現状 ---
            log.AppendLine("[C] " + BlindNowOnlyTagger.Report().Replace("\n", "\n    "));

            AssetDatabase.SaveAssets();
            bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            log.AppendLine("シーン保存: " + (saved ? "成功" : "失敗"));
            return log.ToString();
        }

        static void SetF(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = v;
        }

        static void SetB(SerializedObject so, string name, bool v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = v;
        }

        /// <summary>プロキシに書いた値を UdonBehaviour 側へ確実に移す。</summary>
        static bool PushToUdon(UdonSharp.UdonSharpBehaviour proxy)
        {
            var ub = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (ub == null) return false;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(proxy);
            EditorUtility.SetDirty(ub);
            return true;
        }
    }
}
