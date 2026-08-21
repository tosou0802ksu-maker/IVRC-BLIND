using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room13 の内部間仕切りドア9枚を整理する。
    ///
    /// 現状の問題:
    ///   ・Door_02/03/04 それぞれに Default/Echo/Thermal の3バリアントがある(計9枚)
    ///   ・各バリアントのサイズが微妙にずれている(Door_04のZスケールが93 vs 95)
    ///   ・Z方向にオフセットされて重なりを避けているが、正しくはレイヤー分離で重ねるべき
    ///   ・全てレイヤー0になっている
    ///   ・Door_04 (3) だけ別の親(Vision_Echo)に入っている
    ///
    /// このスクリプトがやること:
    ///   1. 各グループの3バリアントを同じ位置・回転・サイズに統一
    ///   2. 正しいレイヤーを設定(Default=0, Echo=23, Thermal=22)
    ///   3. Echo/Thermalバリアントに正しいマテリアルを割り当て
    ///   4. Defaultバリアントに赤/青/緑のマテリアルを割り当て(Z座標降順)
    ///   5. 全バリアントをWalls配下に統一
    ///
    /// 何度実行しても同じ結果になる(冪等)。
    /// </summary>
    public static class Room13DoorSetup
    {
        static readonly Vector3 UniformScale = new Vector3(120, 100, 95);

        [MenuItem("BLIND/部屋修正/5. room13 クイズドア整理")]
        public static void SetupMenu()
        {
            EditorUtility.DisplayDialog("BLIND", Setup(), "OK");
        }

        /// <summary>ダイアログなし版。自動化から呼ぶ。</summary>
        public static string Setup()
        {
            var room13 = FindRoom("room13");
            if (room13 == null) return "room13 が見つからない";

            var walls = room13.Find("GeneratedRoom/Walls");
            if (walls == null) return "room13/GeneratedRoom/Walls が見つからない";

            // room13 配下の全 Door_02/03/04 を収集
            var allDoors = room13.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Door_02") ||
                            t.name.StartsWith("Door_03") ||
                            t.name.StartsWith("Door_04"))
                .ToList();

            // グループ化
            var groups = new Dictionary<string, List<Transform>>
            {
                { "Door_02", new List<Transform>() },
                { "Door_03", new List<Transform>() },
                { "Door_04", new List<Transform>() }
            };

            foreach (var d in allDoors)
            {
                if (d.name.StartsWith("Door_02")) groups["Door_02"].Add(d);
                else if (d.name.StartsWith("Door_03")) groups["Door_03"].Add(d);
                else if (d.name.StartsWith("Door_04")) groups["Door_04"].Add(d);
            }

            var log = new StringBuilder();

            // マテリアルを読み込み
            var echoMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_BLIND/Art/Materials/Echo/EchoMaterial_Prop.mat");
            var thermalMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_BLIND/Art/Materials/Thermal/Thermal_Prop.mat");

            if (echoMat == null) log.AppendLine("警告: EchoMaterial_Prop.mat が見つからない");
            if (thermalMat == null) log.AppendLine("警告: Thermal_Prop.mat が見つからない");

            // 各グループを統一
            foreach (var kv in groups)
            {
                var baseName = kv.Key;
                var doors = kv.Value;
                if (doors.Count == 0)
                {
                    log.AppendLine(baseName + ": ドアが見つからない");
                    continue;
                }

                // ベースバリアント(サフィックスなし)を基準にする
                var baseDoor = doors.FirstOrDefault(d => d.name == baseName);
                if (baseDoor == null)
                {
                    baseDoor = doors[0];
                    log.AppendLine(baseName + ": ベースが無いため " + baseDoor.name + " を基準に使用");
                }

                var refPos = baseDoor.localPosition;
                var refRot = baseDoor.localRotation;

                foreach (var door in doors)
                {
                    // Walls 配下に統一(Door_04(3) は別親にいる)
                    if (door.parent != walls)
                    {
                        Undo.SetTransformParent(door, walls, "Reparent " + door.name);
                    }

                    Undo.RecordObject(door, "Unify " + door.name);
                    door.localPosition = refPos;
                    door.localRotation = refRot;
                    door.localScale = UniformScale;

                    // レイヤー判定: (1)/(3) = Echo, (2) = Thermal, サフィックスなし = Default
                    int layer;
                    Material mat = null;

                    if (door.name.Contains("(1)") || door.name.Contains("(3)"))
                    {
                        layer = 23; // Echo
                        mat = echoMat;
                    }
                    else if (door.name.Contains("(2)"))
                    {
                        layer = 22; // Thermal
                        mat = thermalMat;
                    }
                    else
                    {
                        layer = 0; // Default
                        // 色は後で設定
                    }

                    door.gameObject.layer = layer;

                    // マテリアル設定(Echo/Thermal)
                    if (mat != null)
                    {
                        var renderer = door.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            Undo.RecordObject(renderer, "Material " + door.name);
                            renderer.sharedMaterial = mat;
                            EditorUtility.SetDirty(renderer);
                        }
                    }

                    EditorUtility.SetDirty(door.gameObject);
                    log.AppendLine("  " + door.name + " → layer=" + layer
                        + " pos=" + refPos.ToString("F2")
                        + " scale=" + UniformScale.ToString("F0"));
                }
            }

            // 色の割り当て: ワールドZ座標降順で赤→青→緑
            var colorOrder = new List<(string name, float worldZ)>();
            foreach (var kv in groups)
            {
                if (kv.Value.Count == 0) continue;
                var baseDoor = kv.Value.FirstOrDefault(d => d.name == kv.Key) ?? kv.Value[0];
                colorOrder.Add((kv.Key, baseDoor.position.z));
            }
            colorOrder.Sort((a, b) => b.worldZ.CompareTo(a.worldZ));

            var colors = new[]
            {
                (color: new Color(0.8f, 0.1f, 0.1f), name: "赤", file: "Door_Red"),
                (color: new Color(0.1f, 0.2f, 0.8f), name: "青", file: "Door_Blue"),
                (color: new Color(0.1f, 0.6f, 0.15f), name: "緑", file: "Door_Green")
            };

            for (int i = 0; i < colorOrder.Count && i < 3; i++)
            {
                var groupName = colorOrder[i].name;
                var defaultDoor = groups[groupName].FirstOrDefault(d => d.name == groupName);
                if (defaultDoor == null) continue;

                var renderer = defaultDoor.GetComponent<Renderer>();
                if (renderer == null) continue;

                // マテリアルをアセットとして保存(永続化)
                string matPath = "Assets/_BLIND/Art/Materials/" + colors[i].file + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Standard"));
                    mat.color = colors[i].color;
                    mat.name = colors[i].file;
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.color = colors[i].color;
                    EditorUtility.SetDirty(mat);
                }

                Undo.RecordObject(renderer, "Color " + defaultDoor.name);
                renderer.sharedMaterial = mat;
                EditorUtility.SetDirty(renderer);
                log.AppendLine(groupName + " → " + colors[i].name
                    + " (worldZ=" + colorOrder[i].worldZ.ToString("F1") + ")");
            }

            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return log.ToString();
        }

        static Transform FindRoom(string name)
        {
            return Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(t => t.name == name && t.parent != null && t.parent.name == "=== ROOMS ===");
        }
    }
}
