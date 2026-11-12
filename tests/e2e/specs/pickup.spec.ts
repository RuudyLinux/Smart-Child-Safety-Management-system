// Pickup & Drop-off desk: RFID scan -> guardian -> camera face check -> approve/reject.
// Camera: Chromium's fake device. Face service: the suite's stub (see support/face-stub.js),
// so each test decides match / no match. Everything else is the real AdminWeb + API.
import { test, expect, face, guardianSession, Data } from '../support/fixtures';

async function guardianWithFace(data: Data, studentId: number) {
  const g = await data.guardian();
  await data.link(studentId, g.id, 'Mother', true);
  await data.setGuardianPassword(g.id, 'Pickup-Pass-1!');
  const session = await guardianSession(g.contactNumber, 'Pickup-Pass-1!');
  const reg = await session.api.post('/api/me/face-registration', { data: { imageBase64: Buffer.from('face').toString('base64') } });
  expect(reg.status(), await reg.text()).toBe(200);
  await session.api.dispose();
  return g;
}

async function scanAt(page: import('@playwright/test').Page, cardUid: string, checkpointLabel: RegExp) {
  await page.goto('/Verification');
  const checkpoint = page.locator('#checkpointSelect');
  const value = await checkpoint.locator('option', { hasText: checkpointLabel }).first().getAttribute('value');
  await checkpoint.selectOption(value!);
  await page.locator('#cardUidInput').fill(cardUid);
  await page.getByRole('button', { name: 'Read card' }).click();
}

