using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using VRC.SDKBase;
using VRC.Udon;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 再生中（ClientSim）のゲームを、そのまま映像素材として書き出す。
    /// BlindShotRecorder は止まったシーンを撮る道具なので、だるまの振り向きや
    /// ミニだるまが飛んでくるところ（＝ゲームが動かないと起きないこと）は撮れない。
    ///
    /// ・Time.captureFramerate で 1コマ = 1/fps 秒ずつゲームを進めるので、書き出しが遅くても動きは滑らか。
    /// ・プレイヤーの目の位置から 過去人・サーモ・エコロケ の3視点を同じ瞬間で書き出す。
    /// ・room11（だるま）用の台本を持つ: だるまが背を向けている間だけ前に歩き、見張りの間は止まる。
    ///   catchOnWatch 回目の見張りでは、わざと動いて見つかる。
    /// ・掛け声・振り向き・名指し・死亡の時刻を events.txt に書き出す（音は後で元のファイルを重ねる）。
    ///
    /// 使い方（再生中に execute_code などから）:
    ///   BlindPlayCapture.StartDaruma("…/out", 2, 30f);
    ///   BlindPlayCapture.Status();
    /// エディタ専用。シーンには何も残さない（HideAndDontSave）。
    ///
    /// ⚠️ Editor フォルダの MonoBehaviour はゲームの物に付けられない（Unity の制限）。
    ///    そのため部品にはせず、PlayerLoop（毎フレームの処理の流れ）の PostLateUpdate に
    ///    自分の処理を差し込んでいる。これで1フレームに必ず1回撮れる。
    /// </summary>
    public class BlindPlayCapture
    {
        public string outDir;
        public int width = 1920, height = 1080;
        public int fps = 30;
        public float fov = 70f;
        public Vector3 startPos = new Vector3(10.2f, 0f, -54f);
        public float walkSpeed = 1.2f;
        public float stopZ = -31f;
        public int catchOnWatch = 2;
        public float maxSeconds = 40f;
        public float tailAfterDeath = 2.6f;
        // "daruma" … だるまの台本 / "walk" … walkDir へ歩き続ける（穴に落ちる場面など）
        public string mode = "daruma";
        public Vector3 walkDir = Vector3.forward;
        public float lookDown = 0.02f;

        Camera cam; Light beam; RenderTexture rt; Texture2D tex;
        UdonBehaviour dw; GameObject deathHud;
        int frame; int lastPhase = -1; int watchCount; float deathTime = -1f;
        Vector3 pos; bool done;
        readonly List<string> events = new List<string>();
        static BlindPlayCapture current;

        public static string StartDaruma(string outDir, int catchOnWatch, float maxSeconds)
        {
            if (!EditorApplication.isPlaying) return "再生中でない";
            if (current != null) current.Finish();
            var c = new BlindPlayCapture();
            c.outDir = outDir; c.catchOnWatch = catchOnWatch; c.maxSeconds = maxSeconds;
            current = c;
            c.Start();
            Hook(true);
            return "開始";
        }

        // 決めた方向へ歩き続ける。位置を瞬間移動させると落ちないので、歩く速さを与えて歩かせる。
        public static string StartWalk(string outDir, Vector3 start, Vector3 dir, float speed, float lookDown, float maxSeconds)
        {
            if (!EditorApplication.isPlaying) return "再生中でない";
            if (current != null) current.Finish();
            var c = new BlindPlayCapture();
            c.outDir = outDir; c.mode = "walk"; c.startPos = start; c.walkDir = dir.normalized;
            c.walkSpeed = speed; c.lookDown = lookDown; c.maxSeconds = maxSeconds;
            current = c;
            c.Start();
            Hook(true);
            return "開始";
        }

        public static string Status()
        {
            if (current == null) return "なし";
            return (current.done ? "完了 " : "撮影中 ") + current.frame + "コマ (" + (current.frame / (float)current.fps).ToString("F1") + "秒) 見張り" + current.watchCount + "回 死亡" + current.deathTime.ToString("F2");
        }

        // PostLateUpdate の最後に Tick を差し込む / 外す
        static void Hook(bool on)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var list = new List<PlayerLoopSystem>(root.subSystemList[i].subSystemList);
                list.RemoveAll(x => x.type == typeof(BlindPlayCapture));
                if (on) list.Add(new PlayerLoopSystem { type = typeof(BlindPlayCapture), updateDelegate = Tick });
                root.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(root);
        }

        static void Tick()
        {
            if (current == null || current.done) return;
            if (!EditorApplication.isPlaying) { current.Finish(); return; }
            current.LateUpdate();
        }

        void Start()
        {
            Directory.CreateDirectory(outDir);
            foreach (var v in new[] { "memory", "thermal", "echo" }) Directory.CreateDirectory(Path.Combine(outDir, v));

            var camGo = new GameObject("BLIND_PlayCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f; cam.fieldOfView = fov;

            // 過去人の懐中電灯（ゲームの MemoryFlashlight と同じ光）。過去人の視点を撮るときだけ点ける
            var lgo = new GameObject("BLIND_PlayCaptureBeam") { hideFlags = HideFlags.HideAndDontSave };
            lgo.transform.SetParent(camGo.transform, false);
            lgo.transform.localPosition = new Vector3(0.18f, -0.28f, 0f);
            beam = lgo.AddComponent<Light>();
            beam.type = LightType.Spot;
            var rig = GameObject.Find("=== SYSTEM ===/GameManagement/MemoryFlashlight");
            var src = rig != null ? rig.GetComponentInChildren<Light>(true) : null;
            if (src != null) { beam.range = src.range; beam.spotAngle = src.spotAngle; beam.intensity = src.intensity; beam.color = src.color; beam.cookie = src.cookie; }
            else { beam.range = 20f; beam.spotAngle = 40f; beam.intensity = 3.5f; }
            beam.shadows = LightShadows.Soft;
            beam.enabled = false;

            rt = new RenderTexture(width, height, 24) { antiAliasing = 8 };
            tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            foreach (var ub in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                if (ub.gameObject.scene.IsValid() && ub.programSource != null && ub.programSource.name == "DarumaWatcher") dw = ub;
            deathHud = GameObject.Find("=== SYSTEM ===/DeathHud");
            if (deathHud == null) foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t.name == "DeathHud" && t.gameObject.scene.IsValid()) deathHud = t.gameObject;

            pos = startPos;
            Networking.LocalPlayer.TeleportTo(pos, Quaternion.LookRotation(mode == "walk" ? walkDir : Vector3.forward));
            Time.captureFramerate = fps;
            events.Add("start");
        }

        void LateUpdate()
        {
            if (done) return;
            float t = frame / (float)fps;

            int phase = dw != null ? (int)dw.GetProgramVariable("phase") : -1;
            if (phase != lastPhase)
            {
                string extra = "";
                if (phase == 0)
                {
                    var cs = dw.GetProgramVariable("chantSound") as AudioSource;
                    if (cs != null && cs.clip != null) extra = " chant=" + cs.clip.name + " len=" + cs.clip.length.ToString("F2");
                    extra += " awayLength=" + ((float)dw.GetProgramVariable("phaseLength")).ToString("F2");
                }
                if (phase == 2) watchCount++;
                events.Add(t.ToString("F3") + " phase=" + phase + extra + (phase == 2 ? " watch#" + watchCount : ""));
                lastPhase = phase;
            }
            if (deathTime < 0f && deathHud != null && deathHud.activeSelf)
            {
                deathTime = t;
                events.Add(t.ToString("F3") + " death");
            }

            var lp = Networking.LocalPlayer;
            if (mode == "walk")
            {
                // 死ぬまで歩き続ける。縦の速さはそのまま（落ちるときは重力に任せる）
                if (deathTime < 0f)
                {
                    Vector3 v = lp.GetVelocity();
                    lp.SetVelocity(new Vector3(walkDir.x * walkSpeed, v.y, walkDir.z * walkSpeed));
                }
            }
            else if (deathTime < 0f)
            {
                // 台本: 背を向けている間だけ前に歩く。
                // catchOnWatch 回目の見張りでは後ろに下がる（＝見つかる）。
                // 横に動くと棚に入り込んで画面がふさがった。後ろなら、だるまが画面の真ん中に残る。
                if (phase == 0 && pos.z < stopZ)
                {
                    pos.z += walkSpeed / fps;
                    lp.TeleportTo(pos, Quaternion.identity);
                }
                else if (phase == 2 && watchCount == catchOnWatch)
                {
                    pos.z -= walkSpeed / fps;
                    lp.TeleportTo(pos, Quaternion.identity);
                }
            }

            Render(Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position);

            frame++;
            if ((deathTime >= 0f && t > deathTime + tailAfterDeath) || t > maxSeconds) Finish();
        }

        void Render(Vector3 head)
        {
            Vector3 fwd = mode == "walk" ? walkDir : Vector3.forward;
            cam.transform.SetPositionAndRotation(head, Quaternion.LookRotation(new Vector3(fwd.x, -lookDown, fwd.z)));

            // 死亡時の文字の板は CheckpointManager が PostLateUpdate で頭の前へ動かすが、
            // この撮影処理の方が先に走ることがあり、板が前の場所に取り残されて写らない。
            // 撮る直前にカメラの前(0.7m)へ置き直す。
            if (deathHud != null && deathHud.activeSelf)
            {
                deathHud.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 0.7f, cam.transform.rotation);
            }
            cam.targetTexture = rt;
            const int ui = 1 << 5;   // 死亡時の文字(UI レイヤー)も写す
            Shot("memory", (1 << 0) | (1 << 9) | (1 << 10) | (1 << 24) | ui, CameraClearFlags.Skybox, true);
            Shot("thermal", (1 << 22) | ui, CameraClearFlags.SolidColor, false);
            Shot("echo", (1 << 23) | ui, CameraClearFlags.SolidColor, false);
        }

        void Shot(string view, int mask, CameraClearFlags clear, bool light)
        {
            cam.cullingMask = mask; cam.clearFlags = clear; cam.backgroundColor = Color.black;
            beam.enabled = light;
            cam.Render();
            beam.enabled = false;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, view, frame.ToString("0000") + ".jpg"), tex.EncodeToJPG(92));
        }

        void Finish()
        {
            done = true;
            Time.captureFramerate = 0;
            events.Add((frame / (float)fps).ToString("F3") + " end frames=" + frame);
            File.WriteAllLines(Path.Combine(outDir, "events.txt"), events);
            if (cam != null) Object.DestroyImmediate(cam.gameObject);
            if (rt != null) rt.Release();
            Hook(false);
        }
    }
}
