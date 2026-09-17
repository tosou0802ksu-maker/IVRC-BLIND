using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// プールに落ちて動けなくなった人を引き上げる保険（room6）。
//
// プールの縁には階段を6箇所付けてあるので、普通は自力で上がれる。
// ただし水底はアヒルだらけで、**囲まれて一歩も動けない窪みが残る**
// （実測：水底で立てる 322 升のうち 78 升がどの階段にも繋がっていない）。
// アヒルを減らせば消せるが、それは部屋の見た目を削ることになる。
// 3人協力なので1人が詰まると全員が止まる。形で潰しきれない以上、保険を置く。
//
// ⚠️ 「水面より下なら即引き上げる」にしてはいけない。
//    階段を下りて水底を歩くのは正しい遊び方（近道のアヒル渡りの下でもある）で、
//    それを毎回巻き戻すと部屋が壊れる。
//    **下にいて、かつ動けていない**ときだけ引き上げる。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PoolRescue : UdonSharpBehaviour
{
    [Header("見る範囲（ワールド座標）")]
    [SerializeField] private Vector3 areaMin;
    [SerializeField] private Vector3 areaMax;

    [Tooltip("この高さより下にいると「落ちている」とみなす。")]
    [SerializeField] private float belowY = -0.35f;

    [Header("引き上げる条件")]
    [Tooltip("この秒数のあいだ、下にいて動けていないこと。")]
    [SerializeField] private float stuckSeconds = 8.0f;
    [Tooltip("この距離より動けていなければ「詰まっている」。歩けている人は救わない。")]
    [SerializeField] private float stuckRadius = 1.5f;

    [Header("戻す場所")]
    [SerializeField] private Transform anchor;

    private float timer;
    private Vector3 mark;
    private bool marked;

    void Update()
    {
        VRCPlayerApi p = Networking.LocalPlayer;
        if (!Utilities.IsValid(p) || anchor == null)
        {
            return;
        }

        Vector3 pos = p.GetPosition();
        bool down = pos.y < belowY
                 && pos.x > areaMin.x && pos.x < areaMax.x
                 && pos.z > areaMin.z && pos.z < areaMax.z;

        if (!down)
        {
            timer = 0f;
            marked = false;
            return;
        }

        if (!marked)
        {
            mark = pos;
            marked = true;
            timer = 0f;
            return;
        }

        // 動けているなら放っておく。自力で歩いている人を巻き戻さない。
        Vector3 d = pos - mark;
        d.y = 0f;
        if (d.sqrMagnitude > stuckRadius * stuckRadius)
        {
            mark = pos;
            timer = 0f;
            return;
        }

        timer += Time.deltaTime;
        if (timer < stuckSeconds)
        {
            return;
        }

        timer = 0f;
        marked = false;
        p.TeleportTo(anchor.position, anchor.rotation);
    }
}
