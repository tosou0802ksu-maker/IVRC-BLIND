
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// 「複数ボタン鍵ドア」のボタン1個分。
//
// 押されると LampPuzzleManager に通知し、同時に自分自身へ「残熱」を与える。
// 残熱はサーモ役だけに見えるので、
//   「さっき誰かがこのボタンを押した」という履歴をサーモ役だけが読める。
// これによりサーモ役が単なるon/off読み上げ係で終わらなくなる。
public class PuzzleButton : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private LampPuzzleManager puzzleManager;

    [Header("このボタンのID(LampPuzzleManagerのcorrectOrderと対応させる)")]
    [SerializeField] private int buttonId;

    [Header("残熱表現(サーモ役だけに見える)")]
    [Tooltip("ThermalHeatシェーダーのマテリアルを持つRendererを制御するDynamicThermalObject。")]
    [SerializeField] private DynamicThermalObject thermalFeedback;

    [Tooltip("押した瞬間の熱の強さ。ここから0へ向かって冷めていく。")]
    [SerializeField] private float pressHeat = 1.0f;

    [Header("押した時の音(任意)")]
    [SerializeField] private AudioSource pressSound;

    public override void Interact()
    {
        if (puzzleManager != null)
        {
            puzzleManager.OnButtonPressed(buttonId);
        }

        // 残熱と音は全員に伝える(サーモ役が他人の押した跡を見るため)
        SendCustomNetworkEvent(NetworkEventTarget.All, "OnPressedGlobal");
    }

    // ネットワーク越しに全員のクライアントで実行される
    public void OnPressedGlobal()
    {
        if (thermalFeedback != null)
        {
            // 一気に熱くしてから、ゆっくり冷ましていく
            thermalFeedback.SetHeatInstant(pressHeat);
            thermalFeedback.SetHeat(0.0f);
        }

        if (pressSound != null)
        {
            pressSound.Play();
        }
    }
}
