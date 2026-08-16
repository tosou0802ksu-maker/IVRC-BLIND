
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 感圧板1枚分。
//
// エコロケ役だけが「床に板がある」形として認識できる想定なので、
// 見た目はEchoレイヤーに置く。
//
// VRChatの OnPlayerTriggerEnter/Exit は全クライアントで全プレイヤー分呼ばれるため、
// 踏んでいる人数はローカル集計だけで全員一致する。同期変数は不要。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PressurePlate : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private PressurePlateDoor door;

    [Header("踏まれている間だけ表示する目印(任意)")]
    [SerializeField] private GameObject pressedVisual;

    [Header("踏んだ時の音(任意)")]
    [SerializeField] private AudioSource pressSound;

    // 板の上にいるプレイヤーのplayerId。最大8人分あれば足りる。
    private int[] insideIds = new int[8];
    private int insideCount;

    public bool IsPressed()
    {
        return insideCount > 0;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid())
        {
            return;
        }

        if (IndexOfPlayer(player.playerId) >= 0)
        {
            return;
        }

        if (insideCount >= insideIds.Length)
        {
            return;
        }

        insideIds[insideCount] = player.playerId;
        insideCount++;

        if (insideCount == 1)
        {
            OnPressedChanged(true);
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null)
        {
            return;
        }

        RemovePlayer(player.playerId);
    }

    // 板の上にいる状態でワールドから抜けた場合の取りこぼし対策
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null)
        {
            return;
        }

        RemovePlayer(player.playerId);
    }

    private void RemovePlayer(int playerId)
    {
        int index = IndexOfPlayer(playerId);
        if (index < 0)
        {
            return;
        }

        insideIds[index] = insideIds[insideCount - 1];
        insideCount--;

        if (insideCount == 0)
        {
            OnPressedChanged(false);
        }
    }

    private int IndexOfPlayer(int playerId)
    {
        for (int i = 0; i < insideCount; i++)
        {
            if (insideIds[i] == playerId)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnPressedChanged(bool pressed)
    {
        if (pressedVisual != null)
        {
            pressedVisual.SetActive(pressed);
        }

        if (pressed && pressSound != null)
        {
            pressSound.Play();
        }

        if (door != null)
        {
            door.RefreshDoor();
        }
    }
}
