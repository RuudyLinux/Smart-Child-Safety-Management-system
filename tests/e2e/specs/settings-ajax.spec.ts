// Settings scanners and School Time on the shared AJAX layer. Calendar entries use dates years ahead (or a
// week or so out, removed again) so the school-day logic the other specs rely on is never touched.
import { test, expect, toast, uid } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

test.describe('Settings scanners without page reloads', () => {
  test('renaming a scanner updates the table in place', async ({ page, api }) => {
    // Its own scanner: renaming the shared one would change what other specs look for.
    const label = ('SCAN-' + uid('A').replace(/\W/g, '')).slice(0, 30);
    const created = await api.post('/api/rfid/devices', { data: { deviceLabel: label } });
    expect(created.ok(), await created.text()).toBeTruthy();
    const d = await created.json() as { rfidDeviceId: number; deviceLabel: string };
    const renamed = ('NORTH-' + uid('R').replace(/\W/g, '')).slice(0, 30);
    await page.goto('/Settings');
    const navs = countNavigations(page);
    await page.getByRole('button', { name: `Rename ${d.deviceLabel}` }).click();
    const modal = page.locator(`#renameDevice-${d.rfidDeviceId}`);
    await modal.locator('input[name="deviceLabel"]').fill(renamed);
    await modal.getByRole('button', { name: 'Save' }).click();
    expect(await toast(page, `Scanner renamed to ${renamed}`)).toContain(renamed);
    await expect(page.locator('main tr', { hasText: renamed })).toHaveCount(1);
    expect(navs.count).toBe(0);
  });
});

// A calendar entry must be within two years of today, so these dates are relative to it. Each test uses its own
// stretch of the calendar, so parallel workers never touch the same day.
const inDays = (n: number) => new Date(Date.now() + n * 86400000);
const iso = (d: Date) => d.toISOString().slice(0, 10);
const viewOf = (d: Date) => `/SchoolTime?year=${d.getUTCFullYear()}&month=${d.getUTCMonth() + 1}`;

test.describe('School Time without page reloads', () => {
  test.use({ allowErrors: [/status of 40[09]/] });

  async function addDay(page: import('@playwright/test').Page, date: string, title: string, type = 'Holiday') {
    await page.locator('[data-st-new]').first().click();
    await page.locator('#stDate').fill(date);
    await page.locator('#stType').selectOption(type);
    await page.locator('#stTitle').fill(title);
    await page.locator('#dayModal').getByRole('button', { name: 'Add day' }).click();
  }

  test('an entry in the month on screen is saved in place; the day modal still works afterwards', async ({ page }) => {
    const title = uid('Fest');
    const day = inDays(200);
    await page.goto(viewOf(day));
    const navs = countNavigations(page);
    await addDay(page, iso(day), title);
    expect(await toast(page, 'saved')).toContain('saved');
    await expect(page.locator(`[data-st-date="${iso(day)}"][data-st-id]`)).toHaveCount(1);
    await expect(page).toHaveURL(new RegExp(`year=${day.getUTCFullYear()}&month=${day.getUTCMonth() + 1}$`));
    expect(navs.count).toBe(0);

    // The modal script survived the refresh: editing fills it from the clicked day, adding clears it.
    await page.locator(`[data-st-date="${iso(day)}"][data-st-id]`).click();
    await expect(page.locator('[data-st-modal-title]')).toHaveText('Edit calendar day');
    await expect(page.locator('#stTitle')).toHaveValue(title);
    await page.locator('#dayModal').getByRole('button', { name: 'Cancel' }).click();
    await page.locator('[data-st-new]').first().click();
    await expect(page.locator('[data-st-modal-title]')).toHaveText('Add calendar day');
    await expect(page.locator('#stDate')).toHaveValue('');
  });

  test('an entry in another month opens that month', async ({ page }) => {
    const base = inDays(300);
    const nextMonth = new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth() + 1, 9));
    await page.goto(viewOf(base));
    await addDay(page, iso(nextMonth), uid('Later'));
    await expect(page).toHaveURL(new RegExp(`year=${nextMonth.getUTCFullYear()}&month=${nextMonth.getUTCMonth() + 1}`));
    expect(await toast(page, 'saved')).toContain('saved');
    await expect(page.locator(`[data-st-date="${iso(nextMonth)}"][data-st-id]`)).toHaveCount(1);
  });

  test('a refused entry is explained and the modal stays open', async ({ page }) => {
    const day = inDays(420);
    await page.goto(viewOf(day));
    const navs = countNavigations(page);
    await addDay(page, iso(day), 'First');
    await expect(page.locator(`[data-st-date="${iso(day)}"][data-st-id]`)).toHaveCount(1);
    await addDay(page, iso(day), 'Second one on the same date');
    await expect(page.locator('.cg-toast').filter({ hasText: /already|exists|date/i }).first()).toBeVisible();
    await expect(page.locator('#dayModal')).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('removing a coming-up entry updates the list in place', async ({ page }) => {
    const title = uid('Soon');
    const inNine = new Date(Date.now() + 9 * 86400000);
    const date = inNine.toISOString().slice(0, 10);
    await page.goto(`/SchoolTime?year=${inNine.getUTCFullYear()}&month=${inNine.getUTCMonth() + 1}`);
    await addDay(page, date, title);
    await expect(page.locator('.cg-toast', { hasText: 'saved' }).first()).toBeVisible();
    const navs = countNavigations(page);
    const entry = page.locator('li, tr', { hasText: title });
    await expect(entry.first()).toBeVisible();
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: `Remove ${title}` }).click();
    expect(await toast(page, 'Calendar entry removed.')).toContain('removed');
    await expect(page.getByRole('button', { name: `Remove ${title}` })).toHaveCount(0);
    expect(navs.count).toBe(0);
  });

  test('saving the school timings stays on the page', async ({ page }) => {
    await page.goto('/SchoolTime');
    const navs = countNavigations(page);
    // Unchanged values: the school-wide late and dismissal cut-offs other specs depend on stay as they are.
    await page.locator('form[action*="SaveSettings"] button[type="submit"]').click();
    expect(await toast(page, 'School timings saved.')).toContain('saved');
    expect(navs.count).toBe(0);
  });
});
