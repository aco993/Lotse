// Re-checks the persona bugs after the fixes. Run: node run.mjs verify-fixes.mjs --out verify
const ok = (cond, label) => console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`);

export default async (h) => {
  const email = `fix-${Date.now()}@lotse.test`;
  await h.register(email);

  // 1. Marko: account link from the interactive nav must not crash the circuit.
  await h.page.getByRole('link', { name: email }).click();
  await h.page.waitForURL(/Account\/Manage/, { timeout: 10000 });
  await h.page.getByText('Dein Konto').waitFor({ timeout: 10000 });
  let text = await h.text();
  ok(text.includes('Passkeys') && text.includes('Passwort'), `account link opens Manage (url ${h.page.url()})`);
  ok(h.log.length === 0, `no console/page errors so far (${h.log.length})`);
  await h.goto('/');

  // 2. Dragan/Milica/Stefan: starting the placement twice must not create two sessions; pause + resume works.
  await h.click('Einstufung starten');
  await h.page.waitForURL(/session\//);
  const first = h.page.url();
  await h.page.getByRole('textbox').first().fill('xyz');
  await h.press('Enter'); await h.press('Enter');
  await h.click('Beenden');
  await h.page.waitForTimeout(500);
  text = await h.text();
  ok(text.includes('Einstufung unterbrechen?'), 'Beenden asks before ending');
  await h.click('Pausieren');
  await h.page.waitForURL(u => !u.toString().includes('/session/'));
  await h.page.waitForTimeout(700);
  text = await h.text();
  ok(text.toLowerCase().includes('einstufung fortsetzen') && text.includes('1 von 28'), 'dashboard offers to resume the paused placement (1 of 28)');
  ok(!text.includes('Session läuft noch'), 'no separate "Session läuft noch" card for the open placement');
  await h.click('Einstufung fortsetzen');
  await h.page.waitForURL(/session\//);
  ok(h.page.url() === first, 'resume goes back to the SAME session (no duplicate)');
  text = await h.text();
  ok(text.includes('2 / 28'), 'resumes at step 2');
  await h.goto('/');
  await h.shot('dashboard-paused');

  // 3. Themen legend filters.
  await h.goto('/themen');
  await h.page.getByRole('button', { name: 'Wortschatz', exact: true }).click();
  await h.page.waitForTimeout(400);
  text = await h.text();
  ok(!text.includes('Verb an Position 2') && text.includes('Büro'), 'Wortschatz filter hides grammar nodes');
  await h.shot('themen-filter');

  // 4. Schreiben: draft survives reload; analyzer findings appear without a tutor; own text shown.
  await h.goto('/schreiben');
  await h.page.locator('.task-card').first().click();
  await h.page.waitForURL(/session\//);
  const draft = 'Sehr geehrte Damen und Herren, ich schreibe Ihnen weil ich habe ein Problem mit meine Wohnung. Gestern ich habe gegangen zu die Firma und der nachbar hat sehr laut Musik gemacht. Ich warte für Ihre Antwort. Mit freundliche Grüße Marko';
  await h.page.getByRole('textbox').first().fill(draft);
  await h.page.waitForTimeout(1800); // draft is saved on the 1-second tick
  await h.page.reload(); await h.page.waitForLoadState('networkidle').catch(() => { }); await h.page.waitForTimeout(1200);
  const restored = await h.page.getByRole('textbox').first().inputValue();
  ok(restored === draft, 'draft restored after reload');
  await h.page.getByRole('button', { name: /Abgeben/ }).click();
  await h.page.waitForTimeout(1500);
  text = await h.text();
  ok(text.includes('Automatisch gefunden'), 'rule-based findings shown without tutor');
  for (const code of ['KOMMA', 'WORTST_NEBENSATZ', 'WORTST_V2', 'TEMPUS_HILFSVERB', 'VERB_PRAEP', 'REG_ANREDE_GRUSS']) ok(text.includes(code), `finding ${code}`);
  ok(text.includes('Dein Text'), 'own text shown next to the model answer');
  await h.shot('schreiben-findings');
  // reload after submitting: self-check state must come back from the database
  await h.page.reload(); await h.page.waitForLoadState('networkidle').catch(() => { }); await h.page.waitForTimeout(1500);
  text = await h.text();
  ok(text.includes('Selbstcheck') && text.includes('Automatisch gefunden'), 'self-check restored after reload');
  await h.goto('/fehler');
  text = await h.text();
  ok(text.includes('Komma') || text.includes('KOMMA') || text.includes('Nebensatz'), 'findings landed in the error journal');
  await h.shot('fehlerjournal');

  // 5. Petar: reset really clears the journal.
  await h.goto('/einstellungen');
  await h.click('Alle Lerndaten löschen');
  await h.click('Ja, löschen');
  await h.page.waitForTimeout(800);
  await h.goto('/fehler');
  text = await h.text();
  ok(!text.includes('Nebensatz') && !text.includes('Komma fehlt'), 'error journal empty after reset');
  await h.goto('/');
  text = await h.text();
  ok(text.includes('Willkommen an Bord') && !text.includes('fortsetzen'), 'dashboard back to a fresh start after reset');

  console.log('\nconsole/page errors:', h.log.length ? h.log : 'none');
};
