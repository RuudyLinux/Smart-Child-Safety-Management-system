// The rest of the suite runs with animations off (automation is detected and the
// page settles instantly). This spec switches them back on, the way a real
// browser sees the app, and checks the motion layer never breaks a page:
// no console errors, counters land on the server's value, navigation works.
import { test, expect } from '../support/fixtures';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(Navigator.prototype, 'webdriver', { get: () => false });
  });
});

const pages = ['/Dashboard', '/Students', '/Guardians', '/Attendance', '/Journey', '/Verification',
  '/Verification/History', '/Transportation', '/Rfid', '/Reports', '/Settings', '/Profile'];

test('every page renders with animations on and settles without errors @smoke', async ({ page }) => {
  for (const url of pages) {
    await page.goto(url);
    await expect(page.locator('html'), url).toHaveClass(/cg-motion/);
    await expect(page.locator('.cg-content > *').first(), url).toBeVisible();
    // Let entrance animations and counters finish.
    await page.waitForTimeout(1300);
    const hidden = await page.locator('.cg-content > *').evaluateAll(els =>
      els.filter(e => getComputedStyle(e).opacity !== '1').length);
    expect(hidden, `${url}: blocks still transparent after the entrance`).toBe(0);
  }
});

test('dashboard counters count up to exactly the server value', async ({ page, api, data }) => {
  await data.student();
  const summary = await (await api.get('/api/dashboard/summary')).json();
  await page.goto('/Dashboard');
  const total = page.locator('.cg-kpi', { has: page.locator('.cg-kpi__label', { hasText: 'Total students' }) }).locator('.cg-kpi__value');
  await expect(total).toHaveText(String(summary.totalStudents));
});

test('sidebar navigation works with page transitions on @smoke', async ({ page }) => {
  await page.goto('/Dashboard');
  await page.locator('.cg-sidebar').getByRole('link', { name: 'Students', exact: true }).click();
  await expect(page).toHaveURL(/\/Students$/);
  await expect(page.locator('.cg-nav__link.is-active')).toHaveText(/Students/);
  await page.goBack();
  await expect(page).toHaveURL(/\/Dashboard$/);
  // The loading line is cleared once the page is shown again (also from bfcache).
  await expect(page.locator('html')).not.toHaveClass(/cg-navigating/);
});
