// Attendance, Journey Tracking and Reports: read views fed by RFID scans.
import { test, expect } from '../support/fixtures';

test.describe('Attendance', () => {
  test('arrivals appear, filters narrow them, export downloads a CSV @smoke', async ({ page, data }) => {
    const s = await data.student({ grade: '8Q' });
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');

    await page.goto('/Attendance');
    const row = page.locator('tr[data-row]', { hasText: s.fullName });
    await expect(row.first()).toBeVisible();

    await page.locator('[data-cg-search][data-cg-search-scope="attendance"]').fill(s.fullName);
    await expect(page.locator('tr[data-row]:visible')).toHaveCount(1);
    await page.locator('[data-cg-filter="kind"]').selectOption({ label: 'Departures only' });
    await expect(page.locator('tr[data-row]:visible')).toHaveCount(0);
    await page.getByRole('button', { name: 'Clear' }).click();

    // Server filter by student id.
    await page.locator('input[name="studentId"]').fill(String(s.id));
    await page.getByRole('button', { name: 'Apply' }).click();
    await expect(page).toHaveURL(new RegExp(`studentId=${s.id}`));
    await expect(page.locator('tr[data-row]')).toHaveCount(1);

    const download = page.waitForEvent('download');
    await page.getByRole('link', { name: 'Export CSV' }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/\.csv$/);
    const text = await (await file.createReadStream())!.toArray().then(c => Buffer.concat(c).toString('utf8'));
    expect(text.split('\n')[0]).toMatch(/student/i);
    expect(text).toContain(s.fullName);
  });

  test('a date range in the future shows the empty state', async ({ page }) => {
    await page.goto('/Attendance?from=2030-01-01&to=2030-01-31');
    await expect(page.locator('main')).toContainText(/No attendance|no records|nothing/i);
  });

  test.describe('bad input', () => {
    test.use({ allowErrors: [/status of 400/] });
    test('a reversed date range is handled without a crash', async ({ page }) => {
      const res = await page.goto('/Attendance?from=2026-11-06&to=2026-10-01');
      expect(res!.status()).toBeLessThan(500);
      await expect(page.locator('body')).not.toContainText(/exception/i);
    });
  });
});

test.describe('Journey Tracking', () => {
  test('board shows live status, filters by state, opens a timeline', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'BusBoarding');

    await page.goto('/Journey');
    await page.locator('[data-cg-search][data-cg-search-scope="journey"]').fill(s.fullName);
    const row = page.locator('[data-row]:visible', { hasText: s.fullName });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText(/on the bus/i);
    await page.locator('[data-cg-filter="state"]').selectOption({ label: 'Safe at school' });
    await expect(row).toHaveCount(0);
    await page.getByRole('button', { name: 'Clear' }).click();

    await data.scan(card.cardUid, 'SchoolArrival');
    await page.getByRole('link', { name: 'Refresh' }).click();
    await page.locator('[data-cg-search][data-cg-search-scope="journey"]').fill(s.fullName);
    await expect(page.locator('[data-row]:visible', { hasText: s.fullName })).toContainText(/safe at school|at school/i);

    await page.getByRole('link', { name: `Full timeline for ${s.fullName}` }).click();
    await expect(page).toHaveURL(new RegExp(`/Journey/Student/${s.id}$`));
    const steps = page.locator('.cg-timeline__item');
    await expect(steps).toHaveCount(2);
  });
});

test.describe('Reports', () => {
  test('run a report, switch tabs, export CSV, print', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');

    await page.goto('/Reports');
    await page.locator('select[name="studentId"]').selectOption(String(s.id));
    await page.getByRole('button', { name: 'Run report' }).click();
    await expect(page).toHaveURL(new RegExp(`studentId=${s.id}`));
    await expect(page.locator('[data-cg-tab="attendance"]')).toContainText(/[1-9]/);

    for (const tab of ['journey', 'verification', 'attendance']) {
      await page.locator(`[data-cg-tab="${tab}"]`).click();
      await expect(page.locator(`[data-cg-tab="${tab}"]`)).toHaveClass(/is-active/);
    }

    const download = page.waitForEvent('download');
    await page.getByRole('link', { name: 'Export attendance CSV' }).click();
    const file = await download;
    const text = await (await file.createReadStream())!.toArray().then(c => Buffer.concat(c).toString('utf8'));
    expect(text).toContain(s.fullName);

    await page.evaluate(() => { (window as any).__printed = false; window.print = () => { (window as any).__printed = true; }; });
    await page.getByRole('button', { name: 'Print' }).click();
    expect(await page.evaluate(() => (window as any).__printed)).toBe(true);
  });
});
