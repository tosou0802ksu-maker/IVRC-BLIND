
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 間欠泉のように定期的に噴き出す熱源トラップ。
//
// サイクル: 待機(冷) -> 予兆(じわじわ熱くなる) -> 噴出(即死判定) -> 待機 に戻る
//
// サーモ役はこの「予兆」段階の温度上昇を見て噴出タイミングを予測し、
// 他の2人に「もうすぐ噴くから離れて」と伝える役目を持つ。
// on/offの二値ではなく連続値にしているのはこの予測行為を成立させるため。
public class HeatGeyser : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("サーモ役に見える熱表現")]
    [SerializeField] private DynamicThermalObject thermalIndicator;

    [Header("噴出時の視覚演出(パーティクルなど、任意)")]
    [SerializeField] private GameObject eruptionVisual;

    [Header("死亡判定コライダー(噴出中のみ有効)")]
    [Tooltip("IsTriggerのCollider。OnPlayerTriggerEnterで判定する。")]
    [SerializeField] private Collider hazardCollider;

    [Header("タイミング(秒)")]
    [SerializeField] private float idleDuration = 4.0f;
    [SerializeField] private float warmupDuration = 2.5f;
    [SerializeField] private float eruptionDuration = 1.0f;

    // 0 = idle, 1 = warmup, 2 = eruption
    private int phase;
    private float timer;

    void Start()
    {
        EnterPhase(0);
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (phase == 0 && timer >= idleDuration)
        {
            EnterPhase(1);
        }
        else if (phase == 1)
        {
            // 予兆中は熱を0->1へ滑らかに上げていく(サーモ役への警告)
            float t = Mathf.Clamp01(timer / warmupDuration);
            if (thermalIndicator != null)
            {
                thermalIndicator.SetHeatInstant(t);
            }

            if (timer >= warmupDuration)
            {
                EnterPhase(2);
            }
        }
        else if (phase == 2 && timer >= eruptionDuration)
        {
            EnterPhase(0);
        }
    }

    private void EnterPhase(int newPhase)
    {
        phase = newPhase;
        timer = 0.0f;

        bool erupting = newPhase == 2;

        if (eruptionVisual != null)
        {
            eruptionVisual.SetActive(erupting);
        }

        if (hazardCollider != null)
        {
            hazardCollider.enabled = erupting;
        }

        if (thermalIndicator != null)
        {
            thermalIndicator.SetHeatInstant(newPhase == 0 ? 0.0f : (erupting ? 1.0f : 0.0f));
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        if (phase != 2)
        {
            return;
        }

        if (checkpointManager != null)
        {
            checkpointManager.TriggerDeath();
        }
    }
}
