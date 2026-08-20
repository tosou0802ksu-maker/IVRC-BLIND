using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// ボタンを押すとドア(1〜2枚)を指定位置から指定位置へ移動する。一度だけ反応。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DoorControlButton : UdonSharpBehaviour
{
    [UdonSynced] private bool hasBeenPressed;

    [Header("ドア1")]
    [SerializeField] private Transform targetDoor1;
    [SerializeField] private Vector3 door1ClosedPos;
    [SerializeField] private Vector3 door1OpenPos;

    [Header("ドア2(任意)")]
    [SerializeField] private Transform targetDoor2;
    [SerializeField] private Vector3 door2ClosedPos;
    [SerializeField] private Vector3 door2OpenPos;

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip pressClip;
    [SerializeField] private AudioSource audioSource;

    void Start()
    {
        ApplyDoorState();
    }

    public override void Interact()
    {
        if (hasBeenPressed) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        hasBeenPressed = true;
        RequestSerialization();
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnDoorActivated));
    }

    public override void OnDeserialization()
    {
        ApplyDoorState();
    }

    public void OnDoorActivated()
    {
        ApplyDoorState();
        PlaySound();
    }

    private void ApplyDoorState()
    {
        if (targetDoor1 != null)
        {
            targetDoor1.localPosition = hasBeenPressed ? door1OpenPos : door1ClosedPos;
        }
        if (targetDoor2 != null)
        {
            targetDoor2.localPosition = hasBeenPressed ? door2OpenPos : door2ClosedPos;
        }

        if (hasBeenPressed)
        {
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
    }

    private void PlaySound()
    {
        if (pressClip == null) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(pressClip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(pressClip, transform.position);
        }
    }
}
