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
    private bool isGlowing = false;
    private bool isWaiting = false;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        // 遅延待機中
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

    // EchoEmitterから距離を受け取って遅延付きで発光
    public void TriggerGlowWithDelay(float delay)
    {
        Debug.Log("TriggerGlowWithDelay呼ばれた delay=" + delay);
        delayTimer = delay;
        isWaiting = true;
        isGlowing = false;
    }

    private void StartGlow()
    {
        glowTimer = glowDuration;
        isGlowing = true;
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
