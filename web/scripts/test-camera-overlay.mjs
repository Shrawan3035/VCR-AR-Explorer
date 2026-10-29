// Smoke test for the camera-overlay mode using Chromium's synthetic camera
// (--use-fake-device-for-media-stream). Usage: node scripts/test-camera-overlay.mjs [baseUrl]
import puppeteer from 'puppeteer-core';
import { existsSync } from 'node:fs';
const base = (process.argv[2] ?? 'http://localhost:5173/').replace(/\/?$/, '/');
const exe = [process.env.BROWSER_PATH, 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Google/Chrome/Application/chrome.exe'].filter(Boolean).find((p) => existsSync(p));
const browser = await puppeteer.launch({ executablePath: exe, headless: true, args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--use-angle=d3d11', '--ignore-gpu-blocklist'] });
const page = await browser.newPage();
await page.setViewport({ width: 1280, height: 800 });
await page.goto(base + '#/explore?overlay=1', { waitUntil: 'networkidle0' });
await page.waitForFunction(() => { const v = document.querySelector('video.cam-feed'); return v && !v.hidden && v.videoWidth > 0; }, { timeout: 20000 });
const info = await page.evaluate(() => { const v = document.querySelector('video.cam-feed'); return { videoWidth: v.videoWidth, videoHeight: v.videoHeight, stageCamOn: !!document.querySelector('.stage.cam-on'), button: document.querySelector('.tool-cam')?.textContent }; });
await new Promise((r) => setTimeout(r, 1500));
await page.screenshot({ path: process.env.SHOT ?? 'camera-overlay-test.png' });
// stopping must release the camera
await page.click('.tool-cam');
const after = await page.evaluate(() => ({ hidden: document.querySelector('video.cam-feed').hidden, src: !!document.querySelector('video.cam-feed').srcObject }));
console.log(JSON.stringify({ ...info, afterStop: after }));
await browser.close();
