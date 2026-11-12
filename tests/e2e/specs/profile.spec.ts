import { test, expect, toast } from '../support/fixtures';
import { readState } from '../support/stack';

// Changes the only admin's password, so it runs on its own and restores it at the end.
test.describe.serial('Profile', () => {
  test.use({ allowErrors: [/status of 400|status of 401/] });

  test('shows the signed-in admin', async ({ page, stack }) => {
    await page.goto('/Profile');
    await expect(page.locator('main')).toContainText(stack.adminEmail);
    await expect(page.locator('main')).toContainText('E2E Administrator');
  });

  test('wrong current password is refused', async ({ page }) => {
    await page.goto('/Profile');
    await page.locator('input[name="currentPassword"]').fill('Not-the-password-1!');
    await page.locator('input[name="newPassword"]').fill('Another-Pass-123!');
    await page.getByRole('button', { name: 'Update password' }).click();
    expect(await toast(page)).toMatch(/incorrect|invalid|wrong|not match/i);
  });

  test('a weak new password is refused', async ({ page, stack }) => {
    await page.goto('/Profile');
    await page.locator('input[name="currentPassword"]').fill(stack.adminPassword);
    await page.locator('input[name="newPassword"]').fill('short');
    await page.getByRole('button', { name: 'Update password' }).click();
    // Browser minlength or API validation; either way the old password still works.
    const res = await page.request.post('http://localhost:5375/api/auth/admin/login', { data: { email: stack.adminEmail, password: stack.adminPassword } });
    expect(res.status()).toBe(200);
  });

  test('change the password, sign in with the new one, then change it back', async ({ page, stack }) => {
    const next = stack.adminPassword + 'Z9';
    await page.goto('/Profile');
    await page.locator('input[name="currentPassword"]').fill(stack.adminPassword);
    await page.locator('input[name="newPassword"]').fill(next);
    await page.locator('[data-cg-reveal="newPassword"]').click();
    await expect(page.locator('input[name="newPassword"]')).toHaveAttribute('type', 'text');
    await page.getByRole('button', { name: 'Update password' }).click();
    expect(await toast(page)).toMatch(/updated|changed/i);

    const login = (pw: string) => page.request.post('http://localhost:5375/api/auth/admin/login', { data: { email: stack.adminEmail, password: pw } });
    expect((await login(next)).status()).toBe(200);
    expect((await login(stack.adminPassword)).status()).toBe(401);

    await page.goto('/Profile');
    await page.locator('input[name="currentPassword"]').fill(next);
    await page.locator('input[name="newPassword"]').fill(readState().adminPassword);
    await page.getByRole('button', { name: 'Update password' }).click();
    await toast(page);
    expect((await login(stack.adminPassword)).status()).toBe(200);
  });
});
