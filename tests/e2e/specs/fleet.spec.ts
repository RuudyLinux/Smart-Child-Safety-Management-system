import path from 'node:path';
import { Page } from '@playwright/test';
import { test, expect, toast } from '../support/fixtures';

const SHOTS = path.join(__dirname, '..', 'test-results', 'phase3c-walkthrough');
const shot = async (page: Page, name: string) => {
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
};

test.use({ viewport: { width: 1366, height: 1500 } });

test.describe('Drivers and buses', () => {
  test('driver profile, licence warning and live roster @smoke', async ({ page, data, api }) => {
    const driver = await data.driver();
    const route = await data.route();
    const bus = await data.bus(driver.id, route.id);
    const s = await data.student();
    expect((await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: bus.id } })).ok()).toBe(true);

    await page.goto('/Transportation');
    await page.getByRole('link', { name: driver.fullName }).click();
    await expect(page).toHaveURL(new RegExp(`/Transportation/Driver/${driver.id}$`));
    await expect(page.locator('[data-roster-row]', { hasText: s.fullName })).toHaveCount(1);

    await page.getByRole('button', { name: 'Edit profile' }).click();
    const modal = page.getByRole('dialog', { name: 'Edit driver profile' });
    await modal.locator('#dpCode').fill('DRV-' + Date.now().toString(36).toUpperCase());
    await modal.locator('#dpCat').fill('HTV');
    await modal.locator('#dpExp').fill('2026-11-20'); // within 30 days of the test clock
    await modal.locator('#dpEcName').fill('Sita');
    await modal.locator('#dpCity').fill('Bengaluru');
    await modal.getByRole('button', { name: 'Save profile' }).click();
    expect(await toast(page)).toContain('Driver profile saved');
    await expect(page.locator('[data-licence-alert]')).toContainText('20 November 2026');
    await expect(page.locator('body')).toContainText('HTV');
    await shot(page, '01-driver-profile');
  });

  test('bus details page: vehicle details, maintenance status and roster count', async ({ page, data, api }) => {
    const bus = await data.bus();
    const s = await data.student();
    expect((await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: bus.id } })).ok()).toBe(true);

    await page.goto(`/Transportation/Bus/${bus.id}`);
    await expect(page.locator('[data-student-count]')).toHaveText('1');
    await expect(page.locator('[data-roster-row]', { hasText: s.fullName })).toHaveCount(1);

    await page.getByRole('button', { name: 'Edit vehicle details' }).click();
    const modal = page.getByRole('dialog', { name: 'Edit vehicle details' });
    await modal.locator('#bdReg').fill('ka 05 mn ' + Math.floor(1000 + Math.random() * 8999));
    await modal.locator('#bdMake').fill('Tata');
    await modal.locator('#bdModel').fill('Starbus');
    await modal.getByRole('button', { name: 'Save' }).click();
    expect(await toast(page)).toContain('Bus details saved');
    await expect(page.locator('.cg-page-head')).toContainText('Registration KA 05 MN');

    await page.locator('#busStatus').selectOption('UnderMaintenance');
    await page.locator('#busStatusReason').fill('Brake service');
    await page.getByRole('button', { name: 'Update status' }).click();
    await expect(page.locator('.cg-page-head [data-bus-status]')).toHaveText('Under maintenance');
    await expect(page.locator('body')).toContainText('still assigned to this bus while it is not in service');
    await shot(page, '02-bus-maintenance');
  });

  test('a bus with history cannot be deleted; an unused one can', async ({ page, data, api }) => {
    const used = await data.bus();
    const s = await data.student();
    expect((await api.post('/api/transportation/assignments', { data: { studentId: s.id, busId: used.id } })).ok()).toBe(true);
    await page.goto(`/Transportation/Bus/${used.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(page.locator('[data-delete-blockers]')).toContainText('student assignment');

    const fresh = await data.bus();
    await page.goto(`/Transportation/Bus/${fresh.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const modal = page.getByRole('dialog', { name: `Permanently delete bus ${fresh.busNumber}?` });
    await modal.getByLabel(/Reason/).fill('Created twice by mistake.');
    await modal.getByLabel(/I understand/).check();
    await modal.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(page).toHaveURL(/\/Transportation$/);
    expect(await toast(page)).toContain('permanently deleted');
  });
});
