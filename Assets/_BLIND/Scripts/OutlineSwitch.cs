
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class OutlineSwitch : UdonSharpBehaviour
{
    [SerializeField]
    private OutlineController targetController;

    // 現在のオン/オフ状態
    private bool isOn = false;

    // プレイヤーがオブジェクトをクリック（Interact）した時に呼ばれる
    public override void Interact()
    {
        if (targetController == null)
        {
            return;
        }

        // 押すたびに ON / OFF を反転させる
        isOn = !isOn;

        // OutlineController に flag を送信する
        targetController.SetHighlight(isOn);
    }
}
