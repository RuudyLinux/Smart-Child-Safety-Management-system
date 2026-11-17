// Transportation on the shared AJAX layer: every page saves in place, the console's assignments list is
// server-paged with its state in the URL, and a refusal is explained without leaving the page.
import { test, expect, toast, uid, PNG_1PX } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

test.describe('Transportation without page reloads', () => {
  // Refusals answer 400/409 on purpose; the browser logs them.
  test.use({ allowErrors: [/status of 40[019]/] });

  test('adding a driver, a route and a bus refreshes the console in place @smoke', async ({ page }) => {
    const driver = uid('AjaxDriver');
    const route = uid('AjaxRoute');
    const bus = 'AJX-' + Math.random().toString(36).slice(2, 6).toUpperCase();
    await page.goto('/Transportation');
    const navs = countNavigations(page);

    await page.getByRole('button', { name: 'Add driver' }).first().click();
    const dm = page.locator('#addDriverModal');
    await dm.locator('input[name="fullName"]').fill(driver);
    await dm.getByRole('button', { name: 'Add driver' }).click();
    expect(await toast(page, `Driver ${driver} added.`)).toContain(driver);
    await expect(dm).toBeHidden();

    await page.getByRole('button', { name: 'Add route' }).first().click();
    await page.locator('#addRouteModal input[name="routeName"]').fill(route);
    await page.locator('#addRouteModal').getByRole('button', { name: 'Add route' }).click();
    expect(await toast(page, `Route ${route} added.`)).toContain(route);

    await page.getByRole('button', { name: 'Add bus' }).first().click();
    const bm = page.locator('#addBusModal');
    await bm.locator('input[name="busNumber"]').fill(bus);
    await bm.locator('input[name="capacity"]').fill('30');
    // The new driver and route are already offered: the selects were refreshed with the page.
    await bm.locator('select[name="driverId"]').selectOption({ label: driver });
    await bm.locator('select[name="routeId"]').selectOption({ label: route });
    await bm.getByRole('button', { name: 'Add bus' }).click();
    expect(await toast(page, `Bus ${bus} added.`)).toContain(bus);

    const row = page.locator('[data-cg-region="fleet-console"] .cg-card', { hasText: bus }).first();
    await expect(row).toContainText(driver);
    expect(navs.count).toBe(0);
  });

  test('a refused add explains why and leaves the form open', async ({ page, data }) => {
    const existing = await data.route();
    await page.goto('/Transportation');
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Add route' }).first().click();
    const rm = page.locator('#addRouteModal');
    await rm.locator('input[name="routeName"]').fill(existing.routeName.toLowerCase());   // same name, different case
    await rm.getByRole('button', { name: 'Add route' }).click();
    await expect(page.locator('.cg-toast', { hasText: /already|exists|unique/i }).first()).toBeVisible();
    await expect(rm).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('editing a driver in its modal updates the table in place', async ({ page, data }) => {
    const d = await data.driver();
    const renamed = uid('RenamedDriver');
    await page.goto('/Transportation');
    const navs = countNavigations(page);
    await page.getByRole('button', { name: `Edit ${d.fullName}` }).click();
    const modal = page.locator(`#editDriver-${d.id}`);
    await modal.locator('input[name="fullName"]').fill(renamed);
    await modal.getByRole('button', { name: 'Save driver' }).click();
    expect(await toast(page, `Driver ${renamed} updated.`)).toContain('updated');
    // The Drivers table shows the new name; the audit list below keeps the old one on purpose (it is history).
    const drivers = page.locator('section.cg-card', { has: page.getByRole('heading', { name: 'Drivers', exact: true }) });
    await expect(drivers).toContainText(renamed);
    await expect(drivers).not.toContainText(d.fullName);
    expect(navs.count).toBe(0);
  });

  test('deactivate and reactivate a driver in place', async ({ page, data }) => {
    const d = await data.driver();
    await page.goto('/Transportation');
    const navs = countNavigations(page);
    page.once('dialog', x => x.accept());
    await page.getByRole('button', { name: `Deactivate ${d.fullName}` }).click();
    expect(await toast(page, 'Driver deactivated.')).toContain('deactivated');
    page.once('dialog', x => x.accept());
    await page.getByRole('button', { name: `Reactivate ${d.fullName}` }).click();
    expect(await toast(page, 'Driver reactivated.')).toContain('reactivated');
    await expect(page.getByRole('button', { name: `Deactivate ${d.fullName}` })).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('bus page: changing the status updates the page in place', async ({ page, data }) => {
    const route = await data.route();
    const bus = await data.bus(undefined, route.id);
    await page.goto(`/Transportation/Bus/${bus.id}`);
    const navs = countNavigations(page);
    await page.locator('#busStatus').selectOption({ label: 'Under maintenance' });
    await page.locator('#busStatusReason').fill('Brake service');
    await page.getByRole('button', { name: 'Update status' }).click();
    expect(await toast(page, 'Bus status set to')).toContain('maintenance');
    await expect(page.locator('[data-cg-region="bus-page"]')).toContainText(/maintenance/i);
    expect(navs.count).toBe(0);
  });

  test('driver page: the profile and the photo save in place', async ({ page, data }) => {
    const d = await data.driver();
    await page.goto(`/Transportation/Driver/${d.id}`);
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    const modal = page.locator('#driverProfile');
    await modal.locator('input[name="employeeCode"], input[name="EmployeeCode"]').first().fill('EMP-' + Date.now().toString().slice(-6));
    await modal.getByRole('button', { name: /Save/ }).click();
    expect(await toast(page, 'Driver profile saved.')).toContain('saved');

    const panel = page.locator('[data-driver-photo]');
    await panel.locator('input[type="file"]').setInputFiles({ name: 'd.png', mimeType: 'image/png', buffer: PNG_1PX });
    await panel.locator('[data-cg-upload-submit]').click();
    expect(await toast(page, 'Driver photo saved')).toContain('saved');
    await expect(page.locator('[data-driver-photo]')).toContainText('Photo on file');
    expect(navs.count).toBe(0);
  });

  test('route page: adding and moving stops need no page load', async ({ page, data }) => {
    const route = await data.route();
    await page.goto(`/Transportation/Route/${route.id}`);
    const navs = countNavigations(page);
    for (const name of ['First Stop', 'Second Stop']) {
      await page.getByRole('button', { name: 'Add stop' }).click();
      const modal = page.getByRole('dialog', { name: 'Add a stop' });
      await modal.getByLabel(/Stop name/).fill(name);
      await modal.getByRole('button', { name: 'Save stop' }).click();
      await expect(page.locator('[data-stop-row]', { hasText: name })).toHaveCount(1);
    }
    await page.locator('[data-stop-row]', { hasText: 'Second Stop' }).getByRole('button', { name: /up/i }).click();
    await expect.poll(async () => (await page.locator('[data-stop-row] [data-stop-name]').allTextContents()).join('|'))
      .toBe('Second Stop|First Stop');
    expect(navs.count).toBe(0);
  });

  test('deleting a bus with no history goes back to the console', async ({ page, data }) => {
    const bus = await data.bus();
    await page.goto(`/Transportation/Bus/${bus.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const dialog = page.getByRole('dialog', { name: /Permanently delete/ });
    await dialog.locator('textarea[name="reason"], input[name="reason"]').first().fill('Created by mistake during setup');
    await dialog.locator('input[name="confirmed"]').check();
    await dialog.getByRole('button', { name: /Delete/ }).last().click();
    await expect(page).toHaveURL(/\/Transportation$/);
    expect(await toast(page, 'deleted')).toBeTruthy();
  });
});

test.describe('Assignments list (server paging)', () => {
  const rows = (page: import('@playwright/test').Page) => page.locator('[data-cg-region="list"] tr[data-row]');

  async function busWithStudents(data: any, api: any, tag: string, count: number) {
    const route = await data.route();
    const bus = await data.bus(undefined, route.id);
    const names: string[] = [];
    for (let i = 0; i < count; i++) {
      const s = await data.student({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
      const r = await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: bus.id } });
      expect(r.ok(), await r.text()).toBeTruthy();
      names.push(s.fullName);
    }
    return { bus, route, names };
  }

  test('search, status filter, sort and pages run on the server and keep the URL', async ({ page, data, api }) => {
    const tag = uid('Asg').replace(/\s/g, '');
    const { bus } = await busWithStudents(data, api, tag, 27);
    await page.goto('/Transportation');
    const navs = countNavigations(page);

    await page.locator('input[name="search"]').fill(tag);
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    await expect(rows(page)).toHaveCount(25);
    await expect(page.locator('[data-cg-count][data-cg-count-scope="assignments"]')).toHaveText('27 records');

    await page.locator('[data-cg-sort="student"]').click();
    await expect(page.locator('[data-cg-sort="student"]')).toHaveClass(/is-asc/);
    expect((await rows(page).first().innerText())).toContain(`${tag} 00`);
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(rows(page)).toHaveCount(2);
    expect(navs.count).toBe(0);

    await page.reload();                                         // the URL holds the whole view
    await expect(rows(page)).toHaveCount(2);
    await page.goBack();
    await expect(rows(page)).toHaveCount(25);

    await page.locator('select[name="status"]').selectOption({ label: 'Ended' });
    await expect(page.locator('[data-cg-noresults]')).toBeVisible();
    await expect(page.locator('[data-cg-count][data-cg-count-scope="assignments"]')).toHaveText('0 records');
    expect(bus.id).toBeGreaterThan(0);
  });

  test('ending an assignment keeps the search and page; changing a bus works from any page', async ({ page, data, api }) => {
    const tag = uid('AsgEnd').replace(/\s/g, '');
    const { names } = await busWithStudents(data, api, tag, 3);
    const other = await data.bus();
    await page.goto(`/Transportation?search=${tag}&sort=student&dir=asc`);
    await expect(rows(page)).toHaveCount(3);
    const navs = countNavigations(page);

    page.once('dialog', d => d.accept());
    await rows(page).filter({ hasText: names[0] }).getByRole('button', { name: 'End' }).click();
    expect(await toast(page, 'Bus assignment ended')).toContain('ended');
    // Still the same search, sort and list; the ended row shows as Ended.
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    await expect(rows(page).filter({ hasText: names[0] })).toContainText('Ended');

    await rows(page).filter({ hasText: names[1] }).getByRole('button', { name: 'Change bus' }).click();
    const modal = page.locator('.cg-modal:not([hidden])');
    await modal.locator('select[name="busId"]').selectOption(String(other.id));
    await modal.getByRole('button', { name: 'Change bus' }).click();
    expect(await toast(page, 'Student assigned to bus')).toContain('assigned');
    await expect(rows(page).filter({ hasText: other.busNumber })).toHaveCount(1);
    expect(navs.count).toBe(0);
  });

  test('a page past the end offers the last page', async ({ page }) => {
    await page.goto('/Transportation?page=9999');
    await expect(page.locator('[data-cg-page-missing]')).toContainText('There is no page 9999');
  });

  test('the console works without JavaScript', async ({ browser, data, api }) => {
    const tag = uid('AsgNoJs').replace(/\s/g, '');
    await busWithStudents(data, api, tag, 26);
    const context = await browser.newContext({ javaScriptEnabled: false, storageState: '.e2e/admin.json' });
    const page = await context.newPage();
    await page.goto('/Transportation');
    await page.locator('input[name="search"]').fill(tag);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.locator('[data-cg-region="list"] tr[data-row]')).toHaveCount(25);
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(page.locator('[data-cg-region="list"] tr[data-row]')).toHaveCount(1);
    await context.close();
  });
});
