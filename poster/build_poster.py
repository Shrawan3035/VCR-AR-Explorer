"""
Builds the four-sheet A3 landscape poster as HTML (poster/poster.html). print_poster.mjs then
prints it to PDF with headless Edge/Chrome and verify_poster.py checks the result.

    python poster/build_poster.py --url https://<user>.github.io/<repo>/ [--ar-url <url>#/ar]

Without --url the QR areas show a clearly marked placeholder (never a localhost/invented URL).
Inputs: data/*.json, poster/assets/*.png (+ *_anchors.json from web/scripts/capture.mjs).
"""
from __future__ import annotations

import argparse
import base64
import html
import io
import json
import math
import os

import qrcode
import qrcode.image.svg

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(HERE, "assets")
DATA = json.load(open(os.path.join(ROOT, "data", "components.json"), encoding="utf-8"))
COMPS = sorted(DATA["components"], key=lambda c: c["number"])
BYID = {c["id"]: c for c in COMPS}
FONT = "../web/node_modules/@fontsource"
E = html.escape


def asset(name):
    return f"assets/{name}"


def has(name):
    return os.path.exists(os.path.join(ASSETS, name))


def anchors(name):
    p = os.path.join(ASSETS, f"{name}_anchors.json")
    return json.load(open(p)) if os.path.exists(p) else None


def qr_svg(url: str, px_mm: float) -> str:
    q = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_H, border=0, box_size=10)
    q.add_data(url)
    q.make(fit=True)
    m = q.get_matrix()
    n = len(m)
    rects = "".join(f'<rect x="{x}" y="{y}" width="1.02" height="1.02"/>' for y, row in enumerate(m) for x, v in enumerate(row) if v)
    return f'<svg class="qr" viewBox="-1 -1 {n + 2} {n + 2}" style="width:{px_mm}mm;height:{px_mm}mm" shape-rendering="crispEdges"><rect x="-1" y="-1" width="{n + 2}" height="{n + 2}" fill="#fff"/><g fill="#13213a">{rects}</g></svg>'


def qr_or_placeholder(url, size_mm, label="WEB"):
    if url:
        return qr_svg(url, size_mm)
    return (f'<div class="qr-ph" style="width:{size_mm}mm;height:{size_mm}mm"><b>QR PLACEHOLDER</b>'
            f'<span>Site not yet deployed — regenerate with --url after deployment</span></div>')


# ----------------------------------------------------------------------------- callouts
def callout_svg(name, ids, push=80, w=1400, h=1000):
    """Numbered circles + leader lines placed around the projected component anchors."""
    a = anchors(name)
    if not a:
        return ""
    parts = a["parts"]
    cx, cy = w / 2, h * 0.52
    pts = []
    for cid in ids:
        p = parts.get(cid)
        if not p or not p["visible"]:
            continue
        ax, ay = p["anchor"] if not BYID[cid].get("line") else p["centre"]
        dx, dy = ax - cx, ay - cy - 120
        d = math.hypot(dx, dy) or 1
        lx, ly = ax + dx / d * push, ay + dy / d * push
        pts.append([cid, ax, ay, lx, ly])
    # simple repulsion so circles don't overlap
    for _ in range(80):
        for i in range(len(pts)):
            for j in range(i + 1, len(pts)):
                dx, dy = pts[j][3] - pts[i][3], pts[j][4] - pts[i][4]
                d = math.hypot(dx, dy)
                if d < 64:
                    f = (64 - d) / 2 / (d or 1)
                    pts[i][3] -= dx * f; pts[i][4] -= dy * f
                    pts[j][3] += dx * f; pts[j][4] += dy * f
        for p in pts:
            p[3] = min(max(p[3], 30), w - 30); p[4] = min(max(p[4], 30), h - 30)
    out = []
    for cid, ax, ay, lx, ly in pts:
        c = BYID[cid]
        cls = "co main" if c["main"] else "co"
        out.append(f'<g class="{cls}"><line x1="{ax:.0f}" y1="{ay:.0f}" x2="{lx:.0f}" y2="{ly:.0f}"/><circle cx="{ax:.0f}" cy="{ay:.0f}" r="5" class="dot"/>'
                   f'<circle cx="{lx:.0f}" cy="{ly:.0f}" r="24"/><text x="{lx:.0f}" y="{ly + 9:.0f}">{c["number"]}</text></g>')
    return f'<svg class="callouts" viewBox="0 0 {w} {h}" preserveAspectRatio="xMidYMid meet">{"".join(out)}</svg>'


def figure(img, label, ids, cls=""):
    name = img.replace(".png", "")
    if not has(img):
        return f'<div class="fig {cls}"><span class="fig-tag">{E(label)}</span><div class="missing">Render not generated yet</div></div>'
    return f'<div class="fig {cls}"><span class="fig-tag">{E(label)}</span><div class="fig-art"><img src="{asset(img)}"/>{callout_svg(name, ids)}</div></div>'


# ----------------------------------------------------------------------------- shared chrome
def frame(sheet, title_block, body, section, title, sub, qr_url):
    ticks_x = "".join(f'<span style="left:{(i + 0.5) * 12.5}%">{i + 1}</span>' for i in range(8))
    ticks_y = "".join(f'<span style="top:{(i + 0.5) * 100 / 6}%">{"ABCDEF"[i]}</span>' for i in range(6))
    qr = qr_or_placeholder(qr_url, 24)
    return f"""
<section class="sheet">
  <div class="zone zx top">{ticks_x}</div><div class="zone zx bottom">{ticks_x}</div>
  <div class="zone zy left">{ticks_y}</div><div class="zone zy right">{ticks_y}</div>
  <div class="inner">
    <header class="head">
      <div class="brandblock">
        <div class="drg">DRG. NO. VCR-001 · SHEET {sheet} / 4</div>
        <div class="logo">VCR<span>/</span>AR</div>
        <div class="inst">{E(DATA["labData"]["institution"]).upper()} · REFRIGERATION &amp; AIR CONDITIONING LAB</div>
      </div>
      <div class="titleblock">
        <div class="eyebrow">{E(section)}</div>
        <h1>{E(title)}</h1>
        <p class="sub">{E(sub)}</p>
      </div>
      <div class="qrmini"><div class="qrframe">{qr}</div><div><b>Explore it in 3D</b><span>Scan for the interactive web explorer and AR view.</span></div></div>
    </header>
    <main class="body">{body}</main>
    <footer class="foot">
      <p class="credit">{title_block["credit"]}</p>
      <table class="tb"><tr><td class="tb-t"><small>TITLE</small><b>{E(title_block["title"])}</b></td><td><small>SHEET</small>{sheet} / 4</td><td><small>SCALE</small>NTS</td></tr></table>
    </footer>
  </div>
</section>"""


