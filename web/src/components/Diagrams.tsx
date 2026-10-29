/** Vector diagrams of the VCR cycle: block schematic and a qualitative p–h sketch. */

interface SchematicProps {
  active?: string | null;
  onPick?: (id: string) => void;
}

const BLOCKS = [
  { id: 'condenser', n: 2, label: 'Condenser', x: 190, y: 40, w: 220, h: 64 },
  { id: 'compressor', n: 1, label: 'Compressor', x: 458, y: 188, w: 174, h: 84 },
  { id: 'evaporator', n: 4, label: 'Evaporator', x: 190, y: 356, w: 220, h: 64 },
  { id: 'expansion_valve', n: 3, label: 'Expansion device', x: 8, y: 188, w: 178, h: 84 },
];

export function CycleSchematic({ active, onPick }: SchematicProps) {
  return (
    <svg className="diagram" viewBox="0 0 640 470" role="img" aria-labelledby="sch-title sch-desc">
      <title id="sch-title">Vapour compression refrigeration cycle schematic</title>
      <desc id="sch-desc">Refrigerant flows from the evaporator to the compressor, condenser and expansion device, then back to the evaporator. Heat is absorbed at the evaporator and rejected at the condenser.</desc>
      <defs>
        <marker id="arr-hp" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--red)" />
        </marker>
        <marker id="arr-lp" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--cyan)" />
        </marker>
        <marker id="arr-ink" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--ink)" />
        </marker>
      </defs>
      {/* pressure side zones */}
      <rect x="4" y="4" width="632" height="226" rx="10" className="zone-hp" />
      <rect x="4" y="230" width="632" height="236" rx="10" className="zone-lp" />
      <text x="16" y="24" className="zone-label hp">HIGH-PRESSURE SIDE</text>
      <text x="16" y="456" className="zone-label lp">LOW-PRESSURE SIDE</text>

      {/* refrigerant lines */}
      <path d="M545 188 V72 H414" className="line hp" markerEnd="url(#arr-hp)" />
      <path d="M190 72 H95 V184" className="line hp" markerEnd="url(#arr-hp)" />
      <path d="M95 272 V388 H186" className="line lp" markerEnd="url(#arr-lp)" />
      <path d="M410 388 H545 V276" className="line lp" markerEnd="url(#arr-lp)" />

      {/* state points */}
      {[
        { n: 1, x: 545, y: 330, t: 'LP vapour' },
        { n: 2, x: 545, y: 120, t: 'HP hot vapour' },
        { n: 3, x: 95, y: 120, t: 'HP liquid' },
        { n: 4, x: 95, y: 330, t: 'LP liquid + vapour' },
      ].map((p) => (
        <g key={p.n}>
          <circle cx={p.x} cy={p.y} r="11" className="pt" />
          <text x={p.x} y={p.y + 4} className="pt-n">{p.n}</text>
          <text x={p.n === 1 || p.n === 2 ? p.x - 18 : p.x + 18} y={p.y + 4} className="pt-t" textAnchor={p.n === 1 || p.n === 2 ? 'end' : 'start'}>{p.t}</text>
        </g>
      ))}

      {/* heat & work arrows */}
      <path d="M300 36 V8" className="heat out" markerEnd="url(#arr-hp)" />
      <text x="312" y="20" className="heat-t hp">Q out · heat rejected to surroundings</text>
      <path d="M300 452 V424" className="heat in" markerEnd="url(#arr-lp)" />
      <text x="312" y="446" className="heat-t lp">Q in · heat absorbed from cooled space</text>
      <path d="M612 140 V182" className="line ink" markerEnd="url(#arr-ink)" />
      <text x="606" y="134" className="heat-t">W in</text>

      {BLOCKS.map((b) => (
        <g
          key={b.id}
          className={`blk ${active === b.id ? 'active' : ''} ${onPick ? 'clickable' : ''}`}
          onClick={() => onPick?.(b.id)}
          role={onPick ? 'button' : undefined}
          tabIndex={onPick ? 0 : undefined}
          onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && onPick?.(b.id)}
          aria-label={onPick ? `${b.n}. ${b.label}` : undefined}
        >
          <rect x={b.x} y={b.y} width={b.w} height={b.h} rx="8" />
          <circle cx={b.x + 22} cy={b.y + b.h / 2} r="13" className="blk-num" />
          <text x={b.x + 22} y={b.y + b.h / 2 + 5} className="blk-n">{b.n}</text>
          <text x={b.x + 44} y={b.y + b.h / 2 + 5} className="blk-t">{b.label}</text>
        </g>
      ))}
    </svg>
  );
}

