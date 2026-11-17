// Guardians pages on the shared AJAX layer: saves happen in place, refusals show in the form, navigation
// happens only where a new page is needed, and the list is server-paged with its state in the URL.
import { test, expect, toast, uid, PNG_1PX } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

const rows = (page: import('@playwright/test').Page) => page.locator('[data-cg-region="list"] tr[data-row]');

test.describe('Guardians without page reloads', () => {
  // Refusals answer 400/409 on purpose; the browser logs them.
  test.use({ allowErrors: [/status of 40[019]/] });

  test('create goes to the new profile with its toast', async ({ page }) => {
    const name = uid('Ajax Guardian');
    const n = Date.now().toString().slice(-8);
    await page.goto('/Guardians/Create');
    await page.locator('input[name="FullName"]').fill(name);
    await page.locator('input[name="Email"]').fill(`aj${n}@childguard.test`);
    await page.locator('input[name="ContactNumber"]').fill(`96${n}`);
    await page.getByRole('button', { name: 'Create guardian' }).click();
    await expect(page).toHaveURL(/\/Guardians\/Details\/\d+$/);
    expect(await toast(page, 'Guardian account created')).toContain('Guardian account created');
    await expect(page.locator('.cg-profile__name')).toHaveText(name);
  });

  test('a duplicate contact number is explained in the form, without leaving it', async ({ page, data }) => {
    const existing = await data.guardian();
    await page.goto('/Guardians/Create');
    const navs = countNavigations(page);
    await page.locator('input[name="FullName"]').fill('Duplicate Number');
    await page.locator('input[name="Email"]').fill(`dup${Date.now()}@childguard.test`);
    await page.locator('input[name="ContactNumber"]').fill(existing.contactNumber);
    await page.getByRole('button', { name: 'Create guardian' }).click();
    await expect(page.locator('[data-cg-error-summary]')).toContainText(/already|in use|registered/i);
    await expect(page.locator('.cg-toast', { hasText: /already|in use|registered/i }).first()).toBeVisible();
    await expect(page).toHaveURL(/\/Guardians\/Create$/);
    expect(navs.count).toBe(0);
  });

  test('a bad email shows beside the email field', async ({ page }) => {
    await page.goto('/Guardians/Create');
    await page.locator('form[action*="/Guardians/Create"]').evaluate((f: HTMLFormElement) => { f.noValidate = true; });
    await page.locator('input[name="FullName"]').fill('Bad Email');
    await page.locator('input[name="Email"]').fill('not-an-email');
    await page.locator('input[name="ContactNumber"]').fill('9812345671');
    await page.getByRole('button', { name: 'Create guardian' }).click();
    await expect(page.locator('[data-valmsg-for="Email"]')).not.toBeEmpty();
    await expect(page.locator('input[name="Email"]')).toHaveAttribute('aria-invalid', 'true');
    await expect(page).toHaveURL(/\/Guardians\/Create$/);
  });

  test('deactivate and activate update the header in place', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}`);
    const navs = countNavigations(page);
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: 'Deactivate' }).click();
    expect(await toast(page, 'deactivated')).toContain('Guardian account deactivated.');
    const header = page.locator('[data-cg-region="guardian-header"]');
    await expect(header).toContainText('Inactive account');
    await header.getByRole('button', { name: 'Activate' }).click();
    await expect(header).toContainText('Active account');
    expect(navs.count).toBe(0);
  });

  test('archive and restore update header, banner and lifecycle in place', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}`);
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Archive guardian' }).click();
    const dialog = page.getByRole('dialog', { name: /Archive/ });
    await dialog.locator('textarea[name="reason"]').fill('Moved away from the area this term');
    await dialog.getByRole('button', { name: 'Archive' }).click();
    await expect(dialog).toBeHidden();
    const banner = page.locator('[data-cg-region="archived-banner"]');
    await expect(banner).toContainText('This guardian is archived');
    await banner.getByRole('button', { name: 'Restore guardian' }).click();
    await expect(banner).not.toContainText('This guardian is archived');
    expect(navs.count).toBe(0);
  });

  test('issuing a password stays on the tab, clears the field and updates the account card', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}#account`);
    const navs = countNavigations(page);
    const field = page.locator('form[action*="UpdateCredentials"] input[name="newPassword"]');
    await field.fill('Temp-Pass-123!');
    page.once('dialog', d => d.accept());
    await page.locator('form[action*="UpdateCredentials"] button[type="submit"]').click();
    expect(await toast(page, 'Password issued')).toContain('Password issued');
    await expect(field).toHaveValue('');                                  // the typed password never stays on the page
    await expect(page.locator('[data-cg-region="account-card"]')).toContainText('Issued');
    await expect(page.locator('[data-cg-tab="account"]')).toHaveClass(/is-active/);
    expect(navs.count).toBe(0);
  });

  test('a password that is too weak is refused beside the field and stays typed', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}#account`);
    const form = page.locator('form[action*="UpdateCredentials"]');
    await form.evaluate((f: HTMLFormElement) => { f.noValidate = true; });
    await form.locator('input[name="newPassword"]').fill('weak');
    page.once('dialog', d => d.accept());
    await form.locator('button[type="submit"]').click();
    await expect(page.locator('.cg-toast').first()).toBeVisible();
    await expect(form.locator('input[name="newPassword"]')).toHaveValue('weak');
  });

  test('photo upload refreshes the photo card and the avatar', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}#photo`);
    const navs = countNavigations(page);
    const region = page.locator('[data-cg-region="photo"]');
    await region.locator('input[type="file"]').setInputFiles({ name: 'g.png', mimeType: 'image/png', buffer: PNG_1PX });
    await region.locator('[data-cg-upload-submit]').click();
    expect(await toast(page, 'Guardian photo saved.')).toContain('saved');
    await expect(region).toContainText('Photo on file');
    await expect(page.locator('[data-cg-region="guardian-header"] img')).toHaveAttribute('src', new RegExp(`/Guardians/Photo/${g.id}`));
    await expect(page.locator('[data-cg-tabpanel="photo"]')).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('a saved guardian edit returns to the profile', async ({ page, data }) => {
    const g = await data.guardian();
    const renamed = uid('Renamed');
    await page.goto(`/Guardians/Edit/${g.id}`);
    await page.locator('input[name="FullName"]').fill(renamed);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(new RegExp(`/Guardians/Details/${g.id}$`));
    expect(await toast(page, 'Guardian details updated')).toContain('updated');
    await expect(page.locator('.cg-profile__name')).toHaveText(renamed);
  });

  test('editing to an email another guardian has is refused in the form', async ({ page, data }) => {
    const other = await data.guardian();
    const g = await data.guardian();
    await page.goto(`/Guardians/Edit/${g.id}`);
    await page.locator('input[name="Email"]').fill(other.email);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.locator('[data-cg-error-summary]')).toContainText(/already|in use|registered/i);
    await expect(page).toHaveURL(new RegExp(`/Guardians/Edit/${g.id}$`));
  });

  test('permanent delete goes to the list', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const dialog = page.getByRole('dialog', { name: /Permanently delete/ });
    await dialog.locator('textarea[name="reason"], input[name="reason"]').first().fill('Created by mistake during setup');
    await dialog.locator('input[name="confirmed"]').check();
    await dialog.getByRole('button', { name: /Delete/ }).last().click();
    await expect(page).toHaveURL(/\/Guardians$/);
    expect(await toast(page, 'deleted')).toBeTruthy();
  });
});

