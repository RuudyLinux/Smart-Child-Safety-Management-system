import path from 'node:path';
import { Page, Locator } from '@playwright/test';
import { test, expect, uid } from '../support/fixtures';

// Walkthrough screenshots, kept with the run's other artifacts.
const SHOTS = path.join(__dirname, '..', 'test-results', 'enrollment-walkthrough');
// A tall viewport instead of fullPage: the layout's fixed header and sidebar would repeat across a stitched capture.
const shot = async (page: Page, name: string) => {
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(400); // let fade-in animations finish
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
};

test.use({ viewport: { width: 1366, height: 1500 } });

const phone = () => '97' + Math.floor(Math.random() * 1e8).toString().padStart(8, '0');
const email = () => `enrol${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}@childguard.test`;
const cardUid = () => 'EN' + Math.random().toString(16).slice(2, 12).toUpperCase();

async function openWizard(page: Page) {
  await page.goto('/Students');
  await page.locator('.cg-page-head').getByRole('link', { name: 'Enroll New Student' }).click();
  await expect(page).toHaveURL(/\/Enrollment$/);
}

async function fillStudent(page: Page, name: string, withAddress = true) {
  await expect(page.locator('#enFullName')).toBeFocused();
  await page.locator('#enFullName').fill(name);
  await page.locator('#enAdmission').fill('ADM-' + Date.now().toString(36).toUpperCase());
  await page.locator('#enDob').fill('2016-05-14');
  await page.locator('#enGender').selectOption('Female');
  await page.locator('#enGrade').fill('4');
  await page.locator('#enSection').fill('B');
  if (withAddress) {
    const a = page.locator('[data-en-address="student"]');
    await a.locator('[data-a="line1"]').fill('12 Lake View Road, Flat 4B');
    await a.locator('[data-a="locality"]').fill('Bandra West');
    await a.locator('[data-a="landmark"]').fill("St. Andrew's Church");
    await a.locator('[data-a="city"]').fill('Mumbai');
    await a.locator('[data-a="district"]').fill('Mumbai Suburban');
    await a.locator('[data-a="state"]').fill('Maharashtra');
    await a.locator('[data-a="postalCode"]').fill('400050');
  }
}

async function fillNewGuardian(g: Locator, name: string, mail = email(), contact = phone()) {
  await g.locator('[data-g="fullName"]').fill(name);
  await g.locator('[data-g="email"]').fill(mail);
  await g.locator('[data-g="contactNumber"]').fill(contact);
  await g.locator('[data-g="relationship"]').fill('Mother');
}

const next = (page: Page) => page.getByRole('button', { name: 'Next' }).click();

