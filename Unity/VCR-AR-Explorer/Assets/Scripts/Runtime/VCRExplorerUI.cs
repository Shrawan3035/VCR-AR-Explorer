using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VCR
{
    /// <summary>
    /// Builds and drives the explorer UI: top bar (home / reset / labels / flow / view mode /
    /// text size), component list, information panel with prev/next/close, explode slider,
    /// cycle controls, loading + error states. Layout adapts to portrait screens.
    /// </summary>
    public class VCRExplorerUI : MonoBehaviour
    {
        public VCRDatabase database;
        public VCRModelBinder binder;
        public SelectionManager selection;
        public OrbitCameraController orbit;
        public ExplodeController explode;
        public FlowAnimator flow;
        public CycleController cycle;
        public LabelManager labels;
        public string homeScene = "MainMenu";
        [Tooltip("Compact layout for the native AR scene: no component list, explode bar or hint.")] public bool arMode;

        private Canvas _canvas;
        private RectTransform _top, _list, _info, _bottom, _stageCard, _status, _hint;
        private Text _infoNum, _infoTitle, _infoSub, _infoBody, _stageTitle, _stageText, _statusText;
        private Button _labelsBtn, _flowBtn, _modeBtn, _playBtn, _partsBtn;
        private Slider _explodeSlider;
        private ScrollRect _infoScroll;
        private readonly Dictionary<string, Button> _listButtons = new Dictionary<string, Button>();
        private bool _portrait, _listOpen;
        private Vector2Int _lastSize;

        private void Start()
        {
            UIFactory.TextScale = PlayerPrefs.GetFloat("vcr.textScale", 1f);
            _canvas = UIFactory.CreateCanvas("ExplorerUI", 10);
            Build();
            selection.SelectionChanged += OnSelection;
            cycle.StageChanged += OnStage;
            if (!binder.IsBound || binder.Parts.Count == 0)
                ShowStatus("The 3D model's components could not be found.\nIn the Unity Editor run  VCR > Setup Project  to re-import the model and rebuild the scenes.", true);
            else if (binder.MissingNodes.Count > 0)
                ShowStatus("Some components are missing from the model: " + string.Join(", ", binder.MissingNodes), true);
            else
                ShowStatus(null, false);
            OnSelection(selection.Selected);
            OnStage();
            Layout();
        }

        private void Update()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != _lastSize) Layout();
            if (VCRInput.NextPressed) Step(1);
            if (VCRInput.PrevPressed) Step(-1);
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            var root = _canvas.transform;
            // top bar
            _top = UIFactory.Panel(root, "TopBar", VCRColors.Navy);
            UIFactory.HRow(_top, 8, 8);
            _partsBtn = UIFactory.Button(_top, "Parts", () => { _listOpen = !_listOpen; Layout(); }, VCRColors.Navy, Color.white, 20);
            var title = UIFactory.Label(_top, "<b>VCR</b> AR Explorer", 24, Color.white, FontStyle.Normal, TextAnchor.MiddleLeft);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            UIFactory.Button(_top, "Home", GoHome, VCRColors.Navy, Color.white, 18);
            UIFactory.Button(_top, "Reset", ResetAll, VCRColors.Navy, Color.white, 18);
            _labelsBtn = UIFactory.Button(_top, "Labels", ToggleLabels, VCRColors.Paper, VCRColors.Navy, 18);
            _flowBtn = UIFactory.Button(_top, "Flow", ToggleFlow, VCRColors.Paper, VCRColors.Navy, 18);
            _modeBtn = UIFactory.Button(_top, "Show all", CycleMode, VCRColors.Paper, VCRColors.Navy, 18);
            UIFactory.Button(_top, "Aa+", ToggleTextSize, VCRColors.Navy, Color.white, 18);
            foreach (var le in _top.GetComponentsInChildren<LayoutElement>()) if (le.GetComponent<Button>()) le.preferredWidth = 110;

            // component list
            _list = UIFactory.Panel(root, "ComponentList", VCRColors.Card);
            var listContent = UIFactory.ScrollView(_list, "ListScroll", out _);
            UIFactory.Stretch((RectTransform)listContent.parent.parent, 8, 8, 8, 8);
            var head = UIFactory.Label(listContent, "<b>Components</b>\n<size=15>Tap a part in 3D or pick it here.</size>", 22, VCRColors.Navy);
            head.gameObject.AddComponent<LayoutElement>().minHeight = 64;
            foreach (var rec in database.Ordered)
            {
                string id = rec.id;
                var b = UIFactory.Button(listContent, $"{rec.number}.  {rec.name}{(rec.illustrative ? "  (illustrative)" : "")}", () => { cycle.Stop(); selection.Select(id); if (_portrait) { _listOpen = false; Layout(); } }, Color.white, VCRColors.Navy, rec.main ? 19 : 17);
                var t = b.GetComponentInChildren<Text>();
                t.alignment = TextAnchor.MiddleLeft;
                t.fontStyle = rec.main ? FontStyle.Bold : FontStyle.Normal;
                _listButtons[id] = b;
            }
            var note = UIFactory.Label(listContent, "Flow arrows are a conceptual animation - not a CFD or thermodynamic simulation.\nModel: original educational visualisation, not CAD.", 14, VCRColors.Muted);
            note.gameObject.AddComponent<LayoutElement>().minHeight = 70;

            // info panel
            _info = UIFactory.Panel(root, "InfoPanel", VCRColors.Card);
            var ih = UIFactory.Panel(_info, "Header", VCRColors.Navy);
            UIFactory.Anchor(ih, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -96), Vector2.zero);
            _infoNum = UIFactory.Label(ih, "1", 30, VCRColors.Copper, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIFactory.Anchor(_infoNum.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(8, 0), new Vector2(64, 0));
            _infoTitle = UIFactory.Label(ih, "", 28, Color.white, FontStyle.Bold, TextAnchor.LowerLeft);
            UIFactory.Anchor(_infoTitle.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 1), new Vector2(70, 0), new Vector2(-64, -6));
            _infoSub = UIFactory.Label(ih, "", 15, new Color(0.8f, 0.85f, 0.9f), FontStyle.Normal, TextAnchor.UpperLeft);
            UIFactory.Anchor(_infoSub.rectTransform, new Vector2(0, 0), new Vector2(1, 0.45f), new Vector2(70, 4), new Vector2(-64, 0));
            var close = UIFactory.Button(ih, "X", () => { cycle.Stop(); selection.Select(null); }, VCRColors.Navy, Color.white, 26);
            UIFactory.Anchor((RectTransform)close.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-58, -26), new Vector2(-6, 26));
            var bodyContent = UIFactory.ScrollView(_info, "InfoScroll", out _infoScroll);
            UIFactory.Anchor((RectTransform)bodyContent.parent.parent, Vector2.zero, Vector2.one, new Vector2(14, 70), new Vector2(-10, -100));
            _infoBody = UIFactory.Label(bodyContent, "", 18, VCRColors.Ink);
            _infoBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var foot = UIFactory.Empty(_info, "Footer");
            UIFactory.Anchor(foot, Vector2.zero, new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 66));
            UIFactory.HRow(foot, 8, 8).childForceExpandWidth = true;
            UIFactory.Button(foot, "< Prev", () => Step(-1), VCRColors.Paper, VCRColors.Navy, 18);
            UIFactory.Button(foot, "Zoom to part", FocusSelected, VCRColors.Paper, VCRColors.Navy, 18);
            UIFactory.Button(foot, "Next >", () => Step(1), VCRColors.Paper, VCRColors.Navy, 18);

            // bottom bar: explode + cycle
            _bottom = UIFactory.Panel(root, "BottomBar", VCRColors.Card);
            UIFactory.HRow(_bottom, 10, 10);
            UIFactory.Label(_bottom, "<b>Explode</b>", 18, VCRColors.Navy, FontStyle.Normal, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().preferredWidth = 90;
            _explodeSlider = UIFactory.Slider(_bottom, v => explode.SetAmount(v, true));
            UIFactory.Button(_bottom, "<<", () => cycle.Prev(), VCRColors.Paper, VCRColors.Navy, 20);
            _playBtn = UIFactory.Button(_bottom, "Play cycle", () => cycle.TogglePlay(), VCRColors.Copper, Color.white, 18);
            _playBtn.GetComponent<LayoutElement>().preferredWidth = 170;
            UIFactory.Button(_bottom, ">>", () => cycle.Next(), VCRColors.Paper, VCRColors.Navy, 20);

            // stage caption
            _stageCard = UIFactory.Panel(root, "StageCard", VCRColors.Card);
            _stageTitle = UIFactory.Label(_stageCard, "", 22, VCRColors.Navy, FontStyle.Bold);
            UIFactory.Anchor(_stageTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -40), new Vector2(-14, -8));
            _stageText = UIFactory.Label(_stageCard, "", 16, VCRColors.Ink);
            UIFactory.Anchor(_stageText.rectTransform, Vector2.zero, Vector2.one, new Vector2(14, 8), new Vector2(-14, -42));

            // hint + status overlay
            _hint = UIFactory.Panel(root, "Hint", new Color(0.075f, 0.13f, 0.23f, 0.85f), false);
            var ht = UIFactory.Label(_hint, "Drag to rotate - right-drag / two fingers to pan - scroll / pinch to zoom - tap a part to learn about it", 16, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            UIFactory.Stretch(ht.rectTransform, 10, 0, 10, 0);
            _status = UIFactory.Panel(root, "Status", new Color(0.965f, 0.953f, 0.918f, 0.95f));
            UIFactory.Stretch(_status);
            _statusText = UIFactory.Label(_status, "Loading…", 24, VCRColors.Red, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_statusText.rectTransform, 80, 80, 80, 80);
            RefreshToggles();
        }

        private void Layout()
        {
            _lastSize = new Vector2Int(Screen.width, Screen.height);
            _portrait = Screen.height > Screen.width;
            var cs = _canvas.GetComponent<CanvasScaler>();
            cs.referenceResolution = _portrait ? new Vector2(900, 1600) : new Vector2(1600, 900);
            bool hasInfo = selection.Selected != null;
            float topH = 72;
            UIFactory.Anchor(_top, new Vector2(0, 1), Vector2.one, new Vector2(0, -topH), Vector2.zero);
            _partsBtn.gameObject.SetActive(_portrait);
            foreach (var b in new[] { _labelsBtn, _flowBtn, _modeBtn }) b.gameObject.SetActive(!_portrait || !hasInfo);

            float bottomH = 72;
            if (_portrait)
            {
                _list.gameObject.SetActive(_listOpen);
                UIFactory.Anchor(_list, Vector2.zero, new Vector2(0.85f, 1), new Vector2(0, 0), new Vector2(0, -topH));
                _list.SetAsLastSibling();
                UIFactory.Anchor(_bottom, Vector2.zero, new Vector2(1, 0), new Vector2(8, 8), new Vector2(-8, 8 + bottomH * 1.6f));
                _bottom.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                UIFactory.Anchor(_info, Vector2.zero, new Vector2(1, 0.55f), Vector2.zero, Vector2.zero);
                UIFactory.Anchor(_stageCard, new Vector2(0, 1), Vector2.one, new Vector2(8, -topH - 170), new Vector2(-8, -topH - 8));
                UIFactory.Anchor(_hint, new Vector2(0, 0), new Vector2(1, 0), new Vector2(8, bottomH * 1.6f + 16), new Vector2(-8, bottomH * 1.6f + 76));
            }
            else
            {
                _list.gameObject.SetActive(true);
                UIFactory.Anchor(_list, Vector2.zero, new Vector2(0, 1), new Vector2(0, 0), new Vector2(320, -topH));
                float right = hasInfo ? 480 : 0;
                UIFactory.Anchor(_bottom, Vector2.zero, new Vector2(1, 0), new Vector2(332, 10), new Vector2(-12 - right, 10 + bottomH));
                UIFactory.Anchor(_info, new Vector2(1, 0), Vector2.one, new Vector2(-470, 10), new Vector2(-10, -topH - 10));
                UIFactory.Anchor(_stageCard, new Vector2(0, 1), new Vector2(0, 1), new Vector2(332, -topH - 150), new Vector2(332 + 520, -topH - 12));
                UIFactory.Anchor(_hint, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-500 + 160, -topH - 56), new Vector2(500 + 160, -topH - 12));
            }
            if (arMode)
            {
                _list.gameObject.SetActive(false);
                _bottom.gameObject.SetActive(false);
                _partsBtn.gameObject.SetActive(false);
                UIFactory.Anchor(_info, Vector2.zero, new Vector2(1, 0.5f), new Vector2(0, 110), Vector2.zero);
            }
            _info.gameObject.SetActive(hasInfo);
            _hint.gameObject.SetActive(!arMode && !hasInfo && cycle.Current == null);
            _status.SetAsLastSibling();
        }

        // ------------------------------------------------------------------ events
        private void OnSelection(string id)
        {
            foreach (var kv in _listButtons)
                UIFactory.SetButtonColors(kv.Value, kv.Key == id ? VCRColors.Navy : Color.white, kv.Key == id ? Color.white : VCRColors.Navy);
            var rec = database.Get(id);
            if (rec != null)
            {
                _infoNum.text = rec.number.ToString();
                _infoTitle.text = rec.name;
                _infoSub.text = rec.subtitle + (rec.illustrative ? "  - illustrative" : "");
                string heat = string.IsNullOrEmpty(rec.heat) ? "" : $"<color=#{(rec.heat == "rejected" ? "d2402a" : "1592b0")}><b>Heat {rec.heat}</b></color>\n";
                _infoBody.text =
                    $"<color=#b8682f><b>{rec.side.ToUpperInvariant()}</b></color>\n{heat}\n" +
                    Section("FUNCTION", rec.@function) + Section("WORKING PRINCIPLE", rec.principle) +
                    Section("REFRIGERANT IN", rec.inlet) + Section("REFRIGERANT OUT", rec.outlet) +
                    Section("ROLE IN THE CYCLE", rec.cycleRole) + Section("WHERE YOU'LL SEE IT", rec.application) +
                    $"<b>In simple words:</b> <i>{rec.student}</i>";
                _infoScroll.verticalNormalizedPosition = 1;
            }
            Layout();
        }

        private static string Section(string title, string body) => $"<color=#b8682f><size=14><b>{title}</b></size></color>\n{body}\n\n";

        private void OnStage()
        {
            var st = cycle.Current;
            _stageCard.gameObject.SetActive(st != null);
            if (st != null)
            {
                _stageTitle.text = $"Stage {st.id} of 4 - {st.title}";
                _stageText.text = st.text;
            }
            UIFactory.SetButtonText(_playBtn, cycle.Playing ? "Pause" : "Play cycle");
            if (_explodeSlider != null) _explodeSlider.SetValueWithoutNotify(explode.Current);
            Layout();
        }

        private void Step(int dir)
        {
            var ordered = database.Ordered;
            int i = System.Array.FindIndex(ordered, c => c.id == selection.Selected);
            int n = (i + dir + ordered.Length) % ordered.Length;
            if (i < 0) n = dir > 0 ? 0 : ordered.Length - 1;
            cycle.Stop();
            selection.Select(ordered[n].id);
        }

        private void FocusSelected()
        {
            if (orbit != null && selection.Selected != null && binder.Parts.TryGetValue(selection.Selected, out var p)) orbit.FocusOn(p.WorldBounds());
        }

        private void ResetAll()
        {
            cycle.Stop();
            selection.Select(null);
            selection.SetMode(VisibilityMode.ShowAll);
            explode.SetAmount(0f, true);
            _explodeSlider.SetValueWithoutNotify(0f);
            if (orbit != null) orbit.ResetView();
            RefreshToggles();
        }

        private void ToggleLabels()
        {
            labels.showLabels = !labels.showLabels;
            RefreshToggles();
        }

        private void ToggleFlow()
        {
            flow.SetRunning(!flow.running);
            RefreshToggles();
        }

        private void CycleMode()
        {
            selection.SetMode((VisibilityMode)(((int)selection.Mode + 1) % 3));
            RefreshToggles();
        }

        private void ToggleTextSize()
        {
            ScalableText.SetScale(UIFactory.TextScale > 1.05f ? 1f : 1.25f);
        }

        private void RefreshToggles()
        {
            UIFactory.SetButtonColors(_labelsBtn, labels.showLabels ? VCRColors.Copper : VCRColors.Paper, labels.showLabels ? Color.white : VCRColors.Navy);
            UIFactory.SetButtonColors(_flowBtn, flow.running ? VCRColors.Copper : VCRColors.Paper, flow.running ? Color.white : VCRColors.Navy);
            UIFactory.SetButtonText(_modeBtn, selection.Mode == VisibilityMode.ShowAll ? "Show all" : selection.Mode == VisibilityMode.GhostOthers ? "Ghost others" : "Isolate");
        }

        private void GoHome()
        {
            if (Application.CanStreamedLevelBeLoaded(homeScene)) SceneManager.LoadScene(homeScene);
        }

        private void ShowStatus(string message, bool error)
        {
            _status.gameObject.SetActive(!string.IsNullOrEmpty(message));
            if (message != null) _statusText.text = message;
            _statusText.color = error ? VCRColors.Red : VCRColors.Navy;
        }

        // used by the automation hook (screenshots / smoke tests)
        public void SetExplodeExternal(float v)
        {
            explode.SetAmount(v, true);
            _explodeSlider.SetValueWithoutNotify(v);
        }
    }
}
