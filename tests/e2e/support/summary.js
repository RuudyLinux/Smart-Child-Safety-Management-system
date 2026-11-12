// Prints a summary of test-results/results.json: totals per project, failures with
// their first error line, and unique axe violations. Usage: node support/summary.js
const fs = require('node:fs');
const path = require('node:path');

const file = path.join(__dirname, '..', 'test-results', 'results.json');
if (!fs.existsSync(file)) { console.error('No results.json - run the suite first.'); process.exit(1); }
const report = JSON.parse(fs.readFileSync(file, 'utf8'));
const strip = s => (s || '').replace(/\x1b\[[0-9;]*m/g, '');

const totals = {};
const byProject = {};
const failures = [];
const axe = {};

function walk(suite, file) {
  for (const s of suite.suites || []) walk(s, s.file || file);
  for (const spec of suite.specs || []) {
    for (const t of spec.tests) {
      const res = t.results[t.results.length - 1] || { status: 'skipped' };
      // "expected" = passed, or failed where a failure is expected (test.fail).
      const outcome = t.status === 'expected' ? (t.expectedStatus === 'failed' ? 'known-bug' : 'passed')
        : t.status === 'skipped' ? 'skipped' : t.status === 'flaky' ? 'flaky' : 'failed';
      totals[outcome] = (totals[outcome] || 0) + 1;
      (byProject[t.projectName] ??= {})[outcome] = (byProject[t.projectName][outcome] || 0) + 1;
      if (outcome === 'failed' || outcome === 'flaky') {
        const msg = strip(res.error?.message);
        for (const m of msg.matchAll(/"([a-z-]+) \((serious|critical)\): ([^—]+)— ([^"]+)"/g)) {
          const key = `${m[1]} (${m[2]}): ${m[3].trim()} — ${m[4].slice(0, 110)}`;
          (axe[key] ??= new Set()).add(spec.title.replace(/.* on /, ''));
        }
        failures.push(`[${t.projectName}] ${path.basename(file)} › ${spec.title}\n    ${msg.split('\n').filter(Boolean).slice(0, 3).join(' | ').slice(0, 400)}`);
      }
    }
  }
}
for (const s of report.suites) walk(s, s.file);

console.log('Totals:', JSON.stringify(totals));
for (const [p, v] of Object.entries(byProject)) console.log(`  ${p.padEnd(11)} ${JSON.stringify(v)}`);
console.log(`Duration: ${Math.round(report.stats.duration / 1000)}s`);
if (failures.length) { console.log('\nFailures:'); failures.forEach(f => console.log(' - ' + f)); }
if (Object.keys(axe).length) {
  console.log('\nUnique accessibility violations:');
  for (const [k, pages] of Object.entries(axe)) console.log(` - ${k}\n     pages: ${[...pages].join(', ')}`);
}
