import { useEffect, useRef } from 'react';
import type { ComponentInfo } from '../data';

interface Props {
  info: ComponentInfo;
  onClose: () => void;
  onPrev: () => void;
  onNext: () => void;
  onFocus?: () => void;
}

export function InfoPanel({ info, onClose, onPrev, onNext, onFocus }: Props) {
  const closeBtn = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    closeBtn.current?.focus({ preventScroll: true });
  }, [info.id]);

  const side = info.side.toLowerCase();
  const sideClass = side.includes('high') && side.includes('low') ? 'tag-both' : side.includes('high') ? 'tag-hp' : side.includes('low') ? 'tag-lp' : 'tag-neutral';

  return (
    <section className="info-panel" aria-labelledby="info-title" role="region">
      <header className="info-head">
        <span className="info-num" aria-hidden="true">{info.number}</span>
        <div>
          <p className="eyebrow">Component {String(info.number).padStart(2, '0')}{info.main ? ' · core cycle' : ''}{info.illustrative ? ' · illustrative' : ''}</p>
          <h2 id="info-title">{info.name}</h2>
          <p className="info-sub">{info.subtitle}</p>
        </div>
        <button ref={closeBtn} className="icon-btn" onClick={onClose} aria-label="Close information panel">✕</button>
      </header>
      <div className="info-body">
        <p className={`tag ${sideClass}`}>{info.side}</p>
        {info.heat && <p className={`tag ${info.heat === 'rejected' ? 'tag-hp' : 'tag-lp'}`}>Heat {info.heat}</p>}
        <dl className="info-dl">
          <dt>Function</dt>
          <dd>{info.function}</dd>
          <dt>Working principle</dt>
          <dd>{info.principle}</dd>
          <dt>Refrigerant in</dt>
          <dd>{info.inlet}</dd>
          <dt>Refrigerant out</dt>
          <dd>{info.outlet}</dd>
          <dt>Role in the cycle</dt>
          <dd>{info.cycleRole}</dd>
          <dt>Where you'll see it</dt>
          <dd>{info.application}</dd>
        </dl>
        <p className="student-note"><strong>In simple words:</strong> {info.student}</p>
      </div>
      <footer className="info-foot">
        <button className="btn btn-ghost" onClick={onPrev} aria-label="Previous component">← Prev</button>
        {onFocus && <button className="btn btn-ghost" onClick={onFocus}>Zoom to part</button>}
        <button className="btn btn-ghost" onClick={onNext} aria-label="Next component">Next →</button>
      </footer>
    </section>
  );
}
