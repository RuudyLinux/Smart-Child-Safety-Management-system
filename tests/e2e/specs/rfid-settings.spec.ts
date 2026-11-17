import { test, expect, toast, uid } from '../support/fixtures';

test.describe('RFID cards', () => {
  test('assign a card to a student through the modal and see it masked in the list @smoke', async ({ page, data }) => {
    const s = await data.student();
    const uidValue = 'CARD' + Date.now().toString(16).toUpperCase();
    await page.goto('/Rfid');
    await page.locator('[data-cg-modal-open="assignCardModal"]').first().click();
    const modal = page.locator('#assignCardModal');
    await modal.locator('input[name="cardUid"]').fill(uidValue);
    await modal.locator('select[name="studentId"]').selectOption(String(s.id));
    await modal.getByRole('button', { name: 'Assign card' }).click();
    await toast(page);
    const row = page.locator('tr[data-row]', { hasText: s.fullName });
    await expect(row).toContainText(uidValue.slice(-4));
    await expect(row).not.toContainText(uidValue); // never shown in full
  });

  test.describe('duplicates', () => {
    test.use({ allowErrors: [/status of 400/] });
    test('the same card UID cannot be assigned twice', async ({ page, data }) => {
      const a = await data.student();
      const card = await data.card(a.id);
      const b = await data.student();
      await page.goto('/Rfid');
      await page.locator('[data-cg-modal-open="assignCardModal"]').first().click();
      const modal = page.locator('#assignCardModal');
      await modal.locator('input[name="cardUid"]').fill(card.cardUid);
      await modal.locator('select[name="studentId"]').selectOption(String(b.id));
      await modal.getByRole('button', { name: 'Assign card' }).click();
      const t = page.locator('.cg-toast--danger');
      await expect(t).toBeVisible();
    });
  });

  test('mark a card lost, filter by status, open details', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await page.goto('/Rfid');
    const form = page.locator(`form[action$="/Rfid/UpdateStatus/${card.id}"]`);
    await form.locator('select[name="status"]').selectOption('Lost');
    await form.getByRole('button', { name: 'Update' }).click();
    await toast(page);
    await page.locator('[data-cg-filter="state"]').selectOption({ label: 'Lost' });
    await expect(page.locator('tr[data-row]:visible', { hasText: s.fullName })).toHaveCount(1);
    await page.locator('[data-cg-filter="state"]').selectOption({ label: 'Active' });
    await expect(page.locator('tr[data-row]:visible', { hasText: s.fullName })).toHaveCount(0);

    await page.goto(`/Rfid/Details/${card.id}`);
    await expect(page.locator('main')).toContainText(/Lost/);
    await page.getByRole('link', { name: 'Student profile' }).click();
    await expect(page).toHaveURL(new RegExp(`/Students/Details/${s.id}$`));
  });
});

test.describe('Settings (scanner keys)', () => {
  // The duplicate-label refusal is an HTTP 400 answer to the form's request, which the browser logs.
  test.use({ allowErrors: [/status of 400/] });
  test('issue a scanner key: shown once, then usable by the scanner, then revoked', async ({ page, data, request }) => {
    const device = await data.device();
    await page.goto('/Settings');
    page.once('dialog', d => d.accept());
    const issue = page.locator(`form[action$="/Settings/IssueKey/${device.rfidDeviceId}"] button[type="submit"]`);
    await issue.click();
    const keyField = page.getByLabel('Scanner API key');
    await expect(keyField).toBeVisible();
    const key = (await keyField.inputValue()).trim();
    expect(key.length).toBeGreaterThan(20);

    // The key really authenticates a scanner (heartbeat as the device).
    const hb = await request.post(`http://localhost:5375/api/rfid/devices/${device.rfidDeviceId}/heartbeat`, { headers: { 'X-Device-Key': key } });
    expect(hb.status()).toBe(200);

    // Shown once: a reload does not reveal it again.
    await page.goto('/Settings');
    await expect(page.locator('main')).not.toContainText(key);

    page.once('dialog', d => d.accept());
    await page.locator(`form[action$="/Settings/RevokeKey/${device.rfidDeviceId}"] button[type="submit"]`).click();
    await toast(page);
    const after = await request.post(`http://localhost:5375/api/rfid/devices/${device.rfidDeviceId}/heartbeat`, { headers: { 'X-Device-Key': key } });
    expect(after.status()).toBe(401);
  });

  test.describe('diagnostics', () => {
  // The page deliberately calls protected endpoints anonymously and sends invalid
  // logins: a 401/400 there is a PASS, and the browser logs each one.
  test.use({ allowErrors: [/status of 40[01]/] });
  test('Run diagnostics checks the API from the browser', async ({ page }) => {
    await page.goto('/Settings');
    await page.getByRole('link', { name: 'Run diagnostics' }).click();
    await expect(page).toHaveURL(/\/Diagnostics$/);
    await page.locator('#apiBase').fill('http://localhost:5375');
    await page.getByRole('button', { name: 'Run all checks' }).click();
    const conn = page.locator('#connectivity');
    await expect(conn.getByText('PASS').first()).toBeVisible();
    await expect(conn).not.toContainText('checking…', { timeout: 15_000 });
    await expect(conn).not.toContainText('FAIL');
    // Checked by the API, which reaches the face service the way pickups do.
    const face = conn.locator('.dg-row', { hasText: 'Face service' });
    await expect(face).toContainText('PASS');
    await expect(face).toContainText('liveness model loaded');
  });
  });

  test('add a scanner, rename it, and use it at the desk', async ({ page }) => {
    const label = ('GATE-' + uid('X').replace(/\W/g, '')).slice(0, 30);
    await page.goto('/Settings');
    await page.getByRole('button', { name: 'Add scanner' }).click();
    await page.locator('#addDeviceModal input[name="deviceLabel"]').fill(label);
    await page.locator('#addDeviceModal').getByRole('button', { name: 'Add scanner' }).click();
    await expect(page.locator('.cg-toast', { hasText: `Scanner ${label} added` })).toBeVisible();
    await expect(page.locator('main tr', { hasText: label })).toContainText('No key');

    // Labels are unique.
    await page.getByRole('button', { name: 'Add scanner' }).click();
    await page.locator('#addDeviceModal input[name="deviceLabel"]').fill(label.toLowerCase());
    await page.locator('#addDeviceModal').getByRole('button', { name: 'Add scanner' }).click();
    await expect(page.locator('.cg-toast', { hasText: 'already exists' })).toBeVisible();
    // The refusal keeps the form open so the label can be corrected; close it before moving on.
    await page.locator('#addDeviceModal').getByRole('button', { name: 'Cancel' }).click();

    const renamed = label.replace('GATE', 'NORTH');
    await page.getByRole('button', { name: `Rename ${label}` }).click();
    const modal = page.locator('.cg-modal:not([hidden])');
    await modal.locator('input[name="deviceLabel"]').fill(renamed);
    await modal.getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('.cg-toast', { hasText: `Scanner renamed to ${renamed}` })).toBeVisible();

    await page.goto('/Verification');
    await expect(page.locator('#deviceSelect option', { hasText: renamed })).toHaveCount(1);
  });
});
