// Layout checks at the five agreed viewports: no sideways scroll, nothing outside the
// viewport, sidebar footer always reachable, drawer + menus usable on small screens.
import { test, expect } from '../support/fixtures';

const VIEWPORTS = [
  { name: 'desktop', width: 1920, height: 1080 },
  { name: 'laptop', width: 1366, height: 768 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 390, height: 844 },
  { name: 'small-mobile', width: 320, height: 568 },
];
const PAGES = ['/Dashboard', '/Students', '/Students/Create', '/Guardians', '/Attendance', '/Journey', '/Verification',
  '/Verification/History', '/Transportation', '/Rfid', '/Reports', '/Profile', '/Settings', '/Audit', '/Enrollment'];

function layoutProblems() {
  const vw = document.documentElement.clientWidth;
  const problems: string[] = [];
  const overflow = document.documentElement.scrollWidth - vw;
  if (overflow > 0) problems.push(`page scrolls sideways by ${overflow}px`);
  const scrolls = (el: Element) => {
    for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
      const s = getComputedStyle(p);
      if (/(auto|scroll|hidden|clip)/.test(s.overflowX) && p.scrollWidth > p.clientWidth + 1) return true;
    }
    return false;
  };
  const sidebar = document.querySelector('.cg-sidebar');
  const offCanvas = sidebar && getComputedStyle(sidebar).visibility === 'hidden';
  for (const el of Array.from(document.querySelectorAll('body *'))) {
    const s = getComputedStyle(el);
    if (s.display === 'none' || s.visibility === 'hidden' || s.position === 'fixed') continue;
    if (offCanvas && sidebar!.contains(el)) continue;
    const b = el.getBoundingClientRect();
    if (!b.width || !b.height) continue;
    if ((b.right > vw + 1 || b.left < -1) && !scrolls(el)) problems.push(`${el.tagName.toLowerCase()}.${String((el as HTMLElement).className).split(' ')[0]} outside viewport (${Math.round(b.left)}..${Math.round(b.right)})`);
  }
  // Sidebar footer must be on screen when the sidebar is shown.
  const foot = document.querySelector('.cg-sidebar__foot');
  if (sidebar && !offCanvas && foot) {
    const f = foot.getBoundingClientRect();
    if (f.bottom > innerHeight + 1) problems.push('sidebar footer below the fold');
    const nav = document.querySelector('.cg-nav')!;
    if (nav.scrollHeight > nav.clientHeight + 1 && !/(auto|scroll)/.test(getComputedStyle(nav).overflowY)) problems.push('nav cannot scroll');
  }
  return [...new Set(problems)].slice(0, 6);
}

for (const vp of VIEWPORTS) {
  test.describe(`${vp.name} ${vp.width}x${vp.height}`, () => {
    test.use({ viewport: { width: vp.width, height: vp.height } });

    test('every page lays out without overflow or clipping', async ({ page }, testInfo) => {
      const failures: string[] = [];
      for (const url of PAGES) {
        await page.goto(url);
        await page.waitForLoadState('networkidle');
        const p = await page.evaluate(layoutProblems);
        if (p.length) {
          failures.push(`${url}: ${p.join('; ')}`);
          await testInfo.attach(`${vp.name}${url.replace(/\//g, '_')}.png`, { body: await page.screenshot(), contentType: 'image/png' });
        }
      }
      await testInfo.attach(`${vp.name}_dashboard.png`, { body: await (await page.goto('/Dashboard'), page.screenshot()), contentType: 'image/png' });
      expect(failures).toEqual([]);
    });

    test('navigation is reachable and menus stay on screen', async ({ page }) => {
      await page.goto('/Dashboard');
      const drawer = vp.width <= 860;
      if (drawer) {
        await expect(page.locator('.cg-sidebar')).toBeHidden();
        await page.getByRole('button', { name: 'Toggle navigation' }).click();
        await expect(page.locator('.cg-sidebar')).toBeVisible();
      }
      const settings = page.locator('.cg-nav a[href="/Settings"]');
      await settings.scrollIntoViewIfNeeded();
      const ok = await settings.evaluate(a => {
        const b = a.getBoundingClientRect(), f = document.querySelector('.cg-sidebar__foot')!.getBoundingClientRect();
        return b.top >= 0 && b.bottom <= f.top + 0.5 && f.bottom <= innerHeight + 1;
      });
      expect(ok, 'Settings visible above the profile footer').toBe(true);
      await settings.click();
      await expect(page).toHaveURL(/\/Settings$/);
      if (drawer) await expect(page.locator('.cg-app')).not.toHaveClass(/cg-app--drawer/);

      for (const name of ['Safety alerts', 'Quick action', 'Account menu']) {
        await page.getByRole('button', { name }).click();
        const box = await page.locator('.cg-dropdown.is-open .cg-dropdown__menu').boundingBox();
        expect(box, name).not.toBeNull();
        expect(box!.x, `${name} left edge`).toBeGreaterThanOrEqual(0);
        expect(box!.x + box!.width, `${name} right edge`).toBeLessThanOrEqual(vp.width);
        await page.keyboard.press('Escape');
      }
    });

    test('modal fits the screen', async ({ page }) => {
      await page.goto('/Transportation');
      await page.getByRole('button', { name: 'Add driver' }).first().click();
      const panelEl = page.locator('#addDriverModal .cg-modal__panel');
      // Measure the resting position, not a frame of the slide-in animation.
      await panelEl.evaluate(el => Promise.all(el.getAnimations().map(a => a.finished)));
      const panel = await panelEl.boundingBox();
      expect(panel!.x).toBeGreaterThanOrEqual(0);
      expect(panel!.x + panel!.width).toBeLessThanOrEqual(vp.width);
      expect(panel!.y + panel!.height).toBeLessThanOrEqual(vp.height + 1);
    });
  });
}
