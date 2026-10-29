using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR
{
    /// <summary>
    /// Finds every component node in the imported VCR_System model by the node names listed in
    /// the database (no hard-coded scene references), adds colliders + VCRSelectable to each and
    /// reports anything that is missing. Runs in the Editor setup and again at runtime (idempotent).
    /// </summary>
    public class VCRModelBinder : MonoBehaviour
    {
        public VCRDatabase database;
        [Tooltip("Root of the instantiated model. Defaults to this transform.")] public Transform modelRoot;
        public Material ghostMaterial;

        public readonly Dictionary<string, VCRSelectable> Parts = new Dictionary<string, VCRSelectable>();
        public readonly List<string> MissingNodes = new List<string>();
        public bool IsBound { get; private set; }
        public event Action Bound;

        private void Awake() => Bind();

        public void Bind()
        {
            Parts.Clear();
            MissingNodes.Clear();
            if (database == null || database.Data == null)
            {
                Debug.LogError("[VCR] No component database assigned to VCRModelBinder.");
                return;
            }
            var root = modelRoot != null ? modelRoot : transform;
            foreach (var rec in database.Data.components)
            {
                var node = FindDeep(root, rec.node);
                if (node == null)
                {
                    MissingNodes.Add(rec.node);
                    continue;
                }
                var sel = node.GetComponent<VCRSelectable>();
                if (sel == null) sel = node.gameObject.AddComponent<VCRSelectable>(); // no ?? : Unity fake-null
                sel.componentId = rec.id;
                sel.Record = rec;
                foreach (var mf in node.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.GetComponent<Collider>() == null && mf.sharedMesh != null)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                foreach (Transform t in node.GetComponentsInChildren<Transform>(true))
                    if (t.name.EndsWith("_Rotor") && t.GetComponent<FanSpinner>() == null) t.gameObject.AddComponent<FanSpinner>();
                sel.Init();
                Parts[rec.id] = sel;
            }
            if (MissingNodes.Count > 0) Debug.LogWarning("[VCR] Model is missing component nodes: " + string.Join(", ", MissingNodes));
            IsBound = Parts.Count > 0;
            Bound?.Invoke();
        }

        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform c in parent)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }

    /// <summary>Spins a fan rotor about its local glTF Z axis while refrigerant flow is shown.</summary>
    public class FanSpinner : MonoBehaviour
    {
        public float degreesPerSecond = 540f;
        public static bool Running = true;

        private void Update()
        {
            if (Running) transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
