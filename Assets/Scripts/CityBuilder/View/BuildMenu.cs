using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.View
{
    public enum BuildCategory
    {
        None,
        Roads,
        Buildings,
        Terrain,
        Demolish,
    }

    public class BuildMenu : MonoBehaviour
    {
        BuildCategory _active;
        bool _open;

        /// <summary>
        /// Tools set this true during frames where they consumed Escape themselves
        /// (cancelling a drag or confirm). BuildMenu checks it before navigating back.
        /// Reset each frame in LateUpdate.
        /// </summary>
        public bool EscapeConsumed { get; set; }

        public BuildCategory Active => _active;
        public bool IsOpen => _open;

        public event Action<BuildCategory> Changed;

        void LateUpdate()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !EscapeConsumed)
            {
                if (_active != BuildCategory.None)
                    Select(BuildCategory.None);
                else if (_open)
                    Close();
            }

            EscapeConsumed = false;
        }

        public void Toggle()
        {
            _open = !_open;
            if (!_open) Select(BuildCategory.None);
        }

        public void Select(BuildCategory category)
        {
            bool changed = category != _active;
            _active = category;
            _open = category != BuildCategory.None || _open;
            if (changed) Changed?.Invoke(_active);
        }

        public void Close()
        {
            _open = false;
            if (_active != BuildCategory.None)
            {
                _active = BuildCategory.None;
                Changed?.Invoke(_active);
            }
        }
    }
}
