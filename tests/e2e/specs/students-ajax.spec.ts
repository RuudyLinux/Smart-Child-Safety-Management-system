// Students pages on the shared AJAX layer: saves happen in place, errors show beside their fields,
// navigation happens only where a new page is genuinely needed, and the no-JavaScript path still works.
import { test, expect, toast, uid, PNG_1PX } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

test.describe('Students without page reloads', () => {
  // The validation test answers 400 and the expiry test 401 on purpose; the browser logs both.
  test.use({ allowErrors: [/status of 40[01]/] });

  test('create goes to the new profile with its toast', async ({ page }) => {
    const name = uid('Ajax Create');
    await page.goto('/Students/Create');
    await page.locator('#FullName').fill(name);
    await page.locator('#DateOfBirth').fill('2016-04-21');
    await page.getByRole('button', { name: 'Create student' }).click();
    await expect(page).toHaveURL(/\/Students\/Details\/\d+$/);
    expect(await toast(page, 'added')).toContain(name);
  });

  test('edit validation error shows inline without leaving the page', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Edit/${s.id}`);
    const navs = countNavigations(page);
    // Remove the browser's own check so the server's validation is what answers.
    await page.locator('#FullName').evaluate((el: HTMLInputElement) => { el.required = false; el.value = '   '; });
    await page.getByRole('button', { name: 'Save changes' }).click();

    const slot = page.locator('[data-valmsg-for="FullName"]');
    // MVC's own required check or the API's validator: either way the message sits under the field.
    await expect(slot).toContainText(/required|must not be empty/i);
    await expect(page.locator('#FullName')).toHaveAttribute('aria-invalid', 'true');
    await expect(page).toHaveURL(new RegExp(`/Students/Edit/${s.id}$`));
    expect(navs.count).toBe(0);

    // Fixing it clears the message and saves.
    await page.locator('#FullName').fill(uid('Fixed Name'));
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
    expect(await toast(page, 'Student details updated.')).toContain('updated');
  });

  test('deactivate updates the header in place', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    const navs = countNavigations(page);
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: 'Deactivate' }).click();
    expect(await toast(page, 'deactivated')).toContain('Student record deactivated.');
    const header = page.locator('[data-cg-region="student-header"]');
    await expect(header).toContainText('Inactive record');
    await header.getByRole('button', { name: 'Activate' }).click();
    await expect(header).toContainText('Active record');
    expect(navs.count).toBe(0);
  });

  test('archive updates header, banner and lifecycle in place', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Archive student' }).click();
    const dialog = page.getByRole('dialog', { name: /Archive/ });
    await dialog.locator('textarea[name="reason"]').fill('Left the school at the end of term');
    await dialog.getByRole('button', { name: 'Archive' }).click();

    await expect(dialog).toBeHidden();
    await expect(page.locator('[data-cg-region="student-header"]')).toContainText('Archived');
    const banner = page.locator('[data-cg-region="archived-banner"]');
    await expect(banner).toContainText('This student is archived');
    // The restore form rendered by the refresh works too.
    await banner.getByRole('button', { name: 'Restore student' }).click();
    await expect(banner).not.toContainText('This student is archived');
    expect(navs.count).toBe(0);
  });

  test('photo upload refreshes the photo card and the avatar', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}#photo`);
    const navs = countNavigations(page);
    const region = page.locator('[data-cg-region="photo"]');
    await region.locator('input[type="file"]').setInputFiles({ name: 'kid.png', mimeType: 'image/png', buffer: PNG_1PX });
    await region.locator('[data-cg-upload-submit]').click();
    expect(await toast(page, 'Student photo saved.')).toContain('saved');
    await expect(region).toContainText('Photo on file');
    await expect(page.locator('[data-cg-region="student-header"] img')).toHaveAttribute('src', new RegExp(`/Students/Photo/${s.id}`));
    // Still on the photo tab after the refresh.
    await expect(page.locator('[data-cg-tabpanel="photo"]')).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('address saves in place on the edit page', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Edit/${s.id}`);
    const navs = countNavigations(page);
    const region = page.locator('[data-cg-region="address"]');
    await region.locator('input[name="Line1"]').fill('12 Market Road');
    await region.locator('input[name="City"]').fill('Pune');
    await region.getByRole('button', { name: /Save address/i }).click();
    expect(await toast(page, 'Address saved.')).toContain('Address saved.');
    await expect(region.locator('input[name="City"]')).toHaveValue('Pune');
    expect(navs.count).toBe(0);
  });

  test('permanent delete goes to the list', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('button', { name: 'Delete permanently' }).click();
    const dialog = page.getByRole('dialog', { name: /Permanently delete/ });
    await dialog.locator('textarea[name="reason"], input[name="reason"]').first().fill('Created by mistake during setup');
    await dialog.locator('input[name="confirmed"]').check();
    await dialog.getByRole('button', { name: /Delete/ }).last().click();
    await expect(page).toHaveURL(/\/Students$/);
    expect(await toast(page, 'deleted')).toBeTruthy();
  });

  test('session expiry during ajax save redirects to sign-in', async ({ page, context, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Edit/${s.id}`);
    await context.clearCookies();
    await page.locator('#Grade').fill('Grade 4');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(/\/Account\/Login/);
  });
});

test.describe('without JavaScript', () => {
  test('editing still saves through a normal post', async ({ browser, data }) => {
    const s = await data.student();
    const context = await browser.newContext({ javaScriptEnabled: false, storageState: '.e2e/admin.json' });
    const page = await context.newPage();
    await page.goto(`/Students/Edit/${s.id}`);
    await page.locator('#Grade').fill('Grade 7');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
    await expect(page.locator('[data-cg-region="student-header"]')).toContainText('Grade 7');
    await context.close();
  });
});
