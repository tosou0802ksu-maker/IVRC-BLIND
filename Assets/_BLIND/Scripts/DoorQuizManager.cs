
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// ドア選択式クイズ(1問分)の「扉の移動」担当。
//
// 仕様:
//   ・どの扉でも Interact → その扉が指定座標へ瞬間移動(開く)。正誤はまだ分からない
//   ・開いている扉は1枚だけ。別の扉を開けると前の扉は閉じる
//   ・正誤・効果音・リスポーンは扉の先に置いた DoorPassJudge が担当する
//     (くぐった時に判定するため。不正解なら DoorPassJudge が CloseDoor を呼ぶ)
//   ・正解の扉をくぐったら MarkSolved で開きっぱなしに固定する
//   ・3セット配置しても各インスタンスが独立しているため混線しない
//
// ドアの閉じた位置は Start() で自動記憶する。
// 開いた位置は doorXOpenPosition で Inspector から指定する（不正解の扉も必要）。
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DoorQuizManager : UdonSharpBehaviour
{
    [Header("ドア(3枚)")]
    [SerializeField] private Transform door0;
    [SerializeField] private Transform door1;
    [SerializeField] private Transform door2;

    [Header("ドアの開いた位置(ローカル座標・3つ分)")]
    [SerializeField] private Vector3 door0OpenPosition;
    [SerializeField] private Vector3 door1OpenPosition;
    [SerializeField] private Vector3 door2OpenPosition;

    // 開いている扉の番号。-1 = 全部閉じている
    [UdonSynced] private int openDoorIndex = -1;

    // 正解の扉をくぐったら 1。以降は扉を動かさない
    [UdonSynced] private int solvedState;

    // 閉じた位置を自動記憶
    private Vector3 door0ClosedPosition;
    private Vector3 door1ClosedPosition;
    private Vector3 door2ClosedPosition;

    void Start()
    {
        if (door0 != null) door0ClosedPosition = door0.localPosition;
        if (door1 != null) door1ClosedPosition = door1.localPosition;
        if (door2 != null) door2ClosedPosition = door2.localPosition;

        ApplyState();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    // DoorQuizChoice から呼ばれる。名前は既存の配線を活かすためそのまま
    public void SubmitChoice(int doorIndex)
    {
        if (solvedState != 0) return;
        if (openDoorIndex == doorIndex) return;

        TakeOwnership();
        openDoorIndex = doorIndex;
        RequestSerialization();
        ApplyState();
    }

    // DoorPassJudge から呼ばれる（不正解の扉をくぐった本人だけが呼ぶ）
    public void CloseDoor(int doorIndex)
    {
        if (solvedState != 0) return;
        if (openDoorIndex != doorIndex) return;

        TakeOwnership();
        openDoorIndex = -1;
        RequestSerialization();
        ApplyState();
    }

    // DoorPassJudge から呼ばれる（正解の扉をくぐった本人だけが呼ぶ）
    public void MarkSolved(int doorIndex)
    {
        if (solvedState != 0) return;

        TakeOwnership();
        solvedState = 1;
        openDoorIndex = doorIndex;
        RequestSerialization();
        ApplyState();
    }

    public bool IsOpen(int doorIndex)
    {
        return openDoorIndex == doorIndex;
    }

    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    private void ApplyState()
    {
        MoveDoor(0, openDoorIndex == 0);
        MoveDoor(1, openDoorIndex == 1);
        MoveDoor(2, openDoorIndex == 2);
    }

    private void MoveDoor(int index, bool open)
    {
        switch (index)
        {
            case 0:
                if (door0 != null) door0.localPosition = open ? door0OpenPosition : door0ClosedPosition;
                break;
            case 1:
                if (door1 != null) door1.localPosition = open ? door1OpenPosition : door1ClosedPosition;
                break;
            case 2:
                if (door2 != null) door2.localPosition = open ? door2OpenPosition : door2ClosedPosition;
                break;
        }
    }
}
