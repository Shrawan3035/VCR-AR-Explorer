using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace VCR
{
    /// <summary>Small helpers that build uGUI elements in code with the project's visual style.</summary>
    public static class UIFactory
    {
        private static Font _font;
        public static float TextScale = 1f;

        public static Font DefaultFont
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }



        public static RectTransform Panel(Transform parent, string name, Color color, bool raycast = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return go.GetComponent<RectTransform>();
        }

        public static RectTransform Empty(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        public static Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = Mathf.RoundToInt(size * TextScale);
            t.color = color;
            t.fontStyle = style;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            // OnEnable runs inside AddComponent (before baseSize is set), so apply the size again afterwards
            go.AddComponent<ScalableText>().baseSize = size;
            t.fontSize = Mathf.RoundToInt(size * TextScale);
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, Color bg, Color fg, int fontSize = 20)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = bg;
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Label(go.transform, label, fontSize, fg, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 6, 2, 6, 2);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 48; // touch target
            le.minWidth = 48;
            return b;
        }

        public static Slider Slider(Transform parent, UnityAction<float> onChange)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var s = go.GetComponent<Slider>();
            s.minValue = 0;
            s.maxValue = 1;
            var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill) fill.color = VCRColors.Copper;
            var bg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bg) bg.color = new Color(0.8f, 0.78f, 0.72f);
            var handle = go.transform.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
            if (handle) handle.color = VCRColors.Navy;
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 40;
            le.flexibleWidth = 1;
            return s;
        }

        /// <summary>Vertical scroll view; returns the content transform (with a VerticalLayoutGroup).</summary>
        public static RectTransform ScrollView(Transform parent, string name, out ScrollRect scroll)
        {
            var root = Panel(parent, name, new Color(0, 0, 0, 0));
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var viewport = Panel(root, "Viewport", new Color(1, 1, 1, 0.01f));
            viewport.gameObject.AddComponent<RectMask2D>();
            Stretch(viewport);
            var content = Empty(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero; // a new RectTransform defaults to 100x100, which would overhang the viewport
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        public static HorizontalLayoutGroup HRow(RectTransform rt, float spacing = 8, int pad = 8)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t) t.text = text;
        }

        public static void SetButtonColors(Button b, Color bg, Color fg)
        {
            b.GetComponent<Image>().color = bg;
            var t = b.GetComponentInChildren<Text>();
            if (t) t.color = fg;
        }
    }

    /// <summary>Lets the accessibility "larger text" option rescale every label at runtime.</summary>
    public class ScalableText : MonoBehaviour
    {
        public int baseSize = 18;
        public static event Action Changed;
        private Text _t;

        private void OnEnable()
        {
            _t = GetComponent<Text>();
            Changed += Apply;
            Apply();
        }

        private void OnDisable() => Changed -= Apply;

        private void Apply()
        {
            if (_t) _t.fontSize = Mathf.RoundToInt(baseSize * UIFactory.TextScale);
        }

        public static void SetScale(float s)
        {
            UIFactory.TextScale = s;
            PlayerPrefs.SetFloat("vcr.textScale", s);
            Changed?.Invoke();
        }
    }
}
