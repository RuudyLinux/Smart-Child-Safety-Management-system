import { test, expect, uid, toast, PNG_1PX } from '../support/fixtures';

test.describe('Students', () => {
  test('create a student, see it listed, open details @smoke', async ({ page }) => {
    const name = uid('Aarav');
    await page.goto('/Students');
    // Scoped: on an empty roster the empty state offers a second "Add student" link.
    await page.locator('.cg-page-head').getByRole('link', { name: 'Add student' }).click();
    await expect(page).toHaveURL(/\/Students\/Create$/);
    // Regression: WebKit applied autofocus late and pulled focus back to the name
    // field mid-way, so the date of birth was typed into the name.
    await expect(page.locator('#FullName')).toBeFocused();
    await page.locator('#FullName').fill(name);
    await page.locator('#DateOfBirth').fill('2016-02-20');
    await page.locator('#Grade').fill('3C');
    await page.getByRole('button', { name: 'Create student' }).click();

    await expect(page).toHaveURL(/\/Students\/Details\/\d+$/);
    expect(await toast(page)).toContain(`${name} added`);
    await expect(page.locator('.cg-profile__name')).toHaveText(name);
    await expect(page.locator('.cg-profile')).toContainText('3C');

    await page.goto('/Students');
    await expect(page.locator('tr[data-row]', { hasText: name })).toHaveCount(1);
  });

  test('create form validation: required name, sane date of birth', async ({ page }) => {
    await page.goto('/Students/Create');
    await page.locator('#FullName').fill('');
    await page.getByRole('button', { name: 'Create student' }).click();
    await expect(page).toHaveURL(/\/Students\/Create/);
    expect(await page.locator('#FullName').evaluate((e: HTMLInputElement) => e.validity.valueMissing)).toBe(true);
    // A future birth date is blocked by the field's max (today); the API also refuses it (api.spec).
    await page.locator('#FullName').fill('Future Kid');
    await page.locator('#DateOfBirth').fill('2099-01-01');
    await page.getByRole('button', { name: 'Create student' }).click();
    await expect(page).toHaveURL(/\/Students\/Create/);
    expect(await page.locator('#DateOfBirth').evaluate((e: HTMLInputElement) => e.validity.rangeOverflow)).toBe(true);
  });

  test('cancel leaves the form without creating anything', async ({ page }) => {
    const name = uid('Cancelled');
    await page.goto('/Students/Create');
    await page.locator('#FullName').fill(name);
    await page.getByRole('link', { name: 'Cancel' }).click();
    await expect(page).toHaveURL(/\/Students$/);
    await expect(page.locator('tr[data-row]', { hasText: name })).toHaveCount(0);
  });

  test('edit details and see them updated', async ({ page, data }) => {
    const s = await data.student();
    const renamed = uid('Renamed');
    await page.goto(`/Students/Details/${s.id}`);
    await page.getByRole('link', { name: 'Edit', exact: true }).click();
    await expect(page.locator('#FullName')).toHaveValue(s.fullName);
    await page.locator('#FullName').fill(renamed);
    await page.locator('#Grade').fill('7A');
    await page.getByRole('button', { name: 'Save changes' }).click();
    expect(await toast(page)).toContain('Student details updated');
    await expect(page.locator('.cg-profile__name')).toHaveText(renamed);
    await expect(page.locator('.cg-profile')).toContainText('7A');
  });

  test('deactivate and reactivate a student (records are never hard-deleted)', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: 'Deactivate' }).click();
    await toast(page);
    await expect(page.locator('.cg-profile')).toContainText(/Inactive/);
    page.once('dialog', d => d.accept());
    await page.getByRole('button', { name: /Reactivate|Activate/ }).click();
    await toast(page);
    await expect(page.locator('.cg-profile')).toContainText(/Active/);
    // Status filter in the list sees the change.
    // Search first: the list pages 25 rows and parallel tests share the database, so
    // an unsearched list can push this student onto page 2.
    await page.goto('/Students');
    await page.locator('input[name="search"]').fill(s.fullName);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.locator('[data-cg-filter="status"]').selectOption({ label: 'Active' });
    await expect(page.locator('tr[data-row]:visible', { hasText: s.fullName })).toHaveCount(1);
  });

  test('server-side search, client filters, clear, and empty state', async ({ page, data }) => {
    const tag = uid('Filt').replace(/\s/g, '');
    const withCard = await data.student({ fullName: `${tag} Carded`, grade: '9Z' });
    await data.card(withCard.id);
    const noCard = await data.student({ fullName: `${tag} Plain`, grade: '9Z' });

    await page.goto('/Students');
    await page.locator('input[name="search"]').fill(tag);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    const rows = page.locator('tr[data-row]:visible');
    await expect(rows).toHaveCount(2);

    await page.locator('[data-cg-filter="rfid"]').selectOption({ label: 'No card' });
    await expect(rows).toHaveCount(1);
    await expect(rows).toContainText(noCard.fullName);
    await expect(page.locator('[data-cg-count][data-cg-count-scope="students"]')).toHaveText('1 record');

    await page.locator('[data-cg-filter="guardian"]').selectOption({ label: 'Has a guardian' });
    await expect(rows).toHaveCount(0);
    await expect(page.locator('[data-cg-noresults][data-cg-noresults-scope="students"]')).toBeVisible();

    await page.getByRole('link', { name: 'Clear' }).click();
    await expect(rows).toHaveCount(2);
    await expect(page.locator('[data-cg-filter="rfid"]')).toHaveValue('__all');

    await page.locator('input[name="search"]').fill('zz-no-such-student-zz');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.locator('main')).toContainText(/No students|no match|0 records/i);
  });

  test('sorting by name toggles ascending and descending', async ({ page, data }) => {
    const tag = uid('Sort').replace(/\s/g, '');
    for (const n of ['Charlie', 'alpha', 'Bravo']) await data.student({ fullName: `${tag} ${n}` });
    await page.goto(`/Students?search=${tag}`);
    const names = () => page.locator('tr[data-row]:visible .cg-identity__name').allInnerTexts();
    const header = page.locator('[data-cg-sort="name"]');
    // The server sorts and re-renders the list: wait for the header's new state before reading rows.
    await header.click();
    await expect(header).toHaveClass(/is-asc/);
    expect(await names()).toEqual([`${tag} alpha`, `${tag} Bravo`, `${tag} Charlie`]);
    await header.click();
    await expect(header).toHaveClass(/is-desc/);
    expect(await names()).toEqual([`${tag} Charlie`, `${tag} Bravo`, `${tag} alpha`]);
  });

  test('pagination shows 25 rows per page and moves between pages', async ({ page, data }) => {
    const tag = uid('Page').replace(/\s/g, '');
    for (let i = 0; i < 27; i++) await data.student({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
    await page.goto(`/Students?search=${tag}`);
    await expect(page.locator('tr[data-row]:visible')).toHaveCount(25);
    // Server-side pages are links (they work without JavaScript too).
    const pager = page.locator('[data-cg-pager][data-cg-pager-scope="students"] a');
    await expect(pager).toHaveCount(2);
    await pager.nth(1).click();
    await expect(page.locator('tr[data-row]:visible')).toHaveCount(2);
    await expect(pager.nth(1)).toHaveClass(/is-active/);
  });

  test('row actions: view, edit and journey links', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students?search=${encodeURIComponent(s.fullName)}`);
    const row = page.locator('tr[data-row]', { hasText: s.fullName });
    await row.getByRole('link', { name: `View ${s.fullName}` }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
    await page.goBack();
    await row.getByRole('link', { name: `Edit ${s.fullName}` }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Edit/${s.id}$`));
    await page.goBack();
    await row.getByRole('link', { name: `Journey for ${s.fullName}` }).click();
    await expect(page).toHaveURL(new RegExp(`/Journey/Student/${s.id}$`));
  });

  test('detail tabs switch panels and deep-link through the URL hash', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}`);
    for (const tab of ['attendance', 'journey', 'guardians', 'rfid', 'photo', 'overview']) {
      await page.locator(`[data-cg-tab="${tab}"]`).click();
      await expect(page.locator(`[data-cg-tab="${tab}"]`)).toHaveClass(/is-active/);
      await expect(page.locator('.cg-tabpanel.is-active')).toHaveCount(1);
    }
    // Deep link from another page (a hash-only change on the same page is not a reload).
    await page.goto('/Students');
    await page.goto(`/Students/Details/${s.id}#guardians`);
    await expect(page.locator('[data-cg-tab="guardians"]')).toHaveClass(/is-active/);
  });

  test('link, update and unlink a guardian', async ({ page, data }) => {
    const s = await data.student();
    const g = await data.guardian();
    await page.goto(`/Students/Details/${s.id}#guardians`);
    await page.getByRole('button', { name: 'Link guardian' }).first().click();
    const modal = page.locator('#linkGuardianModal');
    await expect(modal).toBeVisible();
    const option = await modal.locator('select[name="guardianId"] option', { hasText: g.fullName }).getAttribute('value');
    await modal.locator('select[name="guardianId"]').selectOption(option!);
    await modal.locator('input[name="relationship"]').fill('Father');
    await modal.locator('input[name="isAuthorizedForPickup"]').check();
    await modal.getByRole('button', { name: 'Link guardian' }).click();
    await toast(page);
    // Regression: the page used to return to Overview, hiding what was just changed.
    await expect(page).toHaveURL(new RegExp(`#guardians$`));
    await expect(page.locator('[data-cg-tab="guardians"]')).toHaveClass(/is-active/);
    const link = page.locator('form[action*="UpdateGuardianLink"]', { has: page.locator(`input[name="guardianId"][value="${g.id}"]`) });
    await expect(link.locator('input[name="relationship"]')).toHaveValue('Father');

    await link.locator('input[name="relationship"]').fill('Uncle');
    await link.locator('input[name="isAuthorizedForPickup"]').uncheck();
    await link.getByRole('button', { name: 'Save' }).click();
    await toast(page);
    await expect(link.locator('input[name="relationship"]')).toHaveValue('Uncle');
    await expect(link.locator('input[name="isAuthorizedForPickup"]')).not.toBeChecked();

    page.once('dialog', d => d.accept());
    await page.locator('form[action*="RemoveGuardianLink"]', { has: page.locator(`input[name="guardianId"][value="${g.id}"]`) }).getByRole('button', { name: 'Unlink' }).click();
    await toast(page);
    await expect(page.locator(`form[action*="UpdateGuardianLink"] input[name="guardianId"][value="${g.id}"]`)).toHaveCount(0);
  });

  // Regression: a 1.6 MB phone photo exceeded MySQL's max_allowed_packet (1 MB on XAMPP),
  // the API answered 500 and the student was left without a photo. Large photos are now
  // resized in the browser before upload.
  test('a large phone photo is resized, saved and shown', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}#photo`);
    // A noisy 3000×2000 JPEG compresses badly: several MB, like a real camera photo.
    const big = Buffer.from(await page.evaluate(() => {
      const c = document.createElement('canvas'); c.width = 3000; c.height = 2000;
      const ctx = c.getContext('2d')!; const img = ctx.createImageData(c.width, c.height);
      for (let i = 0; i < img.data.length; i++) img.data[i] = (i % 4 === 3) ? 255 : Math.random() * 255;
      ctx.putImageData(img, 0, 0);
      return c.toDataURL('image/jpeg', 0.95).split(',')[1];
    }), 'base64');
    expect(big.length).toBeGreaterThan(1024 * 1024);

    const form = page.locator(`form[action$="/Students/UploadPhoto/${s.id}"]`).first();
    await form.locator('input[type="file"]').setInputFiles({ name: 'camera.jpg', mimeType: 'image/jpeg', buffer: big });
    await expect(form.locator('[data-cg-preview-size]')).toContainText('resized from');
    await form.locator('[data-cg-upload-submit]').click();
    expect(await toast(page)).toContain('Student photo saved');
    const img = page.locator(`img[src*="/Students/Photo/${s.id}"]`).first();
    await expect(img).toBeVisible();
    expect(await img.evaluate((i: HTMLImageElement) => Math.max(i.naturalWidth, i.naturalHeight))).toBe(1024);
  });

  test('take a student photo with the camera, retake, then save', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}#photo`);
    const form = page.locator(`form[action$="/Students/UploadPhoto/${s.id}"]`).first();
    const open = form.locator('[data-cg-camera-open]');
    const captureBtn = form.locator('[data-cg-camera-capture]');
    // Closing the camera brings the upload choices back and releases the device.
    await open.click();
    await expect(form.locator('[data-cg-camera-box] video')).toBeVisible();
    await form.locator('[data-cg-camera-close]').click();
    await expect(form.locator('[data-cg-camera]')).toBeHidden();
    await expect(form.locator('[data-cg-dropzone]')).toBeVisible();
    // Capture -> preview, nothing uploaded yet.
    await open.click();
    await expect(captureBtn).toBeEnabled();
    await captureBtn.click();
    await expect(form.locator('[data-cg-preview]')).toBeVisible();
    await expect(form.locator('[data-cg-preview-name]')).toContainText(/^camera-\d{14}\.jpg$/);
    await expect(form.locator('[data-cg-camera]')).toBeHidden();
    await expect(open).toBeHidden();
    // Cancel = retake.
    await form.locator('[data-cg-upload-cancel]').click();
    await expect(open).toBeVisible();
    await open.click();
    await expect(captureBtn).toBeEnabled();
    await captureBtn.click();
    await form.locator('[data-cg-upload-submit]').click();
    expect(await toast(page)).toContain('Student photo saved');
    const img = page.locator(`img[src*="/Students/Photo/${s.id}"]`).first();
    await expect(img).toBeVisible();
    expect(await img.evaluate((i: HTMLImageElement) => i.naturalWidth)).toBeGreaterThan(0);
  });

  test('upload, preview, cancel and remove a student photo', async ({ page, data }) => {
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}#photo`);
    const form = page.locator(`form[action$="/Students/UploadPhoto/${s.id}"]`).first();
    const save = form.locator('[data-cg-upload-submit]');
    await expect(save).toBeDisabled();
    // Wrong type is refused in the browser.
    await form.locator('input[type="file"]').setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') });
    await expect(form.locator('[data-cg-upload-error]')).toContainText('Unsupported format');
    await expect(save).toBeDisabled();
    // A real image previews; cancel resets.
    await form.locator('input[type="file"]').setInputFiles({ name: 'face.png', mimeType: 'image/png', buffer: PNG_1PX });
    await expect(form.locator('[data-cg-preview]')).toBeVisible();
    await form.locator('[data-cg-upload-cancel]').click();
    await expect(save).toBeDisabled();
    // Upload for real.
    await form.locator('input[type="file"]').setInputFiles({ name: 'face.png', mimeType: 'image/png', buffer: PNG_1PX });
    await save.click();
    await toast(page);
    const img = page.locator(`img[src*="/Students/Photo/${s.id}"]`).first();
    await expect(img).toBeVisible();
    expect(await img.evaluate((i: HTMLImageElement) => i.naturalWidth)).toBeGreaterThan(0);
    await expect(page.locator('[data-cg-tab="photo"]')).toHaveClass(/is-active/);
    // Remove it again.
    page.once('dialog', d => d.accept());
    await page.locator(`form[action$="/Students/DeletePhoto/${s.id}"] button`).click();
    await toast(page);
    await expect(page.locator(`img[src*="/Students/Photo/${s.id}"]`)).toHaveCount(0);
  });
});
