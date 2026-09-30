using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// Tiny keyboard/mouse wrapper used only for testing without a camera.
    /// Works with both the old Input Manager and the new Input System package.
    /// </summary>
    public static class DevInput
    {
        public static Vector2 MouseViewport
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current == null) return new Vector2(0.5f, 0.5f);
                Vector2 p = Mouse.current.position.ReadValue();
#else
                Vector2 p = Input.mousePosition;
#endif
                return new Vector2(p.x / Mathf.Max(1, Screen.width), p.y / Mathf.Max(1, Screen.height));
            }
        }

        public static bool LeftMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        public static bool RightMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        /// <summary>WASD / arrow keys as a (x = strafe/turn, y = forward) vector.</summary>
        public static Vector2 Move
        {
            get
            {
                Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current;
                if (k == null) return v;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1;
#else
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1;
#endif
                return v;
            }
        }

        public static bool ToggleDebugPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.F1);
#endif
            }
        }
    }
}
