import { test, expect, uid } from '../support/fixtures';

test.describe('Bulk import', () => {
  test('template, CSV upload, fix a row inline, confirm, summary and history @smoke', async ({ page }) => {
    const a = uid('Route'), b = uid('Route');
    await page.goto('/BulkImport');
    await page.locator('[data-bi-category="Routes"]').click();
    await expect(page.locator('[data-bi-panel="2"]')).toBeVisible();
    // Regression: the upload panel itself must be visible, not just its (hidden) file input.
    await expect(page.locator('[data-bi-dropzone]')).toBeVisible();

    // Template download comes back as a real workbook.
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.locator('[data-bi-template="xlsx"]').click(),
    ]);
    expect(download.suggestedFilename()).toBe('childguard-routes-template.xlsx');

    // Row 3 is a formula, row 4 duplicates row 2 (case-insensitive).
    const csv = `Route Name,Description\n${a},Main road\n=1+1,Bad\n${a.toUpperCase()},Copy\n`;
    await page.locator('[data-bi-file]').setInputFiles({ name: 'routes.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });

    const review = page.locator('[data-bi-panel="3"]');
    await expect(review).toBeVisible();
    await expect(page.locator('[data-bi-total]')).toHaveText('3');
    await expect(page.locator('[data-bi-valid]')).toHaveText('1');
    await expect(page.locator('[data-bi-invalid]')).toHaveText('2');
    const formulaRow = review.locator('tr[data-row="3"]');
    await expect(formulaRow).toContainText('formula protection');
    await expect(review.locator('tr[data-row="4"]')).toContainText('row 2');

    // Fix both rows in place and re-check.
    await formulaRow.locator('input[data-key="routeName"]').fill(b);
    await review.locator('tr[data-row="4"] input[data-key="routeName"]').fill(uid('Route'));
    await page.locator('[data-bi-revalidate]').click();
    await expect(page.locator('[data-bi-invalid]')).toHaveText('0');
    await expect(page.locator('[data-bi-valid]')).toHaveText('3');

    await page.locator('[data-bi-confirm-open]').click();
    const modal = page.locator('#biConfirmModal');
    await expect(modal).toBeVisible();
    await expect(modal).toContainText('3 valid rows');
    await modal.getByRole('button', { name: 'Import now' }).click();

    const done = page.locator('[data-bi-panel="4"]');
    await expect(done).toBeVisible();
    await expect(done.locator('[data-bi-summary-inserted]')).toHaveText('3');
    await expect(done.locator('[data-bi-summary-status]')).toContainText('Completed');

    await done.getByRole('link', { name: 'View import details' }).click();
    await expect(page).toHaveURL(/\/BulkImport\/Details\/\d+$/);
    await expect(page.locator('main')).toContainText('routes.csv');
    await expect(page.locator('main')).toContainText('Every row was inserted.');

    await page.goto('/Transportation');
    await expect(page.locator('main')).toContainText(b);

    await page.goto('/BulkImport/History?category=Routes');
    await expect(page.locator('table')).toContainText('routes.csv');
  });

  test('manual entry with skipped invalid rows and a downloadable error report', async ({ page }) => {
    const ok = uid('Driver');
    await page.goto('/BulkImport?category=Drivers');
    await page.getByRole('tab', { name: 'Enter manually' }).click();
    const grid = page.locator('[data-bi-manual-grid]');
    await expect(grid).toBeVisible();
    await expect(page.locator('[data-bi-dropzone]')).toBeHidden();
    await grid.locator('tbody tr').nth(0).locator('input[data-key="fullName"]').fill(ok);
    await grid.locator('tbody tr').nth(0).locator('input[data-key="phone"]').fill('+91 98765 43210');
    await grid.locator('tbody tr').nth(1).locator('input[data-key="fullName"]').fill(uid('Bad'));
    await grid.locator('tbody tr').nth(1).locator('input[data-key="phone"]').fill('12');
    await page.locator('[data-bi-validate-manual]').click();

    await expect(page.locator('[data-bi-valid]')).toHaveText('1');
    await expect(page.locator('[data-bi-invalid]')).toHaveText('1');
    await expect(page.locator('tr[data-row="2"]')).toContainText('8 to 15 digits');

    await page.locator('[data-bi-confirm-open]').click();
    await expect(page.locator('#biConfirmModal')).toContainText('will be skipped');
    await page.getByRole('button', { name: 'Import now' }).click();

    const done = page.locator('[data-bi-panel="4"]');
    await expect(done.locator('[data-bi-summary-status]')).toContainText('Completed with errors');
    await expect(done.locator('[data-bi-summary-failed]')).toHaveText('1');

    const [report] = await Promise.all([
      page.waitForEvent('download'),
      done.locator('[data-bi-summary-errors]').click(),
    ]);
    expect(report.suggestedFilename()).toMatch(/^childguard-import-\d+-errors\.csv$/);
  });
});
