using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// RoomZoneTrigger の visibleRooms を双方向にそろえる。
    ///
    /// ⚠️ 片道のままだと、行きは表示されるのに帰りに部屋が消えて戻れない。
    ///    例: room5 のリストに room4 が無く、room5 → room4 へ戻ると room4 の床が無い
    ///    （箱にたどり着く前に床が消えているので、箱も反応しない）。
    ///
    /// 「A のリストに B がある」なら「B のリストに A を足す」。消すことはしない。
    /// どの部屋の箱かは名前で決める: RoomZoneN → roomN、RoomZone → room1。
    /// Ctrl+Z 1回で戻せる。
    /// </summary>
    public static class RoomZoneSymmetry
    {
        [MenuItem("BLIND/部屋修正/RoomZoneのリストを双方向にそろえる")]
        public static void Menu()
        {
            var msg = Apply();
            Debug.Log(msg);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        public static string Apply()
        {
            var zones = Object.FindObjectsOfType<MonoBehaviour>(true)
                              .Where(c => c != null && c.GetType().Name == "RoomZoneTrigger").ToList();
            if (zones.Count == 0) return "RoomZoneTrigger が見つからない";

            var log = new StringBuilder();
            var zoneOf = new Dictionary<string, MonoBehaviour>();
            foreach (var z in zones)
            {
                string room = RoomOf(z.name);
                if (room == null) { log.AppendLine("  ⚠ " + z.name + " はどの部屋の箱か名前から分からないのでスキップ"); continue; }
                if (zoneOf.ContainsKey(room)) { log.AppendLine("  ⚠ " + room + " の箱が2つある: " + zoneOf[room].name + " / " + z.name + "（後の方はスキップ）"); continue; }
                zoneOf[room] = z;
            }

            // 部屋名 → 部屋の GameObject（リストに入っている物から拾う）
            var roomGo = new Dictionary<string, GameObject>();
            foreach (var z in zoneOf.Values)
                foreach (var g in List(z))
                    if (g != null) roomGo[g.name] = g;

            // 足す物を先に全部決めてから書く（途中で書くと判定が連鎖する）
            var add = new Dictionary<string, List<GameObject>>();
            foreach (var kv in zoneOf)
            {
                string a = kv.Key;
                foreach (var g in List(kv.Value))
                {
                    if (g == null || g.name == a) continue;
                    MonoBehaviour zb;
                    if (!zoneOf.TryGetValue(g.name, out zb)) continue;
                    if (List(zb).Any(x => x != null && x.name == a)) continue;
                    GameObject ga;
                    if (!roomGo.TryGetValue(a, out ga)) continue;
                    if (!add.ContainsKey(g.name)) add[g.name] = new List<GameObject>();
                    if (!add[g.name].Contains(ga)) add[g.name].Add(ga);
                }
            }

            if (add.Count == 0) return "片道のリストは無かった（何もしていない）\n" + log;

            Undo.SetCurrentGroupName("RoomZone 双方向");
            int group = Undo.GetCurrentGroup();
            int total = 0;
            foreach (var kv in add.OrderBy(k => Num(k.Key)))
            {
                var z = zoneOf[kv.Key];
                Undo.RecordObject(z, "RoomZone list");
                var so = new SerializedObject(z);
                var p = so.FindProperty("visibleRooms");
                foreach (var g in kv.Value)
                {
                    p.arraySize++;
                    p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = g;
                }
                so.ApplyModifiedProperties();
                PushUdon(z);
                EditorSceneManager.MarkSceneDirty(z.gameObject.scene);
                total += kv.Value.Count;
                log.AppendLine("  " + z.name + " に追加: " + string.Join(", ", kv.Value.Select(g => g.name).ToArray()));
            }
            Undo.CollapseUndoOperations(group);

            return "RoomZone のリストを双方向にそろえた: " + add.Count + " 個の箱に計 " + total + " 部屋を追加（Ctrl+Z で戻せる）\n" + log;
        }

        static GameObject[] List(MonoBehaviour z)
        {
            var so = new SerializedObject(z);
            var p = so.FindProperty("visibleRooms");
            if (p == null) return new GameObject[0];
            var r = new GameObject[p.arraySize];
            for (int i = 0; i < r.Length; i++) r[i] = p.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            return r;
        }

        static string RoomOf(string zoneName)
        {
            if (zoneName == "RoomZone") return "room1";
            if (!zoneName.StartsWith("RoomZone")) return null;
            string n = zoneName.Substring("RoomZone".Length).Trim();
            int v;
            return int.TryParse(n, out v) ? "room" + v : null;
        }

        static int Num(string room)
        {
            int v;
            return int.TryParse(room.Replace("room", ""), out v) ? v : 999;
        }

        static void PushUdon(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour; if (usb == null) return;
            if (UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) == null) return;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
        }
    }
}
