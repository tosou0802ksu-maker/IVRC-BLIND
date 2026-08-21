
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// ドア選択式クイズのドア1枚分。
// プレイヤーがInteractすると、接続先の DoorQuizManager に選択を送信する。
// ドアに表示する問題文や選択肢ラベルは Memory レイヤーの子オブジェクトとして配置する。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DoorQuizChoice : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private DoorQuizManager doorQuizManager;

    [Header("このドアの番号(0, 1, 2)")]
    [SerializeField] private int doorIndex;

    public override void Interact()
    {
        if (doorQuizManager != null)
        {
            doorQuizManager.SubmitChoice(doorIndex);
        }
    }
}
