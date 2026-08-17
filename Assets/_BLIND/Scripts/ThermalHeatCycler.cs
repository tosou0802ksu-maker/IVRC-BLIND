
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// サーモ視点の動作確認用。DynamicThermalObject の熱量を一定周期で
// 上げ下げして、色が 紫→青→緑→黄→赤 と滑らかに変化するかを確認する。
//
// 本番のギミック(スプリンクラーで冷える、機械が発熱する等)でも
// 同じように DynamicThermalObject.SetHeat() を呼べばよい。
// これはあくまでテスト用なので、完成時は外してよい。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ThermalHeatCycler : UdonSharpBehaviour
{
    [Header("熱量を動かす対象")]
    [SerializeField] private DynamicThermalObject target;

    [Header("熱量の範囲")]
    [SerializeField] private float minHeat = 0f;
    [SerializeField] private float maxHeat = 1f;

    [Header("一往復にかける秒数")]
    [SerializeField] private float cycleSeconds = 6f;

    private float timer;

    void Update()
    {
        if (target == null || cycleSeconds <= 0f)
        {
            return;
        }

        timer += Time.deltaTime;
        if (timer >= cycleSeconds)
        {
            timer -= cycleSeconds;
        }

        // 0→1→0 を繰り返す三角波
        float phase = timer / cycleSeconds;
        float t = phase < 0.5f ? (phase * 2f) : ((1f - phase) * 2f);

        target.SetHeatInstant(Mathf.Lerp(minHeat, maxHeat, t));
    }
}
