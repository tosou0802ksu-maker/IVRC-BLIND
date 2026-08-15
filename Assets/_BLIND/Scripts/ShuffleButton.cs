
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 視点シャッフルギミックの赤/青/緑ボタン1個分。
public class ShuffleButton : UdonSharpBehaviour
{
    [Header("接続先")]
    [SerializeField] private ShuffleSequenceManager sequenceManager;

    [Header("このボタンの色ID(赤=0, 青=1, 緑=2)")]
    [SerializeField] private int buttonId;

    [Header("押した時の音(任意)")]
    [SerializeField] private AudioSource pressSound;

    public override void Interact()
    {
        if (sequenceManager != null)
        {
            sequenceManager.OnButtonPressed(buttonId);
        }

        if (pressSound != null)
        {
            pressSound.Play();
        }
    }
}
