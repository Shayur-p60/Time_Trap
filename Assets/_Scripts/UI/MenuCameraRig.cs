using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // The camera behind the main menu: a loop of slow shots through the facility, with a fade through black between
    // them. Goes on its own Camera object (Map/Gameplay/MenuCamera), which has no AudioListener: the ears stay with the
    // player.
    //
    // While the menu is up the player's camera is off and the player's body, arms and torch are not drawn. Play: the
    // camera glides into the player camera's exact pose and hands over to it.
    // Everything the menu changes is put back at the hand-over: the fog, the fill light (a child of this camera, so it
    // only ever exists here), the previewed portal, the player's renderers. Nothing is written to the saved scene.
    [DefaultExecutionOrder(500)] // after every other Start, so nothing caches a missing main camera
    [RequireComponent(typeof(Camera))]
    public class MenuCameraRig : MonoBehaviour
    {
        [Serializable]
        public class Shot
        {
            public string name = "Shot";
            public Vector3 startPosition;
            public Vector3 startEuler;
            public Vector3 endPosition;
            public Vector3 endEuler;
            [Tooltip("Seconds from the start pose to the end pose.")]
            public float duration = 9f;
            public float fieldOfView = 60f;
        }

        // True while the fly-through owns the picture (the player is hidden and the torch is held off).
        public static bool Active { get; private set; }

        public Shot[] shots = new Shot[0];
        [Tooltip("Seconds of fade through black between two shots (half out, half in).")]
        public float fadeTime = 0.6f;
        [Tooltip("Seconds the camera takes to glide into the player's view when Play is pressed.")]
        public float glideTime = 1.5f;

        [Header("Menu lighting (restored when Play starts)")]
        [Tooltip("Optional. A soft light that travels with this camera, so the shots read on a projector.")]
        public Light fillLight;
        public bool overrideFog = true;
        public Color fogColour = new Color(0.03f, 0.045f, 0.055f, 1f);
        public float fogDensity = 0.035f;

        [Header("Look")]
        [Range(0f, 1f)] public float vignette = 0.85f;
        [Tooltip("A short time-distortion glitch (ScreenFX) every so many seconds: random between these two values.")]
        public Vector2 glitchInterval = new Vector2(8f, 12f);
        public float glitchTime = 0.2f;

        [Header("Scene (found when empty)")]
        public Camera playerCamera;
        [Tooltip("Shown, fully open, while the menu is up: the last shot ends on it.")]
        public EscapePortal portal;

        public bool InMenu { get; private set; }
        public bool Gliding { get; private set; }
        public int ShotIndex { get; private set; }
        // 0 = clear, 1 = black (the cut between two shots).
        public float FadeAlpha { get; private set; }

        Camera cam;
        readonly List<Renderer> hidden = new List<Renderer>();
        Image fadeImage, vignetteImage;
        GameObject overlay;
        float shotTime;
        float glide;
        Vector3 glideFromPosition;
        Quaternion glideFromRotation;
        float glideFromFov;
        Action onArrived;
        float nextGlitch;
        float fillIntensity;

        bool fogSaved;
        bool savedFog;
        FogMode savedFogMode;
        Color savedFogColour;
        float savedFogDensity;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.enabled = false;
            if (fillLight != null)
            {
                fillIntensity = fillLight.intensity;
                fillLight.enabled = false;
            }
        }

        void Start()
        {
            if (playerCamera == null)
            {
                var player = FindAnyObjectByType<PlayerMovementController>();
                if (player != null) playerCamera = player.GetComponentInChildren<Camera>(true);
            }
            if (portal == null) portal = FindAnyObjectByType<EscapePortal>();

            if (GameManager.InMenu) Enter();
        }

        void OnDisable()
        {
            // a reload (or leaving Play mode) with the menu still up
            if (InMenu) Exit();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewPlaySession()
        {
            Active = false;
        }

        // ------------------------------------------------------------------ taking and handing back the picture

        void Enter()
        {
            InMenu = true;
            Active = true;
            Gliding = false;

            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                // the body, the arms, the torch beam and its dust: not drawn, whatever their own scripts do meanwhile
                var root = playerCamera.GetComponentInParent<CharacterController>();
                Transform top = root != null ? root.transform : playerCamera.transform;
                foreach (var r in top.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.forceRenderingOff) continue;
                    r.forceRenderingOff = true;
                    hidden.Add(r);
                }
            }
            cam.enabled = true;

            if (overrideFog)
            {
                fogSaved = true;
                savedFog = RenderSettings.fog;
                savedFogMode = RenderSettings.fogMode;
                savedFogColour = RenderSettings.fogColor;
                savedFogDensity = RenderSettings.fogDensity;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColour;
                RenderSettings.fogDensity = fogDensity;
            }
            if (fillLight != null)
            {
                fillLight.intensity = fillIntensity;
                fillLight.enabled = true;
            }
            if (portal != null) portal.Preview(true);

            BuildOverlay();
            ShotIndex = 0;
            shotTime = 0f;
            nextGlitch = UnityEngine.Random.Range(glitchInterval.x, glitchInterval.y);
            ApplyShot();
        }

        void Exit()
        {
            InMenu = false;
            Active = false;
            Gliding = false;

            foreach (var r in hidden)
                if (r != null) r.forceRenderingOff = false;
            hidden.Clear();

            if (fogSaved)
            {
                fogSaved = false;
                RenderSettings.fog = savedFog;
                RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogColor = savedFogColour;
                RenderSettings.fogDensity = savedFogDensity;
            }
            if (fillLight != null)
            {
                fillLight.enabled = false;
                fillLight.intensity = fillIntensity;
            }
            if (portal != null) portal.Preview(false);
            if (overlay != null) overlay.SetActive(false);

            if (cam != null) cam.enabled = false;
            if (playerCamera != null) playerCamera.enabled = true;
        }

        // The vignette and the black that the shots fade through, on a canvas under the menu itself.
        void BuildOverlay()
        {
            if (overlay != null)
            {
                overlay.SetActive(true);
                return;
            }
            overlay = new GameObject("MenuCameraOverlay");
            overlay.transform.SetParent(transform, false);
            MenuKit.MakeCanvas(overlay, 120, false);
            vignetteImage = MenuKit.AddImage(MenuKit.Fill("Vignette", overlay.transform), MenuKit.VignetteSprite, new Color(1f, 1f, 1f, vignette));
            fadeImage = MenuKit.AddImage(MenuKit.Fill("Fade", overlay.transform), null, new Color(0f, 0f, 0f, 0f));
        }

        // ------------------------------------------------------------------ every frame (real time)

        void Update()
        {
            if (!InMenu) return;
            // the first frame after a scene load can be seconds long: it must not skip a whole shot
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            if (Gliding)
            {
                UpdateGlide(dt);
                return;
            }

            if (shots.Length > 0)
            {
                Shot shot = shots[ShotIndex];
                shotTime += dt;
                if (shotTime >= Mathf.Max(0.5f, shot.duration))
                {
                    ShotIndex = (ShotIndex + 1) % shots.Length;
                    shotTime = 0f;
                }
                ApplyShot();
            }

            nextGlitch -= dt;
            if (nextGlitch <= 0f)
            {
                nextGlitch = UnityEngine.Random.Range(glitchInterval.x, glitchInterval.y);
                if (ScreenFX.Instance != null) ScreenFX.Instance.TriggerGlitch(glitchTime);
            }
        }

        void ApplyShot()
        {
            if (shots.Length == 0)
            {
                SetFade(0f);
                return;
            }
            Shot shot = shots[ShotIndex];
            float duration = Mathf.Max(0.5f, shot.duration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shotTime / duration));
            transform.SetPositionAndRotation(
                Vector3.Lerp(shot.startPosition, shot.endPosition, k),
                Quaternion.Slerp(Quaternion.Euler(shot.startEuler), Quaternion.Euler(shot.endEuler), k));
            cam.fieldOfView = shot.fieldOfView;

            // black at both ends of a shot, so each cut happens in the dark
            float half = Mathf.Max(0.01f, fadeTime * 0.5f);
            SetFade(1f - Mathf.Clamp01(Mathf.Min(shotTime, duration - shotTime) / half));
        }

        void SetFade(float alpha)
        {
            FadeAlpha = alpha;
            if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, alpha);
        }

        // ------------------------------------------------------------------ Play

        // Glides from wherever the fly-through is into the player camera's pose, then hands the picture over.
        public void BeginGlide(Action arrived)
        {
            if (!InMenu || Gliding) return;
            onArrived = arrived;
            Gliding = true;
            glide = 0f;
            glideFromPosition = transform.position;
            glideFromRotation = transform.rotation;
            glideFromFov = cam.fieldOfView;
        }

        void UpdateGlide(float dt)
        {
            glide += dt / Mathf.Max(0.01f, glideTime);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(glide));

            if (playerCamera != null)
            {
                Transform target = playerCamera.transform;
                transform.SetPositionAndRotation(
                    Vector3.Lerp(glideFromPosition, target.position, k),
                    Quaternion.Slerp(glideFromRotation, target.rotation, k));
                cam.fieldOfView = Mathf.Lerp(glideFromFov, playerCamera.fieldOfView, k);
            }

            // the menu's lighting leaves on the way, so the hand-over is to the game's own
            SetFade(Mathf.Min(FadeAlpha, 1f - k));
            if (vignetteImage != null) vignetteImage.color = new Color(1f, 1f, 1f, vignette * (1f - k));
            if (fillLight != null) fillLight.intensity = fillIntensity * (1f - k);
            if (fogSaved)
            {
                RenderSettings.fogColor = Color.Lerp(fogColour, savedFogColour, k);
                RenderSettings.fogDensity = Mathf.Lerp(fogDensity, savedFog ? savedFogDensity : 0f, k);
            }

            if (glide < 1f) return;
            Action done = onArrived;
            onArrived = null;
            Exit();
            // the torch arm comes up into view instead of just appearing
            var weapons = FindAnyObjectByType<PlayerWeaponState>();
            if (weapons != null) weapons.RaiseTorchFromBelow();
            done?.Invoke();
        }
    }
}