# ----------------------------------------------------------------------------- sheets
def sheet1(url):
    ids = [c["id"] for c in COMPS]
    legend = "".join(
        f'<li class="{"main" if c["main"] else ""}"><span class="n">{c["number"]}</span><b>{E(c["name"])}</b>{" <em>illustrative</em>" if c.get("illustrative") else ""} · {E(c["function"].split(".")[0])}</li>'
        for c in COMPS)
    rep = json.load(open(os.path.join(ROOT, "model-generation", "output", "model_report.json")))
    lab = DATA["labData"]
    body = f"""
<div class="s1">
  {figure("render_assembled.png", "FIG. 1A · ASSEMBLED RIG", ids, "fa")}
  {figure("render_exploded.png", "FIG. 1B · EXPLODED VIEW", [c["id"] for c in COMPS if not c.get("line")], "fb")}
  <div class="card legend"><h3>Component legend</h3><ol>{legend}</ol>
    <p class="tiny">Numbers match the callouts in the web explorer, the Unity app and Sheets 2–4. Core refrigeration cycle parts are numbered 1–4.</p></div>
  <div class="card specs"><h3>Model data <span class="pill">verified</span></h3>
    <table class="kv">
      <tr><th>Model</th><td>Original procedural GLB (glTF 2.0), generated by <code>generate_vcr_glb.py</code></td></tr>
      <tr><th>Validation</th><td>Khronos glTF-Validator: 0 errors, 0 warnings</td></tr>
      <tr><th>Geometry</th><td>{rep["triangles"]:,} triangles · 12 named, selectable components</td></tr>
      <tr><th>Footprint</th><td>≈ 1.3 × 0.7 m base, ≈ 0.87 m tall (illustrative scale)</td></tr>
    </table>
    <h3 class="mt">Laboratory rig data <span class="pill ph">to be filled</span></h3>
    <table class="kv ph">
      <tr><th>Refrigerant</th><td>{E(lab["refrigerant"])}</td></tr>
      <tr><th>Compressor</th><td>{E(lab["compressorRating"])}</td></tr>
      <tr><th>Condensing p</th><td>{E(lab["condensingPressure"])}</td></tr>
      <tr><th>Evaporating p</th><td>{E(lab["evaporatingPressure"])}</td></tr>
      <tr><th>COP</th><td>{E(lab["copMeasured"])}</td></tr>
    </table>
    <p class="tiny">No values are invented: bracketed fields are placeholders for measurements from your own rig.</p>
  </div>
</div>"""
    return frame(1, {"title": "THE HARDWARE: VCR RIG, TAKEN APART", "credit": "3D model generated procedurally for this project (original geometry, not CAD, not a specific product). Items 5 and 11 are illustrative accessories. Renders from the project's three.js web viewer."},
                 body, "SECTION A · THE HARDWARE", "A refrigeration rig, taken apart",
                 "An original 3D model of a vapour compression refrigeration demonstration rig, exploded into its parts. Numbers on each drawing match the legend.", url)


def schematic_svg():
    blocks = [("condenser", 2, "Condenser", 190, 40, 220, 64), ("compressor", 1, "Compressor", 458, 188, 174, 84),
              ("evaporator", 4, "Evaporator", 190, 356, 220, 64), ("expansion_valve", 3, "Expansion device", 8, 188, 178, 84)]
    b = "".join(f'<g class="blk"><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8"/><circle cx="{x + 22}" cy="{y + h / 2}" r="13"/>'
                f'<text class="bn" x="{x + 22}" y="{y + h / 2 + 5}">{n}</text><text class="bt" x="{x + 44}" y="{y + h / 2 + 6}">{t}</text></g>' for _, n, t, x, y, w, h in blocks)
    pts = [(1, 545, 330, "LP vapour", "end"), (2, 545, 120, "HP hot vapour", "end"), (3, 95, 120, "HP liquid", "start"), (4, 95, 330, "LP liquid + vapour", "start")]
    p = "".join(f'<circle class="pt" cx="{x}" cy="{y}" r="11"/><text class="ptn" x="{x}" y="{y + 4}">{n}</text>'
                f'<text class="ptt" x="{x - 18 if a == "end" else x + 18}" y="{y + 4}" text-anchor="{a}">{t}</text>' for n, x, y, t, a in pts)
    return f"""<svg viewBox="0 0 640 470" class="schem">
<defs><marker id="ah" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="#d2402a"/></marker>
<marker id="al" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="#1592b0"/></marker>
<marker id="ak" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="#1b2537"/></marker></defs>
<rect x="4" y="4" width="632" height="226" rx="10" class="zhp"/><rect x="4" y="230" width="632" height="236" rx="10" class="zlp"/>
<text x="16" y="24" class="zl hp">HIGH-PRESSURE SIDE</text><text x="16" y="456" class="zl lp">LOW-PRESSURE SIDE</text>
<path d="M545 188 V72 H414" class="ln hp" marker-end="url(#ah)"/><path d="M190 72 H95 V184" class="ln hp" marker-end="url(#ah)"/>
<path d="M95 272 V388 H186" class="ln lp" marker-end="url(#al)"/><path d="M410 388 H545 V276" class="ln lp" marker-end="url(#al)"/>
{p}
<path d="M300 36 V8" class="ln hp thin" marker-end="url(#ah)"/><text x="312" y="20" class="ht hp">Q out · heat rejected to surroundings</text>
<path d="M300 452 V424" class="ln lp thin" marker-end="url(#al)"/><text x="312" y="446" class="ht lp">Q in · heat absorbed from cooled space</text>
<path d="M612 140 V182" class="ln ink" marker-end="url(#ak)"/><text x="606" y="134" class="ht">W in</text>
{b}</svg>"""