test.describe('Guardians list (server paging)', () => {
  test('search by name, email or number runs on the server and keeps the URL', async ({ page, data }) => {
    const tag = uid('GList').replace(/\s/g, '');
    for (let i = 0; i < 27; i++) await data.guardian({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
    await page.goto('/Guardians');
    const navs = countNavigations(page);
    await page.locator('input[name="search"]').fill(tag);
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    await expect(rows(page)).toHaveCount(25);
    await expect(page.locator('[data-cg-count][data-cg-count-scope="guardians"]')).toHaveText('27 records');
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(rows(page)).toHaveCount(2);
    await page.reload();
    await expect(rows(page)).toHaveCount(2);
    expect(navs.count).toBe(1);   // only the explicit reload
  });

  test('filters, sort and Back work together', async ({ page, data }) => {
    const tag = uid('GFlt').replace(/\s/g, '');
    const s = await data.student();
    const parent = await data.guardian({ fullName: `${tag} Parent` });
    await data.link(s.id, parent.id);
    await data.guardian({ fullName: `${tag} Lonely` });
    await page.goto(`/Guardians?search=${tag}`);
    await expect(rows(page)).toHaveCount(2);

    await page.locator('[data-cg-filter="children"]').selectOption({ label: 'No children' });
    await expect(rows(page)).toHaveCount(1);
    await expect(rows(page)).toContainText('Lonely');
    await page.goBack();
    await expect(rows(page)).toHaveCount(2);
    await expect(page.locator('[data-cg-filter="children"]')).toHaveValue('__all');

    await page.locator('[data-cg-sort="name"]').click();
    await expect(page.locator('[data-cg-sort="name"]')).toHaveClass(/is-asc/);
    expect(await rows(page).locator('.cg-identity__name').allInnerTexts()).toEqual([`${tag} Lonely`, `${tag} Parent`]);
    await page.locator('[data-cg-sort="name"]').click();
    // The server sorts and re-renders: wait for the header's new state before reading rows.
    await expect(page.locator('[data-cg-sort="name"]')).toHaveClass(/is-desc/);
    expect(await rows(page).locator('.cg-identity__name').allInnerTexts()).toEqual([`${tag} Parent`, `${tag} Lonely`]);
  });

  test('header figures come from the server summary', async ({ page }) => {
    await page.goto('/Guardians');
    await expect(page.locator('.cg-page-head__sub')).toContainText(/\d+ registered/);
    await expect(page.locator('.cg-kpi', { hasText: 'Registered guardians' }).locator('.cg-kpi__value')).toHaveText(/^\d+$/);
  });

  test('the list works without JavaScript', async ({ browser, data }) => {
    const tag = uid('GNoJs').replace(/\s/g, '');
    for (let i = 0; i < 26; i++) await data.guardian({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
    const context = await browser.newContext({ javaScriptEnabled: false, storageState: '.e2e/admin.json' });
    const page = await context.newPage();
    await page.goto('/Guardians');
    await page.locator('input[name="search"]').fill(tag);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.locator('tr[data-row]')).toHaveCount(25);
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(page.locator('tr[data-row]')).toHaveCount(1);
    await context.close();
  });
});
