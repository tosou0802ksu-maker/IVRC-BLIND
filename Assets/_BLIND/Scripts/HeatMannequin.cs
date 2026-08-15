
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 人型の置物(マネキン/人形)に「呼吸のような」微弱な熱の揺らぎを持たせる。
// 死亡判定は一切ない、純粋な演出用オブジェクト。
//
// サーモ役から見ると、ただの物のはずが僅かに体温のような揺らぎを持って見える
// ("生きているのか？")という違和感を狙う。
// エコロケ役・過去の人役には普通の人形/物として見える(通常のレイヤー構成のまま)。
public class HeatMannequin : UdonSharpBehaviour
{
    [Header("サーモ役に見える熱表現")]
    [SerializeField] private DynamicThermalObject thermalIndicator;

    [Header("揺らぎの中心値と振れ幅")]
    [Tooltip("平均的な体温っぽさ。0だと完全に無反応(死体寄り)、1に近いほど生きてる感が強い。")]
    [SerializeField] private float baseHeat = 0.3f;
    [SerializeField] private float pulseAmount = 0.08f;

    [Header("呼吸の周期(秒)")]
    [SerializeField] private float pulsePeriod = 4.0f;

    private float elapsed;

    void Update()
    {
        if (thermalIndicator == null)
        {
            return;
        }

        elapsed += Time.deltaTime;

        float wave = Mathf.Sin(elapsed / pulsePeriod * Mathf.PI * 2.0f);
        float heat = Mathf.Clamp01(baseHeat + wave * pulseAmount);

        thermalIndicator.SetHeatInstant(heat);
    }
}
