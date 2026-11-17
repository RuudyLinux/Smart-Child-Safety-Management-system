import { test, expect } from '../support/fixtures';

const NAV = [
  ['Dashboard', '/Dashboard', 'Dashboard'],
  ['Students', '/Students', 'Students'],
  ['Guardians', '/Guardians', 'Guardians'],
  ['Attendance', '/Attendance', 'Attendance'],
  ['Journey Tracking', '/Journey', 'Journey Tracking'],
  ['Pickup & Drop-off', '/Verification', 'Pickup & Drop-off'],
  ['Verification Log', '/Verification/History', 'Verification Log'],
  ['Transportation', '/Transportation', 'Transportation'],
  ['RFID', '/Rfid', 'RFID'],
  ['Reports', '/Reports', 'Reports'],
  ['Profile', '/Profile', 'Profile'],
  ['Settings', '/Settings', 'Settings'],
] as const;

test.describe('Navigation', () => {
  test('every sidebar item opens its page and becomes the only active item @smoke', async ({ page }) => {
    await page.goto('/Dashboard');
    for (const [label, url] of NAV) {
      const link = page.locator('.cg-sidebar .cg-nav__link', { hasText: label }).first();
      await link.scrollIntoViewIfNeeded();
      await link.click();
      await expect(page, label).toHaveURL(new RegExp(url.replace('/', '\\/') + '$'));
      await expect(page.locator('.cg-nav__link.is-active'), `${label} active`).toHaveCount(1);
      await expect(page.locator('.cg-nav__link.is-active')).toContainText(label);
      await expect(page.locator('main h1, main .cg-page-head__title').first()).toBeVisible();
    }
  });

  test('page titles and breadcrumbs match the page', async ({ page }) => {
    for (const [label, url] of NAV) {
      await page.goto(url);
      const title = (await page.locator('.cg-topbar__title').innerText()).trim();
      expect(title.toLowerCase(), url).toContain(label.split(' ')[0].toLowerCase());
      await expect(page).toHaveTitle(`${title} — ChildGuard`);
      await expect(page.locator('.cg-breadcrumb')).toContainText(title);
      await page.locator('.cg-breadcrumb a', { hasText: 'Home' }).click();
      await expect(page).toHaveURL(/\/Dashboard$/);
    }
  });

  test('sidebar collapses to an icon rail, remembers it, and expands again', async ({ page }) => {
    await page.goto('/Dashboard');
    const toggle = page.getByRole('button', { name: 'Toggle navigation' });
    await toggle.click();
    await expect(page.locator('.cg-app')).toHaveClass(/cg-app--collapsed/);
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await page.reload();
    await expect(page.locator('.cg-app')).toHaveClass(/cg-app--collapsed/);
    // Tooltip names the icon on hover.
    const item = page.locator('.cg-nav__item', { has: page.locator('a[href="/Reports"]') });
    await item.hover();
    await expect(item.locator('.cg-nav__tip')).toBeVisible();
    await toggle.click();
    await expect(page.locator('.cg-app')).not.toHaveClass(/cg-app--collapsed/);
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  });

  test('global search finds pages and falls back to a student search', async ({ page, data }) => {
    const s = await data.student({ fullName: 'Zelda Searchable' });
    await page.goto('/Dashboard');
    await page.keyboard.press('/');
    await expect(page.locator('[data-cg-global-search]')).toBeFocused();
    await page.keyboard.type('transport');
    await expect(page.locator('.cg-search__result').first()).toContainText('Transportation');
    await page.locator('.cg-search__result', { hasText: 'Transportation' }).click();
    await expect(page).toHaveURL(/\/Transportation$/);

    await page.locator('[data-cg-global-search]').fill('Zelda');
    await page.locator('.cg-search__result', { hasText: 'Search students for' }).click();
    await expect(page).toHaveURL(/\/Students\?search=Zelda/);
    await expect(page.locator('tr[data-row]:visible')).toHaveCount(1);
    await expect(page.locator('tr[data-row]:visible')).toContainText(s.fullName);

    await page.locator('[data-cg-global-search]').fill('reports');
    await page.keyboard.press('Escape');
    await expect(page.locator('[data-cg-global-results]')).toBeHidden();
  });

  test('quick action menu items go where they say', async ({ page }) => {
    const items: [string, RegExp][] = [['Add student', /\/Students\/Create$/], ['Add guardian', /\/Guardians\/Create$/], ['Assign RFID card', /\/Rfid$/], ['Open pickup desk', /\/Verification$/]];
    for (const [label, url] of items) {
      await page.goto('/Dashboard');
      await page.getByRole('button', { name: 'Quick action' }).click();
      await page.getByRole('menu').getByText(label).click();
      await expect(page, label).toHaveURL(url);
    }
  });

  test('account menu shows the signed-in admin and links work', async ({ page, stack }) => {
    for (const [label, url] of [['My profile', /\/Profile$/], ['System settings', /\/Settings$/], ['API diagnostics', /\/Diagnostics$/]] as const) {
      await page.goto('/Dashboard');
      await page.getByRole('button', { name: 'Account menu' }).click();
      const menu = page.locator('.cg-dropdown.is-open .cg-dropdown__menu');
      await expect(menu).toContainText(stack.adminEmail);
      await menu.getByText(label).click();
      await expect(page, label).toHaveURL(url);
    }
  });

  test('alerts bell opens the alert list and links to the alert centre', async ({ page }) => {
    await page.goto('/Dashboard');
    const count = page.locator('[data-cg-alert-count]');
    await expect(page.locator('[data-cg-alert-summary]')).not.toHaveText(/Checking current conditions/);
    if (await count.isVisible()) {
      // A visible badge must carry its number, and the button name must speak it.
      await expect(count).toHaveText(/^(\d|9\+)$/);
      const box = await count.boundingBox();
      expect(box!.width, 'badge wide enough for its text').toBeGreaterThanOrEqual(14);
      await expect(page.getByRole('button', { name: /Safety alerts, \d+ open/ })).toBeVisible();
    }
    await page.getByRole('button', { name: 'Safety alerts' }).click();
    const menu = page.locator('.cg-dropdown.is-open .cg-dropdown__menu');
    await expect(menu).toBeVisible();
    await expect(menu.locator('[data-cg-alert-summary]')).not.toHaveText(/Checking current conditions/);
    await menu.getByText('Open alert centre').click();
    await expect(page).toHaveURL(/\/Dashboard#alerts$/);
  });

  // Regression: with zero alerts the badge carries `hidden`, but its display:grid rule
  // overrode it and a red badge showed on the bell anyway.
  test('a hidden alert badge is really hidden', async ({ page }) => {
    await page.goto('/Dashboard');
    const count = page.locator('[data-cg-alert-count]');
    await expect(page.locator('[data-cg-alert-summary]')).not.toHaveText(/Checking current conditions/);
    await count.evaluate(el => { el.textContent = ''; el.hidden = true; });
    await expect(count).toBeHidden();
  });

  // Regression: when the session's API token expired, pages rendered as if the school
  // had no data ("No buses yet", zeros everywhere). Now the server signs the admin out
  // and background requests answer 401, which sends the browser to sign-in.
  test.describe('expired session', () => {
    test.use({ allowErrors: [/status of 401/] });
    test('a 401 from a background request lands on sign-in with an explanation', async ({ browser }) => {
      // Own context: clearing cookies here must not sign out the shared admin state.
      const ctx = await browser.newContext({ storageState: '.e2e/admin.json' });
      const page = await ctx.newPage();
      await page.route('**/Dashboard/AlertsSummary', async route => {
        await ctx.clearCookies(); // what the server does: the session is ended
        await route.fulfill({ status: 401, contentType: 'application/json', body: '{"message":"Your session has expired. Sign in again."}' });
      });
      await page.goto('/Dashboard');
      await expect(page).toHaveURL(/\/Account\/Login\?expired=true$/);
      await expect(page.locator('.cg-toast')).toContainText('Your session expired');
      await ctx.close();
    });
  });

  test.describe('missing records', () => {
    test.use({ allowErrors: [/status of 404/] });
    test('unknown pages and records answer with a not-found response, not a crash', async ({ page }) => {
      for (const url of ['/Students/Details/987654', '/Guardians/Details/987654', '/Rfid/Details/987654', '/Journey/Student/987654', '/No/Such/Page']) {
        const res = await page.goto(url);
        expect(res!.status(), url).toBeLessThan(500);
        await expect(page.locator('body'), url).not.toContainText(/exception|stack trace/i);
      }
    });
  });
});
