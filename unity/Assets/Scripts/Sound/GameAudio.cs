using RocketSlam.Cameras;
using RocketSlam.Effects;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEngine;
using RocketSlam.Progress;
using UnityEngine.Audio;

namespace RocketSlam.Sound
{
    /// <summary>
    /// All game sound: engine (two cross-faded loops, volume and pitch by thrust), synthesised wind by airspeed,
    /// leg deploy/lock clunks, touchdown thud, layered explosion, and UI clicks for the start screen, pause, restart,
    /// camera/mode switches and the result. Sources are 2D and created at start-up; routed into the
    /// <c>Main.mixer</c> SFX/UI groups (exposed <c>MasterVolume</c> parameter for the settings menu).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public const string MasterVolumeParameter = "MasterVolume";

        [System.Serializable]
        public class Clips
        {
            public AudioClip engineLoop, engineLowLoop;
            public AudioClip explosion1, explosion2, explosionRumble;
            public AudioClip legClunk, legLatch, landingThud;
            public AudioClip uiClick, uiConfirm, uiBack, uiError;
        }

        [SerializeField] LandingSession session;
        [SerializeField] CameraDirector cameras;
        [SerializeField] PlayerControls controls;
        [SerializeField] AudioMixer mixer;
        [SerializeField] AudioMixerGroup sfxGroup;
        [SerializeField] AudioMixerGroup uiGroup;
        [SerializeField] Clips clips = new Clips();
        [SerializeField, Range(0f, 1f)] float engineMax = 0.85f;
        [SerializeField, Range(0f, 1f)] float windMax = 0.45f;

        RocketController rocket;
        AudioSource engineHigh, engineLow, wind, ui;
        readonly AudioSource[] sfx = new AudioSource[4];
        int nextSfx;
        bool ready;
        float engineEnvelope, thrustSmoothed;
        LegsState lastLegs;
        bool touched, lastIntro, finishedSeen;
        float lastViewChange;
        ControlMode lastMode;

        public AudioSource EngineHighSource => engineHigh;
        public AudioSource EngineLowSource => engineLow;
        public AudioSource WindSource => wind;
        public AudioSource UiSource => ui;
        /// <summary>True while any one-shot SFX source is playing (tests).</summary>
        public bool AnySfxPlaying
        {
            get
            {
                foreach (AudioSource src in sfx) if (src != null && src.isPlaying) return true;
                return false;
            }
        }
        /// <summary>Count of one-shot sounds played (tests).</summary>
        public int OneShotCount { get; private set; }
        public AudioMixer Mixer => mixer;

        public void Setup(LandingSession s, CameraDirector c, PlayerControls p, AudioMixer m, AudioMixerGroup sfxOut,
            AudioMixerGroup uiOut, Clips audioClips)
        {
            session = s;
            cameras = c;
            controls = p;
            mixer = m;
            sfxGroup = sfxOut;
            uiGroup = uiOut;
            clips = audioClips;
        }

        /// <summary>Master volume 0..1 (settings menu).</summary>
        public void SetMasterVolume(float linear)
        {
            if (mixer != null) mixer.SetFloat(MasterVolumeParameter, EffectsMath.LinearToDecibels(linear));
        }

        /// <summary>The saved master volume reaches the mixer here (an AudioMixer ignores SetFloat during Awake).</summary>
        void Start() => SettingsApplier.RegisterMixer(mixer);

        /// <summary>Menu sounds (pause menu, results, settings): move, confirm, back, refused.</summary>
        public void PlayMenu(MenuSound s)
        {
            switch (s)
            {
                case MenuSound.Move: PlayUi(clips.uiClick); break;
                case MenuSound.Confirm: PlayUi(clips.uiConfirm); break;
                case MenuSound.Back: PlayUi(clips.uiBack); break;
                default: PlayUi(clips.uiError); break;
            }
        }

        void Awake()
        {
            // The Kenney loops click at the wrap point: play cross-faded copies.
            engineLow = Loop("EngineLow", LoopTools.MakeSeamless(clips.engineLowLoop), sfxGroup);
            engineHigh = Loop("EngineHigh", LoopTools.MakeSeamless(clips.engineLoop), sfxGroup);
            wind = Loop("Wind", WindSynth.CreateClip(), sfxGroup);
            for (int i = 0; i < sfx.Length; i++) sfx[i] = Source("Sfx", sfxGroup);
            ui = Source("Ui", uiGroup);
            ui.ignoreListenerPause = true;
        }

