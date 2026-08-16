
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// セーブポイント管理。
//
// 仕様:
//   ・赤/青/緑のボタンを押すとその地点がセーブポイントになる
//   ・誰か1人でも死亡判定になったら「全員」が現在のセーブポイントに戻る
//
// checkpoints[0] はスタート地点。以降 index 1,2,3... がボタン1,2,3に対応する。
// currentIndex だけを同期し、後退はしない(既に進んだ地点より前には戻さない)。
public class CheckpointManager : UdonSharpBehaviour
{
    [Header("セーブポイント(0番はスタート地点)")]
    [SerializeField] private Transform[] checkpoints;

    [Header("進行に応じて開閉する扉(赤開く/赤閉じる など)")]
    [SerializeField] private ToggleDoor[] doors;

    [Header("復帰時の演出(任意・ローカル)")]
    [SerializeField] private GameObject deathEffect;
    [SerializeField] private AudioSource deathSound;

    [UdonSynced] public int currentIndex;

    void Start()
    {
        RefreshDoors();
    }

    // 同期で currentIndex が更新された時(途中参加・他人がボタンを押した時)
    public override void OnDeserialization()
    {
        RefreshDoors();
    }

    private void RefreshDoors()
    {
        if (doors == null)
        {
            return;
        }

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null)
            {
                doors[i].ApplyState();
            }
        }
    }

    // ------------------------------------------------------------
    // セーブポイント登録
    // ------------------------------------------------------------

    // ボタン側から呼ぶ。index はそのボタンに対応するセーブポイント番号。
    public void SetCheckpoint(int index)
    {
        if (index <= currentIndex)
        {
            return;
        }

        if (checkpoints == null || index >= checkpoints.Length)
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        currentIndex = index;
        RequestSerialization();

        RefreshDoors();
    }

    // ------------------------------------------------------------
    // 死亡判定 → 全員復帰
    // ------------------------------------------------------------

    // 罠に触れたクライアントがローカルで呼ぶ。
    public void TriggerDeath()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(RespawnAll));
    }

    // 全員のクライアントで実行される。自分自身だけをテレポートさせる。
    public void RespawnAll()
    {
        Transform point = GetCurrentPoint();
        if (point == null)
        {
            return;
        }

        if (Networking.LocalPlayer != null)
        {
            Networking.LocalPlayer.TeleportTo(point.position, point.rotation);
        }

        if (deathEffect != null)
        {
            deathEffect.SetActive(true);
        }

        if (deathSound != null)
        {
            deathSound.Play();
        }
    }

    private Transform GetCurrentPoint()
    {
        if (checkpoints == null || checkpoints.Length == 0)
        {
            return null;
        }

        int index = currentIndex;
        if (index < 0)
        {
            index = 0;
        }
        if (index >= checkpoints.Length)
        {
            index = checkpoints.Length - 1;
        }

        return checkpoints[index];
    }
}
