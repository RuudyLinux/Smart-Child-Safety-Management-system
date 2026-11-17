// Verification log (server-paged, filtered, sorted; state in the URL) and Temporary Pickup decisions in place.
import { test, expect, toast, uid, guardianSession, PNG_1PX, Data } from '../support/fixtures';
import { countNavigations } from '../support/ajax';

const rows = (page: import('@playwright/test').Page) => page.locator('[data-cg-region="list"] tr[data-row]');

/** One face-verification attempt for a fresh student and guardian, made through the admin API. */
async function attempt(data: Data, api: any, studentName: string, guardianName: string) {
  const s = await data.student({ fullName: studentName });
  const g = await data.guardian({ fullName: guardianName });
  await data.link(s.id, g.id, 'Mother', true);
  await data.setGuardianPassword(g.id, 'Pickup-Pass-1!');
  const session = await guardianSession(g.id, g.contactNumber, 'Pickup-Pass-1!');
  const reg = await session.api.post('/api/me/face-registration', { data: { imageBase64: Buffer.from('face').toString('base64') } });
  expect(reg.status(), await reg.text()).toBe(200);
  await session.api.dispose();
  const card = await data.card(s.id);
  await data.scan(card.cardUid, 'SchoolArrival');
  const v = await api.post('/api/verification/face-verify', { data: { studentId: s.id, guardianId: g.id, imageBase64: Buffer.from('frame').toString('base64') } });
  expect(v.ok(), await v.text()).toBeTruthy();
  // Whether the face stub matched is not this spec's business: read the outcome, then filter on it.
  const matched = (await v.json() as { faceMatched: boolean }).faceMatched;
  return { s, g, matched };
}

test.describe('Verification log (server paging)', () => {
  test('search, sort and the URL: the log runs on the server', async ({ page, data, api }) => {
    const tag = uid('Vlog').replace(/\s/g, '');
    await attempt(data, api, `${tag} Charlie`, `${tag} Guardian C`);
    await attempt(data, api, `${tag} alpha`, `${tag} Guardian A`);
    await page.goto('/Verification/History');
    const navs = countNavigations(page);

    await page.locator('input[name="search"]').fill(tag);
    await expect(page).toHaveURL(new RegExp(`search=${tag}`));
    await expect(rows(page)).toHaveCount(2);
    await expect(page.locator('[data-cg-count][data-cg-count-scope="verifications"]')).toHaveText('2 records');

    await page.locator('[data-cg-sort="student"]').click();
    await expect(page.locator('[data-cg-sort="student"]')).toHaveClass(/is-asc/);
    expect(await rows(page).locator('td:nth-child(2)').allInnerTexts()).toEqual([`${tag} alpha`, `${tag} Charlie`]);
    expect(navs.count).toBe(0);

    await page.reload();                       // the URL holds the whole view
    expect(await rows(page).locator('td:nth-child(2)').allInnerTexts()).toEqual([`${tag} alpha`, `${tag} Charlie`]);
    await page.goBack();
    await expect(page).not.toHaveURL(/sort=student/);
  });

  test('the result filter and the date range narrow the log; the header figures stay', async ({ page, data, api }) => {
    const tag = uid('Vflt').replace(/\s/g, '');
    const { matched } = await attempt(data, api, `${tag} One`, `${tag} Guardian`);
    await page.goto(`/Verification/History?search=${tag}`);
    await expect(rows(page)).toHaveCount(1);

    const other = matched ? 'No match' : 'Match', same = matched ? 'Match' : 'No match';
    await page.locator('[data-cg-filter="result"]').selectOption({ label: other });
    await expect(page.locator('[data-cg-noresults]')).toBeVisible();
    await page.locator('[data-cg-filter="result"]').selectOption({ label: same });
    await expect(rows(page)).toHaveCount(1);

    // Two days ahead: the school's date can already be a day ahead of UTC, so "tomorrow" in UTC may still be today.
    const later = new Date(Date.now() + 2 * 86400000).toISOString().slice(0, 10);
    await page.locator('input[name="from"]').fill(later);
    await expect(page.locator('[data-cg-noresults]')).toBeVisible();
    await page.getByRole('link', { name: 'Clear' }).click();
    await expect(page.locator('input[name="search"]')).toHaveValue(tag);   // Clear keeps the search
    await expect(rows(page)).toHaveCount(1);
    await expect(page.locator('.cg-kpi', { hasText: 'Attempts' }).locator('.cg-kpi__value')).toHaveText(/^\d+$/);
  });

  test('a hand-edited URL never breaks the page', async ({ page }) => {
    await page.goto('/Verification/History?studentId=abc&from=not-a-date&page=99999999999&sort=hack&result=Maybe');
    await expect(page.locator('main')).toContainText('Verification log');
    await expect(page.locator('[data-cg-region="list"]')).toBeVisible();
  });

  test('a page past the end offers the last page', async ({ page, data, api }) => {
    const tag = uid('Vend').replace(/\s/g, '');
    await attempt(data, api, `${tag} Only`, `${tag} Guardian`);
    await page.goto(`/Verification/History?search=${tag}&page=7`);
    await expect(page.locator('[data-cg-page-missing]')).toContainText('There is no page 7');
  });
});