        AudioSource Source(string name, AudioMixerGroup group)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = group;
            return src;
        }

        AudioSource Loop(string name, AudioClip clip, AudioMixerGroup group)
        {
            AudioSource src = Source(name, group);
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            if (clip != null) src.Play();
            return src;
        }

        void OnEnable()
        {
            if (session == null) return;
            rocket = session.Rocket;
            session.Exploded += OnExploded;
            session.Restarted += OnRestarted;
            session.Finished += OnFinished;
            session.PauseChanged += OnPauseChanged;
            lastIntro = session.IntroVisible;
            AudioListener.pause = session.IsPaused;
            if (cameras != null) lastViewChange = cameras.ViewChangedAt;
            if (controls != null) lastMode = controls.Mode;
        }

        void OnDisable()
        {
            if (session == null) return;
            session.Exploded -= OnExploded;
            session.Restarted -= OnRestarted;
            session.Finished -= OnFinished;
            session.PauseChanged -= OnPauseChanged;
            AudioListener.pause = false;
        }

        void Update()
        {
            if (rocket == null) return;
            PollUi();
            ready = true;
            UpdateLoops(Time.unscaledDeltaTime);
            if (session.IsPaused) return;
            UpdateLegsAndTouchdown();
        }

        void UpdateLoops(float dt)
        {
            bool paused = session.IsPaused;
            float thrust = paused ? 0f : EffectsMath.ThrustFraction(rocket.CurrentThrust, rocket.Config.maxThrustN);
            // Fast attack, slower release: no clicks when the engine starts or cuts.
            float target = thrust > 0f ? 1f : 0f;
            float rate = target > engineEnvelope ? 14f : 5f;
            engineEnvelope = Mathf.MoveTowards(engineEnvelope, target, rate * dt);
            if (thrust > 0f) thrustSmoothed = Mathf.MoveTowards(thrustSmoothed, thrust, 3f * dt);

            float vol = engineEnvelope * EffectsMath.EngineVolume(Mathf.Max(0.01f, thrustSmoothed)) * engineMax;
            float high = EffectsMath.EngineHighWeight(thrustSmoothed);
            float pitch = EffectsMath.EnginePitch(thrustSmoothed);
            engineHigh.volume = vol * Mathf.Lerp(0.25f, 1f, high);
            engineLow.volume = vol * Mathf.Lerp(1f, 0.45f, high);
            engineHigh.pitch = pitch;
            engineLow.pitch = pitch * 0.95f;

            float speed = paused || rocket.Exploded ? 0f : rocket.Velocity.magnitude;
            float windTarget = EffectsMath.WindVolume(speed) * windMax;
            wind.volume = Mathf.MoveTowards(wind.volume, windTarget, 1.5f * dt);
            wind.pitch = EffectsMath.WindPitch(speed);
        }

        void UpdateLegsAndTouchdown()
        {
            LegsState legs = rocket.Legs;
            if (legs != lastLegs)
            {
                if (legs == LegsState.Deploying) PlaySfx(clips.legClunk, 0.45f, 0.8f);
                else if (legs == LegsState.Deployed)
                {
                    PlaySfx(clips.legLatch, 0.8f, 0.9f);
                    PlaySfx(clips.legClunk, 0.9f, 0.7f);
                }
                lastLegs = legs;
            }

            ContactSnapshot c = rocket.Contacts;
            bool contact = c.body || (c.legsOnPadMask | c.legsOnGroundMask) != 0;
            if (!touched && contact)
            {
                touched = true;
                if (!rocket.Exploded)
                {
                    float impact = Mathf.Abs(rocket.VelocityBeforeLastStep.y);
                    PlaySfx(clips.landingThud, Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(impact / 4f)), Mathf.Lerp(0.85f, 0.7f, Mathf.Clamp01(impact / 4f)));
                }
            }
        }

        void PollUi()
        {
            bool intro = session.IntroVisible;
            if (lastIntro && !intro && !finishedSeen) PlayUi(clips.uiConfirm);
            lastIntro = intro;

            if (cameras != null && cameras.ViewChangedAt != lastViewChange)
            {
                lastViewChange = cameras.ViewChangedAt;
                PlayUi(clips.uiClick);
            }
            if (controls != null && controls.Mode != lastMode)
            {
                lastMode = controls.Mode;
                PlayUi(clips.uiClick);
            }
        }

        void OnPauseChanged(bool paused)
        {
            // Pauses every SFX source incl. one-shots in flight; the UI source ignores the listener pause.
            AudioListener.pause = paused;
            if (session.IntroVisible || lastIntro) return; // start screen handled in PollUi
            // Pausing clicks; unpausing is silent here: the pause menu plays its own sound for Resume / Restart.
            if (paused) PlayUi(clips.uiClick);
        }

        void OnFinished(LandingEvaluator e)
        {
            finishedSeen = true;
            if (e.State == LandingState.Success) PlayUi(clips.uiConfirm);
            else if (!e.ImpactTooHard) PlayUi(clips.uiError);
        }

        void OnExploded(Vector3 at)
        {
            engineEnvelope = 0f;
            PlaySfx(Random.value < 0.5f ? clips.explosion1 : clips.explosion2, 1f, Random.Range(0.85f, 1f));
            PlaySfx(clips.explosionRumble, 0.9f, 0.8f);
        }

        void OnRestarted()
        {
            foreach (AudioSource src in sfx)
                if (src != null) src.Stop(); // explosion / thud tails end with the attempt
            if (rocket == null) return;
            lastLegs = rocket.Legs;
            touched = false;
            finishedSeen = false;
            engineEnvelope = 0f;
            thrustSmoothed = 0f;
            if (ready && !session.IntroVisible) PlayUi(clips.uiConfirm);
        }

        void PlaySfx(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null) return;
            AudioSource src = sfx[nextSfx];
            if (src == null) return;
            nextSfx = (nextSfx + 1) % sfx.Length; // round robin: each layer keeps its own pitch
            src.pitch = pitch;
            src.PlayOneShot(clip, volume);
            OneShotCount++;
        }

        void PlayUi(AudioClip clip)
        {
            if (clip == null || ui == null) return;
            ui.PlayOneShot(clip, 0.7f);
            OneShotCount++;
        }
    }
}
