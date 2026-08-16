
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// クイズ部屋の選択肢ボタン1個分。
// ボタンに書かれた文字は過去の人しか読めないので、
// 選択肢のラベルはMemoryレイヤーの子オブジェクトとして持たせる。
public class QuizChoiceButton : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private QuizManager quizManager;

    [Header("この選択肢の番号(0始まり)")]
    [SerializeField] private int choiceIndex;

    public override void Interact()
    {
        if (quizManager != null)
        {
            quizManager.SubmitAnswer(choiceIndex);
        }
    }
}
