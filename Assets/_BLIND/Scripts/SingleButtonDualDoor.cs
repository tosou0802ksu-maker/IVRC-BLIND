using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// ボタン1つでドア2枚を同時に開閉する。
/// SingleButtonDualDoorButton から呼ばれる。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SingleButtonDualDoor : UdonSharpBehaviour
{
    [UdonSynced] private bool pressed;

    [Header("ドア1")]
    [SerializeField] private Transform targetDoor1;
    [SerializeField] private Vector3 door1ClosedPos;
    [SerializeField] private Vector3 door1OpenPos;

    [Header("ドア2")]
    [SerializeField] private Transform targetDoor2;
    [SerializeField] private Vector3 door2ClosedPos;
    [SerializeField] private Vector3 door2OpenPos;

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

        if (targetDoor1 != null)
        {
            targetDoor1.localPosition = doorActivated ? door1OpenPos : door1ClosedPos;
        }
        if (targetDoor2 != null)
        {
            targetDoor2.localPosition = doorActivated ? door2OpenPos : door2ClosedPos;
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
