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
    /// ・カメラは人が遊んでいるように動かす（Humanize）。まっすぐ等速・正面固定だと機械的に見えたため。
    ///   - 首の小さな揺れ（VR の頭は止まっていても少し動く）と、歩くときの上下・左右の揺れ。
    ///   - 歩き出し・止まりは加速と減速をつける。止まった瞬間は勢いで少し前に傾く。
    ///   - 穴: 歩きながら左右を見渡し、床が抜けたら下を向く。
    ///   - だるま: 掛け声を聞いてから少し遅れて走り出し、言い終わる前に止まる。
    ///     見つかる回は、まだ見張られているのに我慢できずに歩き出してしまう（フライング）。
    ///     名指しされたら、のけぞって一歩下がる。
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
        // "path" … pathPos の地点を順に歩く。各区間では pathLook の点を見て、着いたら pathPause 秒止まる
        public string mode = "daruma";
        public Vector3 walkDir = Vector3.forward;
        // 穴などで歩きながら見渡す予定: (秒, 進む向きからの左右の角度, 下向きの角度)
        public Vector3[] lookKeys =
        {
            new Vector3(0.0f, 0f, 6f), new Vector3(0.7f, -38f, 4f), new Vector3(1.6f, -8f, 9f),
            new Vector3(2.2f, 34f, 3f), new Vector3(3.0f, 4f, 12f), new Vector3(3.9f, -6f, 8f),
        };
        public Vector3[] pathPos, pathLook;
        public float[] pathPause;
        public int seed = 1;
        public float runSpeedMin = 1.7f, runSpeedMax = 2.2f;

        Camera cam; Light beam; RenderTexture rt; Texture2D tex;
        UdonBehaviour dw; GameObject deathHud;
        int frame; int lastPhase = -1; int watchCount; float deathTime = -1f;
        Vector3 pos; bool done;

        // 人らしい動き（Humanize）
        System.Random rng;
        float yaw, pitch, roll, yawVel, pitchVel, rollVel;
        float speed, prevSpeed, bob, phaseStart, roundSpeed, react, startHeadY, fallTime = -1f;
        float noiseSeed, pauseLeft;
        int seg;
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
        public static string StartWalk(string outDir, Vector3 start, Vector3 dir, float speed, float maxSeconds)
        {
            if (!EditorApplication.isPlaying) return "再生中でない";
            if (current != null) current.Finish();
            var c = new BlindPlayCapture();
            c.outDir = outDir; c.mode = "walk"; c.startPos = start; c.walkDir = dir.normalized;
            c.walkSpeed = speed; c.maxSeconds = maxSeconds;
            current = c;
            c.Start();
            Hook(true);
            return "開始";
        }

        // 地点を順に歩く（瞬間移動で動かすので落ちない）。止まって助かる場面や、部屋を見て回る場面に使う
        public static string StartPath(string outDir, Vector3 start, Vector3[] points, Vector3[] looks, float[] pauses, float speed, float maxSeconds)
        {
            if (!EditorApplication.isPlaying) return "再生中でない";
            if (current != null) current.Finish();
            var c = new BlindPlayCapture();
            c.outDir = outDir; c.mode = "path"; c.startPos = start; c.pathPos = points; c.pathLook = looks; c.pathPause = pauses;
            c.walkSpeed = speed; c.maxSeconds = maxSeconds;
            Vector3 d = looks[0] - start; d.y = 0f;
            c.walkDir = d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
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
            Networking.LocalPlayer.TeleportTo(pos, Quaternion.LookRotation(mode != "daruma" ? walkDir : Vector3.forward));
            rng = new System.Random(seed);
            noiseSeed = (float)rng.NextDouble() * 100f;
            Vector3 f0 = mode != "daruma" ? walkDir : Vector3.forward;
            yaw = Mathf.Atan2(f0.x, f0.z) * Mathf.Rad2Deg;
            pitch = mode == "walk" ? lookKeys[0].z : 2f;
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
                phaseStart = t;
                if (phase == 0)
                {
                    roundSpeed = Mathf.Lerp(runSpeedMin, runSpeedMax, (float)rng.NextDouble());
                    react = 0.22f + (float)rng.NextDouble() * 0.15f;   // 掛け声を聞いてから動き出すまで
                }
                events.Add(t.ToString("F3") + " phase=" + phase + extra + (phase == 2 ? " watch#" + watchCount : ""));
                lastPhase = phase;
            }
            if (deathTime < 0f && deathHud != null && deathHud.activeSelf)
            {
                deathTime = t;
                events.Add(t.ToString("F3") + " death");
            }

            var lp = Networking.LocalPlayer;
            float dt = 1f / fps;
            float local = t - phaseStart;
            float wantYaw, wantPitch, wantRoll = 0f, smooth = 0.24f;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            // 瞬間移動の直後は頭の位置がまだ前の場所なので、少し待ってから基準の高さを取る
            if (t < 0.3f) startHeadY = head.y;

            if (mode == "path")
            {
                // いまの区間の目標地点へ歩き、着く手前で減速する。区間ごとに見る物を変える。
                int n = pathPos.Length;
                int k = Mathf.Min(seg, n - 1);
                Vector3 to = pathLook[k] - head;
                wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                float want = 0f;
                if (seg < n)
                {
                    Vector3 d = pathPos[seg] - pos; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist > 0.03f)
                    {
                        want = walkSpeed * Mathf.Clamp(dist / 0.9f, 0.25f, 1f);
                        speed = Mathf.MoveTowards(speed, want, (want > speed ? 3.5f : 7f) * dt);
                        Vector3 step = d / dist * Mathf.Min(dist, speed * dt);
                        pos += step;
                        lp.TeleportTo(pos, BodyRotation());
                    }
                    else
                    {
                        speed = Mathf.MoveTowards(speed, 0f, 9f * dt);
                        pauseLeft += dt;
                        if (pauseLeft >= pathPause[seg]) { pauseLeft = 0f; seg++; events.Add(t.ToString("F3") + " point" + seg); }
                    }
                }
                else speed = Mathf.MoveTowards(speed, 0f, 9f * dt);
            }
            else if (mode == "walk")
            {
                // 予定に沿って見渡しながら歩く。横を向いている間は少し歩みが遅くなる。
                Vector3 key = lookKeys[0];
                foreach (var k in lookKeys) if (t >= k.x) key = k;
                float baseYaw = Mathf.Atan2(walkDir.x, walkDir.z) * Mathf.Rad2Deg;
                wantYaw = baseYaw + key.y;
                wantPitch = key.z;
                float want = deathTime < 0f ? walkSpeed * (Mathf.Abs(key.y) > 25f ? 0.65f : 1f) : 0f;
                speed = Mathf.MoveTowards(speed, want, (want > speed ? 3.5f : 6f) * dt);

                // 床が抜けた: 反射的に足元を見る。首は速く動き、体が少し傾く。
                if (fallTime < 0f && head.y < startHeadY - 0.2f) { fallTime = t; events.Add(t.ToString("F3") + " fall"); }
                if (fallTime >= 0f)
                {
                    wantPitch = 58f; wantYaw = baseYaw + 8f; wantRoll = -7f; smooth = 0.17f;
                }
                if (deathTime < 0f)
                {
                    Vector3 v = lp.GetVelocity();
                    float s = fallTime >= 0f ? speed * 0.5f : speed;
                    lp.SetVelocity(new Vector3(walkDir.x * s, v.y, walkDir.z * s));
                }
            }
            else
            {
                // だるまの方を見る
                Vector3 to = (dw != null ? ((Transform)dw.GetProgramVariable("daruma")).position : pos + Vector3.forward * 10f) - head;
                wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantPitch = 1.5f;
                float want = 0f;
                if (deathTime < 0f)
                {
                    float len = dw != null ? (float)dw.GetProgramVariable("phaseLength") : 3f;
                    if (phase == 0)
                    {
                        // 掛け声が聞こえてから走り出し、言い終わる少し前に止まる
                        if (local > react && len - local > 0.3f && pos.z < stopZ) want = roundSpeed;
                        // 1回目は走りながら横の棚をちらっと見る
                        if (watchCount == 0 && local > 0.7f && local < 1.3f) wantYaw += 26f;
                    }
                    else if (phase == 2 && watchCount == catchOnWatch && local > 1.3f)
                    {
                        // 見張りがもう終わると思って歩き出してしまう
                        want = 0.9f;
                        wantPitch = 3f;
                    }
                    else if (phase == 3 && local < 0.4f)
                    {
                        // 名指しされて、のけぞって一歩下がる
                        want = -1.4f;
                    }
                    if (phase == 3) { wantPitch = -6f; smooth = 0.12f; wantRoll = 3f; }
                }
                float acc = Mathf.Abs(want) > Mathf.Abs(speed) ? 4.5f : 9f;
                speed = Mathf.MoveTowards(speed, want, acc * dt);
                if (Mathf.Abs(speed) > 0.001f && deathTime < 0f)
                {
                    pos.z += speed * dt;
                    // 真っすぐではなく、少しふらつきながら進む（棚に入らない幅）
                    pos.x = startPos.x + 0.14f * Mathf.Sin(pos.z * 0.9f + noiseSeed);
                    lp.TeleportTo(pos, BodyRotation());
                }
            }

            // 止まる・走り出すときの勢いで、頭が前後に少し傾く
            float accel = (speed - prevSpeed) / dt;
            prevSpeed = speed;
            wantPitch += Mathf.Clamp(-accel * 0.55f, -3f, 4f);

            yaw = Mathf.SmoothDampAngle(yaw, wantYaw, ref yawVel, smooth, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDampAngle(pitch, wantPitch, ref pitchVel, smooth, Mathf.Infinity, dt);
            roll = Mathf.SmoothDampAngle(roll, wantRoll, ref rollVel, 0.3f, Mathf.Infinity, dt);

            // 歩くときの揺れ（1歩 約0.7m）と、止まっていても残る頭の小さな揺れ
            float sp = Mathf.Clamp01(Mathf.Abs(speed) / 2f);
            bob += Mathf.Abs(speed) * dt / 0.7f * Mathf.PI;
            float n1 = Mathf.PerlinNoise(t * 0.35f, noiseSeed) - 0.5f;
            float n2 = Mathf.PerlinNoise(t * 0.42f, noiseSeed + 7f) - 0.5f;
            float n3 = Mathf.PerlinNoise(t * 0.3f, noiseSeed + 13f) - 0.5f;
            float breathe = Mathf.Sin(t * 1.7f) * 0.004f;
            var rot = Quaternion.Euler(pitch + n2 * 2.2f + Mathf.Abs(Mathf.Sin(bob)) * 0.8f * sp,
                                       yaw + n1 * 3.0f,
                                       roll + n3 * 1.6f + Mathf.Sin(bob) * 0.7f * sp);
            Vector3 offset = rot * new Vector3(Mathf.Sin(bob) * 0.012f * sp, Mathf.Cos(2f * bob) * 0.011f * sp + breathe, 0f);

            Render(head + offset, rot);

            frame++;
            if ((deathTime >= 0f && t > deathTime + tailAfterDeath) || t > maxSeconds) Finish();
        }

        // 体の向き＝見ている向き（左右だけ）。エコロケのパルスは手の向きに出るので、
        // 体を北向きのままにすると、パルスが後ろや横に出て何も光らなかった。
        Quaternion BodyRotation()
        {
            return Quaternion.Euler(0f, yaw, 0f);
        }

        void Render(Vector3 head, Quaternion rot)
        {
            cam.transform.SetPositionAndRotation(head, rot);

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
