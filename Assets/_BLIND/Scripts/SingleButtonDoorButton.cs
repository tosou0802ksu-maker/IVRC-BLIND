using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// ボタン1つ→ドア1枚用のボタン。押すとSingleButtonDoorに通知する。一度だけ反応。
/// </summary>
public class SingleButtonDoorButton : UdonSharpBehaviour
{
    [Header("接続先マネージャー")]
    [SerializeField] private SingleButtonDoor doorManager;

    [Header("効果音(任意)")]
    [SerializeField] private AudioClip pressClip;
    [SerializeField] private AudioSource audioSource;

    private bool pressed;

    public override void Interact()
    {
        if (pressed) return;
        if (doorManager == null) return;

        pressed = true;
        doorManager.OnButtonPressed();
        PlaySound();

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    void Start()
    {
        if (doorManager != null && doorManager.IsPressed())
        {
            pressed = true;
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
