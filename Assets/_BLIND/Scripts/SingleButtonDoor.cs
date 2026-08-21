using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// ボタン1つでドア1枚を開閉する。
/// SingleButtonDoorButton から呼ばれる。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SingleButtonDoor : UdonSharpBehaviour
{
    [UdonSynced] private bool pressed;

    [Header("ドア")]
    [SerializeField] private Transform targetDoor;
    [SerializeField] private Vector3 doorClosedPos;
    [SerializeField] private Vector3 doorOpenPos;

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip doorClip;
    [SerializeField] private AudioSource audioSource;

    private bool doorActivated;

    void Start()
    {
        ApplyDoorState();
    }

    public void OnButtonPressed()
    {
        if (doorActivated) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        pressed = true;
        RequestSerialization();

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnDoorActivated));
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

    public bool IsPressed()
    {
        return pressed;
    }

    private void ApplyDoorState()
    {
        if (pressed) doorActivated = true;

        if (targetDoor != null)
        {
            targetDoor.localPosition = doorActivated ? doorOpenPos : doorClosedPos;
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

