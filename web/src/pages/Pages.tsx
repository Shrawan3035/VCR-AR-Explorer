import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { CycleSchematic, PhSketch } from '../components/Diagrams';
import { asset, components, PDF_URL, project, stages } from '../data';

const MAIN = components.filter((c) => c.main).sort((a, b) => a.number - b.number);

export function Home() {
  const nav = useNavigate();
  return (
    <div className="page home">
      <section className="hero">
        <div className="hero-text">
          <p className="eyebrow">Drg. VCR-001 · Augmented reality learning module</p>
          <h1 className="display">Vapour compression refrigeration, <span className="accent">taken apart</span></h1>
          <p className="lead">
            An original 3D model of a laboratory refrigeration rig you can rotate, explode and tap. Learn what the compressor, condenser, expansion device and evaporator each do —
            then place the rig on your own table in AR.
          </p>
          <div className="cta-row">
            <Link className="btn btn-primary" to="/explore">Launch 3D Explorer</Link>
            <Link className="btn btn-ar" to="/ar">▣ View in AR</Link>
            <a className="btn btn-ghost" href={PDF_URL} download>Download A3 poster (PDF)</a>
          </div>
          <p className="tiny muted">Works in modern browsers on phones, tablets and laptops. Nothing to install.</p>
        </div>
        <Link to="/explore" className="hero-img" aria-label="Open the 3D explorer">
          <img src={asset('img/hero.png')} alt="Rendered 3D model of the refrigeration rig with its compressor, finned coils, fans and copper pipework" loading="eager" />
        </Link>
      </section>

      <section className="section">
        <h2 className="section-title"><span className="sec-badge">A</span>The four parts that make cold</h2>
        <div className="grid4">
          {MAIN.map((c) => (
            <button key={c.id} className="card comp-card" onClick={() => nav(`/explore?c=${c.id}`)}>
              <span className="callout-num big">{c.number}</span>
              <h3>{c.name}</h3>
              <p>{c.function}</p>
              <p className="tag-line">{c.side}</p>
            </button>
          ))}
        </div>
        <p className="muted small">The typical cycle: Evaporator → Compressor → Condenser → Expansion device → back to the Evaporator.</p>
      </section>

      <section className="section two-col">
        <div>
          <h2 className="section-title"><span className="sec-badge">B</span>One loop, two pressures</h2>
          <p>
            The compressor and the expansion device split the circuit into a <strong className="hp">high-pressure side</strong> (where heat is rejected in the condenser) and a{' '}
            <strong className="lp">low-pressure side</strong> (where heat is absorbed in the evaporator). Tap a block to jump to it in 3D.
          </p>
          <Link className="btn btn-ghost" to="/cycle">Step through the cycle →</Link>
        </div>
        <CycleSchematic onPick={(id) => nav(`/explore?c=${id}`)} />
      </section>

      <section className="section qr-help">
        <h2 className="section-title"><span className="sec-badge">C</span>Scanned the QR code?</h2>
        <ol className="steps row">
          <li><span>01</span>You are already here — this site is the experience. No app to install.</li>
          <li><span>02</span>Open the <Link to="/explore">3D Explorer</Link> and tap any numbered part.</li>
          <li><span>03</span>On a compatible phone, open <Link to="/ar">AR</Link> to place the rig on a table.</li>
        </ol>
      </section>
    </div>
  );
}

