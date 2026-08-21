
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// 「複数ボタン鍵ドア」のボタン1個分。
//
// 押されると LampPuzzleManager に通知し、同時に自分自身へ「残熱」を与える。
// 残熱はサーモ役だけに見えるので、
//   「さっき誰かがこのボタンを押した」という履歴をサーモ役だけが見える。
// これによりサーモ役が単なるon/off読み上げ係で終わらなくなる。
//
// 偽ボタン(isFake):
//   エコロケ・サーモからは本物と全く同じ形・同じ熱にしか見えない罠。
//   過去の人だけが memoryColorRenderer の色の違いで見分けられる。
//   押すとパズルが振り出しに戻る(LampPuzzleManager.OnFakeButtonPressed)。
public class PuzzleButton : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private LampPuzzleManager puzzleManager;

    [Header("このボタンのID(LampPuzzleManagerのcorrectOrderと対応させる)")]
    [SerializeField] private int buttonId;

    [Header("偽ボタン設定")]
    [Tooltip("trueなら偽ボタン。押すとパズルがリセットされる罠。")]
    [SerializeField] private bool isFake;

    [Tooltip("過去の人だけに見える色の違い。Memoryレイヤーに置いたRendererを指定すると、" +
             "Start()時に本物色/偽物色へ自動で塗り分ける(ワールド班がマテリアルを作り分ける必要がない)。")]
    [SerializeField] private Renderer memoryColorRenderer;
    [SerializeField] private Color realColor = Color.white;
    [SerializeField] private Color fakeColor = new Color(1f, 0.85f, 0.85f); // ぱっと見はほぼ同じ、よく見ると違う色

    [Header("残熱表現(サーモ役だけに見える)")]
    [Tooltip("ThermalHeatシェーダーのマテリアルを持つRendererを制御するDynamicThermalObject。")]
    [SerializeField] private DynamicThermalObject thermalFeedback;

    [Tooltip("押した瞬間の熱の強さ。ここから0へ向かって冷めていく。")]
    [SerializeField] private float pressHeat = 1.0f;

    [Header("押した時の音(任意)")]
    [SerializeField] private AudioSource pressSound;

    void Start()
    {
        if (memoryColorRenderer != null)
        {
            var block = new MaterialPropertyBlock();
            memoryColorRenderer.GetPropertyBlock(block);
            block.SetColor("_Color", isFake ? fakeColor : realColor);
            memoryColorRenderer.SetPropertyBlock(block);
        }
    }

    public override void Interact()
    {
        if (isFake)
        {
            if (puzzleManager != null)
            {
                puzzleManager.OnFakeButtonPressed();
            }
        }
        else if (puzzleManager != null)
        {
            puzzleManager.OnButtonPressed(buttonId);
        }

        // 残熱と音は全員に伝える(サーモ役が他人の押した跡を見るため)
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnPressedGlobal));
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
