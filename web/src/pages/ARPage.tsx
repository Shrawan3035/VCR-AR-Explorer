import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import anchors from '../../../data/anchors.json';
import { asset, byId, components } from '../data';

/* <model-viewer> is a web component; declare it for TSX. */
declare module 'react' {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace JSX {
    interface IntrinsicElements {
      'model-viewer': React.DetailedHTMLProps<React.HTMLAttributes<HTMLElement>, HTMLElement> & Record<string, unknown>;
    }
  }
}

type ARState = 'checking' | 'supported' | 'unsupported';
type Variant = 'assembled' | 'exploded';

interface ModelViewerElement extends HTMLElement {
  canActivateAR: boolean;
  activateAR: () => Promise<void>;
}

const MAIN = components.filter((c) => c.main);

export function ARPage() {
  const mv = useRef<ModelViewerElement | null>(null);
  const [libReady, setLibReady] = useState(false);
  const [libError, setLibError] = useState('');
  const [loaded, setLoaded] = useState(false);
  const [modelError, setModelError] = useState('');
  const [arState, setArState] = useState<ARState>('checking');
  const [arMsg, setArMsg] = useState('');
  const [variant, setVariant] = useState<Variant>('assembled');
  const [picked, setPicked] = useState<string | null>(null);

  useEffect(() => {
    import('@google/model-viewer')
      .then(() => setLibReady(true))
      .catch(() => setLibError('The AR viewer library could not be loaded. Check your connection and reload.'));
  }, []);

  useEffect(() => {
    const el = mv.current;
    if (!el || !libReady) return;
    const onLoad = () => {
      setLoaded(true);
      setArState(el.canActivateAR ? 'supported' : 'unsupported');
    };
    const onError = () => setModelError('The 3D model could not be loaded. Please reload the page.');
    const onStatus = (e: Event) => {
      const status = (e as CustomEvent<{ status: string }>).detail.status;
      if (status === 'failed')
        setArMsg('AR could not start. If you were asked for camera access, it may have been denied — allow camera access for this site in your browser settings, or keep using the 3D view below.');
      else if (status === 'session-started') setArMsg('Move your phone slowly over a table or floor until the model appears.');
      else if (status === 'not-presenting') setArMsg('');
    };
    el.addEventListener('load', onLoad);
    el.addEventListener('error', onError);
    el.addEventListener('ar-status', onStatus);
    return () => {
      el.removeEventListener('load', onLoad);
      el.removeEventListener('error', onError);
      el.removeEventListener('ar-status', onStatus);
    };
  }, [libReady]);

  const src = asset(variant === 'assembled' ? 'models/VCR_System.glb' : 'models/VCR_System_Exploded.glb');
  const anc = (anchors as Record<Variant, Record<string, { top: number[] }>>)[variant];
  const info = byId(picked);
  // portrait phones crop the wide rig horizontally, so start further out
  const narrow = typeof window !== 'undefined' && window.innerWidth / Math.max(1, window.innerHeight) < 0.8;

  return (
    <div className="page ar-page">
      <header className="page-head">
        <p className="eyebrow">Augmented reality · no app needed</p>
        <h1>Place the rig in your room</h1>
        <p className="lead">
          On a compatible phone, tap <strong>View in your space</strong> to put the refrigeration rig on a real table at life size. The camera is only requested when you start AR.
        </p>
      </header>

      <div className="ar-layout">
        <div className="mv-wrap">
          {libError && <div className="viewer-state viewer-error" role="alert"><p>{libError}</p></div>}
          {libReady && (
            <model-viewer
              ref={mv}
              src={src}
              alt="3D model of a vapour compression refrigeration demonstration rig"
              ar=""
              ar-modes="webxr scene-viewer quick-look"
              ar-scale="auto"
              ar-placement="floor"
              camera-controls=""
              touch-action="pan-y"
              shadow-intensity="1"
              exposure="1"
              environment-image="neutral"
              camera-orbit={`35deg 70deg ${narrow ? 175 : 105}%`}
              interaction-prompt="auto"
              style={{ width: '100%', height: '100%' }}
            >
              {MAIN.map((c) => {
                const a = anc[c.id];
                if (!a) return null;
                return (
                  <button
                    key={`${variant}-${c.id}`}
                    slot={`hotspot-${c.id}`}
                    className={`mv-hotspot ${picked === c.id ? 'active' : ''}`}
                    data-position={`${a.top[0]}m ${a.top[1] + 0.03}m ${a.top[2]}m`}
                    data-normal="0m 1m 0m"
                    onClick={() => setPicked(c.id)}
                    aria-label={`${c.number}. ${c.name}`}
                  >
                    <span className="callout-num">{c.number}</span>
                    <span className="callout-name">{c.name}</span>
                  </button>
                );
              })}
              <button slot="ar-button" className="btn btn-ar mv-ar-btn">▣ View in your space</button>
              <div slot="progress-bar" />
            </model-viewer>
          )}
          {!loaded && !libError && !modelError && (
            <div className="viewer-state" role="status"><div className="spinner" aria-hidden="true" /><p>Loading AR model…</p></div>
          )}
          {modelError && <div className="viewer-state viewer-error" role="alert"><p>{modelError}</p></div>}
          <div className="seg" role="group" aria-label="Model variant">
            <button className={variant === 'assembled' ? 'on' : ''} aria-pressed={variant === 'assembled'} onClick={() => { setVariant('assembled'); setLoaded(false); }}>Assembled</button>
            <button className={variant === 'exploded' ? 'on' : ''} aria-pressed={variant === 'exploded'} onClick={() => { setVariant('exploded'); setLoaded(false); }}>Exploded</button>
          </div>
        </div>

        <aside className="ar-side">
          <div className={`ar-status ${arState}`} role="status" aria-live="polite">
            {arState === 'checking' && 'Checking whether this device supports AR…'}
            {arState === 'supported' && '✓ AR is available on this device. Tap “View in your space”.'}
            {arState === 'unsupported' && (
              <>
                AR is not available in this browser/device. You can still rotate the model here, or{' '}
                <Link to="/explore?overlay=1">see it over your camera feed</Link> (camera overlay, no surface tracking).
              </>
            )}
          </div>
          {arMsg && <p className="ar-msg" role="alert">{arMsg}</p>}

          {info ? (
            <div className="card">
              <p className="eyebrow">Component {info.number}</p>
              <h2 className="h-small">{info.name}</h2>
              <p>{info.function}</p>
              <p className="muted small"><strong>{info.side}.</strong> In: {info.inlet}. Out: {info.outlet}.</p>
              <Link className="btn btn-small" to={`/explore?c=${info.id}`}>Open in 3D Explorer</Link>
            </div>
          ) : (
            <p className="muted">Tap a numbered label on the model to read about that component.</p>
          )}

          <ol className="steps">
            <li><span>01</span>Tap <strong>View in your space</strong> (allow camera when asked).</li>
            <li><span>02</span>Move the phone slowly over a table or the floor.</li>
            <li><span>03</span>Walk around it; switch to <strong>Exploded</strong> to see parts separated.</li>
          </ol>
          <div className="compat">
            <p className="eyebrow">Device requirements</p>
            <ul>
              <li><strong>Android:</strong> Chrome with Google Play Services for AR (ARCore) — uses WebXR or Scene Viewer.</li>
              <li><strong>iPhone / iPad:</strong> Safari on iOS/iPadOS with AR Quick Look (model converted to USDZ automatically by the viewer).</li>
              <li><strong>Desktop / other:</strong> 3D view only — no camera is used.</li>
            </ul>
            <p className="tiny muted">AR placement accuracy depends on your device's tracking. Life-size scale is approximate: this is an educational model, not CAD.</p>
          </div>
          <Link to="/explore" className="btn btn-ghost">Open full 3D Explorer →</Link>
        </aside>
      </div>
    </div>
  );
}
