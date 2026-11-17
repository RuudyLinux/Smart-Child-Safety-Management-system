// The dashboard refreshes itself in place: every minute while visible, not while hidden, paused after 30 idle minutes.
import { test, expect, uid } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

const status = (page: import('@playwright/test').Page) => page.locator('[data-cg-live-status]');
const summaryCalls = (page: import('@playwright/test').Page) => {
  const calls: string[] = [];
  page.on('request', r => { if (new URL(r.url()).pathname === '/Dashboard') calls.push(r.headers()['x-cg-regions'] ?? ''); });
  return calls;
};

test.describe('Dashboard live refresh', () => {
  test('refreshes every 60 seconds while visible, without navigating', async ({ page }) => {
    await page.clock.install();
    await page.goto('/Dashboard');
    await expect(status(page)).toContainText('Updated');
    const navs = countNavigations(page);
    const calls = summaryCalls(page);
    await page.clock.runFor(61_000);
    await expect.poll(() => calls.length).toBeGreaterThanOrEqual(1);
    expect(calls[0]).toContain('dash-live');
    await expect(page.locator('[data-cg-region="dash-live"]')).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('a new student appears on the next refresh', async ({ page, data }) => {
    await page.clock.install();
    await page.goto('/Dashboard');
    const before = Number((await page.locator('.cg-hero__stat-value').first().innerText()).match(/\d+/)![0]);
    await data.student({ fullName: uid('Live') });
    await page.clock.runFor(61_000);
    await expect.poll(async () => Number((await page.locator('.cg-hero__stat-value').first().innerText()).match(/\d+/)![0])).toBeGreaterThan(before);
  });

  test('does not refresh while the tab is hidden, and catches up when it returns', async ({ page }) => {
    await page.clock.install();
    await page.goto('/Dashboard');
    const calls = summaryCalls(page);
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await page.clock.runFor(180_000);
    expect(calls.length).toBe(0);
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await expect.poll(() => calls.length).toBe(1);
  });

  test('stops after 30 idle minutes; the button refreshes and resumes', async ({ page }) => {
    await page.clock.install();
    await page.goto('/Dashboard');
    const calls = summaryCalls(page);
    await page.clock.runFor(31 * 60_000);
    await expect(status(page)).toHaveText('Paused \u2014 click to refresh');
    const seen = calls.length;
    await page.clock.runFor(5 * 60_000);
    expect(calls.length).toBe(seen);
    await page.getByRole('button', { name: 'Refresh' }).click();
    await expect.poll(() => calls.length).toBe(seen + 1);
    await expect(status(page)).toContainText('Updated');
    await page.clock.runFor(61_000);
    await expect.poll(() => calls.length).toBeGreaterThan(seen + 1);
  });

  test('the manual Refresh button updates the time', async ({ page }) => {
    await page.goto('/Dashboard');
    await page.getByRole('button', { name: 'Refresh' }).click();
    await expect(status(page)).toContainText(/Updated \d\d:\d\d/);
  });
});