def ph_svg():
    # same construction as web/src/components/Diagrams.tsx (qualitative only)
    dome = [[(70, 300), (80, 150), (150, 52), (215, 50)], [(215, 50), (280, 52), (340, 150), (360, 300)]]

    def bez(c, t):
        u = 1 - t
        return [u ** 3 * c[0][k] + 3 * u * u * t * c[1][k] + 3 * u * t * t * c[2][k] + t ** 3 * c[3][k] for k in (0, 1)]

    def x_at(c, y):
        lo, hi = 0.0, 1.0
        rising = bez(c, 0)[1] > bez(c, 1)[1]
        for _ in range(40):
            m = (lo + hi) / 2
            ym = bez(c, m)[1]
            if (ym > y) if rising else (ym < y): lo = m
            else: hi = m
        return bez(c, (lo + hi) / 2)[0]

    yH, yL = 118, 246
    x3, x1 = x_at(dome[0], yH), x_at(dome[1], yL)
    x2 = x1 + 70
    d = lambda c: f"M{c[0][0]},{c[0][1]} C{c[1][0]},{c[1][1]} {c[2][0]},{c[2][1]} {c[3][0]},{c[3][1]}"
    return f"""<svg viewBox="0 0 520 360" class="ph">
<defs><marker id="pa" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="#b8682f"/></marker></defs>
<path d="M40 20 V320 H500" class="ax"/><text x="16" y="170" class="axt" transform="rotate(-90 16 170)" text-anchor="middle">Pressure p (log scale)</text>
<text x="240" y="346" class="axt" text-anchor="middle">Specific enthalpy h →</text>
<path d="{d(dome[0])} {d(dome[1]).replace("M", "L")}" class="dome"/>
<text x="96" y="296" class="dt">sat. liquid</text><text x="318" y="296" class="dt">sat. vapour</text>
<path d="M{x1:.1f} {yL} L{x2:.1f} {yH}" class="cy" marker-end="url(#pa)"/><path d="M{x2:.1f} {yH} L{x3:.1f} {yH}" class="cy" marker-end="url(#pa)"/>
<path d="M{x3:.1f} {yH} L{x3:.1f} {yL}" class="cy" marker-end="url(#pa)"/><path d="M{x3:.1f} {yL} L{x1:.1f} {yL}" class="cy" marker-end="url(#pa)"/>
<text x="{x1 + 8:.0f}" y="{yL + 18}" class="pn">1</text><text x="{x2 + 8:.0f}" y="{yH - 8}" class="pn">2</text>
<text x="{x3 - 16:.0f}" y="{yH - 8}" class="pn">3</text><text x="{x3 - 16:.0f}" y="{yL + 18}" class="pn">4</text>
<text x="{(x2 + x3) / 2:.0f}" y="{yH - 10}" class="pl" text-anchor="middle">2→3 condensation</text>
<text x="{(x1 + x3) / 2:.0f}" y="{yL + 20}" class="pl" text-anchor="middle">4→1 evaporation</text>
<text x="{x2 + 6:.0f}" y="{(yH + yL) / 2}" class="pl">1→2 compression</text>
<text x="{x3 + 6:.0f}" y="{(yH + yL) / 2}" class="pl">3→4 throttling</text>
<text x="500" y="34" class="note" text-anchor="end">SCHEMATIC · NOT TO SCALE · NO MEASURED DATA</text></svg>"""


def sheet2(url):
    stages = "".join(f"""<div class="card stage"><div class="sn">{s["id"]}</div><h3>{E(s["title"])}</h3><p>{E(s["text"])}</p>
<p class="ideal"><b>Ideal:</b> {E(s["ideal"])}</p><p class="real"><b>Real:</b> {E(s["real"])}</p></div>""" for s in DATA["stages"])
    flow_img = f'<img src="{asset("render_flow.png")}"/>' if has("render_flow.png") else ""
    body = f"""
<div class="s2">
  <div class="card sch"><span class="fig-tag">FIG. 2A · CYCLE SCHEMATIC</span>{schematic_svg()}
    <p class="tiny">Typical cycle: Evaporator → Compressor → Condenser → Expansion device → Evaporator. State points 1–4 match Fig. 2B.</p></div>
  <div class="s2r">
    <div class="card phc"><span class="fig-tag">FIG. 2B · p–h SKETCH (SCHEMATIC)</span>{ph_svg()}</div>
  </div>
  <div class="stages">{stages}</div>
  <div class="s2c3">
    <div class="card sides">
      <h3><span class="hp">■</span> High-pressure side</h3><p>Compressor outlet → condenser → liquid line → expansion device inlet. Refrigerant condenses here and <b>rejects heat</b> (Q out) to the surroundings.</p>
      <h3><span class="lp">■</span> Low-pressure side</h3><p>Expansion device outlet → evaporator → suction line → compressor inlet. Refrigerant boils here and <b>absorbs heat</b> (Q in) from the cooled space.</p>
      <p class="tiny">Energy balance (ideal, steady state): Q out = Q in + W in. COP = Q in ÷ W in — calculate it from your own readings.</p>
    </div>
  <div class="card flowc"><span class="fig-tag">FIG. 2C · CONCEPTUAL FLOW IN THE 3D MODEL</span>{flow_img}
    <p class="tiny"><span class="sw" style="background:#e0442a"></span>HP vapour <span class="sw" style="background:#f0a33c"></span>HP liquid <span class="sw" style="background:#5ccbe6"></span>LP mixture <span class="sw" style="background:#2f7fd0"></span>LP vapour — animation only, not CFD.</p></div></div>
</div>"""
    return frame(2, {"title": "THE REFRIGERATION CYCLE", "credit": "Cycle descriptions follow standard refrigeration textbooks (see Sheet 4). Diagrams are schematic and contain no measured laboratory data."},
                 body, "SECTION B · THE REFRIGERATION CYCLE", "One loop, two pressures",
                 "How refrigerant carries heat out of a cold space: it boils at low pressure in the evaporator and condenses at high pressure in the condenser.", url)


