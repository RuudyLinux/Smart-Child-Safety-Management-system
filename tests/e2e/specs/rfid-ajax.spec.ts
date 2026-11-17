// RFID pages on the shared AJAX layer: saves happen in place, the card list is server-paged with its state
// in the URL, and a replacement or deletion navigates because the page it was on no longer describes the card.
import { test, expect, toast } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

const rows = (page: import('@playwright/test').Page) => page.locator('[data-cg-region="list"] tr[data-row]');

test.describe('RFID without page reloads', () => {
  test.use({ allowErrors: [/status of 40[019]/] });

  test('assigning a card refreshes the console in place', async ({ page, data }) => {
    const s = await data.student();
    await page.goto('/Rfid');
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Assign card' }).first().click();
    const modal = page.locator('#assignCardModal');
    await modal.locator('input[name="cardUid"]').fill('AJX' + Date.now().toString(16).toUpperCase());
    await modal.locator('select[name="studentId"]').selectOption(String(s.id));
    await modal.getByRole('button', { name: 'Assign card' }).click();
    expect(await toast(page, 'RFID card assigned.')).toContain('assigned');
    await expect(modal).toBeHidden();
    // The student now has a card, so the picker no longer offers them.
    await expect(page.locator(`#assignStudentId option[value="${s.id}"]`)).toHaveCount(0);
    expect(navs.count).toBe(0);
  });

  test('a card status change updates its row in place', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await page.goto(`/Rfid?search=${s.fullName}`);
    const navs = countNavigations(page);
    const row = rows(page).filter({ hasText: s.fullName });
    await row.locator('select[name="status"]').selectOption('Lost');
    await row.getByRole('button', { name: 'Update' }).click();
    expect(await toast(page, 'Card marked Lost.')).toContain('Lost');
    await expect(rows(page).filter({ hasText: s.fullName })).toContainText('Lost');
    expect(navs.count).toBe(0);
    expect(card.cardUid).toBeTruthy();
  });

  test('registering a spare card opens its page', async ({ page }) => {
    await page.goto('/Rfid');
    await page.getByRole('button', { name: 'Register spare card' }).click();
    await page.locator('#registerUid').fill('SPR' + Date.now().toString(16).toUpperCase());
    await page.locator('#registerCardModal').getByRole('button', { name: 'Register card' }).click();
    await expect(page).toHaveURL(/\/Rfid\/Details\/\d+$/);
    expect(await toast(page)).toBeTruthy();
  });

  test('card page: unassign updates the page in place', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await page.goto(`/Rfid?search=${s.fullName}`);
    await page.getByRole('link', { name: /Details for card/ }).first().click();
    await expect(page).toHaveURL(/\/Rfid\/Details\/\d+$/);
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Unassign' }).first().click();
    const modal = page.locator('#cardUnassign');
    await modal.locator('input[name="reason"]').fill('Student left the bus service');
    await modal.getByRole('button', { name: 'Unassign' }).click();
    expect(await toast(page, 'unassigned')).toBeTruthy();
    await expect(page.locator('[data-cg-region="rfid-card-page"]')).toContainText('Unassigned');
    expect(navs.count).toBe(0);
    expect(card.cardUid).toBeTruthy();
  });
});

test.describe('RFID card list (server paging)', () => {
  async function spares(api: any, tag: string, count: number) {
    for (let i = 0; i < count; i++) {
      const r = await api.post('/api/rfid/cards/register', { data: { cardUid: `${tag}${String(i).padStart(2, '0')}` } });
      expect(r.ok(), await r.text()).toBeTruthy();
    }
  }

  test('search by card, status filter and pages run on the server and keep the URL', async ({ page, api }) => {
    const tag = ('CG' + Date.now().toString(36)).toUpperCase();
    await spares(api, tag, 27);
    await page.goto('/Rfid');
    const navs = countNavigations(page);
    await page.locator('input[name="search"]').fill(tag);
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    await expect(rows(page)).toHaveCount(25);
    await expect(page.locator('[data-cg-count][data-cg-count-scope="rfid"]')).toHaveText('27 records');
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(rows(page)).toHaveCount(2);
    expect(navs.count).toBe(0);

    await page.reload();
    await expect(rows(page)).toHaveCount(2);
    await page.goBack();
    await expect(rows(page)).toHaveCount(25);

    await page.locator('[data-cg-filter="state"]').selectOption({ label: 'Lost' });
    await expect(page.locator('[data-cg-noresults]')).toBeVisible();
    await page.locator('[data-cg-filter="state"]').selectOption({ label: 'Active' });
    await expect(page.locator('[data-cg-count][data-cg-count-scope="rfid"]')).toHaveText('27 records');
    await page.locator('[data-cg-filter="assigned"]').selectOption({ label: 'Assigned to a student' });
    await expect(page.locator('[data-cg-noresults]')).toBeVisible();   // all 27 are spares
  });

  test('header figures come from the server summary', async ({ page }) => {
    await page.goto('/Rfid');
    await expect(page.locator('.cg-page-head__sub')).toContainText(/\d+ cards? registered/);
    await expect(page.locator('.cg-kpi', { hasText: 'Active cards' }).locator('.cg-kpi__value')).toHaveText(/^\d+$/);
  });

  test('a page past the end offers the last page', async ({ page }) => {
    await page.goto('/Rfid?page=9999');
    await expect(page.locator('[data-cg-page-missing]')).toContainText('There is no page 9999');
  });

  test('the list works without JavaScript', async ({ browser, api }) => {
    const tag = ('NJ' + Date.now().toString(36)).toUpperCase();
    await spares(api, tag, 26);
    const context = await browser.newContext({ javaScriptEnabled: false, storageState: '.e2e/admin.json' });
    const page = await context.newPage();
    await page.goto('/Rfid');
    await page.locator('input[name="search"]').fill(tag);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.locator('[data-cg-region="list"] tr[data-row]')).toHaveCount(25);
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(page.locator('[data-cg-region="list"] tr[data-row]')).toHaveCount(1);
    await context.close();
  });
});
