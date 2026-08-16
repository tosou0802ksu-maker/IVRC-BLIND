
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 落とし穴 / レーザー / シャワー / スプリンクラー など、
// 触れると死亡判定になるゾーンの共通スクリプト。
//
// GameObject に IsTrigger を有効にした Collider を付けてアタッチする。
// hazardKind で GameManager のどちらの死亡処理を呼ぶかを切り替える。
public class HazardZone : UdonSharpBehaviour
{
    [Header("接続先(こちらを優先。誰か1人が触れたら全員がセーブポイントへ)")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("旧方式のフォールバック(checkpointManager未設定の時だけ使う)")]
    [SerializeField] private GameManager gameManager;

    [Header("旧方式の種類: 0 = 落とし穴系 / 1 = サーマル系")]
    [SerializeField] private int hazardKind;

    [Header("警告表現(任意)")]
    [Tooltip("サーモ役に見せる熱源など。常時オンで構わない。")]
    [SerializeField] private DynamicThermalObject thermalWarning;
    [SerializeField] private float warningHeat = 1.0f;

    [Header("発動間隔(秒)。0なら常時発動")]
    [Tooltip("レーザーやシャワーのように断続的に作動させたい場合に使う。")]
    [SerializeField] private float activeDuration = 0.0f;
    [SerializeField] private float inactiveDuration = 0.0f;

    [Header("作動中だけ表示するオブジェクト(任意)")]
    [SerializeField] private GameObject activeVisual;

    private bool isActive = true;
    private float timer;

    void Start()
    {
        if (thermalWarning != null)
        {
            thermalWarning.SetHeatInstant(warningHeat);
        }

        ApplyActiveState();
    }

    void Update()
    {
        // 断続動作が設定されていない場合は常時作動のまま
        if (activeDuration <= 0.0f || inactiveDuration <= 0.0f)
        {
            return;
        }

        timer += Time.deltaTime;

        float limit = isActive ? activeDuration : inactiveDuration;

        if (timer >= limit)
        {
            timer = 0.0f;
            isActive = !isActive;
            ApplyActiveState();
        }
    }

    private void ApplyActiveState()
    {
        if (activeVisual != null)
        {
            activeVisual.SetActive(isActive);
        }

        if (thermalWarning != null)
        {
            thermalWarning.SetHeat(isActive ? warningHeat : 0.0f);
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        if (!isActive)
        {
            return;
        }

        if (checkpointManager != null)
        {
            checkpointManager.TriggerDeath();
            return;
        }

        if (gameManager == null)
        {
            return;
        }

        if (hazardKind == 0)
        {
            gameManager.TriggerPitfallDeath();
        }
        else
        {
            gameManager.TriggerThermalDeath();
        }
    }
}
