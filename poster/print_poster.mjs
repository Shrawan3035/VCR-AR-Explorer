// Prints poster/poster.html to an A3-landscape PDF (420 × 297 mm) with headless Edge/Chrome.
//   node poster/print_poster.mjs            (run from the repository root; uses web/node_modules)
import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.join(here, '../web/package.json'));
const puppeteer = require('puppeteer-core');
const exe = [process.env.BROWSER_PATH, 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Google/Chrome/Application/chrome.exe', '/usr/bin/google-chrome']
  .filter(Boolean).find((p) => existsSync(p));
const browser = await puppeteer.launch({ executablePath: exe, headless: true, args: ['--allow-file-access-from-files'] });
const page = await browser.newPage();
await page.goto(pathToFileURL(path.join(here, 'poster.html')).href, { waitUntil: 'networkidle0' });
await page.evaluateHandle('document.fonts.ready');
const out = path.join(here, 'VCR-AR-Explorer-A3.pdf');
await page.pdf({ path: out, width: '420mm', height: '297mm', printBackground: true, preferCSSPageSize: true, margin: { top: 0, right: 0, bottom: 0, left: 0 } });
await browser.close();
console.log('wrote', out);
