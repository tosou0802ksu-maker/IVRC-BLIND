
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 「他の人を見た時」の役割別の見え方を実現する。
// 計画書(Excalidraw)の仕様:
//   ・エコロケ　：他プレイヤーはデフォルトアバターor球体にしか見えない
//   ・サーモ　　：他プレイヤーは高温の熱源として見える
//   ・過去の人　：他プレイヤーは普通のアバターで見える(何もしない)
//
// VRChatの仕様上、他人の実際のアバター表示をこちらのクライアントから
// すり替えることはできないため、
//   1) 本人のアバター(Playerレイヤー)を該当役割のカリングマスクから外す
//   2) 代わりに位置追従する球体/熱源プロキシを役割専用レイヤーに出す
// という構成を取る。1)のレイヤー設定はPlayerVisionController側のInspectorで別途必要。
public class RemotePlayerProxyManager : UdonSharpBehaviour
{
    [Header("他プレイヤーに追従させる球体などのプロキシ(最大2人分)")]
    [SerializeField] private Transform[] proxyRoots;

    [Header("サーモ役に見せる熱表現(proxyRootsと同じ並びで対応させる)")]
    [SerializeField] private DynamicThermalObject[] proxyHeat;

    [Header("他プレイヤーに常時持たせる熱の強さ")]
    [SerializeField] private float remotePlayerHeat = 1.0f;

    void Update()
    {
        if (proxyRoots == null || proxyRoots.Length == 0)
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

            if (slot >= proxyRoots.Length)
            {
                break;
            }

            Transform proxy = proxyRoots[slot];
            if (proxy != null)
            {
                proxy.position = p.GetBonePosition(HumanBodyBones.Hips);
                proxy.gameObject.SetActive(true);
            }

            if (proxyHeat != null && proxyHeat.Length > slot && proxyHeat[slot] != null)
            {
                proxyHeat[slot].SetHeatInstant(remotePlayerHeat);
            }

            slot++;
        }

        // 人数が足りない分(2人揃っていない時)は残りのプロキシを隠す
        for (int i = slot; i < proxyRoots.Length; i++)
        {
            if (proxyRoots[i] != null)
            {
                proxyRoots[i].gameObject.SetActive(false);
            }
        }
    }
}
