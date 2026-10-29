using System.Collections.Generic;
using UnityEngine;

namespace VCR
{
    /// <summary>
    /// Conceptual refrigerant-flow animation: small arrow markers travel along the route in
    /// flow_paths.json, coloured by refrigerant state (red = high-pressure / hot, blue = low).
    /// This is NOT a CFD or thermodynamic simulation.
    /// </summary>
    public class FlowAnimator : MonoBehaviour
    {
        public VCRDatabase database;
        [Tooltip("Model-space parent (the glTF scene root) so arrows follow AR placement/scaling.")] public Transform modelSpace;
        public ExplodeController explode;
        public Material arrowMaterial;
        public float spacing = 0.05f;
        public float speed = 0.12f;
        public float arrowSize = 0.026f;
        public bool running = true;

        private readonly List<Vector3> _pts = new List<Vector3>();
        private readonly List<int> _seg = new List<int>();
        private readonly List<float> _dist = new List<float>();
        private readonly List<Transform> _arrows = new List<Transform>();
        private readonly List<Renderer> _arrowRenderers = new List<Renderer>();
        private VCRFlowSegment[] _segments;
        private float _length, _offset;
        private string _focus;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color_ = Shader.PropertyToID("_Color");

        private void Start()
        {
            _mpb = new MaterialPropertyBlock();
            _segments = database != null && database.Flow != null ? database.Flow.segments : null;
            if (_segments == null || _segments.Length == 0)
            {
                Debug.LogWarning("[VCR] No flow path data; flow animation disabled.");
                enabled = false;
                return;
            }
            for (int s = 0; s < _segments.Length; s++)
                foreach (var p in _segments[s].Points())
                {
                    if (_pts.Count > 0) _length += Vector3.Distance(_pts[_pts.Count - 1], p);
                    _pts.Add(p);
                    _seg.Add(s);
                    _dist.Add(_length);
                }
            var mesh = BuildArrowMesh();
            int count = Mathf.Max(1, Mathf.FloorToInt(_length / spacing));
            var holder = new GameObject("FlowArrows").transform;
            holder.SetParent(modelSpace != null ? modelSpace : transform, false);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Arrow" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = arrowMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.SetParent(holder, false);
                go.transform.localScale = Vector3.one * arrowSize;
                _arrows.Add(go.transform);
                _arrowRenderers.Add(r);
            }
        }

        public void SetFocus(string componentId) => _focus = componentId;

        public void SetRunning(bool on)
        {
            running = on;
            FanSpinner.Running = on;
        }

        private void Update()
        {
            bool show = running && (explode == null || explode.Current < 0.05f);
            for (int i = 0; i < _arrows.Count; i++) _arrowRenderers[i].enabled = show;
            if (!show) return;
            _offset += Time.deltaTime * speed;
            float step = _length / _arrows.Count;
            for (int i = 0; i < _arrows.Count; i++)
            {
                Sample(i * step + _offset, out var pos, out var dir, out int seg);
                var t = _arrows[i];
                t.localPosition = pos;
                if (dir.sqrMagnitude > 1e-8f) t.localRotation = Quaternion.LookRotation(dir);
                bool focus = _focus == null || _segments[seg].component == _focus;
                t.localScale = Vector3.one * arrowSize * (focus ? (_focus != null ? 1.35f : 1f) : 0.6f);
                var c = VCRColors.ForState(_segments[seg].state);
                if (!focus) c = Color.Lerp(c, new Color(0.6f, 0.64f, 0.7f), 0.7f);
                _mpb.SetColor(BaseColor, c);
                _mpb.SetColor(Color_, c);
                _arrowRenderers[i].SetPropertyBlock(_mpb);
            }
        }

        private void Sample(float d, out Vector3 pos, out Vector3 dir, out int seg)
        {
            d = Mathf.Repeat(d, _length);
            int lo = 0, hi = _pts.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_dist[mid] <= d) lo = mid; else hi = mid;
            }
            float span = _dist[hi] - _dist[lo];
            float f = span > 1e-6f ? (d - _dist[lo]) / span : 0f;
            pos = Vector3.Lerp(_pts[lo], _pts[hi], f);
            dir = _pts[hi] - _pts[lo];
            seg = _seg[lo];
        }

        /// <summary>Unit cone pointing along +Z (base radius 0.35, length 1).</summary>
        private static Mesh BuildArrowMesh()
        {
            const int n = 10;
            var v = new List<Vector3> { new Vector3(0, 0, 0.5f), new Vector3(0, 0, -0.5f) };
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v.Add(new Vector3(Mathf.Cos(a) * 0.35f, Mathf.Sin(a) * 0.35f, -0.5f));
            }
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = 2 + i, b = 2 + (i + 1) % n;
                tris.AddRange(new[] { 0, b, a, 1, a, b });
            }
            var m = new Mesh { name = "FlowArrow" };
            m.SetVertices(v);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            return m;
        }
    }
}
