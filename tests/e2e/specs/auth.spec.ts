import { test, expect } from '../support/fixtures';

test.describe('Authentication', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('protected pages redirect to sign-in @smoke', async ({ page }) => {
    for (const url of ['/Dashboard', '/Students', '/Settings', '/Reports/ExportAttendance']) {
      await page.goto(url);
      await expect(page, url).toHaveURL(/\/Account\/Login/);
    }
  });

  test.describe('sign-in form', () => {
    // The API answers a wrong password with 401; AdminWeb shows it inline.
    test.use({ allowErrors: [/status of 401/] });

    test('empty fields are rejected without a round trip', async ({ page }) => {
      await page.goto('/Account/Login');
      await page.getByRole('button', { name: /sign in/i }).click();
      await expect(page).toHaveURL(/\/Account\/Login/);
      expect(await page.getByLabel('Email address').evaluate((e: HTMLInputElement) => e.validity.valid)).toBe(false);
    });

    test('wrong password shows an error and keeps the email @smoke', async ({ page }) => {
      await page.goto('/Account/Login');
      await page.getByLabel('Email address').fill('nobody@childguard.test');
      await page.getByLabel('Password', { exact: true }).fill('Wrong-password-1!');
      await page.getByRole('button', { name: /sign in/i }).click();
      await expect(page.getByText('Invalid email or password.')).toBeVisible();
      await expect(page.getByLabel('Email address')).toHaveValue('nobody@childguard.test');
    });

    test('show/hide password toggle reveals the value', async ({ page }) => {
      await page.goto('/Account/Login');
      const pw = page.getByLabel('Password', { exact: true });
      await pw.fill('secret-value');
      await page.locator('[data-cg-reveal="Password"]').click();
      await expect(pw).toHaveAttribute('type', 'text');
      await page.locator('[data-cg-reveal="Password"]').click();
      await expect(pw).toHaveAttribute('type', 'password');
    });

    test('valid sign-in reaches the dashboard and sign-out ends the session @smoke', async ({ page, stack }) => {
      await page.goto('/Account/Login');
      await page.getByLabel('Email address').fill(stack.adminEmail);
      await page.getByLabel('Password', { exact: true }).fill(stack.adminPassword);
      await page.getByRole('button', { name: /sign in/i }).click();
      await expect(page.locator('.cg-topbar__title')).toHaveText('Dashboard');
      await expect(page.locator('.cg-sidebar__user-name')).toHaveText('E2E Administrator');

      await page.getByRole('button', { name: 'Account menu' }).click();
      await page.getByRole('menu').getByText('Sign out').click();
      await expect(page).toHaveURL(/\/Account\/Login/);
      await page.goto('/Dashboard');
      await expect(page).toHaveURL(/\/Account\/Login/);
    });
  });

  test('forgot-password page accepts an email without revealing whether it exists', async ({ page }) => {
    await page.goto('/Account/Login');
    await page.getByRole('link', { name: /forgot password/i }).click();
    await expect(page).toHaveURL(/ForgotPassword/);
    await page.getByLabel(/email/i).fill('unknown-person@childguard.test');
    await page.getByRole('button', { name: /send|reset|continue/i }).click();
    await expect(page.locator('body')).not.toContainText(/not found|does not exist|no account/i);
  });
});
