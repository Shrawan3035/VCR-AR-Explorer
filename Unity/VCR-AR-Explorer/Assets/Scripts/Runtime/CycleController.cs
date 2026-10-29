using System;
using UnityEngine;

namespace VCR
{
    /// <summary>
    /// Steps through the four stages of the cycle (Evaporator → Compressor → Condenser →
    /// Expansion device), highlighting each component and focusing the flow animation on it.
    /// </summary>
    public class CycleController : MonoBehaviour
    {
        public VCRDatabase database;
        public SelectionManager selection;
        public FlowAnimator flow;
        public ExplodeController explode;
        public float secondsPerStage = 6f;

        public int StageIndex { get; private set; } = -1;
        public bool Playing { get; private set; }
        public VCRStageRecord Current => StageIndex >= 0 ? database.Data.stages[StageIndex] : null;
        public event Action StageChanged;

        private float _timer;
        private bool _internalSelect;

        private void Start()
        {
            if (selection != null) selection.SelectionChanged += OnSelectionChanged;
        }

        private void OnSelectionChanged(string id)
        {
            // a manual selection ends cycle mode
            if (_internalSelect) return;
            Stop();
        }

        public void Play()
        {
            Playing = true;
            _timer = 0f;
            if (StageIndex < 0) Go(0);
            StageChanged?.Invoke();
        }

        public void Pause()
        {
            Playing = false;
            StageChanged?.Invoke();
        }

        public void TogglePlay()
        {
            if (Playing) Pause(); else Play();
        }

        public void Next() => Go(StageIndex < 0 ? 0 : (StageIndex + 1) % Count);
        public void Prev() => Go(StageIndex < 0 ? Count - 1 : (StageIndex + Count - 1) % Count);

        private int Count => database.Data.stages.Length;

        public void Go(int i)
        {
            StageIndex = i;
            _timer = 0f;
            var st = database.Data.stages[i];
            if (explode != null) explode.SetAmount(0f);
            if (flow != null)
            {
                flow.SetRunning(true);
                flow.SetFocus(st.component);
            }
            _internalSelect = true;
            selection.Select(st.component);
            _internalSelect = false;
            StageChanged?.Invoke();
        }

        public void Stop()
        {
            Playing = false;
            StageIndex = -1;
            if (flow != null) flow.SetFocus(null);
            StageChanged?.Invoke();
        }

        private void Update()
        {
            if (!Playing) return;
            _timer += Time.deltaTime;
            if (_timer >= secondsPerStage) Next();
        }
    }
}
