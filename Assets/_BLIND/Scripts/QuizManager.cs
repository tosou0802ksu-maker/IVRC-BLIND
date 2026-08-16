
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// 扉の前の「クイズ部屋」。
//
// 仕様:
//   ・問題文と選択肢は文字なので「過去の人」だけが読める(Memoryレイヤーに置く)
//   ・出題内容は、ここまで通ってきた部屋にあった物の色や個数
//   ・解答権は一回。選んだ瞬間に確定する
//   ・正解 → 扉が開く / 不正解 → 足元が抜けて落とし穴 → セーブポイントへ
//
// 不正解時は state を未回答に戻すので、復帰後にもう一度挑戦できる。
// (完全に一回きりにすると詰むため。ペナルティは落下と巻き戻し)
public class QuizManager : UdonSharpBehaviour
{
    [Header("正解の選択肢番号(0始まり)")]
    [SerializeField] private int correctChoice;

    [Header("正解したら開く扉(閉じている間だけ有効なオブジェクト)")]
    [SerializeField] private GameObject doorBody;

    [Header("不正解で開く床。抜けた先に落とし穴のHazardZoneを置く")]
    [SerializeField] private GameObject trapFloor;

    [Header("trapFloorを使わず直接セーブポイントに戻す場合はこちらを設定")]
    [SerializeField] private CheckpointManager checkpointManager;

    [Header("演出(任意)")]
    [SerializeField] private AudioSource correctSound;
    [SerializeField] private AudioSource wrongSound;
    [SerializeField] private GameObject correctEffect;

    [Header("不正解の床が戻るまでの秒数")]
    [SerializeField] private float trapResetDelay = 3.0f;

    // 0 = 未回答 / 1 = 正解済み
    [UdonSynced] public int solvedState;

    void Start()
    {
        ApplyState();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    // QuizChoiceButton から呼ばれる
    public void SubmitAnswer(int choice)
    {
        // 既に正解済みなら何もしない
        if (solvedState != 0)
        {
            return;
        }

        if (choice == correctChoice)
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            solvedState = 1;
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnCorrect));
            return;
        }

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnWrong));
    }

    public void OnCorrect()
    {
        ApplyState();

        if (correctSound != null)
        {
            correctSound.Play();
        }

        if (correctEffect != null)
        {
            correctEffect.SetActive(true);
        }
    }

    public void OnWrong()
    {
        if (wrongSound != null)
        {
            wrongSound.Play();
        }

        if (trapFloor != null)
        {
            // 床を抜く。落下先のHazardZoneがセーブポイントへ戻す。
            trapFloor.SetActive(false);
            SendCustomEventDelayedSeconds(nameof(ResetTrapFloor), trapResetDelay);
            return;
        }

        // 床を用意していない場合は直接セーブポイントへ
        if (checkpointManager != null)
        {
            checkpointManager.TriggerDeath();
        }
    }

    public void ResetTrapFloor()
    {
        if (trapFloor != null)
        {
            trapFloor.SetActive(true);
        }
    }

    private void ApplyState()
    {
        if (doorBody != null)
        {
            doorBody.SetActive(solvedState == 0);
        }
    }
}
