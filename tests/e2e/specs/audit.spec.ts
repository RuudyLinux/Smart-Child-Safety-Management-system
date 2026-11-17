import path from 'node:path';
import { Page } from '@playwright/test';
import { test, expect, uid, toast } from '../support/fixtures';
import { URLS } from '../support/stack';

const SHOTS = path.join(__dirname, '..', 'test-results', 'phase2-walkthrough');
const shot = async (page: Page, name: string) => {
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
};

test.use({ viewport: { width: 1366, height: 1500 } });

async function fillAddress(page: Page, a: Record<string, string>) {
  const form = page.locator('form[data-address-form]');
  for (const [name, value] of Object.entries(a)) await form.locator(`[name="${name}"]`).fill(value);
  return form;
}

test.describe('Addresses and audit log', () => {
  test('edit a student address from the profile, then see it in the audit log @smoke', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('link', { name: 'Edit address' }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Edit/${s.id}#address$`));

    const form = await fillAddress(page, {
      Line1: '42 Lake Shore Drive', Locality: 'Koramangala', City: 'Bengaluru', District: 'Bengaluru Urban',
      State: 'Karnataka', PostalCode: '560034',
    });
    await shot(page, '01-student-address-form');
    await form.getByRole('button', { name: 'Save address' }).click();

    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
    expect(await toast(page)).toContain('Address saved');
    await expect(page.locator('address')).toContainText('42 Lake Shore Drive');
    await expect(page.locator('address')).toContainText('Karnataka 560034');

    await page.getByRole('link', { name: 'View audit history' }).click();
    await expect(page).toHaveURL(new RegExp(`/Audit\\?studentId=${s.id}$`));
    const rows = page.locator('tr[data-audit-row]');
    await expect(rows.first()).toHaveAttribute('data-action', 'StudentAddressUpdated');
    await expect(page.locator('tr[data-audit-row][data-action="StudentCreated"]')).toHaveCount(1);
    await expect(rows.first()).toContainText('E2E Administrator');
    await shot(page, '02-student-audit-history');
  });

  test.describe('refused input', () => {
    // The refusal is an HTTP 400 answer to the form's request, which the browser logs.
    test.use({ allowErrors: [/status of 400/] });

    test('an invalid postal code is refused and nothing changes', async ({ page, data }) => {
      const s = await data.student();
      await page.goto(`/Students/Edit/${s.id}`);
      const form = page.locator('form[data-address-form]');
      // Bypass the field's pattern so the server-side check is what refuses it.
      await form.locator('[name="PostalCode"]').evaluate((el: HTMLInputElement) => el.removeAttribute('pattern'));
      await fillAddress(page, { Line1: '1 Test Road', PostalCode: '<56>' });
      await form.getByRole('button', { name: 'Save address' }).click();
      // The refusal is shown in place (AJAX), so the page stays on the edit form.
      expect(await toast(page, 'ostal code')).toMatch(/postal code/i);
      await expect(page).toHaveURL(new RegExp(`/Students/Edit/${s.id}(#address)?$`));
      await page.goto(`/Students/Details/${s.id}`);
      await expect(page.locator('body')).not.toContainText('1 Test Road');
    });
  });

  test('edit a guardian address', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Edit/${g.id}`);
    const form = await fillAddress(page, { Line1: '9 Temple Street', City: 'Chennai', State: 'Tamil Nadu', PostalCode: '600004' });
    await form.getByRole('button', { name: 'Save address' }).click();
    await expect(page).toHaveURL(new RegExp(`/Guardians/Details/${g.id}$`));
    await expect(page.locator('address')).toContainText('9 Temple Street');
    await page.goto(`/Audit?guardianId=${g.id}`);
    await expect(page.locator('tr[data-audit-row][data-action="GuardianAddressUpdated"]')).toHaveCount(1);
    await expect(page.locator('tr[data-audit-row][data-action="GuardianCreated"]')).toHaveCount(1);
  });

  test('the audit log filters by record type and shows redacted details', async ({ page, data }) => {
    const s = await data.student({ fullName: uid('Filtered') });
    await page.goto('/Audit');
    await expect(page.getByRole('heading', { name: 'Audit Log' })).toBeVisible();
    await page.getByLabel('Record type').selectOption('Student');
    await page.getByLabel('Search summaries').fill(s.fullName);
    await page.getByRole('button', { name: 'Apply' }).click();
    await expect(page).toHaveURL(/entityType=Student/);
    const row = page.locator('tr[data-audit-row]');
    await expect(row).toHaveCount(1);
    await expect(row).toHaveAttribute('data-action', 'StudentCreated');
    await row.locator('summary', { hasText: 'Details' }).click();
    await expect(row.locator('pre')).toContainText(s.fullName);
    await shot(page, '03-audit-log');
  });

  test('the audit log is only for signed-in admins', async ({ browser }) => {
    const anonymous = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const page = await anonymous.newPage();
    await page.goto(`${URLS.web}/Audit`);
    await expect(page).toHaveURL(/\/Account\/Login/);
    const api = await anonymous.request.get(`${URLS.api}/api/audit`);
    expect(api.status()).toBe(401);
    await anonymous.close();
  });
});
