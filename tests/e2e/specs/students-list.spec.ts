// Students list on server paging: search, filters, sort and pages come from the server, only the list region
// is re-rendered, and the URL holds the view (Back, Forward and Refresh restore it).
import { test, expect, uid } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

const rows = (page: import('@playwright/test').Page) => page.locator('[data-cg-region="list"] tr[data-row]');
const names = (page: import('@playwright/test').Page) => page.locator('[data-cg-region="list"] tr[data-row] .cg-identity__name').allInnerTexts();

test.describe('Students list (server paging)', () => {
  test('search filters on the server without a page load @smoke', async ({ page, data }) => {
    const tag = uid('ListX').replace(/\s/g, '');
    for (let i = 0; i < 30; i++) await data.student({ fullName: `${tag} ${String(i).padStart(2, '0')}`, grade: `G${tag}` });
    await page.goto('/Students');
    const navs = countNavigations(page);

    await page.locator('input[name="search"]').fill(tag);
    // Debounced typing replaces the URL; no button press needed.
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    await expect(rows(page)).toHaveCount(25);
    await expect(page.locator('[data-cg-count][data-cg-count-scope="students"]')).toHaveText('30 records');
    await expect(page.locator('.cg-pagination')).toContainText('Showing 1–25 of 30');
    expect(navs.count).toBe(0);
  });

  test('list state survives reload and back', async ({ page, data }) => {
    const tag = uid('State').replace(/\s/g, '');
    for (let i = 0; i < 27; i++) await data.student({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
    await page.goto(`/Students?search=${tag}`);
    const navs = countNavigations(page);

    await page.locator('[data-cg-sort="name"]').click();          // name ascending
    await page.locator('[data-cg-sort="name"]').click();          // name descending
    await expect(page.locator('[data-cg-sort="name"]')).toHaveClass(/is-desc/);
    await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
    await expect(page).toHaveURL(new RegExp(`search=${tag}.*sort=name&dir=desc&page=2`));
    expect(await names(page)).toEqual([`${tag} 01`, `${tag} 00`]);
    expect(navs.count).toBe(0);

    await page.reload();
    expect(await names(page)).toEqual([`${tag} 01`, `${tag} 00`]);
    await expect(page.locator('[data-cg-sort="name"]')).toHaveClass(/is-desc/);

    await page.goBack();   // page 1 of the same sort
    await expect(page).not.toHaveURL(/page=2/);
    await expect(rows(page)).toHaveCount(25);
    expect((await names(page))[0]).toBe(`${tag} 26`);
    await page.goForward();
    await expect(page).toHaveURL(/page=2/);
    await expect(rows(page)).toHaveCount(2);
  });

  test('going to another page brings the start of the table into view', async ({ page, data }) => {
    const tag = uid('Scroll').replace(/\s/g, '');
    // Page 2 is full (25 rows), so the page does not simply get shorter and clamp the scroll.
    for (let i = 0; i < 50; i++) await data.student({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
    await page.setViewportSize({ width: 1280, height: 700 });
    await page.goto(`/Students?search=${tag}`);
    const pagerLink = page.locator('[data-cg-pager] a', { hasText: '2' });
    await pagerLink.scrollIntoViewIfNeeded();
    expect((await page.locator('[data-cg-region="list"]').boundingBox())!.y).toBeLessThan(0);   // table top scrolled away

    await pagerLink.click();
    await expect(page).toHaveURL(/page=2/);
    await expect(rows(page).first()).toContainText(` 25`);
    const top = (await page.locator('[data-cg-region="list"]').boundingBox())!.y;
    expect(top).toBeGreaterThanOrEqual(-1);
    expect(top).toBeLessThan(700);
    // ...below the sticky top bar, so the count and the column headers are visible, not hidden under it.
    const bar = (await page.locator('.cg-topbar').boundingBox())!;
    const header = (await page.locator('[data-cg-region="list"] thead').boundingBox())!;
    expect(header.y).toBeGreaterThanOrEqual(bar.y + bar.height - 1);
  });

  test('a list change announces the new count to screen readers', async ({ page, data }) => {
    const tag = uid('Announce').replace(/\s/g, '');
    await data.student({ fullName: `${tag} One` });
    await data.student({ fullName: `${tag} Two` });
    await page.goto('/Students');
    await page.locator('input[name="search"]').fill(tag);
    await expect(page.locator('[data-cg-list-status]')).toHaveText('2 records');
  });

  test('filters keep each other and the search; changing a filter starts at page 1', async ({ page, data }) => {
    const tag = uid('Combo').replace(/\s/g, '');
    const carded = await data.student({ fullName: `${tag} Carded` });
    await data.card(carded.id);
    await data.student({ fullName: `${tag} Plain` });
    await page.goto(`/Students?search=${tag}`);

    await page.locator('[data-cg-filter="rfid"]').selectOption({ label: 'Card assigned' });
    await expect(page).toHaveURL(new RegExp(`search=${tag}&rfid=assigned`));
    await expect(rows(page)).toHaveCount(1);
    await expect(rows(page)).toContainText('Carded');

    // Back restores the unfiltered view and the select shows it.
    await page.goBack();
    await expect(rows(page)).toHaveCount(2);
    await expect(page.locator('[data-cg-filter="rfid"]')).toHaveValue('__all');
  });

  test('the grade filter offers grades from the whole school, not just this page', async ({ page, data }) => {
    const grade = `Z${uid('g').replace(/\W/g, '').slice(-6)}`;
    await data.student({ grade });
    await page.goto('/Students?search=zz-nobody-zz');
    await expect(page.locator('[data-cg-filter="grade"] option', { hasText: grade })).toHaveCount(1);
  });

  test('no match shows the filtered empty state, Clear keeps the search', async ({ page, data }) => {
    const tag = uid('Empty').replace(/\s/g, '');
    await data.student({ fullName: `${tag} Only` });
    await page.goto(`/Students?search=${tag}`);
    await page.locator('[data-cg-filter="guardian"]').selectOption({ label: 'Has a guardian' });
    await expect(page.locator('[data-cg-noresults][data-cg-noresults-scope="students"]')).toBeVisible();
    await expect(page.locator('[data-cg-count][data-cg-count-scope="students"]')).toHaveText('0 records');

    await page.getByRole('link', { name: 'Clear' }).click();
    await expect(rows(page)).toHaveCount(1);
    await expect(page).toHaveURL(new RegExp(`search=${tag}$`));
    await expect(page.locator('[data-cg-filter="guardian"]')).toHaveValue('__all');
  });

  test.describe('server failure', () => {
    // One pattern: Playwright reads a two-element option array as [value, options].
    test.use({ allowErrors: [/status of 500|\/Students\?/] });

    test('a failed list load keeps the rows and says so', async ({ page, data }) => {
      const tag = uid('Fail').replace(/\s/g, '');
      await data.student({ fullName: `${tag} Kept` });
      await page.goto(`/Students?search=${tag}`);
      await page.route('**/Students?*', route => route.request().headers()['x-cg-regions']
        ? route.fulfill({ status: 500, body: 'boom' })
        : route.continue());
      await page.locator('[data-cg-filter="rfid"]').selectOption({ label: 'No card' });
      await expect(page.locator('.cg-toast', { hasText: 'Something went wrong on the server' })).toBeVisible();
      await expect(rows(page)).toContainText(`${tag} Kept`);
      await expect(page.locator('[data-cg-region="list"]')).not.toHaveClass(/cg-region--loading/);
    });
  });

  test.describe('without JavaScript', () => {
    test('search, filter and paging still work as plain links and forms', async ({ browser, data }) => {
      const tag = uid('NoJs').replace(/\s/g, '');
      for (let i = 0; i < 26; i++) await data.student({ fullName: `${tag} ${String(i).padStart(2, '0')}` });
      const context = await browser.newContext({ javaScriptEnabled: false, storageState: '.e2e/admin.json' });
      const page = await context.newPage();
      await page.goto('/Students');
      await page.locator('input[name="search"]').fill(tag);
      await page.getByRole('button', { name: 'Search', exact: true }).click();
      await expect(page.locator('tr[data-row]')).toHaveCount(25);
      await page.locator('[data-cg-pager] a', { hasText: '2' }).click();
      await expect(page.locator('tr[data-row]')).toHaveCount(1);
      await context.close();
    });
  });
});
