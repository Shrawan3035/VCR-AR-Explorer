import { useEffect, useState } from 'react';
import { HashRouter, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { Explorer } from './pages/Explorer';
import { ARPage } from './pages/ARPage';
import { About, ComponentLibrary, Cycle, Home, References } from './pages/Pages';
import { PDF_URL } from './data';

const NAV = [
  ['/', 'Home'],
  ['/explore', '3D Explorer'],
  ['/components', 'Components'],
  ['/cycle', 'Cycle'],
  ['/exploded', 'Exploded'],
  ['/about', 'Project'],
  ['/references', 'References'],
] as const;

function Shell() {
  const loc = useLocation();
  const [menu, setMenu] = useState(false);
  const [large, setLarge] = useState(() => {
    try {
      return localStorage.getItem('vcr-large-text') === '1';
    } catch {
      return false;
    }
  });
  useEffect(() => setMenu(false), [loc.pathname]);
  useEffect(() => {
    document.documentElement.classList.toggle('large-text', large);
    try {
      localStorage.setItem('vcr-large-text', large ? '1' : '0');
    } catch {
      /* storage unavailable */
    }
  }, [large]);
  const fullBleed = loc.pathname === '/explore' || loc.pathname === '/exploded';

  return (
    <div className={`app ${fullBleed ? 'full' : ''}`}>
      <a href="#main" className="skip">Skip to content</a>
      <header className="topbar">
        <NavLink to="/" className="brand" aria-label="VCR AR Explorer home">
          <span className="brand-mark">VCR</span>
          <span className="brand-text">AR Explorer</span>
        </NavLink>
        <button className="icon-btn menu-btn" aria-expanded={menu} aria-controls="nav" onClick={() => setMenu(!menu)} aria-label="Menu">☰</button>
        <nav id="nav" className={menu ? 'open' : ''} aria-label="Main">
          {NAV.map(([to, label]) => (
            <NavLink key={to} to={to} end={to === '/'}>{label}</NavLink>
          ))}
          <NavLink to="/ar" className="nav-ar">▣ AR</NavLink>
          <a href={PDF_URL} download className="nav-pdf">PDF</a>
          <button className="icon-btn" aria-pressed={large} onClick={() => setLarge(!large)} title="Larger text" aria-label="Toggle larger text">A±</button>
        </nav>
      </header>
      <main id="main">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/explore" element={<Explorer key="explore" />} />
          <Route path="/exploded" element={<Explorer key="exploded" initialExplode={0.85} heading="Exploded view" />} />
          <Route path="/components" element={<ComponentLibrary />} />
          <Route path="/cycle" element={<Cycle />} />
          <Route path="/about" element={<About />} />
          <Route path="/references" element={<References />} />
          <Route path="/ar" element={<ARPage />} />
          <Route path="*" element={<div className="page"><h1>Page not found</h1><NavLink to="/">Go home</NavLink></div>} />
        </Routes>
      </main>
      {!fullBleed && (
        <footer className="footer">
          <p>VCR AR Explorer · Educational visualisation, not CAD · Original procedurally generated model</p>
          <p className="mono">DRG. VCR-001 · SCALE NTS</p>
        </footer>
      )}
    </div>
  );
}

export default function App() {
  return (
    <HashRouter>
      <Shell />
    </HashRouter>
  );
}
