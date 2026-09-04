// node run.mjs <script.mjs> [--mobile] [--dark] [--state file.json] [--out dir]
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { open } from './drive.mjs';

const args = process.argv.slice(2);
const script = args.find(a => !a.startsWith('--'));
if (!script) { console.error('usage: node run.mjs <script.mjs> [--mobile] [--dark] [--state file] [--out dir]'); process.exit(2); }
const flag = (n) => args.includes(`--${n}`);
const val = (n) => { const i = args.indexOf(`--${n}`); return i >= 0 ? args[i + 1] : null; };
const outDir = val('out') ?? path.dirname(path.resolve(script));

const mod = await import(pathToFileURL(path.resolve(script)).href);
const h = await open({ mobile: flag('mobile'), dark: flag('dark'), stateFile: val('state'), outDir });
try {
  await mod.default(h);
} catch (e) {
  console.error('SCRIPT FAILED:', e.message);
  try { console.error('screenshot:', await h.shot('FAILED')); } catch { }
  process.exitCode = 1;
} finally {
  if (h.log.length) console.log('\n--- browser console ---\n' + h.log.slice(0, 20).join('\n'));
  await h.close();
}
