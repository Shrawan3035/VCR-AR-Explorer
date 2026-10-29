// Shared data model for the VCR explorer. The JSON files in Assets/Data are copied from the
// repository's /data folder by model-generation/generate_vcr_glb.py, so the website, poster and
// Unity app all describe the components with exactly the same text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR
{
    [Serializable]
    public class VCRComponentRecord
    {
        public string id;
        public int number;
        public string node;
        public bool main;
        public bool illustrative;
        public bool line;
        public string name;
        public string subtitle;
        public string side;
        public string @function;
        public string principle;
        public string inlet;
        public string outlet;
        public string cycleRole;
        public string application;
        public string student;
        public string heat;
        public float[] explode;

        /// <summary>Explode offset converted from glTF (right-handed) to Unity (left-handed) space.</summary>
        public Vector3 ExplodeOffset => explode != null && explode.Length == 3 ? VCRCoords.FromGltf(new Vector3(explode[0], explode[1], explode[2])) : Vector3.zero;
    }

    [Serializable]
    public class VCRStageRecord
    {
        public int id;
        public string title;
        public string from;
        public string component;
        public string text;
        public string ideal;
        public string real;
    }

    [Serializable]
    public class VCRProjectData
    {
        public string title;
        public string disclaimer;
        public string[] cycleOrder;
        public VCRStageRecord[] stages;
        public VCRComponentRecord[] components;
    }

    [Serializable]
    public class VCRFlowSegment
    {
        public string component;
        public string state;
        public float[] flat;

        public List<Vector3> Points()
        {
            var list = new List<Vector3>();
            for (int i = 0; i + 2 < flat.Length; i += 3)
                list.Add(VCRCoords.FromGltf(new Vector3(flat[i], flat[i + 1], flat[i + 2])));
            return list;
        }
    }

    [Serializable]
    public class VCRFlowData
    {
        public string note;
        public VCRFlowSegment[] segments;
    }

    public static class VCRCoords
    {
        /// <summary>glTFast mirrors the X axis when converting glTF to Unity coordinates.</summary>
        public static Vector3 FromGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);
    }

    public static class VCRColors
    {
        public static readonly Color Navy = new Color32(0x13, 0x21, 0x3a, 0xff);
        public static readonly Color Paper = new Color32(0xf6, 0xf3, 0xea, 0xff);
        public static readonly Color Card = new Color32(0xfb, 0xf9, 0xf3, 0xf5);
        public static readonly Color Copper = new Color32(0xb8, 0x68, 0x2f, 0xff);
        public static readonly Color Cyan = new Color32(0x15, 0x92, 0xb0, 0xff);
        public static readonly Color Red = new Color32(0xd2, 0x40, 0x2a, 0xff);
        public static readonly Color Ink = new Color32(0x1b, 0x25, 0x37, 0xff);
        public static readonly Color Muted = new Color32(0x5d, 0x66, 0x77, 0xff);

        public static Color ForState(string state)
        {
            switch (state)
            {
                case "compression": return new Color32(0xe0, 0x58, 0x3a, 0xff);
                case "hp_vapour": return new Color32(0xe0, 0x44, 0x2a, 0xff);
                case "hp_condensing": return new Color32(0xec, 0x7d, 0x3a, 0xff);
                case "hp_liquid": return new Color32(0xf0, 0xa3, 0x3c, 0xff);
                case "lp_mix": return new Color32(0x5c, 0xcb, 0xe6, 0xff);
                case "lp_evaporating": return new Color32(0x3a, 0xa6, 0xe0, 0xff);
                default: return new Color32(0x2f, 0x7f, 0xd0, 0xff);
            }
        }
    }
}