def shot(name, label, cls=""):
    if not has(name):
        return f'<div class="card shot {cls}"><span class="fig-tag">{E(label)}</span><div class="missing">Screenshot not captured</div></div>'
    return f'<div class="card shot {cls}"><span class="fig-tag">{E(label)}</span><img src="{asset(name)}"/></div>'


def sheet3(url):
    arch = """<svg viewBox="0 0 600 250" class="arch">
<defs><marker id="aa" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="#13213a"/></marker></defs>
<g class="ab"><rect x="10" y="20" width="150" height="60" rx="6"/><text x="85" y="45">generate_vcr_glb.py</text><text x="85" y="63" class="s">Python + numpy</text></g>
<g class="ab hl"><rect x="10" y="150" width="150" height="70" rx="6"/><text x="85" y="176">VCR_System.glb</text><text x="85" y="194" class="s">12 named nodes</text><text x="85" y="208" class="s">+ components / flow JSON</text></g>
<g class="ab"><rect x="240" y="10" width="160" height="62" rx="6"/><text x="320" y="35">Website</text><text x="320" y="53" class="s">React · TS · three.js</text></g>
<g class="ab"><rect x="240" y="94" width="160" height="62" rx="6"/><text x="320" y="119">WebAR</text><text x="320" y="137" class="s">model-viewer: WebXR / Quick Look</text></g>
<g class="ab"><rect x="240" y="178" width="160" height="62" rx="6"/><text x="320" y="203">Unity 6 (URP)</text><text x="320" y="221" class="s">glTFast · AR Foundation</text></g>
<g class="ab"><rect x="450" y="40" width="140" height="62" rx="6"/><text x="520" y="65">GitHub Pages</text><text x="520" y="83" class="s">QR → public URL</text></g>
<g class="ab"><rect x="450" y="160" width="140" height="62" rx="6"/><text x="520" y="185">A3 poster PDF</text><text x="520" y="203" class="s">HTML/SVG → PDF</text></g>
<path d="M85 80 V146" class="aw" marker-end="url(#aa)"/><path d="M160 175 L236 45" class="aw" marker-end="url(#aa)"/><path d="M160 185 L236 125" class="aw" marker-end="url(#aa)"/>
<path d="M160 200 L236 208" class="aw" marker-end="url(#aa)"/><path d="M400 45 L446 65" class="aw" marker-end="url(#aa)"/><path d="M400 125 L446 85" class="aw" marker-end="url(#aa)"/>
<path d="M160 160 C 300 260, 380 250, 446 195" class="aw dash" marker-end="url(#aa)"/></svg>"""
    unity = "unity_explorer.png" if has("unity_explorer.png") else None
    body = f"""
<div class="s3">
  <div class="card c1"><h2><span class="cn">C1</span>Objective &amp; motivation</h2>
    <p>Students meet the vapour compression cycle as a diagram, but the real rig in the lab is a tangle of copper. This project connects the two: every part of an original 3D rig can be <b>tapped to reveal its function</b>, the refrigerant route is animated stage by stage, and the same model can be placed on a real table in AR — all from one QR code, with nothing to install.</p>
    <ul><li>Identify the four core components and where they sit on a real rig.</li><li>Trace the high- and low-pressure sides and where heat is absorbed or rejected.</li><li>Relate the physical layout to the schematic and p–h sketch on Sheet 2.</li></ul></div>
  <div class="card c2"><h2><span class="cn">C2</span>Implementation architecture</h2>{arch}
    <p class="tiny">One shared data source (component text, explode offsets, flow route) drives the website, the Unity app and this poster, so all three always agree.</p></div>
  <div class="card c3"><h2><span class="cn">C3</span>How it works</h2>
    <dl>
      <dt>3D model</dt><dd>Procedural geometry (lathed shells, swept tubes, fin packs) written straight to glTF 2.0; validated with 0 errors.</dd>
      <dt>Web explorer</dt><dd>Tap/click picking that ignores drags, single-part highlight, ghost/isolate, explode slider, numbered callouts, cycle player.</dd>
      <dt>AR</dt><dd>Install-free WebAR: WebXR / Scene Viewer on ARCore Android, AR Quick Look on iOS; 3D fallback elsewhere. Camera is requested only when AR starts.</dd>
      <dt>Unity</dt><dd>Unity 6 + URP: MainMenu and VCRExplorer scenes, ScriptableObject/JSON data, AR Foundation scene for an optional Android build.</dd>
    </dl></div>
  <div class="shots">
    {shot("ui_explorer_desktop.png", "FIG. 3A · WEB EXPLORER (DESKTOP)", "wide")}
    {shot("ui_explorer_mobile.png", "FIG. 3B · PHONE", "tall")}
    {shot("ui_ar.png", "FIG. 3C · AR PAGE", "wide")}
    {shot(unity, "FIG. 3D · UNITY BUILD (WINDOWS PLAYER)", "wide") if unity else shot("ui_cycle.png", "FIG. 3D · CYCLE PAGE", "wide")}
  </div>
  <div class="card c4"><h2><span class="cn">C4</span>Applications &amp; limitations</h2>
    <div class="two"><div><h3>Use it for</h3><ul><li>Pre-lab briefing before running the real trainer</li><li>Self-study and revision on a phone</li><li>Explaining HVAC systems to non-specialists</li></ul></div>
    <div><h3>Keep in mind</h3><ul><li>Educational model — not CAD, not to scale of any product</li><li>Flow animation is conceptual, not CFD or a thermodynamic solver</li><li>No measured data shown; AR depends on the device</li></ul></div></div></div>
</div>"""
    return frame(3, {"title": "THE PROJECT: HOW IT IS BUILT", "credit": "Screenshots are captured from the actual web build and Unity player of this project."},
                 body, "SECTION C · THE PROJECT", "One model, three ways in",
                 "Objective, architecture and implementation of the VCR AR Explorer: one procedural model, three ways to learn from it.", url)


