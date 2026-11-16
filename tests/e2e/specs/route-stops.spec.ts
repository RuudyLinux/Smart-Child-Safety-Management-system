import path from 'node:path';
import { Page } from '@playwright/test';
import { test, expect, toast } from '../support/fixtures';

const SHOTS = path.join(__dirname, '..', 'test-results', 'phase3d-walkthrough');
const shot = async (page: Page, name: string) => {
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
};

test.use({ viewport: { width: 1366, height: 1500 } });

async function addStop(page: Page, name: string, pickup?: string) {
  await page.getByRole('button', { name: 'Add stop' }).click();
  const modal = page.getByRole('dialog', { name: 'Add a stop' });
  await modal.getByLabel(/Stop name/).fill(name);
  if (pickup) await modal.getByLabel('Pickup time').fill(pickup);
  await modal.getByRole('button', { name: 'Save stop' }).click();
  await expect(page.locator('[data-stop-row]', { hasText: name })).toHaveCount(1);
}

const stopNames = (page: Page) => page.locator('[data-stop-row] [data-stop-name]').allTextContents();

test.describe('Routes and stops', () => {
  test('stops are added, reordered and assigned to a student @smoke', async ({ page, data, api }) => {
    const route = await data.route();
    const bus = await data.bus(undefined, route.id);
    const s = await data.student();
    expect((await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: bus.id } })).ok()).toBe(true);

    await page.goto('/Transportation');
    await page.getByRole('link', { name: route.routeName }).click();
    await expect(page).toHaveURL(new RegExp(`/Transportation/Route/${route.id}$`));

    await page.getByRole('button', { name: 'Edit start and destination' }).click();
    const ends = page.getByRole('dialog', { name: 'Start and destination' });
    await ends.getByLabel('Start point').fill('Depot');
    await ends.getByLabel('Destination').fill('School gate');
    await ends.getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('[data-route-ends]')).toHaveText('Depot → School gate');

    await addStop(page, 'Lake View', '07:10');
    await addStop(page, 'Market Square', '07:25');
    await expect(page.locator('[data-stop-count]')).toHaveText('2');
    expect(await stopNames(page)).toEqual(['Lake View', 'Market Square']);

    await page.getByRole('button', { name: 'Move Market Square up' }).click();
    expect(await toast(page, 'Stop order saved')).toContain('Stop order saved');
    expect(await stopNames(page)).toEqual(['Market Square', 'Lake View']);

    const row = page.locator('[data-roster-row]', { hasText: s.fullName });
    await row.getByLabel(`Pickup stop for ${s.fullName}`).selectOption({ label: '2. Lake View' });
    await row.getByLabel(`Drop-off stop for ${s.fullName}`).selectOption({ label: '1. Market Square' });
    await row.getByRole('button', { name: 'Save' }).click();
    expect(await toast(page, 'Stops saved')).toContain('Stops saved');
    await shot(page, '01-route-stops');

    // A stop in use cannot be removed.
    await expect(page.locator('[data-stop-row]', { hasText: 'Lake View' }).getByRole('button', { name: 'Remove' })).toBeDisabled();

    // The student's profile shows bus, route, stops and times.
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('tab', { name: /RFID/ }).click();
    await expect(page.locator('[data-pickup-stop]')).toHaveText('Lake View · 07:10');
    await expect(page.locator('[data-drop-stop]')).toHaveText('Market Square');
    await expect(page.locator('[data-student-transport]')).toContainText(route.routeName);
    await shot(page, '02-student-transport');

    // The bus page lists the route's stops in order.
    await page.goto(`/Transportation/Bus/${bus.id}`);
    await expect(page.locator('[data-bus-route-stops] li')).toHaveText([/Market Square/, /Lake View/]);
  });

  test('an unused stop is removed; a route with a bus cannot be deleted; a free one can', async ({ page, data }) => {
    const route = await data.route();
    await page.goto(`/Transportation/Route/${route.id}`);
    await addStop(page, 'Temporary');
    page.once('dialog', d => d.accept());
    await page.locator('[data-stop-row]', { hasText: 'Temporary' }).getByRole('button', { name: 'Remove' }).click();
    await expect(page.locator('[data-stop-row]')).toHaveCount(0);
    await expect(page.locator('body')).toContainText('No stops yet');

    const busy = await data.route();
    await data.bus(undefined, busy.id);
    await page.goto(`/Transportation/Route/${busy.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(page.locator('[data-delete-blockers]')).toContainText('bus');

    await page.goto(`/Transportation/Route/${route.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const modal = page.getByRole('dialog', { name: `Permanently delete route ${route.routeName}?` });
    await modal.getByLabel(/Reason/).fill('Created twice by mistake.');
    await modal.getByLabel(/I understand/).check();
    await modal.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(page).toHaveURL(/\/Transportation$/);
    expect(await toast(page, 'permanently deleted')).toContain('permanently deleted');
  });

  test('enrollment offers the stops of the chosen bus route', async ({ page, data, api }) => {
    const route = await data.route();
    const stop = await (await api.post(`/api/transportation/routes/${route.id}/stops`, { data: { name: 'Temple Road', pickupTime: '07:05:00' } })).json();
    const bus = await data.bus(undefined, route.id);

    await page.goto('/Enrollment');
    // Jump straight to the transport step markup: the selects are filled from the bus choice.
    const panel = page.locator('[data-en-panel="4"]');
    await panel.evaluate(el => { el.hidden = false; (el.querySelector('[data-en-transport-bus]') as HTMLElement).hidden = false; });
    await panel.locator('#enBus').selectOption(String(bus.id));
    await expect(panel.locator('[data-en-stops]')).toBeVisible();
    await expect(panel.locator('#enPickupStop option')).toHaveText(['Not decided yet', '1. Temple Road · 07:05']);
    await panel.locator('#enPickupStop').selectOption(String(stop.routeStopId));
    await shot(page, '03-enrollment-stops');
  });
});