export function ComponentLibrary() {
  const [filter, setFilter] = useState<'all' | 'main'>('all');
  const list = [...components].sort((a, b) => a.number - b.number).filter((c) => filter === 'all' || c.main);
  return (
    <div className="page">
      <header className="page-head">
        <p className="eyebrow">Section B · Component library</p>
        <h1>Every part, numbered</h1>
        <p className="lead">Numbers match the callouts in the 3D explorer and on the poster.</p>
        <div className="seg" role="group" aria-label="Filter components">
          <button className={filter === 'all' ? 'on' : ''} aria-pressed={filter === 'all'} onClick={() => setFilter('all')}>All 12</button>
          <button className={filter === 'main' ? 'on' : ''} aria-pressed={filter === 'main'} onClick={() => setFilter('main')}>Core four</button>
        </div>
      </header>
      <div className="lib-grid">
        {list.map((c) => (
          <article key={c.id} className="card lib-card" id={c.id}>
            <header>
              <span className="callout-num big">{c.number}</span>
              <div>
                <h2 className="h-small">{c.name}</h2>
                <p className="muted small">{c.subtitle}</p>
              </div>
            </header>
            <p className="tag-line">{c.side}{c.illustrative ? ' · illustrative' : ''}</p>
            <p><strong>Function.</strong> {c.function}</p>
            <p><strong>Principle.</strong> {c.principle}</p>
            <p className="small"><strong>In:</strong> {c.inlet}<br /><strong>Out:</strong> {c.outlet}</p>
            <p className="small"><strong>Role:</strong> {c.cycleRole}</p>
            <p className="small"><strong>Applications:</strong> {c.application}</p>
            <p className="student-note small">{c.student}</p>
            <Link className="btn btn-small" to={`/explore?c=${c.id}`}>Show in 3D</Link>
          </article>
        ))}
      </div>
    </div>
  );
}

export function Cycle() {
  const [i, setI] = useState(0);
  const s = stages[i];
  const order = ['evaporator', 'compressor', 'condenser', 'expansion_valve'];
  return (
    <div className="page">
      <header className="page-head">
        <p className="eyebrow">Section C · The refrigeration cycle</p>
        <h1>Four steps around the loop</h1>
        <p className="lead">{project.cycleOrder.map((id) => components.find((c) => c.id === id)?.name).join(' → ')} → back to the start.</p>
      </header>
      <div className="cycle-layout">
        <div className="card">
          <CycleSchematic active={s.component} onPick={(id) => setI(order.indexOf(id))} />
        </div>
        <div className="card stage-detail" aria-live="polite">
          <div className="stepper" role="tablist" aria-label="Cycle stages">
            {stages.map((st, k) => (
              <button key={st.id} role="tab" aria-selected={k === i} className={k === i ? 'on' : ''} onClick={() => setI(k)}>{st.id}</button>
            ))}
          </div>
          <p className="eyebrow">Stage {s.id} of 4</p>
          <h2>{s.title}</h2>
          <p>{s.text}</p>
          <p className="ideal"><strong>Idealised cycle:</strong> {s.ideal}</p>
          <p className="real"><strong>Real system:</strong> {s.real}</p>
          <div className="row-btns">
            <button className="btn btn-ghost" onClick={() => setI((i + 3) % 4)}>← Previous</button>
            <Link className="btn" to={`/explore?c=${s.component}`}>See it in 3D</Link>
            <button className="btn btn-ghost" onClick={() => setI((i + 1) % 4)}>Next →</button>
          </div>
        </div>
      </div>
      <section className="section two-col">
        <div>
          <h2 className="section-title">Pressure–enthalpy view (schematic)</h2>
          <p>
            Engineers plot the cycle on a pressure–enthalpy (p–h) chart. The sketch shows the <em>shape</em> of the ideal cycle only: it is not to scale, uses no refrigerant property data and
            contains no measured values. Points 1–4 match the schematic above.
          </p>
          <p className="small muted">
            Real cycles differ: vapour leaving the evaporator is usually superheated, liquid leaving the condenser is often subcooled, and pressure drops occur in the heat exchangers and pipes.
          </p>
          <p className="small"><strong>Coefficient of performance (definition):</strong> COP = refrigerating effect ÷ compressor work input. Calculate it from your own laboratory readings.</p>
        </div>
        <div className="card"><PhSketch /></div>
      </section>
    </div>
  );
}

