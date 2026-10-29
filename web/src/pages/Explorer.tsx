import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Viewer3D } from '../components/Viewer3D';
import { InfoPanel } from '../components/InfoPanel';
import { useCameraFeed } from '../components/useCameraFeed';
import type { VCRViewer, VisibilityMode } from '../viewer/VCRViewer';
import { byId, components, stages, stateColor, stateLabel, type FlowState } from '../data';

interface Props {
  initialExplode?: number;
  heading?: string;
}

const ORDER = [...components].sort((a, b) => a.number - b.number);

export function Explorer({ initialExplode = 0, heading = '3D Explorer' }: Props) {
  const viewer = useRef<VCRViewer | null>(null);
  const [params, setParams] = useSearchParams();
  const [ready, setReady] = useState(false);
  const [selected, setSelected] = useState<string | null>(params.get('c'));
  const [mode, setMode] = useState<VisibilityMode>('all');
  // "shot" mode is used by scripts/capture.mjs to render poster images from this exact viewer
  const shot = params.get('shot') === '1';
  const num = (k: string, d: number) => (params.has(k) ? parseFloat(params.get(k)!) : d);
  const [explode, setExplode] = useState(num('explode', initialExplode));
  const [labels, setLabels] = useState(params.get('labels') !== '0');
  const [flow, setFlow] = useState(params.get('flow') !== '0');
  const [stage, setStage] = useState<number | null>(null);
  const [playing, setPlaying] = useState(false);
  const [listOpen, setListOpen] = useState(false);
  const camera = useCameraFeed();
  const camOn = camera.state === 'on';

  // camera overlay: hide the floor grid so the rig sits on the live video
  useEffect(() => viewer.current?.setGrid(!camOn), [camOn, ready]);
  useEffect(() => {
    // opened from the AR page's "camera overlay" link (a user click), so starting right away is expected
    if (ready && params.get('overlay') === '1') camera.start();
  }, [ready]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!ready || !viewer.current) return;
    const v = viewer.current;
    const cam = params.get('cam')?.split(',').map(Number);
    if (cam?.length === 6) v.setCamera(cam.slice(0, 3) as [number, number, number], cam.slice(3) as [number, number, number]);
    else if (initialExplode > 0) v.zoom(1.45);
    if (shot) {
      document.documentElement.classList.add('shot-mode');
      v.setGrid(false);
      // wait two frames so labels / transforms settle, then publish anchors for the capture script
      requestAnimationFrame(() => requestAnimationFrame(() => ((window as unknown as Record<string, unknown>).__vcrShot = { anchors: v.getScreenAnchors() })));
    }
  }, [ready]); // eslint-disable-line react-hooks/exhaustive-deps

  // --- keep viewer in sync with React state
  useEffect(() => viewer.current?.select(selected), [selected, ready]);
  useEffect(() => viewer.current?.setMode(mode), [mode, ready]);
  useEffect(() => viewer.current?.setExplode(explode), [explode, ready]);
  useEffect(() => viewer.current?.setLabels(labels), [labels, ready]);
  useEffect(() => viewer.current?.setFlow(flow), [flow, ready]);
  useEffect(() => {
    const next = new URLSearchParams(params);
    if (selected) next.set('c', selected);
    else next.delete('c');
    if (next.toString() !== params.toString()) setParams(next, { replace: true });
  }, [selected]); // eslint-disable-line react-hooks/exhaustive-deps

  // --- cycle mode
  useEffect(() => {
    if (stage === null) {
      viewer.current?.setFlowFocus(null);
      return;
    }
    const s = stages[stage];
    setSelected(s.component);
    setExplode(0);
    setFlow(true);
    viewer.current?.setFlowFocus(s.component);
  }, [stage, ready]);

  useEffect(() => {
    if (!playing) return;
    const t = setInterval(() => setStage((s) => ((s ?? -1) + 1) % stages.length), 6000);
    return () => clearInterval(t);
  }, [playing]);

  const step = useCallback(
    (dir: 1 | -1) => {
      const i = ORDER.findIndex((c) => c.id === selected);
      const n = ORDER[(i + dir + ORDER.length) % ORDER.length];
      setStage(null);
      setPlaying(false);
      setSelected(n.id);
    },
    [selected],
  );

  const choose = (id: string | null) => {
    setStage(null);
    setPlaying(false);
    setSelected(id);
    setListOpen(false);
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement)?.closest('input, textarea, select')) return;
      if (e.key === 'Escape') choose(null);
      else if (e.key === 'ArrowRight') step(1);
      else if (e.key === 'ArrowLeft') step(-1);
      else if (e.key.toLowerCase() === 'r') viewer.current?.resetView();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [step]);

  const info = byId(selected);
  const curStage = stage !== null ? stages[stage] : null;
  const legendStates = Object.keys(stateLabel).filter((s) => s !== 'compression') as FlowState[];

  return (
    <div className={`explorer ${info ? 'has-panel' : ''}`}>
      <aside className={`comp-list ${listOpen ? 'open' : ''}`} aria-label="Component list">
        <div className="comp-list-head">
          <h1 className="h-small">{heading}</h1>
          <button className="icon-btn only-mobile" onClick={() => setListOpen(false)} aria-label="Close component list">✕</button>
        </div>
        <p className="muted small">Tap a part in 3D or pick it here. Numbers match the callouts.</p>
        <ol className="comp-ol">
          {ORDER.map((c) => (
            <li key={c.id}>
              <button className={`comp-item ${selected === c.id ? 'active' : ''} ${c.main ? 'main' : ''}`} onClick={() => choose(c.id)} aria-pressed={selected === c.id}>
                <span className="num">{c.number}</span>
                <span>
                  {c.name}
                  {c.illustrative && <em className="illus"> illustrative</em>}
                </span>
              </button>
            </li>
          ))}
        </ol>
        <div className="legend" aria-label="Flow colour legend">
          <p className="eyebrow">Refrigerant state (conceptual)</p>
          {legendStates.map((s) => (
            <p key={s} className="legend-row">
              <span className="swatch" style={{ background: stateColor[s] }} />
              {stateLabel[s]}
            </p>
          ))}
          <p className="tiny muted">Flow arrows are a conceptual animation, not a CFD or thermodynamic simulation.</p>
        </div>
      </aside>

      <div className={`stage ${camOn ? 'cam-on' : ''}`}>
        <video ref={camera.videoRef} className="cam-feed" playsInline muted aria-hidden="true" hidden={!camOn} />
        <Viewer3D
          ref={viewer}
          onSelect={choose}
          onReady={(v) => {
            viewer.current = v;
            setReady(true);
          }}
        />

        <div className="stage-top">
          <button className="btn btn-small only-mobile" onClick={() => setListOpen(true)}>☰ Parts</button>
          {curStage && (
            <div className="stage-card" aria-live="polite">
              <p className="eyebrow">Stage {curStage.id} of 4</p>
              <h2>{curStage.title}</h2>
              <p>{curStage.text}</p>
            </div>
          )}
        </div>

        <div className="toolbar" role="toolbar" aria-label="3D view controls">
          <div className="tool-group">
            <button className="tool" onClick={() => viewer.current?.resetView()} title="Reset view (R)">⟲ Reset</button>
            <button className="tool" onClick={() => viewer.current?.zoom(0.8)} aria-label="Zoom in">＋</button>
            <button className="tool" onClick={() => viewer.current?.zoom(1.25)} aria-label="Zoom out">－</button>
          </div>
          <div className="tool-group">
            <button className={`tool ${labels ? 'on' : ''}`} aria-pressed={labels} onClick={() => setLabels(!labels)}>Labels</button>
            <button className={`tool ${flow ? 'on' : ''}`} aria-pressed={flow} onClick={() => setFlow(!flow)}>Flow</button>
            <button
              className={`tool tool-cam ${camOn ? 'on' : ''}`}
              aria-pressed={camOn}
              disabled={camera.state === 'starting'}
              onClick={() => (camOn ? camera.stop() : camera.start())}
              title="Show the rig over your live camera feed"
            >
              {camOn ? '■ Stop camera' : camera.state === 'starting' ? 'Starting…' : '● Camera'}
            </button>
            <label className="tool select-tool">
              <span className="sr-only">Other parts</span>
              <select value={mode} onChange={(e) => setMode(e.target.value as VisibilityMode)} aria-label="How to show the other parts">
                <option value="all">Show all</option>
                <option value="ghost">Ghost others</option>
                <option value="isolate">Isolate selected</option>
              </select>
            </label>
          </div>
          <div className="tool-group grow">
            <label className="slider">
              <span>Explode</span>
              <input type="range" min={0} max={1} step={0.01} value={explode} onChange={(e) => setExplode(parseFloat(e.target.value))} aria-label="Exploded view amount" />
            </label>
          </div>
          <div className="tool-group">
            <button className="tool" onClick={() => setStage((s) => (s === null ? stages.length - 1 : (s + stages.length - 1) % stages.length))} aria-label="Previous cycle stage">⏮</button>
            <button className={`tool ${playing ? 'on' : ''}`} onClick={() => { setPlaying(!playing); if (stage === null) setStage(0); }} aria-pressed={playing}>
              {playing ? '❚❚ Pause cycle' : '▶ Play cycle'}
            </button>
            <button className="tool" onClick={() => setStage((s) => (s === null ? 0 : (s + 1) % stages.length))} aria-label="Next cycle stage">⏭</button>
          </div>
        </div>

        {info && (
          <InfoPanel
            info={info}
            onClose={() => choose(null)}
            onPrev={() => step(-1)}
            onNext={() => step(1)}
            onFocus={() => viewer.current?.focus(info.id)}
          />
        )}
        {camera.error && (
          <div className="cam-error" role="alert">
            <span>{camera.error}</span>
            <button className="icon-btn" onClick={camera.clearError} aria-label="Dismiss camera message">✕</button>
          </div>
        )}
        {camOn && !info && <div className="cam-hint">camera overlay · drag to turn · scroll / pinch to resize · tap a part · not surface-tracked — use AR for that</div>}
        {!info && ready && !camOn && (
          <div className="hint" aria-live="polite">
            Drag to rotate · scroll / pinch to zoom · right-drag or two fingers to pan · <strong>tap a part</strong> to learn about it.{' '}
            <Link to="/ar">Try it in AR →</Link>
          </div>
        )}
      </div>
    </div>
  );
}
