
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 「複数ボタン鍵ドア」のランプ1個分。
//
// 重要: このランプは役割によって見える情報が違う。
//   サーモ役    -> on/off が見える     (thermalIndicator / thermalOnObject)
//   過去の人役  -> 色だけが見える      (memoryColorObject は常時表示のまま)
//   エコロケ役  -> 形だけが見える      (レイヤー分けした静的メッシュ側で表現、ここでは触らない)
//
// memoryColorObject を SetActive で切り替えないのが肝。
// 「過去の人はランプの on/off 以外がわかる」= 色は見えるが点灯状態はわからない、を成立させる。
public class PuzzleLamp : UdonSharpBehaviour
{
    [Header("サーモ役だけに見えるON/OFF表示")]
    [Tooltip("点灯中だけ熱く見えるオブジェクト。Thermalレイヤーに置く。")]
    [SerializeField] private GameObject thermalOnObject;

    [Tooltip("熱の強さで表現する場合に使用(任意)。ThermalHeatシェーダーのマテリアルを持つRenderer用。")]
    [SerializeField] private DynamicThermalObject thermalIndicator;

    [Header("過去の人だけに見える色表示")]
    [Tooltip("ランプの三原色(赤/緑/青)を示すオブジェクト。Memoryレイヤーに置き、常時表示のままにする。")]
    [SerializeField] private GameObject memoryColorObject;

    [Header("通常視点用の実際の光(任意)")]
    [SerializeField] private Light lampLight;

    [Header("点灯時の熱の強さ")]
    [SerializeField] private float onHeat = 1.0f;
    [SerializeField] private float offHeat = 0.0f;

    private bool isOn = true;

    void Start()
    {
        // 色表示は最初から出しっぱなしにする(過去の人は常に色が見える)
        if (memoryColorObject != null)
        {
            memoryColorObject.SetActive(true);
        }

        SetLampOn(true);
    }

    // LampPuzzleManager から呼ばれる
    public void SetLampOn(bool on)
    {
        isOn = on;

        if (thermalOnObject != null)
        {
            thermalOnObject.SetActive(on);
        }

        if (thermalIndicator != null)
        {
            thermalIndicator.SetHeat(on ? onHeat : offHeat);
        }

        if (lampLight != null)
        {
            lampLight.enabled = on;
        }

        // memoryColorObject は意図的に触らない。
        // 過去の人に on/off を悟らせないため。
    }

    public bool IsOn()
    {
        return isOn;
    }
}
