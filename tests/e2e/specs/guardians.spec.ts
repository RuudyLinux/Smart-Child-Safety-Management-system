import { test, expect, uid, toast, PNG_1PX, guardianSession } from '../support/fixtures';

test.describe('Guardians', () => {
  test('create a guardian, see it listed, open details @smoke', async ({ page }) => {
    const name = uid('Priya');
    const n = Date.now().toString().slice(-8);
    await page.goto('/Guardians');
    await page.getByRole('link', { name: 'Add guardian' }).first().click();
    await page.locator('input[name="FullName"]').fill(name);
    await page.locator('input[name="Email"]').fill(`p${n}@childguard.test`);
    await page.locator('input[name="ContactNumber"]').fill(`97${n}`);
    await page.getByRole('button', { name: 'Create guardian' }).click();
    await expect(page).toHaveURL(/\/Guardians\/Details\/\d+$/);
    await toast(page);
    await expect(page.locator('.cg-profile__name')).toHaveText(name);
    await page.goto('/Guardians');
    await expect(page.locator('tr[data-row]', { hasText: name })).toHaveCount(1);
  });

  test.describe('validation', () => {
    test.use({ allowErrors: [/status of 400/] });
    test('rejects a bad email, a bad phone and a duplicate contact number', async ({ page, data }) => {
      const existing = await data.guardian();
      await page.goto('/Guardians/Create');
      await page.locator('input[name="FullName"]').fill('Bad Email');
      await page.locator('input[name="Email"]').fill('not-an-email');
      await page.locator('input[name="ContactNumber"]').fill('9812345670');
      await page.getByRole('button', { name: 'Create guardian' }).click();
      await expect(page).toHaveURL(/\/Guardians\/Create/);

      await page.locator('input[name="Email"]').fill(`dup${Date.now()}@childguard.test`);
      await page.locator('input[name="ContactNumber"]').fill(existing.contactNumber);
      await page.getByRole('button', { name: 'Create guardian' }).click();
      await expect(page).toHaveURL(/\/Guardians\/Create/);
      await expect(page.locator('main')).toContainText(/already|exists|in use/i);

      await page.locator('input[name="ContactNumber"]').fill('12');
      await page.getByRole('button', { name: 'Create guardian' }).click();
      await expect(page).toHaveURL(/\/Guardians\/Create/);
    });
  });

  test('edit details and status from the edit form', async ({ page, data }) => {
    const g = await data.guardian();
    const renamed = uid('Edited');
    await page.goto(`/Guardians/Edit/${g.id}`);
    await expect(page.locator('input[name="Email"]')).toHaveValue(g.email);
    await page.locator('input[name="FullName"]').fill(renamed);
    await page.locator('select[name="Status"]').selectOption('Inactive');
    await page.getByRole('button', { name: 'Save changes' }).click();
    expect(await toast(page)).toContain('Guardian details updated');
    await expect(page.locator('.cg-profile__name')).toHaveText(renamed);
    await expect(page.locator('.cg-profile')).toContainText('Inactive');
  });

  test('deactivate and reactivate from the details page', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}`);
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: 'Deactivate' }).click();
    await toast(page);
    await expect(page.locator('.cg-profile')).toContainText('Inactive');
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: /Reactivate|Activate/ }).click();
    await toast(page);
    await expect(page.locator('.cg-profile')).not.toContainText('Inactive');
  });

  test('search, filters, sorting and clear on the list', async ({ page, data }) => {
    const tag = uid('GFilt').replace(/\s/g, '');
    const s = await data.student();
    const parent = await data.guardian({ fullName: `${tag} Parent` });
    await data.link(s.id, parent.id);
    await data.guardian({ fullName: `${tag} Lonely` });
    await page.goto('/Guardians');
    await page.locator('[data-cg-search][data-cg-search-scope="guardians"]').fill(tag);
    const rows = page.locator('tr[data-row]:visible');
    await expect(rows).toHaveCount(2);
    await page.locator('[data-cg-filter="children"]').selectOption({ label: 'No children' });
    await expect(rows).toHaveCount(1);
    await expect(rows).toContainText('Lonely');
    await page.getByRole('button', { name: 'Clear' }).click();
    await expect(page.locator('[data-cg-search][data-cg-search-scope="guardians"]')).toHaveValue('');
    await page.locator('[data-cg-search][data-cg-search-scope="guardians"]').fill(tag);
    await page.locator('[data-cg-sort="name"]').click();
    expect(await rows.locator('.cg-identity__name').allInnerTexts()).toEqual([`${tag} Lonely`, `${tag} Parent`]);
  });

  test('detail tabs, linked child link, and issuing a password', async ({ page, data }) => {
    const s = await data.student();
    const g = await data.guardian();
    await data.link(s.id, g.id);
    await page.goto(`/Guardians/Details/${g.id}`);
    for (const tab of ['photo', 'verifications', 'account', 'children']) {
      await page.locator(`[data-cg-tab="${tab}"]`).click();
      await expect(page.locator(`[data-cg-tab="${tab}"]`)).toHaveClass(/is-active/);
    }
    await page.getByRole('link', { name: new RegExp(s.fullName) }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));

    await page.goto(`/Guardians/Details/${g.id}#account`);
    await page.locator('form[action*="UpdateCredentials"] input[name="newPassword"]').fill('Temp-Pass-123!');
    // Dismissing the confirmation must not issue anything.
    page.once('dialog', d => d.dismiss());
    await page.locator('form[action*="UpdateCredentials"] button[type="submit"]').click();
    await expect(page.locator('.cg-toast')).toHaveCount(0);
    // Regression: a declined confirmation used to leave the button stuck "loading".
    await expect(page.locator('form[action*="UpdateCredentials"] button[type="submit"]')).not.toHaveClass(/is-loading/);
    page.once('dialog', d => { expect(d.message()).toContain('Issue a new password'); d.accept(); });
    await page.locator('form[action*="UpdateCredentials"] button[type="submit"]').click();
    expect(await toast(page)).toContain('Password issued');
    // Regression: the page returns to the tab the action came from.
    await expect(page).toHaveURL(new RegExp(`/Guardians/Details/${g.id}#account$`));
    await expect(page.locator('[data-cg-tab="account"]')).toHaveClass(/is-active/);
    // The issued password really works for the guardian sign-in (first step of OTP login).
    const session = await guardianSession(g.id, g.contactNumber, 'Temp-Pass-123!');
    const profile = await (await session.api.get('/api/me/profile')).json();
    expect(profile.fullName ?? profile.FullName).toBe(g.fullName);
    await session.api.dispose();
  });

  test('take a guardian photo with the camera and save it', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}#photo`);
    const form = page.locator(`form[action$="/Guardians/UploadPhoto/${g.id}"]`).first();
    await form.locator('[data-cg-camera-open]').click();
    await expect(form.locator('[data-cg-camera-capture]')).toBeEnabled();
    await form.locator('[data-cg-camera-capture]').click();
    await expect(form.locator('[data-cg-preview]')).toBeVisible();
    await form.locator('[data-cg-upload-submit]').click();
    await toast(page);
    const img = page.locator(`img[src*="/Guardians/Photo/${g.id}"]`).first();
    await expect(img).toBeVisible();
    expect(await img.evaluate((i: HTMLImageElement) => i.naturalWidth)).toBeGreaterThan(0);
  });

  test('upload and remove a guardian photo', async ({ page, data }) => {
    const g = await data.guardian();
    await page.goto(`/Guardians/Details/${g.id}#photo`);
    const form = page.locator(`form[action$="/Guardians/UploadPhoto/${g.id}"]`).first();
    await form.locator('input[type="file"]').setInputFiles({ name: 'g.png', mimeType: 'image/png', buffer: PNG_1PX });
    await form.locator('[data-cg-upload-submit]').click();
    await toast(page);
    await expect(page.locator(`img[src*="/Guardians/Photo/${g.id}"]`).first()).toBeVisible();
    await page.locator('[data-cg-tab="photo"]').click();
    page.once('dialog', d => d.accept());
    await page.locator(`form[action$="/Guardians/DeletePhoto/${g.id}"] button`).click();
    await toast(page);
    await expect(page.locator(`img[src*="/Guardians/Photo/${g.id}"]`)).toHaveCount(0);
  });
});
