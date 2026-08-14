
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DynamicThermalObject : UdonSharpBehaviour
{
    [Header("対象レンダラー")]
    [SerializeField] private Renderer targetRenderer;

    [Header("補間設定")]
    [SerializeField] private float lerpSpeed = 2.0f;

    private MaterialPropertyBlock propertyBlock;
    private float currentHeat;
    private float targetHeat;
    private bool isLerping;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
        ApplyHeat(currentHeat);
    }

    // 指定した温度へ即座に切り替える
    public void SetHeatInstant(float targetValue)
    {
        currentHeat = targetValue;
        targetHeat = targetValue;
        isLerping = false;
        ApplyHeat(currentHeat);
    }

    // 指定した温度へ滑らかに変化させる
    public void SetHeat(float targetValue)
    {
        targetHeat = targetValue;
        isLerping = true;
    }

    void Update()
    {
        if (!isLerping)
        {
            return;
        }

        currentHeat = Mathf.Lerp(currentHeat, targetHeat, Time.deltaTime * lerpSpeed);
        ApplyHeat(currentHeat);

        if (Mathf.Abs(targetHeat - currentHeat) < 0.001f)
        {
            currentHeat = targetHeat;
            ApplyHeat(currentHeat);
            isLerping = false;
        }
    }

    private void ApplyHeat(float value)
    {
        if (targetRenderer == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat("_HeatIntensity", value);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
