// Crawls every Admin Web page and records each interactive element, so coverage can be
// checked against a real inventory instead of memory. Also a smoke check per page.
import fs from 'node:fs';
import { test, expect } from '../support/fixtures';

const PAGES = ['/Dashboard', '/Students', '/Students/Create', '/Guardians', '/Guardians/Create', '/Attendance', '/Journey',
  '/Verification', '/Verification/History', '/Transportation', '/Rfid', '/Reports', '/Profile', '/Settings'];

test('inventory of interactive elements on every page', async ({ page, data }) => {
  // Give detail/edit pages something to show.
  const s = await data.student();
  const g = await data.guardian();
  await data.link(s.id, g.id);
  const c = await data.card(s.id);
  const pages = [...PAGES, `/Students/Details/${s.id}`, `/Students/Edit/${s.id}`, `/Guardians/Details/${g.id}`, `/Guardians/Edit/${g.id}`, `/Rfid/Details/${c.id}`, `/Journey/Student/${s.id}`];
  const inventory: Record<string, string[]> = {};
  for (const url of pages) {
    const res = await page.goto(url);
    expect(res?.status(), url).toBeLessThan(400);
    inventory[url] = await page.locator('main').evaluate(main => {
      const out: string[] = [];
      main.querySelectorAll('a[href], button, input, select, textarea, [role="tab"], [data-cg-sort], [data-cg-filter], [data-cg-modal-open], [data-cg-tab]').forEach(el => {
        const e = el as HTMLElement;
        const tag = e.tagName.toLowerCase();
        const desc = [tag,
          e.getAttribute('type') && `type=${e.getAttribute('type')}`,
          e.getAttribute('name') && `name=${e.getAttribute('name')}`,
          e.getAttribute('href') && `href=${e.getAttribute('href')}`,
          ...Array.from(e.attributes).filter(a => a.name.startsWith('data-cg-')).map(a => `${a.name}${a.value ? '=' + a.value : ''}`),
          (e.innerText || e.getAttribute('aria-label') || (e as HTMLInputElement).placeholder || '').trim().replace(/\s+/g, ' ').slice(0, 50),
          e.closest('form') ? `form=${e.closest('form')!.getAttribute('action') ?? ''}` : '',
        ].filter(Boolean).join(' | ');
        out.push(desc);
      });
      return out;
    });
  }
  fs.writeFileSync('.e2e/inventory.json', JSON.stringify(inventory, null, 1));
});
