
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class OutlineController : UdonSharpBehaviour
{
    [SerializeField]
    private Renderer[] targetRenderers;

    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    public void SetHighlight(bool flag)
    {
        Debug.Log("SetHighlight called, flag=" + flag);
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            Debug.Log("targetRenderers is empty!");
            return;
        }
        float value = flag ? 1f : 0f;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer target = targetRenderers[i];
            if (target == null) continue;
            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_HighlightActive", value);
            target.SetPropertyBlock(propertyBlock);
        }
    }
}