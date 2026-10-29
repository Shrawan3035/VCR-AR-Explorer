// Renders poster images and implementation screenshots from the real web viewer with a
// headless Chromium-based browser (Edge or Chrome already installed on the machine).
//
//   node scripts/capture.mjs [baseUrl]        (default http://localhost:5173/)
//
// Outputs: ../poster/assets/*.png (+ *_anchors.json for vector callouts) and public/img/hero.png
import puppeteer from 'puppeteer-core';
import { existsSync, mkdirSync, writeFileSync, copyFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const base = (process.argv[2] ?? 'http://localhost:5173/').replace(/\/?$/, '/');
const out = path.join(here, '../../poster/assets');
mkdirSync(out, { recursive: true });
mkdirSync(path.join(here, '../public/img'), { recursive: true });

const browsers = [
  process.env.BROWSER_PATH,
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/google-chrome',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
].filter(Boolean);
const executablePath = browsers.find((p) => existsSync(p));
if (!executablePath) throw new Error('No Chrome/Edge found; set BROWSER_PATH');

const browser = await puppeteer.launch({ executablePath, headless: true, args: ['--use-angle=d3d11', '--enable-webgl', '--ignore-gpu-blocklist'] });

async function render({ name, route, w, h, scale = 2, transparent = true, wait = 2500, anchors = false, after }) {
  const page = await browser.newPage();
  await page.setViewport({ width: w, height: h, deviceScaleFactor: scale, isMobile: w < 600, hasTouch: w < 600 });
  page.on('pageerror', (e) => console.error(`[${name}] page error:`, e.message));
  await page.goto(base + route, { waitUntil: 'networkidle0', timeout: 60000 });
  if (anchors) {
    await page.waitForFunction('window.__vcrShot', { timeout: 30000 });
    const a = await page.evaluate('window.__vcrShot.anchors');
    writeFileSync(path.join(out, `${name}_anchors.json`), JSON.stringify(a, null, 1));
  }
  if (after) await after(page);
  await new Promise((r) => setTimeout(r, wait));
  const file = path.join(out, `${name}.png`);
  await page.screenshot({ path: file, omitBackground: transparent });
  console.log('wrote', path.relative(process.cwd(), file));
  await page.close();
  return file;
}

const CAM = '1.05,0.95,1.75,0,0.40,0';
const shots = [
  { name: 'render_assembled', route: `#/explore?shot=1&labels=0&flow=0&cam=${CAM}`, w: 1400, h: 1000, anchors: true },
  { name: 'render_exploded', route: `#/explore?shot=1&labels=0&flow=0&explode=1&cam=1.25,1.05,2.2,0,0.42,0.05`, w: 1400, h: 1000, anchors: true },
  { name: 'render_flow', route: `#/explore?shot=1&labels=0&flow=1&cam=0.9,0.8,1.5,0,0.42,0`, w: 1400, h: 1000, anchors: true },
  { name: 'render_front', route: `#/explore?shot=1&labels=0&flow=0&cam=0,0.55,1.9,0,0.42,0`, w: 1400, h: 1000, anchors: true },
  { name: 'ui_explorer_desktop', route: '#/explore?c=compressor', w: 1440, h: 900, scale: 1.5, transparent: false, wait: 3500 },
  { name: 'ui_explorer_mobile', route: '#/explore?c=condenser', w: 390, h: 844, scale: 2, transparent: false, wait: 3500 },
  { name: 'ui_cycle', route: '#/cycle', w: 1440, h: 900, scale: 1.5, transparent: false },
  { name: 'ui_ar', route: '#/ar', w: 1440, h: 900, scale: 1.5, transparent: false, wait: 5000 },
  { name: 'ui_home', route: '#/', w: 1440, h: 900, scale: 1.5, transparent: false },
];

const only = process.env.ONLY?.split(',');
for (const s of shots) if (!only || only.includes(s.name)) await render(s);

// hero image for the home page = assembled render with flow
if (existsSync(path.join(out, 'render_flow.png'))) copyFileSync(path.join(out, 'render_flow.png'), path.join(here, '../public/img/hero.png'));
await browser.close();
