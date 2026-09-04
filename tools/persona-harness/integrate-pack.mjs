// node integrate-pack.mjs <pack.json> [--content dir]  → content/exercises/wortschatz-<name>.json
// Normalises what the authoring agents got wrong in ways the validator only learned later (context enum,
// no answers on Match) and writes the pack in the bank's one-exercise-per-line style.
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const file = args.find(a => !a.startsWith('--'));
const ci = args.indexOf('--content');
const contentDir = ci >= 0 ? args[ci + 1] : 'C:/Users/a.micic/Documents/dev AI/Lotse/content';
if (!file) { console.error('usage: node integrate-pack.mjs <pack.json>'); process.exit(2); }

const j = JSON.parse(fs.readFileSync(file, 'utf8'));
let contextFixes = 0, matchFixes = 0;
for (const e of j.exercises) {
  if (e.context === 'Prüfung') { e.context = 'Pruefung'; contextFixes++; }
  if (e.context === 'IT') { e.context = 'Beruf'; contextFixes++; }
  if (e.type === 'Match' && e.answers) { delete e.answers; matchFixes++; }
}
const fmt = e => '    ' + JSON.stringify(e).replace(/,"/g, ', "').replace(/":/g, '": ').replace(/^\{/, '{ ').replace(/\}$/, ' }').replace(/\[\{ /g, '[ { ').replace(/ \},\{ /g, ' }, { ').replace(/ \}\]/g, ' } ]');
const target = path.join(contentDir, 'exercises', `wortschatz-${path.basename(file, '.json')}.json`);
fs.writeFileSync(target, '{\n  "exercises": [\n' + j.exercises.map(fmt).join(',\n') + '\n  ]\n}\n');
console.log(`${target}: ${j.exercises.length} Übungen (context fixes ${contextFixes}, Match answers removed ${matchFixes})`);
