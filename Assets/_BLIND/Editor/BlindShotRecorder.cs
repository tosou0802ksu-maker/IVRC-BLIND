using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 紹介映像の素材を書き出す。決めた道筋でカメラを動かし、
    /// 過去人・サーモ・エコロケの3視点を**同じ瞬間**で連番画像にする。
    ///
    /// なぜ必要か:
    ///   手で録画すると手ブレが入るうえ、3視点を同時に撮れない（1人1視点しか見えない）。
    ///   3視点が同じ瞬間でそろっていないと、分割画面や「同じ場所で見え方だけ変わる」切り替えが作れない。
    ///
    /// 使い方（execute_code などから）:
    ///   BlindShotRecorder.Begin("…/shots.json");   // 予定を読み込む（EditorApplication.update でも少しずつ進む）
    ///   BlindShotRecorder.Pump(20f);                 // 20 秒ぶん書き出す。終わるまで繰り返す
    ///   BlindShotRecorder.Status();
    ///
    /// ⚠️ シーンは汚さない。カメラと懐中電灯は HideAndDontSave、エコロケの光は MaterialPropertyBlock で
    ///    最後に 0 に戻す。
    /// ⚠️ エコロケは EchoEmitter と同じ規則で光らせる（範囲 12m・前方 55°・1m あたり 0.045 秒遅れ・0.6 秒で消える）。
    /// </summary>
    public static class BlindShotRecorder
    {
        [Serializable] public class Key { public float t; public Vector3 pos; public Vector3 look; }

        [Serializable] public class Shot
        {
            public string name;
            public float duration = 6f;
            public float fps = 30f;
            public float fov = 60f;
            public bool ease = true;
            public float pulseStart = 0.4f;
            public float pulseInterval = 2.2f;
            public string views = "memory,thermal,echo";
            public Key[] keys;
        }

        [Serializable] public class ShotList { public string outDir; public int width = 1920; public int height = 1080; public Shot[] shots; }

        // エコロケの規則（EchoEmitter / EchoReceiver と同じ値）
        const float PulseRange = 12f, PulseAngle = 55f, DelayPerMeter = 0.045f, GlowDuration = 0.6f;

        static ShotList list;
        static int shotIndex, frameIndex;
        static bool running, hooked;
        static GameObject camGo;
        static Camera cam;
        static Light beam;
        static RenderTexture rt;
        static Texture2D tex;
        static Vector3[] recvPos;
        static List<Renderer[]> recvRenderers;
        static MaterialPropertyBlock mpb;
        static string lastMessage = "";

        public static string Begin(string jsonPath)
        {
            Cleanup();
            list = JsonUtility.FromJson<ShotList>(File.ReadAllText(jsonPath));
            shotIndex = 0; frameIndex = 0; running = true;
            Setup();
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return "開始: " + list.shots.Length + " カット";
        }

        public static string Status()
        {
            if (list == null) return "予定なし";
            if (!running) return "完了 " + lastMessage;
            var s = list.shots[shotIndex];
            return "撮影中 " + (shotIndex + 1) + "/" + list.shots.Length + " " + s.name + " " + frameIndex + "/" + FrameCount(s);
        }

        public static string Pump(float seconds)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (running && EditorApplication.timeSinceStartup < until) Step();
            return Status();
        }

        static void Tick()
        {
            if (!running) return;
            var until = EditorApplication.timeSinceStartup + 0.4;
            while (running && EditorApplication.timeSinceStartup < until) Step();
        }

        static int FrameCount(Shot s) { return Mathf.CeilToInt(s.duration * s.fps); }

        static void Setup()
        {
            camGo = new GameObject("REC_Camera") { hideFlags = HideFlags.HideAndDontSave };
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            var lgo = new GameObject("REC_Beam") { hideFlags = HideFlags.HideAndDontSave };
            lgo.transform.SetParent(camGo.transform, false);
            lgo.transform.localPosition = new Vector3(0.18f, -0.28f, 0f);
            beam = lgo.AddComponent<Light>();
            beam.type = LightType.Spot;
            var rig = GameObject.Find("=== SYSTEM ===/GameManagement/MemoryFlashlight");
            var src = rig != null ? rig.GetComponentInChildren<Light>(true) : null;
            if (src != null)
            {
                beam.range = src.range; beam.spotAngle = src.spotAngle; beam.intensity = src.intensity;
                beam.color = src.color; beam.cookie = src.cookie;
            }
            else { beam.range = 20f; beam.spotAngle = 40f; beam.intensity = 3.5f; }
            beam.shadows = LightShadows.Soft;
            beam.renderMode = LightRenderMode.ForcePixel;

            rt = new RenderTexture(list.width, list.height, 24) { antiAliasing = 8 };
            tex = new Texture2D(list.width, list.height, TextureFormat.RGB24, false);
            mpb = new MaterialPropertyBlock();

            // エコロケの受信機（位置と、光らせるレンダラー）
            var pos = new List<Vector3>();
            recvRenderers = new List<Renderer[]>();
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "EchoReceiver" || !mb.gameObject.activeInHierarchy) continue;
                var so = new SerializedObject(mb);
                var arr = so.FindProperty("targetRenderers");
                if (arr == null || arr.arraySize == 0) continue;
                var rs = new List<Renderer>();
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var r = arr.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                    if (r != null) rs.Add(r);
                }
                if (rs.Count == 0) continue;
                pos.Add(mb.transform.position);
                recvRenderers.Add(rs.ToArray());
            }
            recvPos = pos.ToArray();
        }

        static void Step()
        {
            var s = list.shots[shotIndex];
            int n = FrameCount(s);
            float t = frameIndex / s.fps;

            Pose(s, t, out var p, out var look);
            camGo.transform.position = p;
            camGo.transform.rotation = Quaternion.LookRotation(look - p);
            cam.fieldOfView = s.fov;
            cam.targetTexture = rt;

            foreach (var v in s.views.Split(','))
            {
                string view = v.Trim();
                if (view == "memory")
                {
                    cam.cullingMask = (1 << 0) | (1 << 24);
                    cam.clearFlags = CameraClearFlags.Skybox;
                    beam.enabled = true;
                }
                else if (view == "thermal")
                {
                    cam.cullingMask = 1 << 22;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                    beam.enabled = false;
                }
                else if (view == "echo")
                {
                    cam.cullingMask = 1 << 23;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                    beam.enabled = false;
                    ApplyEcho(s, t);
                }
                else continue;

                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, list.width, list.height), 0, 0);
                tex.Apply(false);
                RenderTexture.active = null;
                string dir = Path.Combine(list.outDir, s.name, view);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, frameIndex.ToString("0000") + ".jpg"), tex.EncodeToJPG(93));
            }

            frameIndex++;
            if (frameIndex >= n)
            {
                frameIndex = 0;
                shotIndex++;
                if (shotIndex >= list.shots.Length)
                {
                    lastMessage = list.shots.Length + " カット書き出し済み → " + list.outDir;
                    Cleanup();
                }
            }
        }

        // 各受信機の光り方 = 直近2回のパルスのうち明るいほう
        static void ApplyEcho(Shot s, float t)
        {
            var pulses = new List<float>();
            if (t >= s.pulseStart)
            {
                int k = Mathf.FloorToInt((t - s.pulseStart) / s.pulseInterval);
                for (int j = Mathf.Max(0, k - 1); j <= k; j++) pulses.Add(s.pulseStart + j * s.pulseInterval);
            }
            var origins = new List<Vector3>(); var fwds = new List<Vector3>();
            foreach (var tp in pulses)
            {
                Pose(s, tp, out var p0, out var l0);
                origins.Add(p0); fwds.Add((l0 - p0).normalized);
            }
            for (int i = 0; i < recvPos.Length; i++)
            {
                float g = 0f;
                for (int j = 0; j < pulses.Count; j++)
                {
                    Vector3 to = recvPos[i] - origins[j];
                    float d = to.magnitude;
                    if (d > PulseRange) continue;
                    if (d > 0.01f && Vector3.Angle(fwds[j], to / d) > PulseAngle) continue;
                    float dt = t - pulses[j];
                    float delay = d * DelayPerMeter;
                    float v;
                    if (dt < delay) v = 0f;
                    else if (dt < delay * 2f) v = 1f;
                    else v = Mathf.Clamp01(1f - (dt - delay * 2f) / GlowDuration);
                    g = Mathf.Max(g, v);
                }
                SetGlow(recvRenderers[i], g);
            }
        }

        static void SetGlow(Renderer[] rs, float g)
        {
            foreach (var r in rs)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat("_GlowIntensity", g);
                r.SetPropertyBlock(mpb);
            }
        }

        // カメラの位置と注視点。キーの間は Catmull-Rom でなめらかにつなぎ、全体に緩急を付ける
        public static void Pose(Shot s, float t, out Vector3 pos, out Vector3 look)
        {
            var k = s.keys;
            if (k.Length == 1) { pos = k[0].pos; look = k[0].look; return; }
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, s.duration));
            if (s.ease) u = u * u * (3f - 2f * u);
            float tt = Mathf.Lerp(k[0].t, k[k.Length - 1].t, u);
            int i = 0;
            while (i < k.Length - 2 && tt > k[i + 1].t) i++;
            float a = Mathf.InverseLerp(k[i].t, k[i + 1].t, tt);
            var p0 = k[Mathf.Max(0, i - 1)]; var p1 = k[i]; var p2 = k[i + 1]; var p3 = k[Mathf.Min(k.Length - 1, i + 2)];
            pos = CR(p0.pos, p1.pos, p2.pos, p3.pos, a);
            look = CR(p0.look, p1.look, p2.look, p3.look, a);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public static void Cleanup()
        {
            running = false;
            if (recvRenderers != null) foreach (var rs in recvRenderers) SetGlow(rs, 0f);
            if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            camGo = null; cam = null; beam = null; rt = null; tex = null;
            if (hooked) { EditorApplication.update -= Tick; hooked = false; }
        }
    }
}