// ----------------------------------------------------------------- p-h sketch
type P = [number, number];
const DOME: P[][] = [
  [[70, 300], [80, 150], [150, 52], [215, 50]],
  [[215, 50], [280, 52], [340, 150], [360, 300]],
];
const bez = (c: P[], t: number): P => {
  const u = 1 - t;
  return [0, 1].map((k) => u * u * u * c[0][k] + 3 * u * u * t * c[1][k] + 3 * u * t * t * c[2][k] + t * t * t * c[3][k]) as P;
};
const xAtY = (c: P[], y: number) => {
  let lo = 0,
    hi = 1;
  const rising = bez(c, 0)[1] > bez(c, 1)[1];
  for (let i = 0; i < 40; i++) {
    const m = (lo + hi) / 2;
    const ym = bez(c, m)[1];
    if (rising ? ym > y : ym < y) lo = m;
    else hi = m;
  }
  return bez(c, (lo + hi) / 2)[0];
};

export function PhSketch() {
  const yH = 118,
    yL = 246;
  const x3 = xAtY(DOME[0], yH);
  const x1 = xAtY(DOME[1], yL);
  const x2 = x1 + 70;
  const d = (c: P[]) => `M${c[0]} C${c[1]} ${c[2]} ${c[3]}`;
  return (
    <svg className="diagram" viewBox="0 0 520 360" role="img" aria-labelledby="ph-title ph-desc">
      <title id="ph-title">Schematic pressure–enthalpy diagram of the ideal VCR cycle</title>
      <desc id="ph-desc">Qualitative sketch only, not to scale and not based on measured data.</desc>
      <defs>
        <marker id="ph-arr" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--copper)" />
        </marker>
      </defs>
      <path d="M40 20 V320 H500" className="axis" />
      <text x="16" y="170" className="ax-t" transform="rotate(-90 16 170)">Pressure p (log scale)</text>
      <text x="240" y="346" className="ax-t" textAnchor="middle">Specific enthalpy h →</text>
      <path d={d(DOME[0]) + ' ' + d(DOME[1]).replace('M', 'L')} className="dome" />
      <text x="96" y="296" className="dome-t">sat. liquid</text>
      <text x="318" y="296" className="dome-t">sat. vapour</text>
      <path d={`M${x1} ${yL} L${x2} ${yH}`} className="cyc" markerEnd="url(#ph-arr)" />
      <path d={`M${x2} ${yH} L${x3} ${yH}`} className="cyc" markerEnd="url(#ph-arr)" />
      <path d={`M${x3} ${yH} L${x3} ${yL}`} className="cyc" markerEnd="url(#ph-arr)" />
      <path d={`M${x3} ${yL} L${x1} ${yL}`} className="cyc" markerEnd="url(#ph-arr)" />
      {[
        [x1, yL, '1', 8, 18],
        [x2, yH, '2', 8, -8],
        [x3, yH, '3', -16, -8],
        [x3, yL, '4', -16, 18],
      ].map(([x, y, n, dx, dy]) => (
        <g key={n as string}>
          <circle cx={x as number} cy={y as number} r="4.5" className="ph-pt" />
          <text x={(x as number) + (dx as number)} y={(y as number) + (dy as number)} className="ph-n">{n}</text>
        </g>
      ))}
      <text x={(x2 + x3) / 2} y={yH - 10} className="ph-l" textAnchor="middle">2→3 condensation</text>
      <text x={(x1 + x3) / 2} y={yL + 20} className="ph-l" textAnchor="middle">4→1 evaporation</text>
      <text x={x2 + 6} y={(yH + yL) / 2} className="ph-l">1→2 compression</text>
      <text x={x3 + 6} y={(yH + yL) / 2} className="ph-l">3→4 throttling</text>
      <text x="500" y="34" className="ph-note" textAnchor="end">SCHEMATIC · NOT TO SCALE · NO MEASURED DATA</text>
    </svg>
  );
}
