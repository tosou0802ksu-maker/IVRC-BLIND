using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace BLIND.EditorTools
{
    public static class BlindRoomImporter
    {
        private const string MainScenePath = "Assets/_BLIND/Scenes/MainWorld/MainWorld.unity";
        private const string SoshiScenePath = "Assets/_BLIND/Scenes/soshi.unity";

        [MenuItem("BLIND/部屋合体/1. soshiから部屋を合体＆視界生成(全自動)", priority = -10)]
        public static void ImportAndBuildAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[BLIND] プレイモード中は実行できません。");
                return;
            }

            Debug.Log("[BLIND] ===== 部屋合体＆視界レイヤー自動生成プロセスを開始 =====");

            // 1. MainWorld シーンを開く
            Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            if (!mainScene.IsValid())
            {
                Debug.LogError($"[BLIND] {MainScenePath} を開けませんでした。");
                return;
            }

            // 2. === ROOMS === を探す
            Transform roomsRoot = null;
            foreach (var rootGo in mainScene.GetRootGameObjects())
            {
                if (rootGo.name == "=== ROOMS ===")
                {
                    roomsRoot = rootGo.transform;
                    break;
                }
            }

            if (roomsRoot == null)
            {
                Debug.LogError("[BLIND] MainWorld 内に '=== ROOMS ===' が見つかりません。");
                return;
            }

            // 3. soshi シーンを加算ロード
            Scene soshiScene = EditorSceneManager.OpenScene(SoshiScenePath, OpenSceneMode.Additive);
            if (!soshiScene.IsValid())
            {
                Debug.LogError($"[BLIND] {SoshiScenePath} を開けませんでした。");
                return;
            }

            string[] targetRoomNames = new string[] { "room8", "room11", "room13", "room17", "room19" };

            // soshi シーンから該当の部屋を探す（重複部屋は除外）
            Dictionary<string, GameObject> soshiRooms = new Dictionary<string, GameObject>();
            foreach (var rootGo in soshiScene.GetRootGameObjects())
            {
                foreach (var name in targetRoomNames)
                {
                    if (rootGo.name == name && !soshiRooms.ContainsKey(name))
                    {
                        // room11 が2つある場合、子オブジェクト数が多い方（実体）を優先
                        if (name == "room11" && rootGo.transform.childCount == 0) continue;
                        soshiRooms[name] = rootGo;
                    }
                }
            }

            Undo.RegisterFullObjectHierarchyUndo(roomsRoot.gameObject, "Import Rooms From Soshi");

            // 4. 各部屋の統合処理
            foreach (var name in targetRoomNames)
            {
                if (!soshiRooms.TryGetValue(name, out GameObject soshiRoomGo))
                {
                    Debug.LogWarning($"[BLIND] soshi シーン内に '{name}' が見つかりませんでした。スキップします。");
                    continue;
                }

                // MainWorld 側の既存の部屋を探して削除
                Transform existingRoom = roomsRoot.Find(name);
                if (existingRoom != null)
                {
                    Object.DestroyImmediate(existingRoom.gameObject);
                }

                // soshi 側の部屋を MainWorld に移動（加算ロードからのシーン移動）
                SceneManager.MoveGameObjectToScene(soshiRoomGo, mainScene);
                soshiRoomGo.transform.SetParent(roomsRoot, false);

                // ドア・位置調整
                AdjustRoom(name, soshiRoomGo);

                Debug.Log($"[BLIND] [OK] {name} を soshi から MainWorld へ統合・調整完了");
            }

            // 5. soshi シーンを閉じる
            EditorSceneManager.CloseScene(soshiScene, true);

            // 6. サーマル材質を作り直す
            Debug.Log("[BLIND] [Step 2/5] サーマル材質の生成中...");
            BlindThermalTable.BuildMaterials();

            // 7. 全部屋にサーモ・エコロケ層を生成
            Debug.Log("[BLIND] [Step 3/5] 全部屋にサーモ・エコロケ層を自動生成中...");
            string visionLog = BlindVisionBuilder.BuildMine();
            Debug.Log("[BLIND] " + visionLog);

            // 8. エコロケ受信機を EchoEmitter に再登録
            Debug.Log("[BLIND] [Step 4/5] EchoReceiver の再登録中...");
            EchoReceiverCollector.Collect();

            // 9. 実機テスト用の数値を適用＆シーン保存
            Debug.Log("[BLIND] [Step 5/5] 実機数値の適用＆シーン保存中...");
            string tuningLog = BlindTuning1200.Apply();
            Debug.Log("[BLIND] " + tuningLog);

            Debug.Log("[BLIND] ===== 全工程が正常に完了しました！ =====");
            EditorUtility.DisplayDialog("BLIND", "部屋合体と視界レイヤー生成（サーマル・エコロケ）が全自動で完了しました！
Playモードで動作確認を行ってください。", "OK");
        }

        private static void AdjustRoom(string name, GameObject roomGo)
        {
            var rb = roomGo.GetComponent<RoomBuilder3>();

            switch (name)
            {
                case "room8":
                    // そのまま
                    break;

                case "room11":
                    if (rb != null)
                    {
                        // 東ドア（room10接続用）: -3.0
                        rb.eastWall.hasDoor = true;
                        rb.eastWall.doorOffset = -3.0f;

                        // 北ドア（room12接続用）: -3.0
                        rb.northWall.hasDoor = true;
                        rb.northWall.doorOffset = -3.0f;

                        rb.BuildRoom();
                    }
                    break;

                case "room13":
                    // soshi の設定のまま
                    break;

                case "room17":
                    // room16（北ドア Z=-15.4）に合わせて +2.1m シフト (Z: -20.0 -> -17.9)
                    roomGo.transform.localPosition = new Vector3(20.6f, 0f, -17.9f);
                    break;

                case "room19":
                    // room17 に合わせて +2.1m シフト (Z: -20.0 -> -17.9)
                    roomGo.transform.localPosition = new Vector3(45.78f, 0f, -17.9f);
                    break;
            }
        }
    }
}
