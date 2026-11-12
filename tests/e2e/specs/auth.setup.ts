import { test as setup, expect } from '@playwright/test';
import { readState } from '../support/stack';

setup('admin signs in through the login page', async ({ page }) => {
  const { adminEmail, adminPassword } = readState();
  await page.goto('/Account/Login');
  await page.getByLabel('Email address').fill(adminEmail);
  await page.getByLabel('Password', { exact: true }).fill(adminPassword);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).not.toHaveURL(/Account\/Login/);
  await expect(page.locator('.cg-topbar__title')).toHaveText('Dashboard');
  await page.context().storageState({ path: '.e2e/admin.json' });
});
