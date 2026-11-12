import { test, expect } from '../support/fixtures';

test.describe('Dashboard', () => {
  test('KPIs reflect the data in the system @smoke', async ({ page, api, data }) => {
    await data.student();
    const summary = await (await api.get('/api/dashboard/summary')).json();
    await page.goto('/Dashboard');
    const kpi = (label: string) => page.locator('.cg-kpi', { has: page.locator('.cg-kpi__label', { hasText: label }) });
    await expect(kpi('Total students').locator('.cg-kpi__value')).toHaveText(String(summary.totalStudents ?? summary.students?.total ?? summary.totalStudents));
    await expect(page.locator('.cg-hero')).toContainText(/Good (morning|afternoon|evening)/);
  });

  test('a student without a card and without check-in raises alerts with working actions', async ({ page, data }) => {
    const s = await data.student({ fullName: 'Alerted Student' });
    await page.goto('/Dashboard');
    const centre = page.locator('#alerts');
    await expect(centre).toBeVisible();
    const row = centre.locator('.cg-alertrow', { hasText: 'Alerted Student' }).first();
    await expect(row).toBeVisible();
    await row.getByRole('link').first().click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
  });

  test('dashboard links open their pages', async ({ page }) => {
    for (const [name, url] of [['Open attendance', /\/Attendance$/], ['Manage guardians', /\/Guardians$/], ['View the full journey board', /\/Journey$/], ['Open the verification log', /\/Verification\/History$/]] as const) {
      await page.goto('/Dashboard');
      await page.getByRole('link', { name }).first().click();
      await expect(page, name).toHaveURL(url);
    }
  });
});
