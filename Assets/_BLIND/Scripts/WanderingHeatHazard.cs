
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 一定区間を彷徨う熱の塊。サーモ役にだけ常時見える移動する熱源で、
// 触れると即死する。
//
// エコロケ役・過去の人役には見えない(通常視点/記憶視点のレイヤーには乗せない)ので、
// サーモ役が「今どこにいるか」を逐一実況して他2人を誘導する必要がある。
// これによりサーモ役にも常時の索敵という能動的な仕事が生まれる。
public class WanderingHeatHazard : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("巡回ポイント(2点以上、ループする)")]
    [SerializeField] private Transform[] waypoints;

    [Header("移動速度")]
    [SerializeField] private float moveSpeed = 1.5f;

    [Header("サーモ役に見える熱表現")]
    [SerializeField] private DynamicThermalObject thermalIndicator;
    [SerializeField] private float wanderHeat = 1.0f;

    [Header("死亡判定コライダー")]
    [SerializeField] private Collider hazardCollider;

    private int targetIndex;

    void Start()
    {
        if (thermalIndicator != null)
        {
            thermalIndicator.SetHeatInstant(wanderHeat);
        }

        if (waypoints != null && waypoints.Length > 0)
        {
            transform.position = waypoints[0].position;
        }
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            return;
        }

        Transform target = waypoints[targetIndex];
        if (target == null)
        {
            targetIndex = (targetIndex + 1) % waypoints.Length;
            return;
        }
        Vector3 toTarget = target.position - transform.position;

        if (toTarget.magnitude <= 0.05f)
        {
            targetIndex = (targetIndex + 1) % waypoints.Length;
            return;
        }

        transform.position += toTarget.normalized * moveSpeed * Time.deltaTime;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        if (checkpointManager != null)
        {
            checkpointManager.TriggerDeath();
        }
    }
}
