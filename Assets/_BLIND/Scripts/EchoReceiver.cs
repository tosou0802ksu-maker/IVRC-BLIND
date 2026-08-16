using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EchoReceiver : UdonSharpBehaviour
{
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private float glowDuration = 1.5f;

    private MaterialPropertyBlock propertyBlock;
    private float glowTimer = 0f;
    private float delayTimer = 0f;
    private float fadeDelay = 0f;
    private float fadeDelayTimer = 0f;
    private bool isGlowing = false;
    private bool isWaiting = false;
    private bool isFadeWaiting = false;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        // 点灯の遅延待機中
        if (isWaiting)
        {
            delayTimer -= Time.deltaTime;
            if (delayTimer <= 0f)
            {
                isWaiting = false;
                StartGlow();
            }
            return;
        }

        // 消灯の遅延待機中
        if (isFadeWaiting)
        {
            fadeDelayTimer -= Time.deltaTime;
            if (fadeDelayTimer <= 0f)
            {
                isFadeWaiting = false;
                isGlowing = true;
                glowTimer = glowDuration;
            }
            return;
        }

        if (!isGlowing) return;

        glowTimer -= Time.deltaTime;
        float intensity = Mathf.Clamp01(glowTimer / glowDuration);
        SetGlow(intensity);

        if (glowTimer <= 0f)
        {
            isGlowing = false;
            SetGlow(0f);
        }
    }

    public void TriggerGlowWithDelay(float startDelay, float fadeStartDelay)
    {
        delayTimer = startDelay;
        fadeDelay = fadeStartDelay;
        isWaiting = true;
        isGlowing = false;
        isFadeWaiting = false;
    }

    private void StartGlow()
    {
        // 点灯したらフェードアウト開始までの遅延をセット
        fadeDelayTimer = fadeDelay;
        isFadeWaiting = true;
        SetGlow(1f);
    }

    private void SetGlow(float intensity)
    {
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        if (targetRenderers == null || targetRenderers.Length == 0) return;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] == null) continue;
            targetRenderers[i].GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_GlowIntensity", intensity);
            targetRenderers[i].SetPropertyBlock(propertyBlock);
        }
    }
}