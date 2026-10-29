using System;
using System.Linq;
using UnityEngine;

namespace VCR
{
    /// <summary>
    /// ScriptableObject wrapper around the shared JSON data. Edit the JSON (or replace the
    /// TextAssets) to change the educational content without touching code or scenes.
    /// </summary>
    [CreateAssetMenu(menuName = "VCR/Component Database", fileName = "VCRDatabase")]
    public class VCRDatabase : ScriptableObject
    {
        [Tooltip("Assets/Data/components.json")] public TextAsset componentsJson;
        [Tooltip("Assets/Data/flow_paths.json")] public TextAsset flowJson;
        [Tooltip("Public website URL opened from the main menu (leave empty until deployed).")] public string websiteUrl = "";

        [NonSerialized] private VCRProjectData _data;
        [NonSerialized] private VCRFlowData _flow;

        public VCRProjectData Data
        {
            get
            {
                if (_data == null && componentsJson != null) _data = JsonUtility.FromJson<VCRProjectData>(componentsJson.text);
                return _data;
            }
        }

        public VCRFlowData Flow
        {
            get
            {
                if (_flow == null && flowJson != null) _flow = JsonUtility.FromJson<VCRFlowData>(flowJson.text);
                return _flow;
            }
        }

        public VCRComponentRecord Get(string id) => Data?.components.FirstOrDefault(c => c.id == id);

        public VCRComponentRecord[] Ordered => Data == null ? Array.Empty<VCRComponentRecord>() : Data.components.OrderBy(c => c.number).ToArray();

        /// <summary>Clears the parsed cache, e.g. after the JSON is edited in the Editor.</summary>
        public void Reload()
        {
            _data = null;
            _flow = null;
        }

        private void OnValidate() => Reload();
    }
}
