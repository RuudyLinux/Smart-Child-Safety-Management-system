import path from 'node:path';
import { Page } from '@playwright/test';
import { test, expect, uid, toast } from '../support/fixtures';

const SHOTS = path.join(__dirname, '..', 'test-results', 'phase3a-walkthrough');
const shot = async (page: Page, name: string) => {
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
};

test.use({ viewport: { width: 1366, height: 1500 } });

test.describe('Student and guardian management', () => {
  test('edit section and gender; the admission number is assigned automatically and read-only', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Edit/${s.id}`);
    const admission = page.locator('#AdmissionNumber');
    await expect(admission).toHaveAttribute('readonly', '');
    const number = await admission.inputValue();
    expect(number).toMatch(/^ADM\d{4}\d{5}/);
    await page.locator('#Section').fill('D');
    await page.locator('#Gender').selectOption('Female');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
    await expect(page.locator('body')).toContainText(number);
    await expect(page.locator('body')).toContainText('Section D');
  });

  test('archive a student with a reason, see the banner, then restore @smoke', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('button', { name: 'Archive student' }).click();
    const modal = page.getByRole('dialog', { name: `Archive ${s.fullName}?` });
    await modal.getByLabel(/Reason/).fill('Moved to another city.');
    await shot(page, '01-archive-dialog');
    await modal.getByRole('button', { name: 'Archive' }).click();

    expect(await toast(page)).toContain('Student archived');
    const banner = page.locator('[data-archived-banner]');
    await expect(banner).toContainText('Moved to another city.');
    await expect(page.locator('.cg-profile')).toContainText('Archived');
    await shot(page, '02-archived-profile');

    await page.goto('/Students');
    await page.locator('[data-cg-filter="status"][data-cg-filter-scope="students"]').selectOption('Archived');
    await expect(page.locator('tr[data-row]:visible', { hasText: s.fullName })).toHaveCount(1);

    await page.goto(`/Students/Details/${s.id}`);
    await banner.getByRole('button', { name: 'Restore student' }).click();
    expect(await toast(page)).toContain('restored');
    await expect(page.locator('[data-archived-banner]')).toHaveCount(0);
    await expect(page.locator('.cg-profile')).toContainText('Active record');
  });

  test('a student with history shows why it cannot be deleted', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const modal = page.getByRole('dialog', { name: `Permanently delete ${s.fullName}?` });
    await expect(modal).toContainText('cannot be deleted');
    await expect(modal.locator('[data-delete-blockers]')).toContainText('attendance');
    await expect(modal.getByRole('button', { name: 'Delete permanently' })).toHaveCount(0);
    await shot(page, '03-delete-blocked');
  });

  test('a student created by mistake is deleted after confirmation', async ({ page, data }) => {
    const s = await data.student({ fullName: uid('Mistake') });
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const modal = page.getByRole('dialog', { name: `Permanently delete ${s.fullName}?` });
    await modal.getByLabel(/Reason/).fill('Created twice by mistake.');
    await modal.getByLabel(/I understand/).check();
    await shot(page, '04-delete-confirm');
    await modal.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(page).toHaveURL(/\/Students$/);
    expect(await toast(page)).toContain('permanently deleted');
    await expect(page.locator('tr[data-row]', { hasText: s.fullName })).toHaveCount(0);

    await page.goto(`/Audit?auditAction=StudentDeleted`);
    await expect(page.locator('tr[data-audit-row]', { hasText: s.fullName })).toHaveCount(1);
  });

  test('archive and restore a guardian', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}`);
    await page.getByRole('button', { name: 'Archive guardian' }).click();
    const modal = page.getByRole('dialog', { name: `Archive ${g.fullName}?` });
    await expect(modal).toContainText('signs the guardian out');
    await modal.getByLabel(/Reason/).fill('Account replaced by a new one.');
    await modal.getByRole('button', { name: 'Archive' }).click();
    await expect(page.locator('[data-archived-banner]')).toContainText('Account replaced by a new one.');
    await page.locator('[data-archived-banner]').getByRole('button', { name: 'Restore guardian' }).click();
    await expect(page.locator('[data-archived-banner]')).toHaveCount(0);
  });
});