test.describe('Pickup & Drop-off desk', () => {
  // Regression: without a camera the desk showed "Camera not started" forever, and
  // while the permission prompt was open it gave no feedback at all.
  test('no camera on the desk computer: the desk says so, capture stays off', async ({ page, data }) => {
    await page.addInitScript(() => {
      navigator.mediaDevices.getUserMedia = () => Promise.reject(new DOMException('Requested device not found', 'NotFoundError'));
    });
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    await guardianWithFace(data, s.id);

    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#guardianFaceBadge')).toContainText('Face registered');
    await page.getByRole('button', { name: 'Start camera' }).click();
    await expect(page.locator('#cameraBox')).toContainText('No camera was found');
    await expect(page.getByRole('button', { name: 'Capture & verify' })).toBeDisabled();
  });

  test('arrival scan records the checkpoint and attendance @smoke', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await scanAt(page, card.cardUid, /School arrival/);
    await expect(page.locator('#studentName')).toHaveText(s.fullName);
    await expect(page.locator('#studentStatusBadge')).toContainText('Checkpoint recorded');

    await page.goto('/Attendance');
    await expect(page.locator('main tr', { hasText: s.fullName }).first()).toBeVisible();
    await page.goto(`/Journey/Student/${s.id}`);
    await expect(page.locator('main')).toContainText(/arriv/i);
  });

  test('an unknown card and an empty field are explained, not recorded', async ({ page }) => {
    await page.goto('/Verification');
    await page.getByRole('button', { name: 'Read card' }).click();
    await expect(page.locator('#studentMessage')).toContainText('No card read');
    await page.locator('#cardUidInput').fill('NOT-A-REAL-CARD');
    await page.getByRole('button', { name: 'Read card' }).click();
    await expect(page.locator('#studentName')).toHaveText('Unknown card');
    await expect(page.locator('#studentMessage')).toContainText('Card not recognised');
  });

  test('scanning the same checkpoint twice is not recorded twice', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await scanAt(page, card.cardUid, /School arrival/);
    await expect(page.locator('#studentStatusBadge')).toContainText('Checkpoint recorded');
    await page.locator('#cardUidInput').fill(card.cardUid);
    await page.getByRole('button', { name: 'Read card' }).click();
    // Within the duplicate window it is acknowledged; outside it, refused as a duplicate.
    await expect(page.locator('#studentMessage')).toContainText(/already|duplicate|Checkpoint recorded/i);
    // Only one arrival exists.
    await page.goto(`/Journey/Student/${s.id}`);
    await expect(page.locator('.cg-timeline__item')).toHaveCount(1);
  });

  test('a student with a photo shows it at the desk; one without makes no photo request', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    const photoRequests: string[] = [];
    page.on('request', r => { if (r.url().includes('/Students/Photo/')) photoRequests.push(r.url()); });
    await scanAt(page, card.cardUid, /School arrival/);
    await expect(page.locator('#studentName')).toHaveText(s.fullName);
    // Regression: the desk used to request a photo for every student, 404 when none.
    expect(photoRequests).toEqual([]);
  });

  test('pickup: face match, approve handover, student leaves with the guardian', async ({ page, data, api }) => {
    // Regression BUG-001: the desk used to record the departure on the card read, so
    // the approval (the only intended writer) was refused as a duplicate.
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await guardianWithFace(data, s.id);
    await face.willMatch(true);

    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#studentName')).toHaveText(s.fullName);
    await expect(page.locator('#guardianSelect')).toContainText(g.fullName);
    await expect(page.locator('#guardianFaceBadge')).toContainText('Face registered');
    await expect(page.locator('#studentStatusBadge')).toContainText('Identified');
    const departures = async () => ((await (await api.get(`/api/journey/${s.id}`)).json()) as { eventType: string }[])
      .filter(e => e.eventType === 'SchoolDeparture').length;
    expect(await departures(), 'nothing recorded before the guardian is verified').toBe(0);

    await page.getByRole('button', { name: 'Start camera' }).click();
    await expect(page.locator('#cameraBox video')).toBeVisible();
    await page.getByRole('button', { name: 'Capture & verify' }).click();
    await expect(page.locator('#faceResult')).toContainText('Face verified');
    expect((await face.calls()).at(-1)).toMatchObject({ path: '/verify-face', hasImage: true });

    await page.getByRole('button', { name: 'Approve handover' }).click();
    await expect(page.locator('#decisionResult')).toContainText('Handover approved');

    // Result is visible everywhere it should be.
    await page.goto('/Verification/History');
    const row = page.locator('tr[data-row]', { hasText: s.fullName }).first();
    await expect(row).toContainText(g.fullName);
    await expect(row).toContainText(/Approved/i);
    expect(await departures(), 'approval records exactly one departure').toBe(1);
    await page.goto(`/Journey/Student/${s.id}`);
    await expect(page.locator('main')).toContainText(/left school|departure/i);
  });

  test('pickup: face does not match, approval stays locked, handover can be rejected', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    await guardianWithFace(data, s.id);
    await face.willMatch(false);

    await scanAt(page, card.cardUid, /School departure/);
    await page.getByRole('button', { name: 'Start camera' }).click();
    await page.getByRole('button', { name: 'Capture & verify' }).click();
    await expect(page.locator('#faceResult')).toContainText('Face did not match');
    await expect(page.getByRole('button', { name: 'Approve handover' })).toBeDisabled();
    await page.getByRole('button', { name: 'Reject' }).click();
    await expect(page.locator('#decisionResult')).toContainText('Handover rejected');

    await page.goto('/Verification/History');
    await page.locator('[data-cg-filter="result"]').selectOption({ label: 'No match' });
    await expect(page.locator('tr[data-row]:visible', { hasText: s.fullName })).toHaveCount(1);
  });

  // Regression: a guardian with no registered face could be "captured" and the desk then
  // showed "Face did not match · 0.00", which reads like an impostor.
  test('a guardian with no registered face is not reported as a mismatch', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Father', true);
    const before = (await face.calls()).length;

    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#guardianFaceBadge')).toContainText('No face registered');
    await expect(page.locator('#faceResult')).toContainText('cannot be face-verified');
    await expect(page.locator('#faceResult')).not.toContainText('did not match');
    const link = page.locator('#faceResult a', { hasText: 'Register face on guardian page' });
    await expect(link).toHaveAttribute('href', `/Guardians/Details/${g.id}#photo`);
    await page.getByRole('button', { name: 'Start camera' }).click();
    await expect(page.locator('#cameraBox video')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Capture & verify' })).toBeDisabled();
    expect((await face.calls()).length, 'no comparison was attempted').toBe(before);
  });

  test('admin registers a guardian face in person, then the desk verifies them', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Mother', true);

    await page.goto(`/Guardians/Details/${g.id}#photo`);
    const enrol = page.locator('[data-cg-face-enrol]');
    await expect(enrol).toContainText('Not registered');
    await enrol.getByRole('button', { name: 'Start camera' }).click();
    await expect(enrol.locator('[data-cg-face-view] video')).toBeVisible();
    await enrol.getByRole('button', { name: 'Capture' }).click();
    await expect(enrol.locator('[data-cg-face-view] img')).toBeVisible();
    await enrol.getByRole('button', { name: 'Register face' }).click();
    await expect(page.locator('.cg-toast').first()).toContainText('Face registered');
    await expect(page.locator('[data-cg-face-enrol]')).toContainText('Registered');
    await expect(page.locator('[data-cg-face-enrol]').getByRole('button', { name: 'Start camera' })).toBeVisible();
    expect((await face.calls()).filter(c => c.path === '/register-face').at(-1)).toMatchObject({ hasImage: true });

    await face.willMatch(true);
    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#guardianFaceBadge')).toContainText('Face registered');
    await page.getByRole('button', { name: 'Start camera' }).click();
    await page.getByRole('button', { name: 'Capture & verify' }).click();
    await expect(page.locator('#faceResult')).toContainText('Face verified');
  });

  test.describe('face registration refused', () => {
    test.use({ allowErrors: [/status of 400/] });
    test('the reason is shown and the guardian stays unregistered', async ({ page, data }) => {
      const g = await data.guardian();
      await page.goto(`/Guardians/Details/${g.id}#photo`);
      const enrol = page.locator('[data-cg-face-enrol]');
      await enrol.getByRole('button', { name: 'Start camera' }).click();
      await enrol.getByRole('button', { name: 'Capture' }).click();
      await face.failNext('More than one face is in the photo.', 422);
      await enrol.getByRole('button', { name: 'Register face' }).click();
      await expect(enrol.locator('[data-cg-face-result]')).toContainText('More than one face is in the photo.');
      await expect(enrol.getByRole('button', { name: 'Retake' })).toBeEnabled();
      await page.reload();
      await expect(page.locator('[data-cg-face-enrol]')).toContainText('Not registered');
    });
  });

  test.describe('face service failure', () => {
    test.use({ allowErrors: [/status of 400|status of 502|status of 503|status of 500/] });
    test('a face service error is reported and nothing is approved', async ({ page, data }) => {
      const s = await data.student();
      const card = await data.card(s.id);
      await data.scan(card.cardUid, 'SchoolArrival');
      await guardianWithFace(data, s.id);
      await face.failNext('No face detected', 422);
      await scanAt(page, card.cardUid, /School departure/);
      await page.getByRole('button', { name: 'Start camera' }).click();
      await page.getByRole('button', { name: 'Capture & verify' }).click();
      await expect(page.locator('#faceResult')).toContainText(/could not run|No face/i);
      await expect(page.getByRole('button', { name: 'Approve handover' })).toBeDisabled();
    });
  });

  test('a student with no authorised guardian cannot be released', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Neighbour', false);
    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#faceResult')).toContainText('Nobody may collect this student');
    await expect(page.locator('#guardianSelect')).toBeDisabled();
  });

  test('New scan resets the desk', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await scanAt(page, card.cardUid, /School arrival/);
    await expect(page.locator('#studentName')).toHaveText(s.fullName);
    await page.getByRole('button', { name: 'New scan' }).click();
    await expect(page.locator('#studentCard')).toBeHidden();
    await expect(page.locator('#cardUidInput')).toHaveValue('');
  });
});
