using UnityEngine;

namespace VCR
{
    /// <summary>
    /// Added to each top-level component node of the imported model (C01_Compressor, ...).
    /// Owns that component's highlight, ghost, visibility and explode offset.
    /// </summary>
    [DisallowMultipleComponent]
    public class VCRSelectable : MonoBehaviour
    {
        public string componentId;
        [HideInInspector] public Vector3 basePosition;
        [HideInInspector] public bool basePositionSet;

        public VCRComponentRecord Record { get; set; }

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor");
        private Renderer[] _renderers;
        private Material[][] _original;
        private Collider[] _colliders;
        private MaterialPropertyBlock _mpb;
        private bool _ghost;

        public Renderer[] Renderers => _renderers;

        public void Init()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _original = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _original[i] = _renderers[i].sharedMaterials;
            _mpb = new MaterialPropertyBlock();
            if (!basePositionSet)
            {
                basePosition = transform.localPosition;
                basePositionSet = true;
            }
        }

        /// <summary>World-space bounds of all renderers (used for camera focus and label anchors).</summary>
        public Bounds WorldBounds()
        {
            if (_renderers == null) Init();
            var b = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in _renderers)
            {
                if (!r.enabled) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        public void SetHighlight(Color emission)
        {
            if (_renderers == null) Init();
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, emission);
                _mpb.SetColor(EmissiveFactor, emission);
                r.SetPropertyBlock(_mpb);
            }
        }

        public void SetGhost(bool ghost, Material ghostMaterial)
        {
            if (_renderers == null) Init();
            if (ghost == _ghost || ghostMaterial == null) return;
            _ghost = ghost;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (ghost)
                {
                    var mats = new Material[_original[i].Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = ghostMaterial;
                    _renderers[i].sharedMaterials = mats;
                }
                else _renderers[i].sharedMaterials = _original[i];
            }
        }

        public void SetVisible(bool visible)
        {
            if (_renderers == null) Init();
            foreach (var r in _renderers) r.enabled = visible;
            foreach (var c in _colliders) c.enabled = visible;
        }

        public void SetExplode(float t)
        {
            if (Record == null) return;
            transform.localPosition = basePosition + Record.ExplodeOffset * t;
        }
    }
}
