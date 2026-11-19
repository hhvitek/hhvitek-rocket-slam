using System;
using System.Collections.Generic;
using RocketSlam.Gameplay;
using RocketSlam.Progress;
using RocketSlam.Sound;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Menus
{
    /// <summary>
    /// The settings screen (<c>Assets/UI/Settings.uxml</c>), shared by the main menu and the pause menu: master volume,
    /// graphics quality (Low / Medium / High), Easy assist on/off (the default control mode) and the default camera.
    /// Every change applies at once; the save file is written when the screen closes.
    /// </summary>
    public class SettingsPanel
    {
        static readonly string[] QualityLabels = { "LOW", "MEDIUM", "HIGH" };
        static readonly string[] CameraLabels = { "CHASE", "ENGINE", "PAD-SIDE" };

        readonly VisualElement root;
        readonly Slider volume;
        readonly Label volumeValue;
        readonly List<VisualElement> qualityChips = new List<VisualElement>();
        readonly List<VisualElement> assistChips = new List<VisualElement>();
        readonly List<VisualElement> cameraChips = new List<VisualElement>();
        readonly Label hint;
        int shownSelection = -2;

        public MenuList List { get; }
        public VisualElement Root => root;
        public Slider VolumeSlider => volume;
        /// <summary>Raised by Back / Esc after the settings were saved.</summary>
        public event Action Closed;

        static GameSettings S => SaveGame.Settings;

        public SettingsPanel(VisualElement panelRoot, Action<MenuSound> sound)
        {
            root = panelRoot;
            root.style.display = DisplayStyle.None;
            List = new MenuList(horizontal: false, sound) { onCancel = Close };
            volume = root.Q<Slider>("volume-slider");
            volumeValue = root.Q<Label>("volume-value");
            hint = root.Q<Label>("settings-hint");
            volume.focusable = false;
            foreach (VisualElement child in volume.Query<VisualElement>().ToList()) child.focusable = false;
            volume.RegisterValueChangedCallback(evt => SetVolume(evt.newValue / 100f, fromSlider: true));

            List.Add(root.Q("row-volume"), null, d => SetVolume(Mathf.Round(S.masterVolume * 20f + d) / 20f, fromSlider: false));
            List.Add(root.Q("row-quality"), () => { SetQuality((S.quality + 1) % 3); return true; }, d => SetQuality(Mathf.Clamp(S.quality + d, 0, 2)));
            List.Add(root.Q("row-assist"), () => { SetAssist(!S.easyAssist); return true; }, d => SetAssist(d < 0));
            List.Add(root.Q("row-camera"), () => { SetCamera((S.cameraView + 1) % 3); return true; }, d => SetCamera((S.cameraView + d + 3) % 3));
            List.Add(root.Q("settings-back"), () => { Close(); return true; });

            Chips(root.Q("quality-chips"), QualityLabels, qualityChips, i => SetQuality(i));
            Chips(root.Q("assist-chips"), new[] { "ON", "OFF" }, assistChips, i => SetAssist(i == 0));
            Chips(root.Q("camera-chips"), CameraLabels, cameraChips, i => SetCamera(i));
            Refresh();
        }

        void Chips(VisualElement parent, string[] labels, List<VisualElement> into, Action<int> pick)
        {
            parent.Clear();
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var chip = new Label(labels[i]);
                chip.AddToClassList("chip");
                chip.RegisterCallback<ClickEvent>(evt =>
                {
                    pick(index);
                    evt.StopPropagation();
                });
                parent.Add(chip);
                into.Add(chip);
            }
        }

        public bool Visible { get; private set; }

        public void Open()
        {
            Visible = true;
            root.style.display = DisplayStyle.Flex;
            List.Select(0, playSound: false);
            List.Refresh();
            Refresh();
        }

        public void Close()
        {
            if (!Visible) return;
            Visible = false;
            SaveGame.CommitSettings();
            root.style.display = DisplayStyle.None;
            Closed?.Invoke();
        }

        public void SetVolume(float linear, bool fromSlider)
        {
            S.masterVolume = Mathf.Clamp01(linear);
            SettingsApplier.ApplyVolume(S.masterVolume);
            if (!fromSlider) volume.SetValueWithoutNotify(S.masterVolume * 100f);
            Refresh();
        }

        public void SetQuality(int q)
        {
            S.quality = Mathf.Clamp(q, 0, 2);
            SettingsApplier.ApplyQuality((QualityPreset)S.quality);
            Refresh();
        }

        public void SetAssist(bool easy)
        {
            S.easyAssist = easy;
            Refresh();
        }

        public void SetCamera(int view)
        {
            S.cameraView = Mathf.Clamp(view, 0, 2);
            Refresh();
        }

        void Refresh()
        {
            volume.SetValueWithoutNotify(S.masterVolume * 100f);
            volumeValue.text = $"{S.masterVolume * 100f:0} %";
            Mark(qualityChips, S.quality);
            Mark(assistChips, S.easyAssist ? 0 : 1);
            Mark(cameraChips, S.cameraView);
            shownSelection = List.Selected;
            if (hint != null)
            {
                hint.text = List.Selected switch
                {
                    1 => "Low: lower resolution, simple shadows (fastest).  High: sharper shadows and anti-aliasing.",
                    2 => InputHints.Pick("ON: hold SPACE and the rocket steers itself (Easy mode).  OFF: start levels in Manual mode.  M still switches in flight.",
                                         "ON: hold A / RT and the rocket steers itself (Easy mode).  OFF: start levels in Manual mode.  X still switches in flight."),
                    3 => InputHints.Pick("The view each level starts with.  C still cycles views in flight.",
                                         "The view each level starts with.  R3 still cycles views in flight."),
                    _ => InputHints.Pick("Arrows / mouse to change  ·  Esc or BACK to return", "D-pad / stick to change  ·  B or BACK to return"),
                };
            }
        }

        /// <summary>Call every frame while open (hint text follows the selection).</summary>
        public void Tick(MenuCommand cmd)
        {
            List.Handle(cmd);
            if (cmd.Any || List.Selected != shownSelection) Refresh();
        }

        static void Mark(List<VisualElement> chips, int active)
        {
            for (int i = 0; i < chips.Count; i++) chips[i].EnableInClassList("chip-on", i == active);
        }
    }
}
