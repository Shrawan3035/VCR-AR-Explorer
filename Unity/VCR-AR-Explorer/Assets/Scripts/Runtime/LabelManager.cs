using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VCR
{
    /// <summary>
    /// Numbered callouts that follow each component on screen. They are UI elements created by
    /// the app (not baked into the model); clicking one selects that component.
    /// </summary>
    public class LabelManager : MonoBehaviour
    {
        public VCRModelBinder binder;
        public SelectionManager selection;
        public ExplodeController explode;
        public Camera cam;
        public bool showLabels = true;

        private RectTransform _layer;
        private Canvas _canvas;
        private readonly Dictionary<string, (RectTransform rt, Image bg, Text text, VCRSelectable part)> _labels = new Dictionary<string, (RectTransform, Image, Text, VCRSelectable)>();

        private void Start()
        {
            if (cam == null) cam = Camera.main;
            _canvas = UIFactory.CreateCanvas("LabelCanvas", 0);
            _layer = UIFactory.Empty(_canvas.transform, "Labels");
            UIFactory.Stretch(_layer);
            foreach (var kv in binder.Parts)
            {
                var rec = kv.Value.Record;
                string text = rec.main ? $" {rec.number}  {rec.name} " : $" {rec.number} ";
                var b = UIFactory.Button(_layer, text, () => selection.Select(kv.Key), VCRColors.Card, VCRColors.Navy, 17);
                var rt = b.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(rec.main ? 52 + rec.name.Length * 11 : 40, 34);
                b.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
                rt.pivot = new Vector2(0.5f, 0f);
                var outline = b.gameObject.AddComponent<Outline>();
                outline.effectColor = rec.main ? VCRColors.Copper : VCRColors.Navy;
                outline.effectDistance = new Vector2(2, -2);
                Destroy(b.GetComponent<LayoutElement>());
                _labels[kv.Key] = (rt, b.GetComponent<Image>(), b.GetComponentInChildren<Text>(), kv.Value);
            }
        }

        private void LateUpdate()
        {
            if (_canvas == null) return;
            float scale = _canvas.scaleFactor;
            foreach (var kv in _labels)
            {
                var (rt, bg, text, part) = kv.Value;
                bool lineHidden = part.Record.line && explode != null && explode.Current > 0.2f;
                bool isolatedAway = selection.Mode == VisibilityMode.IsolateSelected && selection.Selected != null && selection.Selected != kv.Key;
                var b = part.WorldBounds();
                var anchor = part.Record.line ? b.center : new Vector3(b.center.x, b.max.y, b.center.z);
                var sp = cam.WorldToScreenPoint(anchor);
                bool visible = showLabels && !lineHidden && !isolatedAway && sp.z > 0;
                rt.gameObject.SetActive(visible);
                if (!visible) continue;
                rt.anchoredPosition = new Vector2(sp.x, sp.y + 6) / scale;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                bool sel = selection.Selected == kv.Key;
                bg.color = sel ? VCRColors.Navy : VCRColors.Card;
                text.color = sel ? Color.white : VCRColors.Navy;
                var c = bg.color;
                c.a = selection.Selected != null && !sel ? 0.55f : 0.95f;
                bg.color = c;
            }
        }
    }
}
