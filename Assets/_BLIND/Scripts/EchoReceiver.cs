using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EchoReceiver : UdonSharpBehaviour
{
    [SerializeField] private Renderer[] targetRenderers;

    // 光っている時間
    [SerializeField] private float glowDuration = 1.5f;

    private MaterialPropertyBlock propertyBlock;
    private float glowTimer = 0f;
    private bool isGlowing = false;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (!isGlowing) return;

        glowTimer -= Time.deltaTime;

        // 残り時間に応じてフェードアウト
        float intensity = Mathf.Clamp01(glowTimer / glowDuration);
        SetGlow(intensity);

        if (glowTimer <= 0f)
        {
            isGlowing = false;
            SetGlow(0f);
        }
    }

    // EchoEmitterから呼ばれる
    public void TriggerGlow()
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
