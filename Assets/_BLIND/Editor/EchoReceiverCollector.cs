using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// エディタ専用ツール（ワールドには含まれない）。
//
// EchoEmitter は「パルスを届ける相手」を receivers 配列で持っているが、
// マップにブロックを置くたびに手作業で登録するのは現実的でないため、
// シーン内の EchoReceiver を全部集めて一括で入れ直す。
//
// 使い方: メニューバーの [BLIND] -> [エコロケ受信機を集め直す]
public static class EchoReceiverCollector
{
    private const string MenuPath = "BLIND/エコロケ受信機を集め直す";

    [MenuItem(MenuPath)]
    public static void CollectAll()
    {
        EditorUtility.DisplayDialog("BLIND", Collect() + "\nシーンを保存してください。", "OK");
    }

    /// <summary>
    /// ダイアログを出さない版。ツールやスクリプトからはこちらを呼ぶこと。
    /// DisplayDialog はモーダルなので、外部から自動実行するとエディタが
    /// 「OK が押されるまで」丸ごと止まってしまう。
    /// </summary>
    public static string Collect()
    {
        var emitters = Object.FindObjectsOfType<EchoEmitter>(true);
        if (emitters.Length == 0)
        {
            return "シーンに EchoEmitter が見つかりませんでした。";
        }

        var receivers = Object.FindObjectsOfType<EchoReceiver>(true);
        if (receivers.Length == 0)
        {
            return "シーンに EchoReceiver が1つもありません。\n" +
                   "TriRole_Block などのプレハブを配置してから実行してください。";
        }

        foreach (var emitter in emitters)
        {
            Undo.RecordObject(emitter, "Collect Echo Receivers");

            var so = new SerializedObject(emitter);
            var array = so.FindProperty("receivers");
            array.arraySize = receivers.Length;

            for (int i = 0; i < receivers.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = receivers[i];
            }

            so.ApplyModifiedProperties();

            // UdonSharpのプロキシ側だけ書き換わっても実機に反映されないので、
            // 実体のUdonBehaviourへコピーする
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(emitter);

            EditorUtility.SetDirty(emitter);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        var msg = $"EchoReceiver {receivers.Length}個 を EchoEmitter {emitters.Length}個 に登録しました。";
        Debug.Log("[BLIND] " + msg);
        return msg;
    }
}