def sheet4(url, ar_url):
    lab = DATA["labData"]
    disp = lambda u: E(u.replace("https://", "")) if u else "[URL — available after deployment]"
    body = f"""
<div class="s4">
  <div class="card qrcard ar"><span class="band ar">AUGMENTED REALITY</span>
    <div class="qrbig">{qr_or_placeholder(ar_url, 98)}</div>
    <div class="qrtext"><div class="eyebrow">CODE 1 · AR</div><h2>The rig, in your room</h2>
      <p>Place the refrigeration rig on a real table at life size, then switch to the exploded view and walk around it.</p>
      <ol class="steps"><li><span>01</span>Scan, then tap <b>View in your space</b>.</li><li><span>02</span>Move the phone slowly over a table or floor.</li><li><span>03</span>Walk around it; switch to <b>Exploded</b>.</li></ol>
      <p class="tiny">Android: Chrome with Google Play Services for AR (ARCore). iPhone/iPad: Safari with AR Quick Look. Other devices get the 3D view. Camera permission is asked only when AR starts.</p>
      <p class="url"><small>OR TYPE</small>{disp(ar_url)}</p></div></div>
  <div class="card qrcard web"><span class="band web">WEB EXPLORER</span>
    <div class="qrbig">{qr_or_placeholder(url, 98)}</div>
    <div class="qrtext"><div class="eyebrow cy">CODE 2 · WEB</div><h2>The whole cycle, interactive</h2>
      <p>Rotate the rig in 3D, tap any of the 12 numbered parts, play the refrigeration cycle and read the component library.</p>
      <ol class="steps"><li><span>01</span>Scan to open the explorer.</li><li><span>02</span>Tap the compressor, condenser, valve or evaporator.</li><li><span>03</span>Press <b>Play cycle</b> or drag <b>Explode</b>.</li></ol>
      <p class="tiny">Works in modern browsers on phones, tablets and laptops. Nothing to install.</p>
      <p class="url"><small>OR TYPE</small>{disp(url)}</p></div></div>
  <div class="card info"><h3>Project</h3><table class="kv"><tr><th>Title</th><td>VCR AR Explorer — Vapour Compression Refrigeration in AR</td></tr>
    <tr><th>Authors</th><td>{E(lab["authors"])}</td></tr><tr><th>Guide</th><td>{E(lab["guide"])}</td></tr><tr><th>Institution</th><td>{E(lab["institution"])}</td></tr></table></div>
  <div class="card refs"><h3>References &amp; credits</h3><ul>
    <li>C. P. Arora, <i>Refrigeration and Air Conditioning</i>, McGraw-Hill.</li>
    <li>W. F. Stoecker &amp; J. W. Jones, <i>Refrigeration and Air Conditioning</i>, McGraw-Hill.</li>
    <li>Y. A. Çengel &amp; M. A. Boles, <i>Thermodynamics: An Engineering Approach</i>, McGraw-Hill.</li>
    <li>Software: three.js, Google &lt;model-viewer&gt;, React, Vite, Unity 6 (URP, glTFast, AR Foundation, ARCore), Khronos glTF-Validator. Fonts: Archivo, IBM Plex Mono (OFL).</li>
    <li>3D model: original, generated procedurally by this project — educational visualisation, not CAD.</li></ul></div>
</div>"""
    return frame(4, {"title": "SCAN · EXPLORE", "credit": ("Both codes use high error correction and were test-decoded from the rendered PDF before printing." if url else "QR codes will be generated from the verified public URL after deployment.")},
                 body, "SECTION D · ACCESS", "Scan · Explore",
                 "Two codes, two ways in. Open your phone camera and point it at either code. Nothing to install.", url)


