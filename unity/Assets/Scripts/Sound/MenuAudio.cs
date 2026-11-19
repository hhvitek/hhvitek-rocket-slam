using RocketSlam.Progress;
using UnityEngine;
using UnityEngine.Audio;

namespace RocketSlam.Sound
{
    public enum MenuSound { Move, Confirm, Back, Error }

    /// <summary>UI sounds of the main menu scene (the levels use <see cref="GameAudio.PlayMenu"/>), routed into the
    /// <c>Main.mixer</c> UI group; also registers the mixer so the saved master volume applies.</summary>
    public class MenuAudio : MonoBehaviour
    {
        [SerializeField] AudioMixer mixer;
        [SerializeField] AudioMixerGroup uiGroup;
        [SerializeField] AudioClip move, confirm, back, error;

        AudioSource source;

        public AudioMixer Mixer => mixer;

        public void Setup(AudioMixer m, AudioMixerGroup group, AudioClip moveClip, AudioClip confirmClip, AudioClip backClip, AudioClip errorClip)
        {
            mixer = m;
            uiGroup = group;
            move = moveClip;
            confirm = confirmClip;
            back = backClip;
            error = errorClip;
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.outputAudioMixerGroup = uiGroup;
        }

        void Start() => SettingsApplier.RegisterMixer(mixer);

        public void Play(MenuSound s)
        {
            AudioClip clip = s switch
            {
                MenuSound.Move => move,
                MenuSound.Confirm => confirm,
                MenuSound.Back => back,
                _ => error,
            };
            if (clip != null && source != null) source.PlayOneShot(clip, 0.7f);
        }
    }
}
