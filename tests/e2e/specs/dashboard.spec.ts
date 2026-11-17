import { test, expect, uid } from '../support/fixtures';

test.describe('Dashboard', () => {
  test('KPIs reflect the data in the system @smoke', async ({ page, api, data }) => {
    await data.student();
    // Other workers add students in parallel, so the page's count must lie between the API's
    // count just before and just after the page was rendered (never assert a global count exactly).
    const total = async () => {
      const s = await (await api.get('/api/dashboard/summary')).json();
      return Number(s.totalStudents ?? s.students?.total);
    };
    const before = await total();
    await page.goto('/Dashboard');
    const kpi = (label: string) => page.locator('.cg-kpi', { has: page.locator('.cg-kpi__label', { hasText: label }) });
    const value = kpi('Total students').locator('.cg-kpi__value');
    await expect(value).toHaveText(/^\d+$/);
    const after = await total();
    // The count-up animation may still be running; wait until it settles inside the window.
    await expect.poll(async () => Number(await value.textContent())).toBeGreaterThanOrEqual(before);
    expect(Number(await value.textContent())).toBeLessThanOrEqual(after);
    await expect(page.locator('.cg-hero')).toContainText(/Good (morning|afternoon|evening)/);
  });

  test('a student without a card and without check-in raises alerts with working actions', async ({ page, data, api }) => {
    const name = uid('Alerted Student');
    const s = await data.student({ fullName: name });
    // The dashboard shows the top 8 alerts, and parallel tests keep adding card-less students, so whether
    // this one is on screen is a race. The alert itself is checked through the API...
    const summary = await (await api.get('/api/dashboard/summary')).json();
    const mine = (summary.alerts as { studentId: number | null; studentName: string | null; link: string | null }[])
      .find(a => a.studentId === s.id);
    expect(mine, 'an alert for the new student').toBeTruthy();
    expect(mine!.studentName).toBe(name);
    expect(mine!.link).toContain(`/Students/Details/${s.id}`);

    // ...and the page proves the alert actions work, on whichever student alert is shown.
    await page.goto('/Dashboard');
    const centre = page.locator('#alerts');
    await expect(centre).toBeVisible();
    const link = centre.locator('.cg-alertrow a[href*="/Students/Details/"]').first();
    const href = await link.getAttribute('href');
    await link.click();
    await expect(page).toHaveURL(new RegExp(href!.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '$'));
  });

  test('dashboard links open their pages', async ({ page }) => {
    for (const [name, url] of [['Open attendance', /\/Attendance$/], ['Manage guardians', /\/Guardians$/], ['View the full journey board', /\/Journey$/], ['Open the verification log', /\/Verification\/History$/]] as const) {
      await page.goto('/Dashboard');
      await page.getByRole('link', { name }).first().click();
      await expect(page, name).toHaveURL(url);
    }
  });
});
