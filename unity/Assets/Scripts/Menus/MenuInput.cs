using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketSlam.Menus
{
    /// <summary>One frame of menu input.</summary>
    public struct MenuCommand
    {
        /// <summary>-1 / 0 / +1 (x = left/right, y = up/down with +1 = up).</summary>
        public Vector2Int move;
        public bool submit;
        public bool cancel;
        /// <summary>Gamepad Start this frame (also counted in <see cref="submit"/>; the pause menu uses it to resume).</summary>
        public bool start;

        public bool Any => move != Vector2Int.zero || submit || cancel;
    }

    /// <summary>
    /// Menu input straight from the Input System devices (works without an EventSystem and with test devices):
    /// arrows / WASD, Enter, Esc / Backspace on the keyboard; d-pad / left stick, South (A) = confirm,
    /// East (B) = back, Start = confirm on the gamepad (the pause menu treats Start as Resume). Held directions repeat (0.4 s, then every 0.09 s). Uses unscaled
    /// time so it works while the game is paused.
    /// </summary>
    public class MenuInput
    {
        const float RepeatDelay = 0.4f;
        const float RepeatRate = 0.09f;
        const float StickThreshold = 0.6f;

        Vector2Int held;
        float nextRepeat;
        bool resync;

        public MenuCommand Read()
        {
            var cmd = new MenuCommand();
            Vector2Int dir = Vector2Int.zero;   // held now
            Vector2Int press = Vector2Int.zero; // pressed this frame (catches a press and release within one frame)
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                Dir(ref dir, ref press, k.upArrowKey, k.wKey, new Vector2Int(0, 1));
                Dir(ref dir, ref press, k.downArrowKey, k.sKey, new Vector2Int(0, -1));
                Dir(ref dir, ref press, k.rightArrowKey, k.dKey, new Vector2Int(1, 0));
                Dir(ref dir, ref press, k.leftArrowKey, k.aKey, new Vector2Int(-1, 0));
                cmd.submit |= k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
                cmd.cancel |= k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame;
            }
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                // d-pad presses count even when pressed and released within one frame (like keys)
                if (pad.dpad.up.wasPressedThisFrame) press.y += 1;
                if (pad.dpad.down.wasPressedThisFrame) press.y -= 1;
                if (pad.dpad.right.wasPressedThisFrame) press.x += 1;
                if (pad.dpad.left.wasPressedThisFrame) press.x -= 1;
                Vector2 v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                if (v.y > StickThreshold) dir.y += 1;
                if (v.y < -StickThreshold) dir.y -= 1;
                if (v.x > StickThreshold) dir.x += 1;
                if (v.x < -StickThreshold) dir.x -= 1;
                cmd.start = pad.startButton.wasPressedThisFrame;
                cmd.submit |= pad.buttonSouth.wasPressedThisFrame || cmd.start;
                cmd.cancel |= pad.buttonEast.wasPressedThisFrame;
            }
            dir.x = Mathf.Clamp(dir.x, -1, 1);
            dir.y = Mathf.Clamp(dir.y, -1, 1);
            // vertical wins over horizontal on diagonals (lists are vertical)
            if (dir.y != 0) dir.x = 0;
            if (press.y != 0) press.x = 0;

            float now = Time.unscaledTime;
            if (resync)
            {
                resync = false;
                held = dir;
                nextRepeat = now + RepeatDelay;
                return cmd;
            }
            if (press != Vector2Int.zero)
            {
                held = dir;
                nextRepeat = now + RepeatDelay;
                cmd.move = new Vector2Int(Mathf.Clamp(press.x, -1, 1), Mathf.Clamp(press.y, -1, 1));
            }
            else if (dir != held)
            {
                held = dir;
                nextRepeat = now + RepeatDelay;
                cmd.move = dir;
            }
            else if (dir != Vector2Int.zero && now >= nextRepeat)
            {
                nextRepeat = now + RepeatRate;
                cmd.move = dir;
            }
            return cmd;
        }

        static void Dir(ref Vector2Int dir, ref Vector2Int press, UnityEngine.InputSystem.Controls.KeyControl a,
            UnityEngine.InputSystem.Controls.KeyControl b, Vector2Int d)
        {
            if (a.isPressed || b.isPressed) dir += d;
            if (a.wasPressedThisFrame || b.wasPressedThisFrame) press += d;
        }

        /// <summary>Ignore what is held right now (after a screen change, so a held key does not act on the new screen).</summary>
        public void Reset() => resync = true;
    }
}
