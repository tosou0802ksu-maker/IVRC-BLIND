using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// 3つのボタンが全て押されたらドア(1〜2枚)を指定位置へ移動する。
/// 各ボタン(MultiButtonDoorButton)から呼ばれる。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MultiButtonDoor : UdonSharpBehaviour
{
    [UdonSynced] private int pressedFlags;

    [Header("ドア1")]
    [SerializeField] private Transform targetDoor1;
    [SerializeField] private Vector3 door1ClosedPos;
    [SerializeField] private Vector3 door1OpenPos;

    [Header("ドア2(任意)")]
    [SerializeField] private Transform targetDoor2;
    [SerializeField] private Vector3 door2ClosedPos;
    [SerializeField] private Vector3 door2OpenPos;

    [Header("全ボタン押下時の効果音(任意)")]
    [SerializeField] private AudioClip doorClip;
    [SerializeField] private AudioSource audioSource;

    private bool doorActivated;

    void Start()
    {
        ApplyDoorState();
    }

    public void OnButtonPressed(int buttonId)
    {
        if (buttonId < 0 || buttonId > 2) return;
        if (doorActivated) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        pressedFlags |= (1 << buttonId);
        RequestSerialization();

        if (AreAllPressed())
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnDoorActivated));
        }
    }

    public override void OnDeserialization()
    {
        ApplyDoorState();
    }

    public void OnDoorActivated()
    {
        doorActivated = true;
        ApplyDoorState();
        PlaySound();
    }

    public bool IsButtonPressed(int buttonId)
    {
        return (pressedFlags & (1 << buttonId)) != 0;
    }

    private bool AreAllPressed()
    {
        return (pressedFlags & 0b111) == 0b111;
    }

    private void ApplyDoorState()
    {
        bool allPressed = AreAllPressed();
        if (allPressed) doorActivated = true;

        if (targetDoor1 != null)
        {
            targetDoor1.localPosition = allPressed ? door1OpenPos : door1ClosedPos;
        }
        if (targetDoor2 != null)
        {
            targetDoor2.localPosition = allPressed ? door2OpenPos : door2ClosedPos;
        }
    }

    private void PlaySound()
    {
        if (doorClip == null) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(doorClip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(doorClip, transform.position);
        }
    }
}
