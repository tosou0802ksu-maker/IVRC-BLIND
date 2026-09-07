using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

// 持ち運べる物の共通スクリプト。
//
// 鍵（room11）・アヒル（room6）など、「拾って別の場所へ持っていく」物すべてに付ける。
// 掴む・投げる・受け渡すこと自体は VRChat 標準の VRCPickup が全部やるので、
// ここが受け持つのは **無くさないための後始末** だけ。
//
// ⚠️ VRCPickup だけでは位置が同期しない。必ず VRCObjectSync も一緒に付けること。
//    付け忘れると「自分だけ鍵を持っていて、他の2人には棚に置いたまま見える」
//    という、この作品でいちばん厄介な種類の食い違いになる。
//
// ⚠️ このスクリプト自体は同期しない(None)。位置は VRCObjectSync の担当で、
//    こちらが二重に同期すると取り合いになる。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarryableItem : UdonSharpBehaviour
{
    [Header("位置同期。VRCObjectSync を入れる（必須）")]
    [SerializeField] private VRCObjectSync objectSync;

    [Header("戻す条件")]
    [Tooltip("この高さより下に落ちたら置き場所へ戻す。床下に抜けたとき用。")]
    [SerializeField] private float respawnBelowY = -1.5f;
    [Tooltip("置き場所からこれ以上離れたら戻す。部屋の外へ投げ捨てられたとき用。")]
    [SerializeField] private float respawnBeyond = 45.0f;

    [Header("置き場所。空なら開始位置")]
    [SerializeField] private Transform homeAnchor;

    private Vector3 home;
    private bool held;

    void Start()
    {
        home = homeAnchor != null ? homeAnchor.position : transform.position;
    }

    public override void OnPickup()
    {
        held = true;
    }

    public override void OnDrop()
    {
        held = false;
    }

    /// <summary>誰かが持っているか。受け取り口(ItemReceptacle)から見る。</summary>
    public bool IsHeld()
    {
        return held;
    }

    void Update()
    {
        // 持たれている間は何もしない。持ち主が部屋の外へ運ぶのは正しい遊び方。
        if (held)
        {
            return;
        }

        // ⚠️ 戻す処理はオーナーだけ。全員が戻すと取り合いになって震える。
        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        Vector3 p = transform.position;
        bool lost = p.y < respawnBelowY;
        if (!lost)
        {
            Vector3 d = p - home;
            lost = d.sqrMagnitude > respawnBeyond * respawnBeyond;
        }
        if (!lost)
        {
            return;
        }

        if (objectSync != null)
        {
            objectSync.Respawn();
        }
        else
        {
            transform.position = home;
            transform.rotation = Quaternion.identity;
        }
    }
}
