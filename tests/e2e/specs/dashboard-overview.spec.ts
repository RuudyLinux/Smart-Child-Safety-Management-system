import path from 'node:path';
import { test, expect } from '../support/fixtures';

const SHOTS = path.join(__dirname, '..', 'test-results', 'phase3e-walkthrough');

test.use({ viewport: { width: 1366, height: 2200 } });

// Browser projects share one stack and run in parallel, so global totals are checked in the API integration
// tests; here each assertion is about a record this test created.
test.describe('Dashboard overview', () => {
  test('people, RFID and fleet KPIs, bus loads and admin alerts @smoke', async ({ page, data, api }) => {
    const s = await data.student();
    // One seat, so once taken the bus is 100% full and always among the fullest buses listed.
    const busNumber = 'BUS-' + Math.random().toString(36).slice(2, 7).toUpperCase();
    const created = await api.post('/api/transportation/buses', { data: { busNumber, capacity: 1 } });
    expect(created.ok()).toBe(true);
    const bus = { id: (await created.json()).busId as number, busNumber };

    await page.goto('/');
    for (const card of ['people', 'rfid', 'fleet', 'gaps']) await expect(page.locator(`[data-kpi="${card}"]`)).toBeVisible();
    for (const key of ['students-active', 'rfid-available', 'buses-active', 'gap-transport', 'licence-expired'])
      await expect(page.locator(`[data-kpi-value="${key}"]`)).toHaveText(/^\d+$/);
    const row = page.locator(`[data-bus-load="${bus.busNumber}"]`);

    expect((await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: bus.id } })).ok()).toBe(true);
    expect((await api.patch(`/api/transportation/buses/${bus.id}/operational-status`, { data: { status: 'UnderMaintenance', reason: 'Brakes' } })).ok()).toBe(true);
    await page.reload();
    await expect(row.locator('td').nth(2)).toHaveText('1');
    await expect(row).toContainText('Under maintenance');
    await row.getByRole('link', { name: bus.busNumber }).click();
    await expect(page).toHaveURL(new RegExp(`/Transportation/Bus/${bus.id}$`));

    await page.goto('/');
    await page.screenshot({ path: path.join(SHOTS, '01-dashboard-overview.png'), fullPage: true });
  });
});