test.describe('Enrollment wizard', () => {
  test('enrolls a student with a new and an existing guardian, a scanned card and a bus @smoke', async ({ page, data }) => {
    const existing = await data.guardian();
    const driver = await data.driver();
    const route = await data.route();
    const bus = await data.bus(driver.id, route.id);
    const name = uid('Diya');
    const guardianName = uid('Meera');
    const uidValue = cardUid();

    await openWizard(page);
    await fillStudent(page, name);
    await shot(page, '01-student');
    await next(page);

    const first = page.locator('[data-en-guardian]').nth(0);
    await fillNewGuardian(first, guardianName);
    await page.getByRole('button', { name: 'Add another guardian' }).click();
    const second = page.locator('[data-en-guardian]').nth(1);
    await second.getByRole('radio', { name: 'Link existing' }).click();
    await second.locator('[data-en-gquery]').fill(existing.email);
    await second.locator('.cg-enrol-result').first().click();
    await expect(second.locator('[data-en-gselected-name]')).toHaveText(existing.fullName);
    await second.locator('[data-g="relationship"]').fill('Father');
    await shot(page, '02-guardians');
    await next(page);

    // The USB reader in keyboard mode: it types the UID into the focused field and presses Enter.
    await expect(page.locator('#enCardUid')).toBeFocused();
    await page.keyboard.type(uidValue, { delay: 5 });
    await page.keyboard.press('Enter');
    await expect(page.locator('[data-en-scan]')).toHaveAttribute('data-state', 'ok');
    await expect(page.locator('[data-en-scan-status]')).toContainText('New card');
    await shot(page, '03-rfid');
    await next(page);

    await page.getByRole('radio', { name: 'Travels by school bus' }).click();
    await page.locator('#enBus').selectOption(String(bus.id));
    await expect(page.locator('[data-en-bus-driver]')).toHaveText(driver.fullName);
    await shot(page, '04-transport');
    await next(page);

    const review = page.locator('[data-en-review]');
    await expect(review).toContainText(name);
    await expect(review).toContainText('New account');
    await expect(review).toContainText('Existing account');
    await expect(review).toContainText(uidValue.slice(-4));
    await expect(review).toContainText(bus.busNumber);
    await shot(page, '05-review');

    // Going back keeps everything entered.
    await review.getByRole('button', { name: 'Edit' }).first().click();
    await expect(page.locator('#enFullName')).toHaveValue(name);
    for (let i = 0; i < 4; i++) await next(page);

    await page.getByRole('button', { name: 'Complete enrollment' }).click();
    const done = page.locator('[data-en-done]');
    await expect(done.getByRole('heading', { level: 2 })).toHaveText(`${name} is enrolled`);
    await expect(done).toContainText('Sign-in details emailed');
    await expect(done).toContainText('Existing account linked');
    await expect(done).toContainText(bus.busNumber);
    await shot(page, '06-done');

    await done.getByRole('link', { name: 'Open student profile' }).click();
    await expect(page).toHaveURL(/\/Students\/Details\/\d+/);
    await expect(page.locator('body')).toContainText('12 Lake View Road');
    await expect(page.locator('body')).toContainText('Section B');
    await expect(page.locator('body')).toContainText(guardianName);
    await expect(page.locator('body')).toContainText(existing.fullName);
    await shot(page, '07-profile');
  });

  test('a card already assigned is refused at the scan step, and RFID can be left pending', async ({ page, data }) => {
    const taken = await data.student();
    const card = await data.card(taken.id);
    const name = uid('Kabir');

    await openWizard(page);
    await fillStudent(page, name, false);
    await next(page);
    await fillNewGuardian(page.locator('[data-en-guardian]').nth(0), uid('Ravi'));
    await next(page);

    await page.keyboard.type(card.cardUid, { delay: 5 });
    await page.keyboard.press('Enter');
    await expect(page.locator('[data-en-scan]')).toHaveAttribute('data-state', 'error');
    await expect(page.locator('[data-en-scan-status]')).toContainText('already assigned');
    await shot(page, '08-rfid-already-assigned');
    await next(page);
    await expect(page.locator('[data-en-panel="3"]')).toBeVisible();

    await page.getByRole('radio', { name: 'Leave RFID pending' }).click();
    await next(page);
    await next(page);
    await expect(page.locator('[data-en-review]')).toContainText('RFID pending');
    await page.getByRole('button', { name: 'Complete enrollment' }).click();
    await expect(page.locator('[data-en-done]')).toContainText('RFID pending');
  });

  test('a new guardian whose email is registered is offered the existing account', async ({ page, data }) => {
    const existing = await data.guardian();
    await openWizard(page);
    await fillStudent(page, uid('Ira'), false);
    await next(page);

    const g = page.locator('[data-en-guardian]').nth(0);
    await fillNewGuardian(g, uid('Someone'), existing.email);
    await g.locator('[data-g="relationship"]').click();
    await expect(g.locator('[data-en-gdup]')).toBeVisible();
    await expect(g.locator('[data-en-gdup-text]')).toContainText(existing.fullName);
    await next(page);
    await expect(g.locator('[data-g-error="email"]')).toContainText('Already registered');
    await shot(page, '09-duplicate-guardian');

    await g.getByRole('button', { name: 'Link this guardian instead' }).click();
    await expect(g.locator('[data-en-gselected-name]')).toHaveText(existing.fullName);
    await next(page);
    await expect(page.locator('[data-en-panel="3"]')).toBeVisible();
  });

  test('same name and date of birth warns and needs a recorded reason (twins)', async ({ page, data }) => {
    const existing = await data.student({ dateOfBirth: '2016-05-14' });
    await openWizard(page);
    await fillStudent(page, existing.fullName, false);
    await next(page);

    const warning = page.locator('[data-en-dup]');
    await expect(warning).toBeVisible();
    await expect(warning.getByRole('link', { name: existing.fullName })).toHaveAttribute('href', `/Students/Details/${existing.id}`);
    await expect(page.locator('[data-en-error="duplicateReason"]')).toContainText('at least 10 characters');
    await expect(page.locator('[data-en-panel="1"]')).toBeVisible();
    await shot(page, '13-duplicate-student-warning');

    await page.locator('#enDupReason').fill('Twin sibling of the existing student, separate admission.');
    await next(page);
    await fillNewGuardian(page.locator('[data-en-guardian]').nth(0), uid('Twin Parent'));
    await next(page);
    await page.getByRole('radio', { name: 'Leave RFID pending' }).click();
    await next(page);
    await next(page);
    await expect(page.locator('[data-en-review]')).toContainText('Enrolling anyway');
    await page.getByRole('button', { name: 'Complete enrollment' }).click();
    await expect(page.locator('[data-en-done]').getByRole('heading', { level: 2 })).toHaveText(`${existing.fullName} is enrolled`);
  });

  test('a deactivated guardian is flagged and needs explicit confirmation to link', async ({ page, data, api }) => {
    const g = await data.guardian();
    expect((await api.patch(`/api/guardians/${g.id}/status`, { data: { status: 'Inactive' } })).ok()).toBe(true);

    await openWizard(page);
    await fillStudent(page, uid('Neel'), false);
    await next(page);
    const card = page.locator('[data-en-guardian]').nth(0);
    await card.getByRole('radio', { name: 'Link existing' }).click();
    await card.locator('[data-en-gquery]').fill(g.email);
    await expect(card.locator('.cg-enrol-result').first()).toContainText('Inactive');
    await card.locator('.cg-enrol-result').first().click();
    await expect(card.locator('[data-en-ginactive]')).toBeVisible();
    await next(page);
    await expect(card.locator('[data-g-error="inactive"]')).toContainText('Confirm linking');
    await shot(page, '14-inactive-guardian-confirmation');

    await card.getByLabel(/I confirm linking this deactivated account/).check();
    await next(page);
    await page.getByRole('radio', { name: 'Leave RFID pending' }).click();
    await next(page);
    await next(page);
    await expect(page.locator('[data-en-review]')).toContainText('Deactivated (confirmed)');
    await page.getByRole('button', { name: 'Complete enrollment' }).click();
    await expect(page.locator('[data-en-done]')).toContainText('Existing account linked');
  });

  test('fits a phone screen without horizontal scrolling', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await openWizard(page);
    const overflow = () => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(await overflow()).toBeLessThanOrEqual(0);
    await shot(page, '11-mobile-student');
    await fillStudent(page, uid('Mobile'), false);
    await next(page);
    await fillNewGuardian(page.locator('[data-en-guardian]').nth(0), uid('Parent'));
    expect(await overflow()).toBeLessThanOrEqual(0);
    await next(page);
    expect(await overflow()).toBeLessThanOrEqual(0);
    await shot(page, '12-mobile-rfid');
  });

  test('required details are checked before moving on', async ({ page }) => {
    await openWizard(page);
    await next(page);
    await expect(page.locator('[data-en-error="student.fullName"]')).toContainText('full name');
    await expect(page.locator('[data-en-error="student.dateOfBirth"]')).toContainText('date of birth');
    await expect(page.locator('#enFullName')).toBeFocused();

    await page.locator('#enFullName').fill(uid('Arjun'));
    await page.locator('#enDob').fill('2017-01-09');
    await next(page);
    await next(page);
    const g = page.locator('[data-en-guardian]').nth(0);
    await expect(g.locator('[data-g-error="fullName"]')).not.toBeEmpty();
    await expect(g.locator('[data-g-error="email"]')).not.toBeEmpty();
    await expect(g.locator('[data-g-error="contactNumber"]')).not.toBeEmpty();
    await shot(page, '10-validation');
  });
});
