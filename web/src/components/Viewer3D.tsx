import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { VCRViewer } from '../viewer/VCRViewer';
import { MODEL_URL } from '../data';

function webglAvailable() {
  try {
    const c = document.createElement('canvas');
    return !!(c.getContext('webgl2') || c.getContext('webgl'));
  } catch {
    return false;
  }
}

interface Props {
  onSelect?: (id: string | null) => void;
  onReady?: (v: VCRViewer) => void;
  className?: string;
}

/** Mounts a VCRViewer and shows loading / error states over it. */
export const Viewer3D = forwardRef<VCRViewer | null, Props>(function Viewer3D({ onSelect, onReady, className }, ref) {
  const host = useRef<HTMLDivElement>(null);
  const [viewer, setViewer] = useState<VCRViewer | null>(null);
  const [progress, setProgress] = useState(0);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState('');
  const selectRef = useRef(onSelect);
  selectRef.current = onSelect;
  const readyRef = useRef(onReady);
  readyRef.current = onReady;

  useImperativeHandle(ref, () => viewer as VCRViewer, [viewer]);

  useEffect(() => {
    if (!host.current) return;
    if (!webglAvailable()) {
      setStatus('error');
      setError('Your browser or device does not support WebGL, so the 3D model cannot be shown. The component library and cycle pages still work.');
      return;
    }
    const v = new VCRViewer(host.current, {
      onSelect: (id) => selectRef.current?.(id),
      onProgress: setProgress,
      onLoad: () => {
        setStatus('ready');
        readyRef.current?.(v);
      },
      onError: (m) => {
        setStatus('error');
        setError(m);
      },
    });
    setViewer(v);
    v.load(MODEL_URL);
    return () => v.dispose();
  }, []);

  return (
    <div className={`viewer-host ${className ?? ''}`} ref={host} data-status={status}>
      {status === 'loading' && (
        <div className="viewer-state" role="status" aria-live="polite">
          <div className="spinner" aria-hidden="true" />
          <p>Loading 3D model… {progress > 0 ? `${Math.round(progress * 100)}%` : ''}</p>
        </div>
      )}
      {status === 'error' && (
        <div className="viewer-state viewer-error" role="alert">
          <strong>3D view unavailable</strong>
          <p>{error}</p>
          <button className="btn" onClick={() => location.reload()}>Try again</button>
        </div>
      )}
    </div>
  );
});