export function About() {
  const lab = project.labData;
  return (
    <div className="page prose">
      <header className="page-head">
        <p className="eyebrow">Section D · The project</p>
        <h1>Project overview &amp; methodology</h1>
      </header>
      <h2>Objective</h2>
      <p>
        Make the vapour compression refrigeration cycle easier to understand by letting students explore a 3D, tappable model of a lab rig on any device, with optional augmented reality, from
        a single QR code on a printed poster.
      </p>
      <h2>Method</h2>
      <ol>
        <li><strong>Procedural model.</strong> A Python script builds the rig from primitives (lathed shells, swept tubes, fin packs) and writes a validated glTF 2.0 binary (GLB) with one named node per component. No downloaded models are used.</li>
        <li><strong>Shared data.</strong> Component text, cycle stages, explode offsets and the conceptual flow route live in JSON files used by the website, the Unity project and the poster.</li>
        <li><strong>Web experience.</strong> React + TypeScript + three.js viewer with picking, highlighting, ghost/isolate, exploded view, numbered callouts and a cycle player.</li>
        <li><strong>WebAR.</strong> Google's &lt;model-viewer&gt; launches WebXR / Scene Viewer on Android and AR Quick Look on iOS — no native app needed.</li>
        <li><strong>Unity project.</strong> A Unity 6 (URP) project with MainMenu and VCRExplorer scenes, ScriptableObject/JSON data and an AR Foundation scene for an optional native Android build.</li>
        <li><strong>Poster.</strong> Four A3 landscape sheets generated from HTML/SVG with real renders and decoded-and-checked QR codes.</li>
      </ol>
      <h2>Laboratory data (placeholders)</h2>
      <p className="muted small">{lab.note}</p>
      <table className="tbl">
        <tbody>
          {Object.entries(lab).filter(([k]) => k !== 'note').map(([k, v]) => (
            <tr key={k}><th>{k.replace(/([A-Z])/g, ' $1').toLowerCase()}</th><td>{v}</td></tr>
          ))}
        </tbody>
      </table>
      <h2>Limitations</h2>
      <ul>
        <li>{project.disclaimer}</li>
        <li>Flow arrows are a conceptual animation, not a CFD or thermodynamic simulation; colours indicate the general refrigerant state only.</li>
        <li>No pressures, temperatures, refrigerant type or COP values are shown because none were measured for this model.</li>
        <li>AR availability depends on the device and browser; the 3D view works without AR.</li>
      </ul>
      <p><Link className="btn btn-ghost" to="/references">References &amp; credits →</Link></p>
    </div>
  );
}

export function References() {
  return (
    <div className="page prose">
      <header className="page-head">
        <p className="eyebrow">Section E · References &amp; credits</p>
        <h1>Sources and credits</h1>
      </header>
      <h2>Background reading (standard textbooks)</h2>
      <ul>
        <li>C. P. Arora, <em>Refrigeration and Air Conditioning</em>, McGraw-Hill.</li>
        <li>W. F. Stoecker &amp; J. W. Jones, <em>Refrigeration and Air Conditioning</em>, McGraw-Hill.</li>
        <li>Y. A. Çengel &amp; M. A. Boles, <em>Thermodynamics: An Engineering Approach</em>, McGraw-Hill — chapter on refrigeration cycles.</li>
        <li>ASHRAE Handbook — Fundamentals and Refrigeration volumes.</li>
      </ul>
      <h2>Software</h2>
      <ul>
        <li>three.js (MIT) — WebGL rendering.</li>
        <li>&lt;model-viewer&gt; by Google (Apache-2.0) — WebXR / Scene Viewer / Quick Look AR.</li>
        <li>React, React Router, Vite, TypeScript (MIT).</li>
        <li>Khronos glTF-Validator (Apache-2.0) — model validation.</li>
        <li>Unity 6, Universal Render Pipeline, glTFast, AR Foundation and ARCore XR Plug-in (Unity Companion License).</li>
        <li>Archivo and IBM Plex Mono fonts (SIL Open Font License) via Fontsource.</li>
      </ul>
      <h2>3D model</h2>
      <p>
        The VCR rig model is original and was generated procedurally by this project's script (<code>model-generation/generate_vcr_glb.py</code>). It is a schematic educational
        visualisation, not a manufacturer's CAD model. The receiver/drier, sight glass, service port and pressure gauges are illustrative accessories.
      </p>
      <h2>Authors</h2>
      <p>{project.labData.authors} · {project.labData.guide} · {project.labData.institution}</p>
    </div>
  );
}