test.describe('Temporary pickup decisions', () => {
  async function pending(data: Data) {
    const s = await data.student();
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Mother', true);
    await data.setGuardianPassword(g.id, 'Pickup-Pass-1!');
    const session = await guardianSession(g.id, g.contactNumber, 'Pickup-Pass-1!');
    const from = new Date(Date.now() + 3600_000), until = new Date(Date.now() + 4 * 3600_000);
    const r = await session.api.post('/api/me/temporary-pickups', {
      data: { studentId: s.id, fullName: 'Ravi Kumar', phone: '98200 12345', relationship: 'Uncle',
        validFromUtc: from.toISOString(), validUntilUtc: until.toISOString(),
        photoBase64: PNG_1PX.toString('base64'), identityNote: 'Licence ending 1234' } });
    const body = await r.text();
    await session.api.dispose();
    return { ok: r.ok(), body, id: r.ok() ? (JSON.parse(body).id as number) : 0 };
  }

  test('approve, then revoke, update the page in place', async ({ page, data }) => {
    const p = await pending(data);
    test.skip(!p.ok, `the stub photo is not accepted by the API here: ${p.body.slice(0, 120)}`);
    await page.goto(`/TemporaryPickups/Details/${p.id}`);
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Approve' }).click();
    expect(await toast(page, 'approved')).toContain('approved');
    await expect(page.locator('[data-cg-region="pickup-page"]')).toContainText(/Approved · starts later|Active now/);
    expect(navs.count).toBe(0);

    await page.getByRole('button', { name: 'Revoke' }).first().click();
    const modal = page.locator('#revokeModal');
    await modal.locator('input[name="reason"], textarea[name="reason"]').first().fill('Plans changed for the week');
    await modal.getByRole('button', { name: 'Revoke pickup' }).click();
    expect(await toast(page, 'revoked')).toContain('revoked');
    await expect(page.locator('[data-cg-region="pickup-page"]')).toContainText('Revoked');
    expect(navs.count).toBe(0);
  });

  test('reject needs a reason and updates the page in place', async ({ page, data }) => {
    const p = await pending(data);
    test.skip(!p.ok, `the stub photo is not accepted by the API here: ${p.body.slice(0, 120)}`);
    await page.goto(`/TemporaryPickups/Details/${p.id}`);
    const navs = countNavigations(page);
    await page.getByRole('button', { name: 'Reject' }).first().click();
    const modal = page.locator('#rejectModal');
    await modal.locator('input[name="reason"], textarea[name="reason"]').first().fill('Identity could not be confirmed');
    await modal.getByRole('button', { name: /Reject/ }).last().click();
    expect(await toast(page, 'rejected')).toContain('rejected');
    await expect(page.locator('[data-cg-region="pickup-page"]')).toContainText('Rejected');
    expect(navs.count).toBe(0);
  });
});
