using System;
using UnityEngine;

namespace VCR
{
    public enum VisibilityMode { ShowAll, GhostOthers, IsolateSelected }

    /// <summary>
    /// Tap / click selection. A press counts as a tap only if it did not start over UI, moved less
    /// than a few pixels and was released quickly, so orbiting never selects by accident and UI
    /// clicks never select the object behind them. Owns highlight + ghost / isolate logic.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        public VCRModelBinder binder;
        public Camera cam;
        public Color highlightColor = new Color(0.08f, 0.57f, 0.69f) * 1.2f;
        public Color hoverColor = new Color(0.72f, 0.41f, 0.18f) * 0.6f;
        public float tapSlopPixels = 12f;
        public float tapMaxSeconds = 0.6f;
        public bool inputEnabled = true;

        public string Selected { get; private set; }
        public VisibilityMode Mode { get; private set; } = VisibilityMode.ShowAll;
        public event Action<string> SelectionChanged;

        private Vector2 _downPos;
        private float _downTime;
        private bool _pressValid;
        private string _hovered;

        public static SelectionManager Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            if (cam == null) cam = Camera.main;
        }

        private void Update()
        {
            if (!inputEnabled || binder == null || !binder.IsBound) return;
            var touches = VCRInput.Touches;
            if (touches.Count == 1)
            {
                var t = touches[0];
                if (t.began) Press(t.position);
                if (t.ended) Release(t.position);
            }
            else if (touches.Count > 1) _pressValid = false; // pinch is never a tap
            else if (VCRInput.UseMouse)
            {
                var mp = VCRInput.MousePosition;
                if (VCRInput.LeftDown) Press(mp);
                if (VCRInput.LeftUp) Release(mp);
                if (!VCRInput.LeftHeld) SetHover(VCRInput.IsOverUI(mp) ? null : Pick(mp));
            }
            if (VCRInput.EscapePressed) Select(null);
        }

        private void Press(Vector2 pos)
        {
            _downPos = pos;
            _downTime = Time.unscaledTime;
            _pressValid = !VCRInput.IsOverUI(pos);
        }

        private void Release(Vector2 pos)
        {
            if (!_pressValid) return;
            _pressValid = false;
            if ((pos - _downPos).magnitude > tapSlopPixels || Time.unscaledTime - _downTime > tapMaxSeconds) return;
            Select(Pick(pos));
        }

        public string Pick(Vector2 screenPos)
        {
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 50f);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var sel = h.collider.GetComponentInParent<VCRSelectable>();
                if (sel == null) continue;
                // ghosted components can be clicked "through"
                if (Mode == VisibilityMode.GhostOthers && Selected != null && sel.componentId != Selected) continue;
                return sel.componentId;
            }
            return null;
        }

        public void Select(string id)
        {
            if (id != null && !binder.Parts.ContainsKey(id)) id = null;
            if (id == Selected) return;
            Selected = id;
            Apply();
            SelectionChanged?.Invoke(id);
        }

        public void SetMode(VisibilityMode mode)
        {
            Mode = mode;
            Apply();
        }

        private void SetHover(string id)
        {
            if (id == _hovered) return;
            _hovered = id;
            Apply();
        }

        public void Apply()
        {
            foreach (var kv in binder.Parts)
            {
                bool sel = kv.Key == Selected;
                bool other = Selected != null && !sel;
                kv.Value.SetVisible(!(other && Mode == VisibilityMode.IsolateSelected) && ExplodeController.LineVisible(kv.Value));
                kv.Value.SetGhost(other && Mode == VisibilityMode.GhostOthers, binder.ghostMaterial);
                kv.Value.SetHighlight(sel ? highlightColor : kv.Key == _hovered ? hoverColor : Color.black);
            }
        }

        private void LateUpdate()
        {
            if (Selected == null || !binder.Parts.TryGetValue(Selected, out var p)) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            p.SetHighlight(highlightColor * pulse);
        }
    }
}
