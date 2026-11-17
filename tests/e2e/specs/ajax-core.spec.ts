// Shared AJAX layer (wwwroot/js/cg-ajax.js): forms save without a page load, regions refresh in place,
// widgets inside refreshed regions keep working, and double submits / network loss are handled.
// Exercised through the Students Details guardian forms, the pilot pages for the layer.
import { test, expect, toast, PNG_1PX } from '../support/fixtures';
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

  test('two quick saves both show their result (a refresh never cancels another)', async ({ page, data }) => {
    const s = await data.student();
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Mother', true);
    await page.goto(`/Students/Details/${s.id}#guardians`);

    // Hold the first region refresh back so the second save happens while it is still loading.
    let held = false;
    await page.route(`**/Students/Details/${s.id}`, async route => {
      if (route.request().headers()['x-cg-regions'] && !held) {
        held = true;
        await new Promise(r => setTimeout(r, 1500));
      }
      await route.continue();
    });

    const guardians = page.locator('[data-cg-region="guardians"]');
    await guardians.locator('input[name="relationship"]').fill('Aunt');
    await guardians.getByRole('button', { name: 'Save' }).click();
    await expect.poll(() => held).toBe(true);

    // A second, independent refresh (another part of the page) starts while the first is still loading.
    // A region being refreshed cannot be clicked, so this is the shared call any page script makes.
    await page.evaluate(() => (window as unknown as { cg: { refresh(n: string): Promise<void> } }).cg.refresh('lifecycle'));

    // The first save's parts were refreshed too, not cancelled by the second refresh.
    await expect(guardians.locator('.cg-personcard__meta')).toHaveText('Aunt');
    await expect(page.locator('[data-cg-region="guardian-count"]')).toHaveText('1');
  });

  test('refreshing the photo section again and again adds no listeners', async ({ page, data }) => {
    await page.addInitScript(() => {
      const w = window as unknown as { __pagehide: number };
      w.__pagehide = 0;
      const original = window.addEventListener;
      window.addEventListener = function (type: string, ...rest: unknown[]) {
        if (type === 'pagehide') w.__pagehide++;
        return (original as (...a: unknown[]) => void).call(this, type, ...rest);
      } as typeof window.addEventListener;
    });
    const s = await data.student();
    await page.goto(`/Students/Details/${s.id}#photo`);
    const count = () => page.evaluate(() => (window as unknown as { __pagehide: number }).__pagehide);
    const before = await count();

    const region = page.locator('[data-cg-region="photo"]');
    for (let i = 0; i < 3; i++) {
      await region.locator('input[type="file"]').setInputFiles({ name: `p${i}.png`, mimeType: 'image/png', buffer: PNG_1PX });
      await region.locator('[data-cg-upload-submit]').click();
      await expect(page.locator('.cg-toast', { hasText: 'Student photo saved.' }).first()).toBeVisible();
      // Wait for the refresh to finish (the button is also disabled while busy, so that alone is not enough).
      await expect(page.locator('[data-cg-region="photo"].cg-region--loading')).toHaveCount(0);
      await expect(region.locator('[data-cg-upload-submit]:not(.is-loading)')).toBeDisabled();   // the refreshed, empty form
    }
    expect(await count()).toBe(before);
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
