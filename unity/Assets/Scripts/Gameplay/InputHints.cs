using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RocketSlam.Gameplay
{
    /// <summary>
    /// Which device the player used last (keyboard or gamepad), so on-screen key hints can show "A / RT" instead of
    /// "SPACE". <see cref="Poll"/> is called by the HUD and the menus every frame (once per frame is enough);
    /// <see cref="Version"/> changes when the device switches.
    /// </summary>
    public static class InputHints
    {
        const float StickThreshold = 0.5f;
        static int polledFrame = -1;

        public static bool UsingGamepad { get; private set; }
        /// <summary>Incremented on every keyboard/gamepad switch.</summary>
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            UsingGamepad = false;
            Version = 0;
            polledFrame = -1;
        }

        /// <summary>Keyboard text or gamepad text (both literals: no allocation).</summary>
        public static string Pick(string keyboard, string gamepad) => UsingGamepad ? gamepad : keyboard;

        /// <summary>The Easy-mode fire control in the current device's terms.</summary>
        public static string Fire => UsingGamepad ? "A / RT" : "SPACE";

        public static void Poll()
        {
            if (polledFrame == Time.frameCount) return;
            polledFrame = Time.frameCount;
            Keyboard k = Keyboard.current;
            Gamepad p = Gamepad.current;
            bool keyboard = k != null && k.anyKey.wasPressedThisFrame;
            bool pad = p != null && PadUsed(p);
            if (pad && !UsingGamepad) Set(true);
            else if (keyboard && !pad && UsingGamepad) Set(false);
        }

        /// <summary>For tests and the start-up default.</summary>
        public static void Set(bool gamepad)
        {
            if (gamepad == UsingGamepad) return;
            UsingGamepad = gamepad;
            Version++;
        }

        static bool PadUsed(Gamepad p)
        {
            return p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame ||
                   p.buttonNorth.wasPressedThisFrame || p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame ||
                   p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame ||
                   p.leftTrigger.wasPressedThisFrame || p.rightTrigger.wasPressedThisFrame ||
                   p.leftStickButton.wasPressedThisFrame || p.rightStickButton.wasPressedThisFrame ||
                   p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame ||
                   p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame ||
                   p.leftStick.ReadValue().sqrMagnitude > StickThreshold * StickThreshold;
        }
    }

    /// <summary>
    /// Swaps the static key-hint labels of one UI document (classes <c>ctl-key</c>, <c>intro-key</c>, <c>intro-tip</c>,
    /// <c>intro-start</c>, <c>help</c>, <c>key-cap</c>, <c>menu-hint</c>) between their keyboard text (as authored in the UXML) and a gamepad text, when
    /// <see cref="InputHints"/> switches device. A label whose gamepad text is empty is hidden.
    /// </summary>
    public class HintLabels
    {
        static readonly string[] Classes = { "ctl-key", "intro-key", "intro-tip", "intro-start", "help", "key-cap", "menu-hint" };

        // Keyboard text -> gamepad text (flight controls).
        static readonly Dictionary<string, string> Flight = new Dictionary<string, string>
        {
            { "HOLD SPACE", "HOLD A / RT" },
            { "Arrows", "Left stick" },
            { "R", "Select" },
            { "C", "R3" },
            { "M", "X" },
            { "Esc", "Start" },
            { "SPACE", "A" },
            { "SHIFT / CTRL", "RT / LT" },
            { "WASD / Arrows", "Left stick" },
            { "G", "Y" },
            { "R / C / Esc", "Select / R3 / Start" },
            { "Wait for FIRE NOW on screen, then hold SPACE.", "Wait for FIRE NOW on screen, then hold A or RT." },
            { "Press any key to start", "Press A to start" },
            { "SPACE engine on/off  ·  WASD / ARROWS steer  ·  SHIFT / CTRL throttle (Z full, X min)  ·  G legs  ·  C camera  ·  R restart  ·  ESC pause  ·  M easy mode",
              "A engine on/off  ·  LEFT STICK steer  ·  RT / LT throttle (RB full, LB min)  ·  Y legs  ·  R3 camera  ·  SELECT restart  ·  START pause  ·  X easy mode" },
        };

        // Menus: Esc means back here, and Next is simply the selected button.
        static readonly Dictionary<string, string> Menu = new Dictionary<string, string>
        {
            { "R", "Select" },
            { "N", "" },
            { "Esc", "B" },
            { "Arrows / mouse  ·  Enter select  ·  Esc back", "D-pad / stick  ·  A select  ·  B back" },
            { "Arrows choose  ·  Enter fly  ·  Esc back", "D-pad choose  ·  A fly  ·  B back" },
            { "Esc resume  ·  R restart", "Start / B resume  ·  Select restart" },
        };

        readonly List<(Label label, string keyboard, string gamepad)> labels = new List<(Label, string, string)>();
        readonly bool menu;
        int appliedVersion = -1;
        VisualElement root;

        /// <param name="menuContext">true for menus (Esc = back = B), false for the flight HUD (Esc = pause = Start).</param>
        public HintLabels(bool menuContext) => menu = menuContext;

        public void Update(VisualElement documentRoot)
        {
            InputHints.Poll();
            if (documentRoot == null) return;
            if (documentRoot != root)
            {
                root = documentRoot;
                labels.Clear();
                appliedVersion = -1;
                Dictionary<string, string> map = menu ? Menu : Flight;
                foreach (string cls in Classes)
                    root.Query<Label>(className: cls).ForEach(l =>
                    {
                        string text = l.text ?? "";
                        labels.Add((l, text, map.TryGetValue(text, out string pad) ? pad : text));
                    });
            }
            if (appliedVersion == InputHints.Version) return;
            appliedVersion = InputHints.Version;
            bool gp = InputHints.UsingGamepad;
            foreach ((Label l, string kb, string pad) in labels)
            {
                string text = gp ? pad : kb;
                l.text = text;
                // Only hide; otherwise leave display to the USS classes (e.g. manual-only help in Easy mode).
                if (text.Length > 0) l.style.display = StyleKeyword.Null;
                else l.style.display = DisplayStyle.None;
            }
        }
    }
}
