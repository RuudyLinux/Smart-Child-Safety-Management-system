// Shared AJAX layer (wwwroot/js/cg-ajax.js): forms save without a page load, regions refresh in place,
// widgets inside refreshed regions keep working, and double submits / network loss are handled.
// Exercised through the Students Details guardian forms, the pilot pages for the layer.
import { test, expect, toast } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

test.describe('AJAX layer', () => {
  test('linking a guardian refreshes the guardians region without a page load @smoke', async ({ page, data }) => {
    const s = await data.student();
    const g = await data.guardian();
    await page.goto(`/Students/Details/${s.id}#guardians`);
    const navs = countNavigations(page);

    await page.locator('[data-cg-modal-open="linkGuardianModal"]').click();
    await page.locator('#linkGuardianId').selectOption(String(g.id));
    await page.locator('#linkRelationship').fill('Aunt');
    await page.locator('#linkGuardianModal').getByRole('button', { name: 'Link guardian' }).click();

    expect(await toast(page, 'Guardian linked to this student.')).toContain('Guardian linked');
    const region = page.locator('[data-cg-region="guardians"]');
    await expect(region).toContainText(g.fullName);
    await expect(page.locator('#linkGuardianModal')).toBeHidden();
    expect(navs.count).toBe(0);
  });

  test('widgets work after region refresh', async ({ page, data }) => {
    const s = await data.student();
    const g = await data.guardian();
    await page.goto(`/Students/Details/${s.id}#guardians`);
    const navs = countNavigations(page);

    await page.locator('[data-cg-modal-open="linkGuardianModal"]').click();
    await page.locator('#linkGuardianId').selectOption(String(g.id));
    await page.locator('#linkGuardianModal').getByRole('button', { name: 'Link guardian' }).click();
    const region = page.locator('[data-cg-region="guardians"]');
    await expect(region).toContainText(g.fullName);

    // Tabs still switch after the refresh, and come back to the refreshed panel.
    await page.locator('[data-cg-tab="overview"]').click();
    await expect(page.locator('[data-cg-tabpanel="overview"]')).toBeVisible();
    await page.locator('[data-cg-tab="guardians"]').click();
    await expect(region).toBeVisible();

    // The confirm dialog on a form inside the swapped region still runs, exactly once.
    let dialogs = 0;
    page.on('dialog', d => { dialogs++; d.accept(); });
    await region.getByRole('button', { name: 'Unlink' }).click();
    expect(await toast(page, 'Guardian unlinked.')).toContain('The guardian account itself was not deleted.');
    await expect(region).not.toContainText(g.fullName);
    expect(dialogs).toBe(1);

    // The modal opener rendered by the refresh opens the modal again.
    await page.locator('[data-cg-modal-open="linkGuardianModal"]').click();
    await expect(page.locator('#linkGuardianModal')).toBeVisible();
    expect(navs.count).toBe(0);
  });

  test('double submit sends one request', async ({ page, data }) => {
    const s = await data.student();
    const g = await data.guardian();
    await page.goto(`/Students/Details/${s.id}#guardians`);
    let posts = 0;
    page.on('request', r => { if (r.method() === 'POST' && /\/Students\/AssignGuardian\//.test(r.url())) posts++; });

    await page.locator('[data-cg-modal-open="linkGuardianModal"]').click();
    await page.locator('#linkGuardianId').selectOption(String(g.id));
    const submit = page.locator('#linkGuardianModal').getByRole('button', { name: 'Link guardian' });
    await submit.dblclick();
    await page.keyboard.press('Enter');
    await expect(page.locator('[data-cg-region="guardians"]')).toContainText(g.fullName);
    expect(posts).toBe(1);
  });

  test.describe('offline', () => {
    // The browser logs the blocked request itself; that is the point of this test.
    // One pattern: Playwright reads a two-element option array as [value, options].
    test.use({ allowErrors: [/\/Students\/UpdateGuardianLink\/|ERR_INTERNET_DISCONNECTED/] });

    test('offline save shows offline toast and re-enables the form', async ({ page, data, context }) => {
      const s = await data.student();
      const g = await data.guardian();
      await data.link(s.id, g.id, 'Mother', true);
      await page.goto(`/Students/Details/${s.id}#guardians`);

      await context.setOffline(true);
      const region = page.locator('[data-cg-region="guardians"]');
      const save = region.getByRole('button', { name: 'Save' });
      await save.click();
      expect(await toast(page, 'offline')).toContain('You appear to be offline');
      await expect(save).toBeEnabled();
      await expect(save).not.toHaveClass(/is-loading/);
      await context.setOffline(false);
    });
  });
});
