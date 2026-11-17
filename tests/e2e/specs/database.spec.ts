import { test, expect, toast, PNG_1PX } from '../support/fixtures';

// Browser projects share one database and run in parallel, so these tests never reset or import:
// the reset itself, the demo school and the export/import round trip are covered by the API
// integration tests (DatabaseMaintenanceTests) on MySQL and PostgreSQL.
test.describe('Settings > Database', () => {
  test('shows the counts, exports a backup, and only enables reset once RESET is typed', async ({ page }) => {
    await page.goto('/Settings');
    const card = page.locator('[data-db-card]');
    await expect(card).toBeVisible();
    await expect(card.locator('[data-db-count="Students"] .cg-kpi__value')).toHaveText(/^\d+$/);

    const download = page.waitForEvent('download');
    await card.locator('[data-db-export]').click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/^childguard-backup-\d{8}-\d{6}\.json$/);
    const body = JSON.parse(await (await import('node:fs/promises')).readFile((await file.path())!, 'utf8'));
    expect(body.format).toBe('childguard-backup');
    expect(Object.keys(body.tables)).toContain('Students');
    expect(Object.keys(body.tables)).not.toContain('Admins');

    await card.locator('[data-db-reset]').click();
    const modal = page.getByRole('dialog', { name: 'Reset database' });
    const go = modal.getByRole('button', { name: 'Back up and reset' });
    await expect(go).toBeDisabled();
    await modal.locator('[data-db-confirm]').fill('reset');
    await expect(go).toBeDisabled();
    await modal.getByRole('button', { name: 'Cancel' }).click();
    await expect(modal).toBeHidden();
  });

  test('a file that is not a backup is refused and nothing changes', async ({ page }) => {
    await page.goto('/Settings');
    const before = await page.locator('[data-db-count="Students"] .cg-kpi__value').innerText();
    await page.locator('[data-db-import]').click();
    const modal = page.getByRole('dialog', { name: 'Import database' });
    await modal.locator('input[type="file"]').setInputFiles({ name: 'notes.json', mimeType: 'application/json', buffer: Buffer.from('{"hello":1}') });
    await modal.getByRole('button', { name: 'Import and replace' }).click();
    expect(await toast(page, 'not a ChildGuard backup')).toContain('not a ChildGuard backup');
    await expect(page.locator('[data-db-count="Students"] .cg-kpi__value')).toHaveText(before);
  });
});

test.describe('Driver photo', () => {
  test('upload a photo on the driver profile', async ({ page, data }) => {
    const driver = await data.driver();
    await page.goto(`/Transportation/Driver/${driver.id}`);
    const panel = page.locator('[data-driver-photo]');
    await expect(panel).toContainText('No photo');
    await panel.locator('input[type="file"]').setInputFiles({ name: 'driver.png', mimeType: 'image/png', buffer: PNG_1PX });
    await panel.locator('[data-cg-upload-submit]').click();
    expect(await toast(page, 'Driver photo saved')).toContain('Driver photo saved');
    await expect(page.locator('[data-driver-photo]')).toContainText('Photo on file');
    await expect(page.locator('.cg-page-head img')).toHaveAttribute('src', new RegExp(`/Drivers/Photo/${driver.id}`));
  });
});
