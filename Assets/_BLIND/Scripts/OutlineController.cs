
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

    // 消灯時は幅0・アルファ0で統一
    private static readonly float OffOutlineWidth = 0f;
    private static readonly Color OffOutlineColor = new Color(0f, 0f, 0f, 0f);

    [UdonSynced] private bool syncedOutlineState;

    private MaterialPropertyBlock propertyBlock;
    private int outlineWidthPropertyId;
    private int outlineColorPropertyId;
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

        if (hasWidthProperty)
        {
            outlineWidthPropertyId = Shader.PropertyToID(outlineWidthPropertyName);
        }
        if (hasColorProperty)
        {
            outlineColorPropertyId = Shader.PropertyToID(outlineColorPropertyName);
        }

        ApplyOutlineState();
    }

    /// <summary>
    /// 外部スクリプト/イベントから呼び出す。呼び出したプレイヤーがOwnerでなければ
    /// Ownerを取得した上で状態を変更・全体に同期する。
    /// </summary>
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

        // 既存の値を保持したまま対象プロパティだけ上書きするため、まず現在のブロックを取得
        targetRenderer.GetPropertyBlock(propertyBlock);

        if (hasWidthProperty)
        {
            propertyBlock.SetFloat(outlineWidthPropertyId, syncedOutlineState ? onOutlineWidth : OffOutlineWidth);
        }
        if (hasColorProperty)
        {
            propertyBlock.SetColor(outlineColorPropertyId, syncedOutlineState ? onOutlineColor : OffOutlineColor);
        }

        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
