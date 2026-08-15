
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// 「複数ボタン鍵ドア」の進行管理。
//
/// 役割ごとの情報分担:
///   エコロケ  : 部屋とボタンの「形」だけ見える  -> ボタンの位置を伝える担当
///   サーモ    : ランプのon/offだけ見える        -> 今どこまで消えたかを伝える担当
///   過去の人  : on/off以外(看板・ランプの色)    -> 正しい順番を読み解く担当
///
/// 正しい順番でボタンを押すとランプが1つずつ消え、全部消えるとドアが開く。
/// 順番を間違えると全ランプが点き直る(= 総当たりで解けないようにする)。
/// </summary>
public class LampPuzzleManager : UdonSharpBehaviour
{
    [Header("ランプ(消える順に並べる)")]
    [SerializeField] private PuzzleLamp[] lamps;

    [Header("正解のボタンID(正しく押す順に並べる)")]
    [SerializeField] private int[] correctOrder;

    [Header("解除時に消す障害物(ドアの壁やコライダー)")]
    [SerializeField] private GameObject doorBlocker;

    [Header("解除時に有効化する演出(任意)")]
    [SerializeField] private GameObject solvedEffect;

    [UdonSynced] public int progressIndex;
    [UdonSynced] public bool isSolved;

    void Start()
    {
        ApplyState();
    }

    // PuzzleButton から呼ばれる
    public void OnButtonPressed(int buttonId)
    {
        if (isSolved)
        {
            return;
        }

        // 進行状況を書き換えるのでオーナーを取る
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (progressIndex < correctOrder.Length && correctOrder[progressIndex] == buttonId)
        {
            progressIndex++;

            if (progressIndex >= correctOrder.Length)
            {
                isSolved = true;
            }
        }
        else
        {
            // 間違えたら最初からやり直し
            progressIndex = 0;
        }

        RequestSerialization();
        ApplyState();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    // 同期された進行状況を見た目に反映する
    private void ApplyState()
    {
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null)
            {
                continue;
            }

            // 消えた本数 = progressIndex。それより後ろのランプはまだ点いている。
            lamps[i].SetLampOn(i >= progressIndex);
        }

        if (doorBlocker != null)
        {
            doorBlocker.SetActive(!isSolved);
        }

        if (solvedEffect != null)
        {
            solvedEffect.SetActive(isSolved);
        }
    }

    // 外部から強制リセットしたい場合(デバッグ・審査用短縮版など)
    public void ResetPuzzle()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        progressIndex = 0;
        isSolved = false;
        RequestSerialization();
        ApplyState();
    }
}
