
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 「他の人を見た時」の役割別の見え方を実現する。
// 計画書(Excalidraw)の仕様:
//   ・エコロケ　：他プレイヤーは人型のシルエットとして見える
//   ・サーモ　　：他プレイヤーは人型の熱源として見える
//   ・過去の人　：他プレイヤーは普通のアバターで見える(何もしない)
//
// VRChatの仕様上、他人の実際のアバター表示をこちらのクライアントから
// すり替えることはできないため、
//   1) 本人のアバター(Playerレイヤー)を該当役割のカリングマスクから外す
//   2) 代わりに主要ボーンを追従する人型プロキシ(MannequinProxyRig)を役割専用レイヤーに出す
// という構成を取る。1)のレイヤー設定はPlayerVisionController側のInspectorで別途必要。
public class RemotePlayerProxyManager : UdonSharpBehaviour
{
    [Header("他プレイヤーに追従させる人型プロキシ(最大2人分)")]
    [SerializeField] private MannequinProxyRig[] proxyRigs;

    [Header("他プレイヤーに常時持たせる熱の強さ")]
    [SerializeField] private float remotePlayerHeat = 1.0f;

    [Header("サーマル非表示ゾーン(ゾーン内のプレイヤーはサーマルで見えなくなる)")]
    [SerializeField] private ThermalHideZone[] thermalHideZones;

    void Update()
    {
        if (proxyRigs == null || proxyRigs.Length == 0)
        {
            return;
        }

        VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(players);

        int slot = 0;
        for (int i = 0; i < players.Length; i++)
        {
            VRCPlayerApi p = players[i];
            if (p == null || !p.IsValid() || p.isLocal)
            {
                continue;
            }

            if (slot >= proxyRigs.Length)
            {
                break;
            }

            MannequinProxyRig rig = proxyRigs[slot];
            if (rig != null)
            {
                rig.SetVisible(true);
                rig.UpdatePose(p);

                bool hidden = IsInThermalHideZone(p.GetPosition());
                rig.SetHeat(hidden ? 0f : remotePlayerHeat);
                rig.SetThermalVisible(!hidden);
            }

            slot++;
        }

        // 人数が足りない分(2人揃っていない時)は残りのプロキシを隠す
        // ※thermalHideZonesが未設定でも通常動作に影響なし
        for (int i = slot; i < proxyRigs.Length; i++)
        {
            if (proxyRigs[i] != null)
            {
                proxyRigs[i].SetVisible(false);
            }
        }
    }

    private bool IsInThermalHideZone(Vector3 position)
    {
        if (thermalHideZones == null) return false;

        for (int i = 0; i < thermalHideZones.Length; i++)
        {
            if (thermalHideZones[i] != null && thermalHideZones[i].IsInside(position))
            {
                return true;
            }
        }

        return false;
    }
}
