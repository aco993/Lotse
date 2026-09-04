// Lotse persona harness: a thin layer over Playwright for driving the running app (http://localhost:5310).
// Usage from a script:  export default async (h) => { await h.register('anna@lotse.test'); console.log(await h.text()); }
// Run:                  node run.mjs <script.mjs> [--mobile] [--dark] [--state <file.json>]
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

export const BASE = process.env.LOTSE_URL ?? 'http://localhost:5310';

export async function open({ mobile = false, dark = false, stateFile = null, outDir = '.' } = {}) {
  // Muted twice over: headless Chromium still routes the OS speech synthesiser to the real speakers, and the app
  // reads dictations aloud automatically - the person at the machine must never hear a test run.
  const browser = await chromium.launch({ headless: true, args: ['--mute-audio'] });
  const ctx = await browser.newContext({
    baseURL: BASE,
    viewport: mobile ? { width: 375, height: 812 } : { width: 1280, height: 800 },
    isMobile: mobile, hasTouch: mobile,
    locale: 'de-DE',
    colorScheme: dark ? 'dark' : 'light',
    storageState: stateFile && fs.existsSync(stateFile) ? stateFile : undefined,
  });
  await ctx.addInitScript(() => {
    // Keep the API shape (the app checks for support) but never produce sound.
    try {
      const noop = () => { };
      Object.defineProperty(window, 'speechSynthesis', { value: { speak: noop, cancel: noop, pause: noop, resume: noop, getVoices: () => [], speaking: false, pending: false, paused: false, addEventListener: noop, removeEventListener: noop }, configurable: true });
    } catch { }
  });
  const page = await ctx.newPage();
  const log = [];
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') log.push(`[console.${m.type()}] ${m.text().slice(0, 300)}`); });
  page.on('pageerror', e => log.push(`[pageerror] ${e.message.slice(0, 300)}`));
  let shotIndex = 0;
  const settle = async (ms = 500) => { await page.waitForTimeout(ms); };

  const h = {
    page, browser, log,
    /** Navigate to a path (relative to BASE) and wait for the page to settle. */
    async goto(p) { await page.goto(p); await page.waitForLoadState('networkidle').catch(() => { }); await settle(700); },
    /** Visible text of the page (what a user reads). */
    async text() { return (await page.locator('body').innerText()).replace(/\n{3,}/g, '\n\n'); },
    /** Accessibility tree (roles, names, links) - the structure of the page. */
    async aria() { return await page.locator('body').ariaSnapshot(); },
    /** Click a control by its accessible name (buttons by default). Substring, case-insensitive; exact: true for exact. */
    async click(name, { role = 'button', exact = false, nth = 0 } = {}) {
      await page.getByRole(role, { name, exact }).nth(nth).click(); await settle();
    },
    /** Click an element by visible text. */
    async clickText(t, { exact = false } = {}) { await page.getByText(t, { exact }).first().click(); await settle(); },
    /** Fill an input by CSS selector or by label text. */
    async fill(target, value) {
      const loc = target.startsWith('#') || target.startsWith('.') || target.includes('[') ? page.locator(target) : page.getByLabel(target);
      await loc.first().fill(value); await settle(150);
    },
    /** Type into the currently focused element (for MudBlazor text fields, first click them). */
    async typeKeys(value) { await page.keyboard.type(value); await settle(150); },
    async press(key) { await page.keyboard.press(key); await settle(400); },
    /** Screenshot into outDir; returns the file path. */
    async shot(name) { const f = path.join(outDir, `${String(++shotIndex).padStart(2, '0')}-${name}.png`); await page.screenshot({ path: f }); return f; },
    /** Register a fresh account and land on the dashboard. */
    async register(email, pw = 'Lotse-Test-2026') {
      await h.goto('/Account/Register');
      await page.fill('#email', email); await page.fill('#password', pw); await page.fill('#confirm', pw);
      await page.getByRole('button', { name: 'Konto erstellen' }).click();
      await page.waitForURL(u => !u.toString().includes('/Account/'), { timeout: 15000 });
      await page.waitForLoadState('networkidle').catch(() => { }); await settle(800);
    },
    async login(email, pw = 'Lotse-Test-2026') {
      await h.goto('/Account/Login');
      await page.fill('#email', email); await page.fill('#password', pw);
      await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
      await page.waitForURL(u => !u.toString().includes('/Account/'), { timeout: 15000 });
      await page.waitForLoadState('networkidle').catch(() => { }); await settle(800);
    },
    async logout() { await page.locator('form[action*="Logout"]').evaluate(f => f.requestSubmit()); await settle(1000); },
    /** Persist cookies so the next script can continue as the same learner. */
    async saveState() { if (stateFile) await ctx.storageState({ path: stateFile }); },
    async close() { await h.saveState(); await browser.close(); },
  };
  return h;
}
