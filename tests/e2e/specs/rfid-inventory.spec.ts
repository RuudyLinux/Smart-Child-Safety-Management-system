import path from 'node:path';
import { Page } from '@playwright/test';
import { test, expect, toast } from '../support/fixtures';

const SHOTS = path.join(__dirname, '..', 'test-results', 'phase3b-walkthrough');
const shot = async (page: Page, name: string) => {
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
};
const cardUid = () => 'INV' + Math.random().toString(16).slice(2, 12).toUpperCase();

test.use({ viewport: { width: 1366, height: 1500 } });

test.describe('RFID inventory', () => {
  test('register a spare with the reader, assign it, replace it, and read the history @smoke', async ({ page, data }) => {
    const s = await data.student();
    const spare = cardUid();
    const replacement = cardUid();

    await page.goto('/Rfid');
    await page.getByRole('button', { name: 'Register spare card' }).click();
    const modal = page.getByRole('dialog', { name: 'Register a spare card' });
    await modal.locator('#registerUid').click();
    // The USB reader types the UID and presses Enter.
    await page.keyboard.type(spare, { delay: 5 });
    await page.keyboard.press('Enter');
    expect(await toast(page, 'registered and available')).toContain('registered and available');
    await expect(page).toHaveURL(/\/Rfid\/Details\/\d+$/);

    await page.getByRole('button', { name: 'Assign to student' }).click();
    const assign = page.getByRole('dialog', { name: 'Assign this card' });
    await assign.locator('select[name="studentId"]').selectOption(String(s.id));
    await assign.getByRole('button', { name: 'Assign card' }).click();
    expect(await toast(page, `assigned to ${s.fullName}`)).toContain(`assigned to ${s.fullName}`);
    await expect(page.locator('[data-history-row]')).toHaveCount(1);

    await page.getByRole('button', { name: 'Replace card' }).click();
    const replace = page.getByRole('dialog', { name: 'Replace this card' });
    await replace.locator('#replaceUid').click();
    await page.keyboard.type(replacement, { delay: 5 });
    await page.keyboard.press('Enter'); // moves to the next field, does not submit
    await expect(replace).toBeVisible();
    await replace.locator('#replaceOld').selectOption('Damaged');
    await replace.locator('#replaceReason').fill('Card cracked in a bag.');
    await shot(page, '01-replace-dialog');
    await replace.getByRole('button', { name: 'Replace card' }).click();

    expect(await toast(page, `replaced for ${s.fullName}`)).toContain(`replaced for ${s.fullName}`);
    await expect(page.locator('h1')).toContainText(replacement.slice(-4));
    await shot(page, '02-new-card');

    await page.goto(`/Students/Details/${s.id}`);
    await expect(page.locator('.cg-profile')).toContainText(replacement.slice(-4));
  });

  test('unassign a card as lost and see it in the history', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await page.goto(`/Rfid/Details/${card.id}`);
    await page.getByRole('button', { name: 'Unassign' }).click();
    const modal = page.getByRole('dialog', { name: 'Unassign this card' });
    await modal.locator('#unassignStatus').selectOption('Lost');
    await modal.locator('#unassignReason').fill('Lost at the park.');
    await modal.getByRole('button', { name: 'Unassign' }).click();
    expect(await toast(page, 'unassigned')).toContain('unassigned');
    await expect(page.locator('[data-card-actions]')).toContainText('Marked Lost');
    await expect(page.locator('[data-history-row]').first()).toContainText('Lost at the park.');
    await shot(page, '03-history');
  });

  test('a never-used spare can be deleted permanently', async ({ page }) => {
    const uid = cardUid();
    await page.goto('/Rfid');
    await page.getByRole('button', { name: 'Register spare card' }).click();
    await page.locator('#registerUid').fill(uid);
    await page.getByRole('dialog', { name: 'Register a spare card' }).getByRole('button', { name: 'Register card' }).click();
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const modal = page.getByRole('dialog', { name: 'Permanently delete this card?' });
    await modal.locator('#deleteReason').fill('Registered by mistake.');
    await modal.getByLabel(/cannot be undone/).check();
    await modal.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(page).toHaveURL(/\/Rfid$/);
    expect(await toast(page, 'permanently deleted')).toContain('permanently deleted');
  });
});
