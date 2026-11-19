using System;
using System.Collections.Generic;
using RocketSlam.Sound;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Menus
{
    /// <summary>One selectable entry of a <see cref="MenuList"/>.</summary>
    public class MenuEntry
    {
        public VisualElement element;
        /// <summary>Enter / click. Returns false when refused (locked level): error sound, nothing happens.</summary>
        public Func<bool> activate;
        /// <summary>Optional left/right (-1 / +1) on a vertical list (settings values).</summary>
        public Action<int> adjust;

        /// <summary>A hidden entry (display: none, e.g. Next on the last level) cannot be selected.</summary>
        public bool Selectable => element.resolvedStyle.display != DisplayStyle.None && element.style.display != DisplayStyle.None;
    }

    /// <summary>
    /// Keyboard / gamepad / mouse navigation of one menu screen without UI Toolkit focus (deterministic, and the same
    /// code path for real devices and test devices): the selected item gets the <c>selected</c> USS class; arrows move
    /// (vertical lists: up/down, rows: left/right; wraps), Enter activates, left/right on a vertical list adjusts a value.
    /// The mouse selects on hover and activates on click.
    /// </summary>
    public class MenuList
    {
        readonly List<MenuEntry> items = new List<MenuEntry>();
        readonly bool horizontal;
        readonly Action<MenuSound> sound;

        public int Selected { get; private set; } = -1;
        public int Count => items.Count;
        public MenuEntry SelectedItem => Selected >= 0 && Selected < items.Count ? items[Selected] : null;
        /// <summary>Esc / gamepad B on this screen.</summary>
        public Action onCancel;

        public MenuList(bool horizontal, Action<MenuSound> sound)
        {
            this.horizontal = horizontal;
            this.sound = sound;
        }

        public MenuEntry Add(VisualElement element, Func<bool> activate, Action<int> adjust = null)
        {
            var item = new MenuEntry { element = element, activate = activate, adjust = adjust };
            int index = items.Count;
            items.Add(item);
            element.AddToClassList("menu-item");
            element.focusable = false;
            element.RegisterCallback<PointerEnterEvent>(_ => Select(index, playSound: Selected != index));
            element.RegisterCallback<ClickEvent>(evt =>
            {
                Select(index, playSound: false);
                Activate();
                evt.StopPropagation();
            });
            if (Selected < 0) Select(0, playSound: false);
            return item;
        }

        public void Select(int index, bool playSound = true)
        {
            if (items.Count == 0) return;
            index = (index % items.Count + items.Count) % items.Count;
            if (!items[index].Selectable) return;
            if (index == Selected) return;
            if (SelectedItem != null) SelectedItem.element.RemoveFromClassList("selected");
            Selected = index;
            items[index].element.AddToClassList("selected");
            if (playSound) sound?.Invoke(MenuSound.Move);
        }

        public void Select(VisualElement element, bool playSound = false)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].element == element) { Select(i, playSound); return; }
        }

        /// <summary>Re-apply the selected class (after the screen was rebuilt or shown again).</summary>
        public void Refresh()
        {
            for (int i = 0; i < items.Count; i++) items[i].element.EnableInClassList("selected", i == Selected);
        }

        /// <summary>Move by one entry in <paramref name="direction"/>, skipping hidden entries (wraps).</summary>
        public void Step(int direction)
        {
            int n = items.Count;
            for (int i = 1; i <= n; i++)
            {
                int index = ((Selected + direction * i) % n + n) % n;
                if (!items[index].Selectable) continue;
                Select(index);
                return;
            }
        }

        public bool Activate()
        {
            MenuEntry item = SelectedItem;
            if (item?.activate == null || !item.Selectable) return false;
            bool ok = item.activate();
            sound?.Invoke(ok ? MenuSound.Confirm : MenuSound.Error);
            return ok;
        }

        public void Handle(MenuCommand cmd)
        {
            if (cmd.cancel)
            {
                if (onCancel != null)
                {
                    sound?.Invoke(MenuSound.Back);
                    onCancel();
                }
                return;
            }
            if (cmd.submit)
            {
                Activate();
                return;
            }
            Vector2Int m = cmd.move;
            if (m == Vector2Int.zero) return;
            int step = horizontal ? m.x : -m.y;
            if (step != 0)
            {
                Step(step);
                return;
            }
            int side = horizontal ? 0 : m.x;
            if (side != 0 && SelectedItem?.adjust != null)
            {
                SelectedItem.adjust(side);
                sound?.Invoke(MenuSound.Move);
            }
        }
    }
}
