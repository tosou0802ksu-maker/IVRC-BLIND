
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 巨大な手や覗き込む目のような「一瞬だけ現れて消える」不気味な演出。
// トリガーゾーンにプレイヤーが入ると発動し、短時間だけ visual を表示して消える。
// 一度発動すると cooldown が明けるまで再発動しない(常時出ていると怖くなくなるため)。
//
// thermalIndicator を設定すると、サーマル視点でも一瞬だけ熱源として映る。
// 「振り返って通常視点で見ても何もいない」状態を作れるので、
// 視点による見え方の食い違いそのものを恐怖演出に使える。
public class PhantomPresence : UdonSharpBehaviour
{
    [Header("演出本体(巨大な手/目など)")]
    [SerializeField] private GameObject visual;

    [Header("表示中だけ熱を持たせる場合(任意)")]
    [SerializeField] private DynamicThermalObject thermalIndicator;
    [SerializeField] private float presenceHeat = 1.0f;

    [Header("タイミング(秒)")]
    [SerializeField] private float visibleDuration = 1.2f;
    [SerializeField] private float cooldownDuration = 30.0f;

    [Header("演出音(任意)")]
    [SerializeField] private AudioSource presenceSound;

    private bool isReady = true;

    void Start()
    {
        if (visual != null)
        {
            visual.SetActive(false);
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        if (!isReady)
        {
            return;
        }

        isReady = false;

        if (visual != null)
        {
            visual.SetActive(true);
        }

        if (thermalIndicator != null)
        {
            thermalIndicator.SetHeatInstant(presenceHeat);
        }

        if (presenceSound != null)
        {
            presenceSound.Play();
        }

        SendCustomEventDelayedSeconds(nameof(HidePresence), visibleDuration);
    }

    public void HidePresence()
    {
        if (visual != null)
        {
            visual.SetActive(false);
        }

        if (thermalIndicator != null)
        {
            thermalIndicator.SetHeatInstant(0.0f);
        }

        SendCustomEventDelayedSeconds(nameof(ResetReady), cooldownDuration);
    }

    public void ResetReady()
    {
        isReady = true;
    }
}
