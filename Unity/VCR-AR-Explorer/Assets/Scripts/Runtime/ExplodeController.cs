using UnityEngine;

namespace VCR
{
    /// <summary>
    /// Exploded view: moves every component along its offset from the database. Refrigerant
    /// lines stay in place and are hidden once the parts separate (they would connect nothing).
    /// </summary>
    public class ExplodeController : MonoBehaviour
    {
        public VCRModelBinder binder;
        [Range(0, 1)] public float amount;
        public float animateSpeed = 3f;

        private float _goal;
        private static float _current;

        public float Current => _current;

        public static bool LineVisible(VCRSelectable s) => s.Record == null || !s.Record.line || _current < 0.25f;

        private void Start()
        {
            _goal = _current = amount;
            ApplyNow();
        }

        /// <summary>Set target explode amount (0..1). Animates unless instant.</summary>
        public void SetAmount(float t, bool instant = false)
        {
            _goal = Mathf.Clamp01(t);
            if (instant)
            {
                _current = _goal;
                ApplyNow();
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(_current, _goal)) return;
            _current = Mathf.MoveTowards(_current, _goal, animateSpeed * Time.deltaTime);
            ApplyNow();
        }

        private void ApplyNow()
        {
            amount = _current;
            if (binder == null) return;
            foreach (var p in binder.Parts.Values) p.SetExplode(_current);
            if (SelectionManager.Instance != null) SelectionManager.Instance.Apply();
        }
    }
}
