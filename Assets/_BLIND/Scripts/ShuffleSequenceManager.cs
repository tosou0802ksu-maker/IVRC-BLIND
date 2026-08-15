
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// 視点シャッフルを起動する「赤→青→緑」ボタンギミック。
// 正しい順番でボタンを押すたびに ViewpointShuffleManager.ShuffleRoles() を呼び、
// 3人の視点役割(エコロケ/サーモ/過去の人)をその場で入れ替える。
// 最後まで押し切るとゴールへの道が繋がる想定(演出はcompleteEffectで表現)。
// 間違えると最初からやり直し(シャッフルは起きない)。
public class ShuffleSequenceManager : UdonSharpBehaviour
{
    [Header("視点シャッフルを実行する対象")]
    [SerializeField] private ViewpointShuffleManager shuffleManager;

    [Header("正解の順番(赤=0, 青=1, 緑=2)")]
    [SerializeField] private int[] correctOrder = new int[] { 0, 1, 2 };

    [Header("完了演出(任意、全員に表示)")]
    [SerializeField] private GameObject completeEffect;
    [SerializeField] private AudioSource completeSound;

    [UdonSynced] public int progressIndex;

    // ShuffleButton から呼ばれる
    public void OnButtonPressed(int buttonId)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (progressIndex < correctOrder.Length && correctOrder[progressIndex] == buttonId)
        {
            progressIndex++;

            bool isComplete = progressIndex >= correctOrder.Length;
            if (isComplete)
            {
                progressIndex = 0;
            }

            RequestSerialization();

            // 正しいボタンを押すたびに毎回シャッフル
            if (shuffleManager != null)
            {
                shuffleManager.ShuffleRoles();
            }

            if (isComplete)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlayCompleteEffect));
            }

            return;
        }

        // 間違えたら最初からやり直し(シャッフルはしない)
        progressIndex = 0;
        RequestSerialization();
    }

    public void PlayCompleteEffect()
    {
        if (completeEffect != null)
        {
            completeEffect.SetActive(true);
        }

        if (completeSound != null)
        {
            completeSound.Play();
        }
    }
}
