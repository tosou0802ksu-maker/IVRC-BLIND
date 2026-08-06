
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class OutlineController : UdonSharpBehaviour
{
    [Header("対象Renderer（未設定なら自身のRendererを使用）")]
    [SerializeField] private Renderer targetRenderer;

    [Header("シェーダープロパティ名")]
    [SerializeField] private string outlineWidthPropertyName = "_OutlineWidth";
    [SerializeField] private string outlineColorPropertyName = "_OutlineColor";

    [Header("点灯時の値")]
    [SerializeField] private float onOutlineWidth = 0.02f;
    [SerializeField] private Color onOutlineColor = Color.white;

    private readonly float offOutlineWidth = 0f;
    private readonly Color offOutlineColor = new Color(0f, 0f, 0f, 0f);

    [UdonSynced] private bool syncedOutlineState;

    private MaterialPropertyBlock propertyBlock;
    private bool hasWidthProperty;
    private bool hasColorProperty;

    private void Start()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        propertyBlock = new MaterialPropertyBlock();

        hasWidthProperty = !string.IsNullOrEmpty(outlineWidthPropertyName);
        hasColorProperty = !string.IsNullOrEmpty(outlineColorPropertyName);

        ApplyOutlineState();
    }

    public void SetOutlineState(bool state)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        syncedOutlineState = state;
        ApplyOutlineState();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        ApplyOutlineState();
    }

    private void ApplyOutlineState()
    {
        if (targetRenderer == null || propertyBlock == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(propertyBlock);

        if (hasWidthProperty)
        {
            propertyBlock.SetFloat(outlineWidthPropertyName, syncedOutlineState ? onOutlineWidth : offOutlineWidth);
        }
        if (hasColorProperty)
        {
            propertyBlock.SetColor(outlineColorPropertyName, syncedOutlineState ? onOutlineColor : offOutlineColor);
        }

        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}