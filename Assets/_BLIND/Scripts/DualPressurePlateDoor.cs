using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 2つの感圧板が両方踏まれている間だけドア1枚を開く。
/// PressurePlate から呼ばれる。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DualPressurePlateDoor : UdonSharpBehaviour
{
    [UdonSynced] private int plateFlags;

    [Header("ドア")]
    [SerializeField] private Transform targetDoor;
    [SerializeField] private Vector3 doorClosedPos;
    [SerializeField] private Vector3 doorOpenPos;

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    [SerializeField] private AudioSource audioSource;

    private bool wasOpen;

    void Start()
    {
        ApplyDoorState();
    }

    public void OnPlateEnter(int plateId)
    {
        if (plateId < 0 || plateId > 1) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        plateFlags |= (1 << plateId);
        RequestSerialization();
        ApplyDoorState();
    }

    public void OnPlateExit(int plateId)
    {
        if (plateId < 0 || plateId > 1) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        plateFlags &= ~(1 << plateId);
        RequestSerialization();
        ApplyDoorState();
    }

    public override void OnDeserialization()
    {
        ApplyDoorState();
    }

    private bool AreBothPressed()
    {
        return (plateFlags & 0b11) == 0b11;
    }

    private void ApplyDoorState()
    {
        bool isOpen = AreBothPressed();

        if (targetDoor != null)
        {
            targetDoor.localPosition = isOpen ? doorOpenPos : doorClosedPos;
        }

        if (isOpen && !wasOpen)
        {
            PlaySound(openClip);
        }
        else if (!isOpen && wasOpen)
        {
            PlaySound(closeClip);
        }

        wasOpen = isOpen;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