CSS = f"""
@font-face {{ font-family: Archivo; font-weight: 400; src: url({FONT}/archivo/files/archivo-latin-400-normal.woff2); }}
@font-face {{ font-family: Archivo; font-weight: 600; src: url({FONT}/archivo/files/archivo-latin-600-normal.woff2); }}
@font-face {{ font-family: Archivo; font-weight: 800; src: url({FONT}/archivo/files/archivo-latin-800-normal.woff2); }}
@font-face {{ font-family: Plex; font-weight: 400; src: url({FONT}/ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2); }}
@font-face {{ font-family: Plex; font-weight: 600; src: url({FONT}/ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff2); }}
@page {{ size: 420mm 297mm; margin: 0; }}
* {{ box-sizing: border-box; }}
html, body {{ margin: 0; padding: 0; }}
body {{ font-family: Archivo, sans-serif; color: #1b2537; -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
.sheet {{ width: 420mm; height: 297mm; position: relative; overflow: hidden; page-break-after: always; break-after: page;
  background-color: #ece6d6; background-image: linear-gradient(rgba(19,33,58,.07) .25mm, transparent .25mm), linear-gradient(90deg, rgba(19,33,58,.07) .25mm, transparent .25mm);
  background-size: 7mm 7mm; }}
.sheet:last-child {{ page-break-after: auto; break-after: auto; }}
.zone {{ position: absolute; font: 400 2.6mm Plex, monospace; color: #6d7586; }}
.zx {{ left: 9mm; right: 9mm; height: 5mm; }} .zx.top {{ top: 2mm; }} .zx.bottom {{ bottom: 2mm; }}
.zx span {{ position: absolute; transform: translateX(-50%); }}
.zy {{ top: 9mm; bottom: 9mm; width: 5mm; }} .zy.left {{ left: 3mm; }} .zy.right {{ right: 2mm; }}
.zy span {{ position: absolute; transform: translateY(-50%); }}
.inner {{ position: absolute; inset: 8mm; border: .7mm solid #13213a; outline: .3mm solid #13213a; outline-offset: 1.4mm; padding: 6mm 9mm 4mm; display: flex; flex-direction: column; }}
.head {{ display: grid; grid-template-columns: 78mm 1fr 70mm; gap: 7mm; align-items: center; border-bottom: .6mm solid #13213a; padding-bottom: 4mm; }}
.drg {{ display: inline-block; background: #13213a; color: #fff; font: 600 2.6mm Plex, monospace; letter-spacing: .12em; padding: 1.2mm 2.5mm; }}
.logo {{ font: 800 15mm/1 Archivo; color: #13213a; letter-spacing: -.02em; text-shadow: -.9mm 0 0 #1592b0, .9mm 0 0 #d2402a; margin: 2mm 0 1mm; }}
.logo span {{ color: #b8682f; text-shadow: none; }}
.inst {{ font: 600 2.3mm Plex, monospace; letter-spacing: .14em; color: #13213a; }}
.titleblock {{ border-left: .4mm solid #13213a; padding-left: 7mm; }}
.eyebrow {{ font: 600 2.8mm Plex, monospace; letter-spacing: .14em; color: #d2402a; }}
.eyebrow.cy {{ color: #1592b0; }}
h1 {{ font: 800 13mm/1.02 Archivo; color: #13213a; margin: 1mm 0 1.5mm; letter-spacing: -.01em; }}
.sub {{ font-size: 3.5mm; margin: 0; color: #3a4660; max-width: 210mm; }}
.qrmini {{ display: flex; gap: 3mm; align-items: center; }}
.qrframe {{ padding: 2mm; background: #fff; border: .4mm solid #d2402a; }}
.qrmini b {{ display: block; font: 800 4.2mm Archivo; color: #13213a; }}
.qrmini span {{ font: 400 2.5mm Plex, monospace; color: #5d6677; }}
.qr-ph {{ display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center; border: .5mm dashed #d2402a; background: repeating-linear-gradient(45deg,#fff,#fff 2mm,#fbe9e5 2mm,#fbe9e5 4mm); color: #d2402a; font: 600 2.4mm Plex, monospace; padding: 2mm; }}
.qr-ph b {{ font-size: 3mm; }}
.body {{ flex: 1; padding-top: 5mm; min-height: 0; overflow: hidden; }}
.body > div {{ height: 100%; }}
.foot {{ display: flex; justify-content: space-between; align-items: flex-end; gap: 10mm; padding-top: 2mm; }}
.credit {{ font: 400 2.3mm/1.4 Plex, monospace; color: #5d6677; margin: 0; max-width: 250mm; }}
.tb {{ border-collapse: collapse; border: .5mm solid #13213a; background: #fbf9f3; font: 600 3.4mm Archivo; color: #13213a; }}
.tb td {{ border-left: .3mm solid #13213a; padding: 1.2mm 3mm; min-width: 22mm; }}
.tb td.tb-t {{ min-width: 85mm; border-left: 0; }}
.tb small {{ display: block; font: 400 2mm Plex, monospace; color: #5d6677; }}
.card {{ background: #fbf9f3; border: .4mm solid #13213a; padding: 4mm; position: relative; }}
.card h3 {{ font: 800 4.4mm Archivo; color: #13213a; margin: 0 0 2mm; }}
.card h2 {{ font: 800 5.6mm Archivo; color: #13213a; margin: 0 0 2.5mm; display: flex; align-items: center; gap: 2.5mm; }}
.cn {{ display: inline-grid; place-items: center; width: 8mm; height: 8mm; border: .5mm solid #13213a; border-radius: 50%; font: 600 3mm Plex; }}
.tiny {{ font: 400 2.4mm/1.45 Plex, monospace; color: #5d6677; margin: 1.5mm 0 0; }}
.fig {{ position: relative; border: .4mm solid #13213a; background: #fbf9f3; }}
.fig-tag {{ position: absolute; top: -.4mm; left: -.4mm; background: #fbf9f3; border: .4mm solid #13213a; font: 600 2.4mm Plex, monospace; letter-spacing: .08em; padding: 1mm 2mm; z-index: 2; color: #13213a; }}
.fig-art {{ position: absolute; inset: 0; }}
.fig-art img, .fig-art svg {{ position: absolute; inset: 0; width: 100%; height: 100%; object-fit: contain; }}
.callouts line {{ stroke: #13213a; stroke-width: 2.4; }}
.callouts circle {{ fill: #fbf9f3; stroke: #13213a; stroke-width: 3; }}
.callouts .main circle {{ stroke: #b8682f; stroke-width: 4; }}
.callouts .dot {{ fill: #13213a; }}
.callouts text {{ font: 600 26px Plex, monospace; text-anchor: middle; fill: #13213a; }}
.missing {{ display: grid; place-items: center; height: 100%; color: #d2402a; font: 600 3mm Plex; }}
code {{ font-family: Plex, monospace; font-size: .92em; }}
.pill {{ font: 600 2.2mm Plex, monospace; background: #1592b0; color: #fff; padding: .4mm 1.5mm; border-radius: 1mm; vertical-align: middle; letter-spacing: .06em; }}
.pill.ph {{ background: #d2402a; }}
.kv {{ width: 100%; border-collapse: collapse; font-size: 3mm; }}
.kv th {{ text-align: left; font: 600 2.4mm Plex, monospace; text-transform: uppercase; color: #5d6677; padding: 1.1mm 2mm 1.1mm 0; width: 30mm; vertical-align: top; letter-spacing: .05em; }}
.kv td {{ padding: 1.1mm 0; border-bottom: .2mm dashed #c9c2ae; }}
.kv.ph td {{ color: #d2402a; font-family: Plex, monospace; font-size: 2.7mm; }}
.mt {{ margin-top: 4mm !important; }}
/* sheet 1 */
.s1 {{ display: grid; grid-template-columns: 205mm 1fr; grid-template-rows: 124mm minmax(0, 1fr); gap: 5mm; height: 100%; }}
.s1 .fa {{ grid-column: 1; grid-row: 1; }} .s1 .fb {{ grid-column: 2; grid-row: 1; }}
.legend ol {{ list-style: none; padding: 0; margin: 0; columns: 2; column-gap: 7mm; font-size: 2.65mm; line-height: 1.28; }}
.legend li {{ break-inside: avoid; margin-bottom: 1.1mm; display: flex; gap: 1.8mm; align-items: baseline; }}
.legend li b {{ white-space: nowrap; }}
.legend .n {{ flex: none; display: inline-grid; place-items: center; width: 5.2mm; height: 5.2mm; border: .35mm solid #13213a; border-radius: 50%; font: 600 2.5mm Plex; }}
.legend li.main .n {{ border-color: #b8682f; color: #b8682f; border-width: .5mm; }}
.legend em {{ font-style: normal; font: 600 2.1mm Plex; color: #b8682f; }}
.specs .kv th {{ width: 27mm; }}
/* sheet 2 */
.s2 {{ display: grid; grid-template-columns: 158mm 1fr 92mm; grid-template-rows: 128mm minmax(0, 1fr); gap: 5mm; height: 100%; }}
.s2 .sch {{ grid-column: 1; grid-row: 1; padding-top: 7mm; }}
.s2r {{ grid-column: 2; grid-row: 1; display: grid; }}
.s2 .phc {{ padding-top: 7mm; }}
.s2c3 {{ grid-column: 3; grid-row: 1 / span 2; display: flex; flex-direction: column; gap: 5mm; min-height: 0; }}
.s2 .flowc {{ padding-top: 7mm; display: flex; flex-direction: column; flex: 1; min-height: 0; }}
.flowc img {{ width: 100%; flex: 1; object-fit: contain; min-height: 0; }}
.s2 .stages {{ grid-column: 1 / span 2; grid-row: 2; display: grid; grid-template-columns: repeat(4, 1fr); gap: 4mm; }}
.stage {{ font-size: 2.9mm; line-height: 1.38; padding: 3.5mm; }}
.stage p {{ margin: 0 0 1.4mm; }}
.stage .sn {{ position: absolute; top: -3mm; right: 4mm; width: 7mm; height: 7mm; border-radius: 50%; background: #13213a; color: #fff; display: grid; place-items: center; font: 600 3.4mm Plex; }}
.stage h3 {{ padding-right: 6mm; font-size: 3.9mm; margin-bottom: 1.2mm; }}
.ideal {{ border-left: .7mm solid #13213a; padding-left: 2mm; }} .real {{ border-left: .7mm solid #b8682f; padding-left: 2mm; }}
.sides p {{ font-size: 2.75mm; margin: 0 0 1.6mm; line-height: 1.35; }}
.sides h3 {{ font-size: 3.8mm; margin-bottom: 1mm; }}
.hp {{ color: #d2402a; }} .lp {{ color: #1592b0; }}
.sw {{ display: inline-block; width: 2.6mm; height: 2.6mm; margin: 0 .8mm 0 1.5mm; vertical-align: middle; }}
svg.schem, svg.ph {{ width: 100%; height: auto; display: block; }}
.zhp {{ fill: rgba(210,64,42,.06); stroke: rgba(210,64,42,.4); stroke-dasharray: 5 4; }}
.zlp {{ fill: rgba(21,146,176,.06); stroke: rgba(21,146,176,.4); stroke-dasharray: 5 4; }}
.zl {{ font: 600 11px Plex; letter-spacing: .12em; }} .zl.hp {{ fill: #d2402a; }} .zl.lp {{ fill: #1592b0; }}
.ln {{ fill: none; stroke-width: 4; }} .ln.hp {{ stroke: #d2402a; }} .ln.lp {{ stroke: #1592b0; }} .ln.thin {{ stroke-width: 3; }} .ln.ink {{ stroke: #1b2537; stroke-width: 2.5; }}
.ht {{ font: 600 12px Plex; fill: #1b2537; }} .ht.hp {{ fill: #d2402a; }} .ht.lp {{ fill: #1592b0; }}
.pt {{ fill: #fff; stroke: #1b2537; stroke-width: 1.5; }} .ptn {{ font: 600 12px Plex; text-anchor: middle; }} .ptt {{ font: 400 11px Plex; fill: #5d6677; }}
.blk rect {{ fill: #fbf9f3; stroke: #13213a; stroke-width: 2.2; }} .blk circle {{ fill: #fff; stroke: #b8682f; stroke-width: 2.4; }}
.bn {{ font: 600 13px Plex; text-anchor: middle; fill: #b8682f; }} .bt {{ font: 800 17px Archivo; fill: #13213a; }}
.ax {{ fill: none; stroke: #1b2537; stroke-width: 1.5; }} .axt {{ font: 400 12px Plex; }}
.dome {{ fill: rgba(21,146,176,.07); stroke: #13213a; stroke-width: 2; }} .dt {{ font: 400 10px Plex; fill: #5d6677; }}
.cy {{ stroke: #b8682f; stroke-width: 3; fill: none; }} .pn {{ font: 600 13px Plex; }} .pl {{ font: 400 10.5px Plex; }}
.note {{ font: 600 9.5px Plex; fill: #d2402a; letter-spacing: .06em; }}
/* sheet 3 */
.s3 {{ display: grid; grid-template-columns: 112mm 135mm 1fr; grid-template-rows: auto minmax(0, 1fr); gap: 5mm; height: 100%; }}
.s3 .c1 {{ grid-column: 1; grid-row: 1; font-size: 3.05mm; line-height: 1.42; }}
.s3 .c2 {{ grid-column: 2; grid-row: 1; }}
.s3 .c3 {{ grid-column: 3; grid-row: 1; font-size: 2.9mm; line-height: 1.38; }}
.s3 ul {{ padding-left: 4mm; margin: 1mm 0; }}
.s3 dl {{ margin: 0; }} .s3 dt {{ font: 600 2.5mm Plex; color: #b8682f; letter-spacing: .08em; text-transform: uppercase; margin-top: 1.6mm; }} .s3 dd {{ margin: .4mm 0 0; }}
.shots {{ grid-column: 1 / span 2; grid-row: 2; display: grid; grid-template-columns: 1fr 1fr 44mm; grid-template-rows: 1fr 1fr; gap: 4mm; min-height: 0; }}
.shot {{ padding: 6mm 2mm 2mm; display: flex; min-height: 0; }}
.shot img {{ width: 100%; height: 100%; object-fit: contain; min-height: 0; }}
.shot.tall {{ grid-column: 3; grid-row: 1 / span 2; }}
.s3 .c4 {{ grid-column: 3; grid-row: 2; font-size: 2.9mm; line-height: 1.38; }}
.two {{ display: grid; grid-template-columns: 1fr; gap: 2mm; }}
.two h3 {{ font-size: 3.6mm; margin: 1mm 0 .5mm; }}
svg.arch {{ width: 100%; height: auto; }}
.ab rect {{ fill: #fff; stroke: #13213a; stroke-width: 1.6; }} .ab.hl rect {{ fill: #13213a; }} .ab.hl text {{ fill: #fff; }}
.ab text {{ font: 800 13px Archivo; text-anchor: middle; fill: #13213a; }} .ab text.s {{ font: 400 9.5px Plex; fill: #5d6677; }} .ab.hl text.s {{ fill: #c8d3e6; }}
.aw {{ fill: none; stroke: #13213a; stroke-width: 1.6; }} .aw.dash {{ stroke-dasharray: 4 3; stroke: #b8682f; }}
/* sheet 4 */
.s4 {{ display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); grid-template-rows: minmax(0, 1fr) 44mm; gap: 6mm; height: 100%; }}
.qrcard {{ display: grid; grid-template-columns: 108mm minmax(0, 1fr); gap: 6mm; align-items: center; padding: 10mm 6mm 6mm; }}
.band {{ position: absolute; top: -3.5mm; left: 6mm; font: 600 2.8mm Plex; letter-spacing: .12em; color: #fff; padding: 1.2mm 3mm; }}
.band.ar {{ background: #d2402a; }} .band.web {{ background: #1592b0; }}
.qrbig {{ padding: 5mm; background: #fff; position: relative; justify-self: center; }}
.qrcard.ar .qrbig {{ outline: 1.2mm solid #d2402a; }} .qrcard.web .qrbig {{ outline: 1.2mm solid #1592b0; }}
.qrtext h2 {{ font: 800 9mm/1 Archivo; margin: 1mm 0 3mm; display: block; }}
.qrtext p {{ font-size: 3.2mm; line-height: 1.4; }}
.steps {{ list-style: none; padding: 0; margin: 3mm 0; display: grid; gap: 2mm; font-size: 3.1mm; }}
.steps li {{ position: relative; padding-left: 10mm; min-height: 5mm; }}
.steps span {{ position: absolute; left: 0; top: -.4mm; font: 600 2.6mm Plex; border: .35mm solid #d2402a; color: #d2402a; padding: .6mm 1.4mm; }}
.web .steps span {{ border-color: #1592b0; color: #1592b0; }}
.url {{ font: 600 3.1mm Plex; word-break: break-all; border-top: .3mm dashed #c9c2ae; padding-top: 2mm; }}
.url small {{ display: block; font-size: 2.2mm; color: #5d6677; }}
.info .kv td {{ font-size: 3.1mm; }}
.refs ul {{ margin: 0; padding-left: 4mm; font-size: 2.8mm; line-height: 1.4; }}
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="", help="verified public website URL (https)")
    ap.add_argument("--ar-url", default="", help="verified AR route URL (defaults to <url>#/ar)")
    a = ap.parse_args()
    url = a.url.strip()
    if url and not url.startswith("https://"):
        raise SystemExit("Refusing to encode a non-https URL in the QR code: " + url)
    if url and any(h in url for h in ("localhost", "127.0.0.1")):
        raise SystemExit("Refusing to encode a localhost URL")
    ar_url = a.ar_url.strip() or (url.rstrip("/") + "/#/ar" if url else "")
    doc = f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><title>VCR AR Explorer — A3 poster series</title><style>{CSS}</style></head>
<body>{sheet1(url)}{sheet2(url)}{sheet3(url)}{sheet4(url, ar_url)}</body></html>"""
    out = os.path.join(HERE, "poster.html")
    open(out, "w", encoding="utf-8").write(doc)
    json.dump({"url": url, "ar_url": ar_url}, open(os.path.join(HERE, "qr_targets.json"), "w"), indent=1)
    print("wrote", out, "| QR:", url or "PLACEHOLDER", "|", ar_url or "")


if __name__ == "__main__":
    main()
