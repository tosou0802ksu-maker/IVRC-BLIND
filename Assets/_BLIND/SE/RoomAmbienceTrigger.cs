
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)] // 音はローカルでいいので同期不要
public class RoomAmbienceTrigger : UdonSharpBehaviour
{
    [Header("参照")]
    public AudioSource bgmSource;      // 常時流れているBGM
    public AudioSource roomAmbience;   // この部屋専用の音(水滴音など)

    [Header("設定")]
    public float bgmNormalVolume = 0.6f;
    public float bgmDuckedVolume = 0.15f;
    public float fadeSpeed = 2f; // 大きいほど速く切り替わる

    private float targetBgmVolume;
    private bool playerInside = false;

    void Start()
    {
        targetBgmVolume = bgmNormalVolume;
        if (bgmSource != null)
            bgmSource.volume = bgmNormalVolume;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal) return;

        playerInside = true;
        targetBgmVolume = bgmDuckedVolume;

        if (roomAmbience != null && !roomAmbience.isPlaying)
            roomAmbience.Play();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal) return;

        playerInside = false;
        targetBgmVolume = bgmNormalVolume;

        if (roomAmbience != null)
            roomAmbience.Stop();
    }

    void Update()
    {
        if (bgmSource == null) return;

        // なめらかにフェードさせる
        bgmSource.volume = Mathf.Lerp(bgmSource.volume, targetBgmVolume, Time.deltaTime * fadeSpeed);
    }
}
