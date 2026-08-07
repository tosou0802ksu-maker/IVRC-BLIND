
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class OutlineController : UdonSharpBehaviour
{
    [SerializeField]
    private Renderer[] targetRenderers;

    private const string HighlightActiveProperty = "_HighlightActive";

    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    // Entry point for other gimmicks to turn the red edge highlight on/off.
    public void SetHighlight(bool flag)
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        float value = flag ? 1f : 0f;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer target = targetRenderers[i];
            if (target == null)
            {
                continue;
            }

            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(HighlightActiveProperty, value);
            target.SetPropertyBlock(propertyBlock);
        }
    }
}
