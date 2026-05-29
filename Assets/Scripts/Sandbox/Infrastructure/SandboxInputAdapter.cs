using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EvacLogix.Sandbox.Infrastructure
{
    public static class SandboxInputAdapter
    {
        private const float RightMousePanThresholdPixels = 6f;

        private static int rightMouseGestureFrame = -1;
        private static bool rightMousePressed;
        private static bool rightMousePanActive;
        private static bool rightMouseReleasedWithoutPanThisFrame;
        private static Vector2 rightMousePressStartScreenPoint;

        public static Vector2 PointerScreenPosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Pointer.current != null)
                {
                    return Pointer.current.position.ReadValue();
                }

                if (Mouse.current != null)
                {
                    return Mouse.current.position.ReadValue();
                }

                return Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        public static Vector2 MouseScrollDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;
#else
                return Input.mouseScrollDelta;
#endif
            }
        }

        public static bool GetMouseButton(int button)
        {
            return GetRawMouseButton(button);
        }

        public static bool GetMouseButtonDown(int button)
        {
            return GetRawMouseButtonDown(button);
        }

        public static bool GetMouseButtonUp(int button)
        {
            return GetRawMouseButtonUp(button);
        }

        public static bool IsRightMousePanActive
        {
            get
            {
                UpdateRightMouseGestureState();
                return rightMousePanActive;
            }
        }

        public static bool WasRightMouseClickReleasedThisFrame()
        {
            UpdateRightMouseGestureState();
            return rightMouseReleasedWithoutPanThisFrame;
        }

        private static void UpdateRightMouseGestureState()
        {
            if (rightMouseGestureFrame == Time.frameCount)
            {
                return;
            }

            rightMouseGestureFrame = Time.frameCount;
            rightMouseReleasedWithoutPanThisFrame = false;

            var rightMouseDown = GetRawMouseButtonDown(1);
            var rightMouseHeld = GetRawMouseButton(1);
            var rightMouseUp = GetRawMouseButtonUp(1);
            var pointerScreenPosition = PointerScreenPosition;

            if (rightMouseDown)
            {
                rightMousePressed = true;
                rightMousePanActive = false;
                rightMousePressStartScreenPoint = pointerScreenPosition;
            }

            if (rightMousePressed &&
                rightMouseHeld &&
                !rightMousePanActive &&
                (pointerScreenPosition - rightMousePressStartScreenPoint).sqrMagnitude >= RightMousePanThresholdPixels * RightMousePanThresholdPixels)
            {
                rightMousePanActive = true;
            }

            if (rightMouseUp)
            {
                rightMouseReleasedWithoutPanThisFrame = rightMousePressed && !rightMousePanActive;
                rightMousePressed = false;
                rightMousePanActive = false;
                return;
            }

            if (!rightMouseHeld && !rightMouseDown)
            {
                rightMousePressed = false;
                rightMousePanActive = false;
            }
        }

        private static bool GetRawMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            return button switch
            {
                0 => mouse.leftButton.isPressed,
                1 => mouse.rightButton.isPressed,
                2 => mouse.middleButton.isPressed,
                _ => false
            };
#else
            return Input.GetMouseButton(button);
#endif
        }

        private static bool GetRawMouseButtonDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            return button switch
            {
                0 => mouse.leftButton.wasPressedThisFrame,
                1 => mouse.rightButton.wasPressedThisFrame,
                2 => mouse.middleButton.wasPressedThisFrame,
                _ => false
            };
#else
            return Input.GetMouseButtonDown(button);
#endif
        }

        private static bool GetRawMouseButtonUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            return button switch
            {
                0 => mouse.leftButton.wasReleasedThisFrame,
                1 => mouse.rightButton.wasReleasedThisFrame,
                2 => mouse.middleButton.wasReleasedThisFrame,
                _ => false
            };
#else
            return Input.GetMouseButtonUp(button);
#endif
        }

        public static bool GetKey(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && TryMapKeyCode(keyCode, out var key) && keyboard[key].isPressed;
#else
            return Input.GetKey(keyCode);
#endif
        }

        public static bool GetKeyDown(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && TryMapKeyCode(keyCode, out var key) && keyboard[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(keyCode);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool TryMapKeyCode(KeyCode keyCode, out Key key)
        {
            key = keyCode switch
            {
                KeyCode.A => Key.A,
                KeyCode.B => Key.B,
                KeyCode.C => Key.C,
                KeyCode.D => Key.D,
                KeyCode.E => Key.E,
                KeyCode.F => Key.F,
                KeyCode.G => Key.G,
                KeyCode.H => Key.H,
                KeyCode.I => Key.I,
                KeyCode.J => Key.J,
                KeyCode.K => Key.K,
                KeyCode.L => Key.L,
                KeyCode.M => Key.M,
                KeyCode.N => Key.N,
                KeyCode.O => Key.O,
                KeyCode.P => Key.P,
                KeyCode.Q => Key.Q,
                KeyCode.R => Key.R,
                KeyCode.S => Key.S,
                KeyCode.T => Key.T,
                KeyCode.U => Key.U,
                KeyCode.V => Key.V,
                KeyCode.W => Key.W,
                KeyCode.X => Key.X,
                KeyCode.Y => Key.Y,
                KeyCode.Z => Key.Z,
                KeyCode.Alpha0 => Key.Digit0,
                KeyCode.Alpha1 => Key.Digit1,
                KeyCode.Alpha2 => Key.Digit2,
                KeyCode.Alpha3 => Key.Digit3,
                KeyCode.Alpha4 => Key.Digit4,
                KeyCode.Alpha5 => Key.Digit5,
                KeyCode.Alpha6 => Key.Digit6,
                KeyCode.Alpha7 => Key.Digit7,
                KeyCode.Alpha8 => Key.Digit8,
                KeyCode.Alpha9 => Key.Digit9,
                KeyCode.LeftControl => Key.LeftCtrl,
                KeyCode.RightControl => Key.RightCtrl,
                KeyCode.LeftShift => Key.LeftShift,
                KeyCode.RightShift => Key.RightShift,
                KeyCode.LeftAlt => Key.LeftAlt,
                KeyCode.RightAlt => Key.RightAlt,
                KeyCode.LeftCommand => Key.LeftMeta,
                KeyCode.RightCommand => Key.RightMeta,
                KeyCode.Home => Key.Home,
                KeyCode.Backspace => Key.Backspace,
                KeyCode.Delete => Key.Delete,
                KeyCode.Escape => Key.Escape,
                _ => Key.None
            };

            return key != Key.None;
        }
#endif
    }
}
