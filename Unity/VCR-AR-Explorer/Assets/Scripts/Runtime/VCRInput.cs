// Thin input abstraction so the rest of the code works with either the Input System package
// (ProjectSettings > Active Input Handling = Input System) or the legacy Input Manager.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace VCR
{
    public struct VCRTouch
    {
        public int id;
        public Vector2 position;
        public Vector2 delta;
        public bool began;
        public bool ended;
    }

    public static class VCRInput
    {
        private static readonly List<VCRTouch> _touches = new List<VCRTouch>();
        private static int _frame = -1;
        private static readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        private static void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _touches.Clear();
#if ENABLE_INPUT_SYSTEM
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
            foreach (var t in ETouch.activeTouches)
            {
                _touches.Add(new VCRTouch
                {
                    id = t.touchId,
                    position = t.screenPosition,
                    delta = t.delta,
                    began = t.phase == UnityEngine.InputSystem.TouchPhase.Began,
                    ended = t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled,
                });
            }
#else
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                _touches.Add(new VCRTouch
                {
                    id = t.fingerId,
                    position = t.position,
                    delta = t.deltaPosition,
                    began = t.phase == TouchPhase.Began,
                    ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled,
                });
            }
#endif
        }

        public static IReadOnlyList<VCRTouch> Touches
        {
            get { Refresh(); return _touches; }
        }

        /// <summary>True when the mouse should be read (no active touches — avoids touch→mouse double events).</summary>
        public static bool UseMouse
        {
            get
            {
                Refresh();
#if ENABLE_INPUT_SYSTEM
                return _touches.Count == 0 && Mouse.current != null;
#else
                return _touches.Count == 0 && Input.mousePresent;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool LeftUp => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static bool PanHeld => Mouse.current != null && (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed);
        public static bool PanDown => Mouse.current != null && (Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame);
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool KeyDown(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        public static bool EscapePressed => KeyDown(Key.Escape);
        public static bool NextPressed => KeyDown(Key.RightArrow);
        public static bool PrevPressed => KeyDown(Key.LeftArrow);
        public static bool ResetPressed => KeyDown(Key.R);
#else
        public static Vector2 MousePosition => Input.mousePosition;
        public static Vector2 MouseDelta => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
        public static bool LeftDown => Input.GetMouseButtonDown(0);
        public static bool LeftHeld => Input.GetMouseButton(0);
        public static bool LeftUp => Input.GetMouseButtonUp(0);
        public static bool PanHeld => Input.GetMouseButton(1) || Input.GetMouseButton(2);
        public static bool PanDown => Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool EscapePressed => Input.GetKeyDown(KeyCode.Escape);
        public static bool NextPressed => Input.GetKeyDown(KeyCode.RightArrow);
        public static bool PrevPressed => Input.GetKeyDown(KeyCode.LeftArrow);
        public static bool ResetPressed => Input.GetKeyDown(KeyCode.R);
#endif

        /// <summary>
        /// UI hit test that works with every input module: raycasts all UI at the screen position,
        /// so clicks on buttons/panels never select 3D objects behind them.
        /// </summary>
        public static bool IsOverUI(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = screenPos };
            _uiHits.Clear();
            es.RaycastAll(data, _uiHits);
            return _uiHits.Count > 0;
        }
    }
}
