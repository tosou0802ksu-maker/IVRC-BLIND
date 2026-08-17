
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// スプリンクラーギミック。
//
// 一定周期で「作動中(水を撒く)」「休止中」を繰り返し、
// 作動中にこのゾーンへ入ったプレイヤーは、エコロケ・サーモ役の視界が
// 一時的に完全な暗闇になる(過去の人は影響を受けない)。
//
// GameObjectにIsTriggerを有効にしたColliderを付けてアタッチする。
// 全クライアントで同じ周期になるよう Time.timeSinceLevelLoad を基準にするので、
// 同期変数は不要(全員のUnity時間が同じ前提)。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Sprinkler : UdonSharpBehaviour
{
    [Header("このクライアントのローカル視点コントローラ")]
    [SerializeField] private PlayerVisionController localVisionController;

    [Header("作動サイクル(秒)")]
    [SerializeField] private float activeDuration = 3f;
    [SerializeField] private float inactiveDuration = 7f;

    [Header("作動中にゾーン内へいると視界を奪う長さ(秒)")]
    [SerializeField] private float blackoutDuration = 3f;

    [Header("作動中だけ表示する見た目(水しぶき等・任意)")]
    [SerializeField] private GameObject activeVisual;
    [SerializeField] private AudioSource activeSound;

    private bool wasActive;
    private bool localPlayerInside;

    void Start()
    {
        wasActive = IsActiveNow();
        if (activeVisual != null)
        {
            activeVisual.SetActive(wasActive);
        }
    }

    void Update()
    {
        bool active = IsActiveNow();
        if (active == wasActive)
        {
            return;
        }

        wasActive = active;

        if (activeVisual != null)
        {
            activeVisual.SetActive(active);
        }

        if (active)
        {
            if (activeSound != null)
            {
                activeSound.Play();
            }

            if (localPlayerInside)
            {
                ApplyBlackout();
            }
        }
    }

    private bool IsActiveNow()
    {
        float cycle = activeDuration + inactiveDuration;
        if (cycle <= 0f)
        {
            return false;
        }

        float t = Time.timeSinceLevelLoad % cycle;
        return t < activeDuration;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        localPlayerInside = true;

        if (IsActiveNow())
        {
            ApplyBlackout();
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        localPlayerInside = false;
    }

    private void ApplyBlackout()
    {
        if (localVisionController != null)
        {
            localVisionController.TriggerForcedBlackout(blackoutDuration);
        }
    }
}
