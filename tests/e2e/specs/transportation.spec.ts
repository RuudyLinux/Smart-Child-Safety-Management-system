import { test, expect, uid, toast } from '../support/fixtures';

test.describe('Transportation', () => {
  test('add a driver, a route and a bus through the modals @smoke', async ({ page }) => {
    const driver = uid('Driver');
    const route = uid('Route');
    const bus = 'BUS-' + Math.random().toString(36).slice(2, 6).toUpperCase();
    await page.goto('/Transportation');

    await page.getByRole('button', { name: 'Add driver' }).first().click();
    const dm = page.locator('#addDriverModal');
    await expect(dm).toBeVisible();
    await dm.locator('input[name="fullName"]').fill(driver);
    await dm.locator('input[name="phone"]').fill('9876543210');
    await dm.locator('input[name="licenseNumber"]').fill('DL-0420110012345');
    await dm.getByRole('button', { name: 'Add driver' }).click();
    await toast(page);
    await expect(page.locator('main')).toContainText(driver);

    await page.getByRole('button', { name: 'Add route' }).first().click();
    const rm = page.locator('#addRouteModal');
    await rm.locator('input[name="routeName"]').fill(route);
    await rm.locator('textarea[name="description"]').fill('North gate, Library, Park');
    await rm.getByRole('button', { name: 'Add route' }).click();
    await toast(page);
    await expect(page.locator('main')).toContainText(route);

    await page.getByRole('button', { name: 'Add bus' }).first().click();
    const bm = page.locator('#addBusModal');
    await bm.locator('input[name="busNumber"]').fill(bus);
    await bm.locator('input[name="capacity"]').fill('32');
    await bm.locator('select[name="driverId"]').selectOption({ label: driver });
    await bm.locator('select[name="routeId"]').selectOption({ label: route });
    await bm.getByRole('button', { name: 'Add bus' }).click();
    await toast(page);
    const busRow = page.locator('main tr, main .cg-card', { hasText: bus }).first();
    await expect(busRow).toContainText(driver);
    await expect(busRow).toContainText(route);
  });

  test('modals close with Cancel, the close icon, Escape and the backdrop without saving', async ({ page }) => {
    await page.goto('/Transportation');
    const modal = page.locator('#addDriverModal');
    const open = () => page.getByRole('button', { name: 'Add driver' }).first().click();

    await open(); await modal.getByRole('button', { name: 'Cancel' }).click(); await expect(modal).toBeHidden();
    await open(); await modal.locator('[data-cg-modal-close]').first().click(); await expect(modal).toBeHidden();
    await open(); await page.keyboard.press('Escape'); await expect(modal).toBeHidden();
    await open(); await page.mouse.click(5, 5); await expect(modal).toBeHidden();
  });

  test.describe('validation', () => {
    test.use({ allowErrors: [/status of 400/] });
    test('bus capacity and required fields are enforced', async ({ page }) => {
      await page.goto('/Transportation');
      await page.getByRole('button', { name: 'Add bus' }).first().click();
      const bm = page.locator('#addBusModal');
      await bm.getByRole('button', { name: 'Add bus' }).click();
      expect(await bm.locator('input[name="busNumber"]').evaluate((e: HTMLInputElement) => e.validity.valid)).toBe(false);
      await bm.locator('input[name="busNumber"]').fill('BUS-ZERO');
      await bm.locator('input[name="capacity"]').fill('0');
      await bm.getByRole('button', { name: 'Add bus' }).click();
      // Refused by the browser (min) or the API (error toast); never saved.
      await page.goto('/Transportation');
      await expect(page.locator('main')).not.toContainText('BUS-ZERO');
    });
  });

  test('assign a student to a bus', async ({ page, data }) => {
    const s = await data.student();
    const r = await data.route();
    const b = await data.bus(undefined, r.id);
    await page.goto('/Transportation');
    const opener = page.locator('[data-cg-modal-open]', { hasText: /assign student/i }).first();
    await expect(opener, 'a button opens the "Assign student" modal').toBeVisible();
    await opener.click();
    const form = page.locator('form[action$="/Transportation/AddAssignment"]');
    await form.locator('select[name="studentId"]').selectOption(String(s.id));
    await form.locator('select[name="busId"]').selectOption(String(b.id));
    await form.getByRole('button', { name: 'Assign student' }).click();
    await toast(page);
    await expect(page.locator('main')).toContainText(s.fullName);
  });

  test('edit and deactivate a driver and a route', async ({ page, data }) => {
    const d = await data.driver();
    const r = await data.route();
    await page.goto('/Transportation');

    await page.getByRole('button', { name: `Edit ${d.fullName}` }).click();
    const dm = page.locator(`#editDriver-${d.id}`);
    const newName = uid('Renamed');
    await dm.locator('input[name="fullName"]').fill(newName);
    await dm.locator('input[name="phone"]').fill('9000011111');
    await dm.getByRole('button', { name: 'Save driver' }).click();
    await expect(page.locator('.cg-toast', { hasText: `Driver ${newName} updated` })).toBeVisible();
    const driverRow = page.locator('main tr', { hasText: newName });
    await expect(driverRow).toContainText('9000011111');

    page.once('dialog', dlg => dlg.accept());
    await driverRow.getByRole('button', { name: 'Deactivate' }).click();
    await expect(page.locator('.cg-toast', { hasText: 'Driver deactivated' })).toBeVisible();
    await expect(page.locator('main tr', { hasText: newName })).toContainText('Inactive');
    // An inactive driver is not offered for a new bus.
    await expect(page.locator('#addBusModal select[name="driverId"] option', { hasText: newName })).toHaveCount(0);

    await page.getByRole('button', { name: `Edit ${r.routeName}` }).click();
    const rm = page.locator(`#editRoute-${r.id}`);
    const newRoute = uid('Loop');
    await rm.locator('input[name="routeName"]').fill(newRoute);
    await rm.getByRole('button', { name: 'Save route' }).click();
    await expect(page.locator('.cg-toast', { hasText: `Route ${newRoute} updated` })).toBeVisible();
    page.once('dialog', dlg => dlg.accept());
    await page.locator('main tr', { hasText: newRoute }).getByRole('button', { name: 'Deactivate' }).click();
    await expect(page.locator('.cg-toast', { hasText: 'Route deactivated' })).toBeVisible();
    await expect(page.locator('main tr', { hasText: newRoute })).toContainText('Inactive');
  });

  test('edit a bus, take it out of service and back', async ({ page, data }) => {
    const d = await data.driver();
    const bus = await data.bus(d.id);
    await page.goto('/Transportation');
    const card = () => page.locator('.cg-personcard', { hasText: bus.busNumber });

    await card().getByRole('button', { name: 'Edit' }).click();
    const bm = page.locator(`#editBus-${bus.id}`);
    await expect(bm.locator('select[name="driverId"]')).toHaveValue(String(d.id)); // current driver preselected
    await bm.locator('input[name="capacity"]').fill('55');
    await bm.getByRole('button', { name: 'Save bus' }).click();
    await expect(page.locator('.cg-toast', { hasText: `Bus ${bus.busNumber} updated` })).toBeVisible();
    await expect(card()).toContainText('of 55 seats');

    page.once('dialog', dlg => dlg.accept());
    await card().getByRole('button', { name: 'Take out of service' }).click();
    await expect(page.locator('.cg-toast', { hasText: 'out of service' })).toBeVisible();
    await expect(card()).toContainText('Out of service');
    page.once('dialog', dlg => dlg.accept());
    await card().getByRole('button', { name: 'Return to service' }).click();
    await expect(card()).toContainText('In service');
  });

  test('change a student\'s bus, then end the assignment; history is kept', async ({ page, data, api }) => {
    const s = await data.student();
    const bus1 = await data.bus();
    const bus2 = await data.bus();
    expect((await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: bus1.id } })).ok()).toBeTruthy();
    await page.goto('/Transportation');
    await page.locator('[data-cg-search-scope="assignments"]').fill(s.fullName);

    const active = () => page.locator('tr[data-row]', { hasText: s.fullName }).filter({ hasText: 'Active' });
    await active().getByRole('button', { name: 'Change bus' }).click();
    const modal = page.locator('.cg-modal:not([hidden])');
    await modal.locator('select[name="busId"]').selectOption({ label: bus2.busNumber });
    await modal.getByRole('button', { name: 'Change bus' }).click();
    await expect(page.locator('.cg-toast', { hasText: 'Student assigned to bus' })).toBeVisible();
    await page.locator('[data-cg-search-scope="assignments"]').fill(s.fullName);
    await expect(active()).toContainText(bus2.busNumber);
    await expect(page.locator('tr[data-row]', { hasText: s.fullName }).filter({ hasText: 'Ended' })).toContainText(bus1.busNumber);

    page.once('dialog', dlg => dlg.accept());
    await active().getByRole('button', { name: 'End' }).click();
    await expect(page.locator('.cg-toast', { hasText: 'Bus assignment ended' })).toBeVisible();
    await page.locator('[data-cg-search-scope="assignments"]').fill(s.fullName);
    await expect(active()).toHaveCount(0);
    await expect(page.locator('tr[data-row]', { hasText: s.fullName })).toHaveCount(2); // both kept as history
    // With no active bus the student can be assigned again.
    await expect(page.locator('#assignStudent option', { hasText: s.fullName })).toHaveCount(1);
  });
});
