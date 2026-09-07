// Validates a candidate vocabulary file against the Lotse content contract and the existing bank.
// node validate-vocab.mjs <candidate.json> [--content <contentDir>]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = process.argv.slice(2);
const file = args.find(a => !a.startsWith('--'));
const ci = args.indexOf('--content');
// Default relative to this script, so the tool works from any clone (and no absolute home path
// from the author's machine ends up in a public repository).
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const contentDir = ci >= 0 ? args[ci + 1] : path.join(repoRoot, 'content');
if (!file) { console.error('usage: node validate-vocab.mjs <candidate.json> [--content dir]'); process.exit(2); }

const bands = new Set(['B1_1', 'B1_2', 'B2_1', 'B2_2', 'C1']);
const taxonomy = JSON.parse(fs.readFileSync(path.join(contentDir, 'taxonomy.json'), 'utf8'));
const nodeIds = new Set(taxonomy.nodes.map(n => n.id));

const existingIds = new Set();
const existingLemmas = new Map();
for (const f of fs.readdirSync(path.join(contentDir, 'exercises'))) {
  if (!f.endsWith('.json') || path.resolve(path.join(contentDir, 'exercises', f)) === path.resolve(file)) continue;
  const parsed = JSON.parse(fs.readFileSync(path.join(contentDir, 'exercises', f), 'utf8'));
  for (const e of parsed.exercises ?? []) {
    existingIds.add(e.id);
    if (e.type === 'Vocab' && e.lemma) existingLemmas.set(e.lemma.toLowerCase(), e.id);
  }
}
for (const f of fs.readdirSync(path.join(contentDir, 'lessons'))) {
  if (!f.endsWith('.json')) continue;
  const parsed = JSON.parse(fs.readFileSync(path.join(contentDir, 'lessons', f), 'utf8'));
  for (const e of parsed.exercises ?? []) existingIds.add(e.id);
}

let candidate;
try { candidate = JSON.parse(fs.readFileSync(file, 'utf8')); }
catch (e) { console.error('JSON-Fehler:', e.message); process.exit(1); }

const problems = [];
const seenIds = new Set();
const seenLemmas = new Set();
const articleRe = /^(der|die|das) /;
const items = candidate.exercises ?? [];
for (const e of items) {
  const id = e.id ?? '(ohne id)';
  if (!e.id) problems.push(`${id}: id fehlt`);
  if (seenIds.has(e.id)) problems.push(`${id}: id doppelt in der Datei`);
  seenIds.add(e.id);
  if (existingIds.has(e.id)) problems.push(`${id}: id existiert schon im Bestand`);
  if (!nodeIds.has(e.nodeId)) problems.push(`${id}: unbekannter Knoten '${e.nodeId}'`);
  if (!bands.has(e.band)) problems.push(`${id}: band '${e.band}' ungültig (B1_1|B1_2|B2_1|B2_2|C1)`);
  if (e.context && !['Alltag', 'Beruf', 'Pruefung'].includes(e.context)) problems.push(`${id}: context '${e.context}' ungültig (Alltag|Beruf|Pruefung - ASCII!)`);
  if (!e.prompt || e.prompt.trim().length < 2) problems.push(`${id}: prompt (Serbisch) fehlt`);
  if (e.type !== 'Match' && (!Array.isArray(e.answers) || e.answers.length === 0 || e.answers.some(a => !a || !a.trim()))) problems.push(`${id}: answers fehlen`);
  if (e.type === 'Match' && e.answers) problems.push(`${id}: Match hat keine answers (Feld entfernen)`);
  if (e.type === 'Vocab') {
    if (!e.lemma) problems.push(`${id}: lemma fehlt`);
    const lemmaKey = (e.lemma ?? '').toLowerCase();
    if (existingLemmas.has(lemmaKey)) problems.push(`${id}: Lemma '${e.lemma}' gibt es schon (${existingLemmas.get(lemmaKey)})`);
    if (seenLemmas.has(lemmaKey)) problems.push(`${id}: Lemma '${e.lemma}' doppelt in der Datei`);
    seenLemmas.add(lemmaKey);
    const stem = (e.lemma ?? '').split(' ').pop().toLowerCase();
    const stemKey = stem.slice(0, Math.max(4, stem.length - 3));
    if (!e.exampleDe || e.exampleDe.length < 15) problems.push(`${id}: exampleDe fehlt/zu kurz`);
    else if (stemKey && !e.exampleDe.toLowerCase().includes(stemKey)) problems.push(`${id}: exampleDe enthält das Lemma nicht`);
    if (e.article) {
      if (!['der', 'die', 'das'].includes(e.article)) problems.push(`${id}: article '${e.article}' ungültig`);
      if (!articleRe.test(e.answers?.[0] ?? '')) problems.push(`${id}: erster answer muss mit dem Artikel beginnen (z. B. 'die Frist')`);
      if (!e.plural) problems.push(`${id}: plural fehlt (bei Nomen; 'nur Sg.' ist erlaubt)`);
      if (e.answers?.[0] && !e.answers[0].toLowerCase().includes(stemKey)) problems.push(`${id}: answers[0] '${e.answers[0]}' enthält das Lemma '${e.lemma}' nicht`);
    }
    if (!e.instruction) problems.push(`${id}: instruction fehlt ('Deutsch, mit Artikel.' oder 'Deutsch.')`);
    if (/[a-zA-Z]/.test(e.prompt ?? '') && /\b(der|die|das|the)\b/i.test(e.prompt ?? '')) problems.push(`${id}: prompt sieht nicht serbisch aus`);
  } else if (e.type === 'Cloze') {
    if (!e.prompt.includes('___')) problems.push(`${id}: Cloze-prompt braucht '___'`);
    if (!e.explanation) problems.push(`${id}: explanation fehlt`);
  } else if (e.type === 'Match') {
    if (!Array.isArray(e.pairs) || e.pairs.length < 3) problems.push(`${id}: Match braucht >= 3 pairs`);
    else if (new Set(e.pairs.map(p => p.right)).size !== e.pairs.length) problems.push(`${id}: rechte Seiten müssen eindeutig sein`);
  } else {
    problems.push(`${id}: Typ '${e.type}' ist in Wortschatzpaketen nicht vorgesehen (Vocab|Cloze|Match)`);
  }
}

const byNode = {};
for (const e of items) byNode[e.nodeId] = (byNode[e.nodeId] ?? 0) + 1;
console.log(`Datei: ${file}\nÜbungen: ${items.length} (Vocab ${items.filter(e => e.type === 'Vocab').length}, Cloze ${items.filter(e => e.type === 'Cloze').length}, Match ${items.filter(e => e.type === 'Match').length})`);
console.log('Je Knoten:', JSON.stringify(byNode));
console.log('Bänder:', JSON.stringify(items.reduce((a, e) => (a[e.band] = (a[e.band] ?? 0) + 1, a), {})));
if (problems.length) { console.log(`\n${problems.length} Probleme:\n- ` + problems.slice(0, 60).join('\n- ')); process.exit(1); }
console.log('\nOK - keine Probleme.');
