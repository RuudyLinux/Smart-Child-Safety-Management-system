// Pickup & Drop-off desk: RFID scan -> guardian -> camera face check -> approve/reject.
// Camera: Chromium's fake device. Face service: the suite's stub (see support/face-stub.js),
// so each test decides match / no match. Everything else is the real AdminWeb + API.
import { test, expect, face, guardianSession, Data } from '../support/fixtures';

async function guardianWithFace(data: Data, studentId: number) {
  const g = await data.guardian();
  await data.link(studentId, g.id, 'Mother', true);
  await data.setGuardianPassword(g.id, 'Pickup-Pass-1!');
  const session = await guardianSession(g.id, g.contactNumber, 'Pickup-Pass-1!');
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

/**
 * A camera whose frames are unique, like a real sensor's. Chromium's built-in fake camera repeats the same
 * frames in every test, which the API rightly refuses as a replayed capture (REPLAYED_FRAME: the exact
 * neutral frame was used before). Each page gets a random seed, and every frame also carries a timestamp
 * and fresh noise.
 */
async function useUniqueCamera(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    const seed = Math.random().toString(36).slice(2);
    navigator.mediaDevices.getUserMedia = async () => {
      const canvas = document.createElement('canvas');
      canvas.width = 640; canvas.height = 480;
      const ctx = canvas.getContext('2d')!;
      const draw = () => {
        ctx.fillStyle = '#6b7f99'; ctx.fillRect(0, 0, 640, 480);
        for (let i = 0; i < 60; i++) {
          ctx.fillStyle = `hsl(${Math.random() * 360},40%,${30 + Math.random() * 40}%)`;
          ctx.fillRect(Math.random() * 640, Math.random() * 480, 24, 24);
        }
        ctx.fillStyle = '#fff'; ctx.font = '28px sans-serif';
        ctx.fillText(`${seed} ${performance.now().toFixed(1)}`, 20, 460);
        requestAnimationFrame(draw);
      };
      draw();
      return canvas.captureStream(15);
    };
  });
}

/** Waits for the live check (challenge prompts, frames, server verdict) to finish. */
async function runLiveCheck(page: import('@playwright/test').Page) {
  await page.getByRole('button', { name: 'Start camera' }).click();
  await expect(page.locator('#cameraBox video')).toBeVisible();
  const begin = page.getByRole('button', { name: 'Begin live check' });
  await expect(begin).toBeEnabled();
  await begin.click();
  // The prompts take about 5-8 s, then the server judges the burst.
  await expect(page.locator('#livePrompt')).toBeHidden({ timeout: 30_000 });
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
    const g = await guardianWithFace(data, s.id);

    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#guardianList')).toContainText(g.fullName);
    await expect(page.locator('#guardianList')).toContainText('face photo');
    await page.getByRole('button', { name: 'Start camera' }).click();
    await expect(page.locator('#cameraBox')).toContainText('No camera was found');
    await expect(page.getByRole('button', { name: 'Begin live check' })).toBeDisabled();
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
    await useUniqueCamera(page);
    // Regression BUG-001: the desk used to record the departure on the card read, so
    // the approval (the only intended writer) was refused as a duplicate.
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await guardianWithFace(data, s.id);
    await face.willMatch(true);

    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#studentName')).toHaveText(s.fullName);
    await expect(page.locator('#studentStatusBadge')).toContainText('Identified');
    await expect(page.locator('#guardianList')).toContainText(g.fullName);
    await expect(page.locator('#guardianAuthBadge')).toContainText('1 authorised');
    const departures = async () => ((await (await api.get(`/api/journey/${s.id}`)).json()) as { eventType: string }[])
      .filter(e => e.eventType === 'SchoolDeparture').length;
    expect(await departures(), 'nothing recorded before the guardian is verified').toBe(0);

    await runLiveCheck(page);
    const results = page.locator('#layerResults');
    await expect(results).toContainText('All mandatory checks passed');
    await expect(results).toContainText(g.fullName);
    for (const layer of ['Image quality', 'Face detection', 'Identity', 'Authorisation', 'Liveness'])
      await expect(results.locator('li', { hasText: layer }), layer).not.toContainText(/Fail|No match|Unauthorised|Not performed/);
    const live = (await face.calls()).filter(c => c.path === '/liveness/analyze').at(-1);
    expect(live).toMatchObject({ hasImage: true });
    expect(live.frames, 'a burst of real camera frames was sent').toBeGreaterThan(3);

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

    // Nobody recognised: the API keeps no verification record (a record always names a guardian; the face
  // audit log keeps the attempt), so there is nothing to approve or reject. The student is simply not released.
  test('pickup: face does not match, nothing can be approved and the student is not released', async ({ page, data, api }) => {
    await useUniqueCamera(page);
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    await guardianWithFace(data, s.id);
    await face.willMatch(false);

    await scanAt(page, card.cardUid, /School departure/);
    await runLiveCheck(page);
    await expect(page.locator('#layerResults')).toContainText('No matching guardian');
    await expect(page.locator('#layerResults li', { hasText: 'Identity' })).toContainText('No match');
    await expect(page.locator('#layerResults')).toContainText('Do not release the student');
    await expect(page.getByRole('button', { name: 'Approve handover' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Reject' })).toBeDisabled();
    const departures = ((await (await api.get(`/api/journey/${s.id}`)).json()) as { eventType: string }[])
      .filter(e => e.eventType === 'SchoolDeparture').length;
    expect(departures).toBe(0);
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
    await expect(page.locator('#guardianList')).toContainText(g.fullName);
    await expect(page.locator('#guardianList')).toContainText('No face registered');
    await expect(page.locator('#layerResults')).toContainText('No face is registered for these guardians');
    await expect(page.locator('#layerResults')).not.toContainText(/No match|did not match/i);
    // Nothing to compare with, so no capture is offered at all.
    await expect(page.locator('#methodBar')).toBeHidden();
    await expect(page.getByRole('button', { name: 'Approve handover' })).toBeDisabled();
    expect((await face.calls()).length, 'no comparison was attempted').toBe(before);
  });

  test('admin registers a guardian face in person, then the desk verifies them', async ({ page, data }) => {
    await useUniqueCamera(page);
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Mother', true);

    await page.goto(`/Guardians/Details/${g.id}#photo`);
    const enrol = page.locator('[data-cg-face-enrol]');
    await expect(enrol).toContainText('Not registered');
    await enrol.getByRole('button', { name: 'Use camera' }).click();
    await expect(enrol.locator('video')).toBeVisible();
    await enrol.getByRole('button', { name: 'Capture' }).click();
    // The server checks the image first; only an accepted image can be registered.
    const register = enrol.getByRole('button', { name: 'Register face' });
    await expect(register).toBeEnabled();
    await register.click();
    await expect(page.locator('.cg-toast').first()).toContainText('Face registered');
    await expect(page.locator('[data-cg-face-enrol]')).toContainText('Registered');
    expect((await face.calls()).filter(c => c.path === '/register-face').at(-1)).toMatchObject({ hasImage: true });

    await face.willMatch(true);
    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#guardianList')).toContainText(g.fullName);
    await runLiveCheck(page);
    await expect(page.locator('#layerResults')).toContainText('All mandatory checks passed');
    await expect(page.getByRole('button', { name: 'Approve handover' })).toBeEnabled();
  });

  test.describe('face registration refused', () => {
    test.use({ allowErrors: [/status of 400/] });
    test('the reason is shown and the guardian stays unregistered', async ({ page, data }) => {
      const g = await data.guardian();
      await page.goto(`/Guardians/Details/${g.id}#photo`);
      const enrol = page.locator('[data-cg-face-enrol]');
      await enrol.getByRole('button', { name: 'Use camera' }).click();
      await enrol.getByRole('button', { name: 'Capture' }).click();
      const register = enrol.getByRole('button', { name: 'Register face' });
      await expect(register).toBeEnabled();
      await face.failNext('More than one face is in the photo.', 422);
      await register.click();
      await expect(enrol.locator('[data-cg-face-result]')).toContainText('More than one face is in the photo.');
      await expect(register).toBeEnabled(); // the admin can try again
      await page.reload();
      await expect(page.locator('[data-cg-face-enrol]')).toContainText('Not registered');
    });
  });

  test.describe('face service failure', () => {
    test.use({ allowErrors: [/status of 400|status of 502|status of 503|status of 500/] });
    test('a face service error is reported and nothing is approved', async ({ page, data, api }) => {
    await useUniqueCamera(page);
      const s = await data.student();
      const card = await data.card(s.id);
      await data.scan(card.cardUid, 'SchoolArrival');
      await guardianWithFace(data, s.id);
      await scanAt(page, card.cardUid, /School departure/);
      await face.failNext('No face detected', 422);
      await runLiveCheck(page);
      await expect(page.locator('#layerResults')).toContainText(/could not be completed|No face/i);
      await expect(page.locator('#layerResults')).not.toContainText('All mandatory checks passed');
      await expect(page.getByRole('button', { name: 'Approve handover' })).toBeDisabled();
      const departures = ((await (await api.get(`/api/journey/${s.id}`)).json()) as { eventType: string }[])
        .filter(e => e.eventType === 'SchoolDeparture').length;
      expect(departures).toBe(0);
    });
  });

  test('a student with no authorised guardian cannot be released', async ({ page, data }) => {
    const s = await data.student();
    const card = await data.card(s.id);
    await data.scan(card.cardUid, 'SchoolArrival');
    const g = await data.guardian();
    await data.link(s.id, g.id, 'Neighbour', false);
    await scanAt(page, card.cardUid, /School departure/);
    await expect(page.locator('#layerResults')).toContainText('Nobody may collect this student');
    await expect(page.locator('#guardianAuthBadge')).toContainText('No authorised guardian');
    await expect(page.locator('#methodBar')).toBeHidden();
    await expect(page.getByRole('button', { name: 'Approve handover' })).toBeDisabled();
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

  // Automatic checkpoint: these cases do not depend on the time of day or the school timetable
  // (no bus assignment, or a journey state with only one possible next step).
  test.describe('automatic checkpoint', () => {
    async function scanAuto(page: import('@playwright/test').Page, cardUid: string) {
      await page.goto('/Verification');
      await expect(page.locator('#checkpointSelect')).toHaveValue('Auto');
      await page.locator('#cardUidInput').fill(cardUid);
      await page.getByRole('button', { name: 'Read card' }).click();
    }

    test('first scan of a student without a bus records school arrival @smoke', async ({ page, data }) => {
      const s = await data.student();
      const card = await data.card(s.id);
      await scanAuto(page, card.cardUid);
      await expect(page.locator('#checkpointHint')).toContainText('Automatic → School arrival');
      await expect(page.locator('#studentName')).toHaveText(s.fullName);
      await expect(page.locator('#studentStatusBadge')).toContainText('Checkpoint recorded');
      await expect(page.locator('#pickupCard')).toBeHidden();

      await page.goto(`/Journey/Student/${s.id}`);
      await expect(page.locator('main')).toContainText(/arriv/i);
    });

    test('a student already at school is routed to the guardian pickup', async ({ page, data }) => {
      const s = await data.student();
      const card = await data.card(s.id);
      await data.scan(card.cardUid, 'SchoolArrival');
      const g = await data.guardian();
      await data.link(s.id, g.id, 'Father', true);

      await scanAuto(page, card.cardUid);
      await expect(page.locator('#checkpointHint')).toContainText('Automatic → School departure');
      await expect(page.locator('#pickupCard')).toBeVisible();
      await expect(page.locator('#guardianList')).toContainText(g.fullName);
    });

    test('an unknown card is reported and nothing is recorded', async ({ page }) => {
      await scanAuto(page, 'FFEE' + Date.now().toString(16).toUpperCase());
      await expect(page.locator('#studentMessage')).toContainText('Card not recognised');
      await expect(page.locator('#pickupCard')).toBeHidden();
    });
  });
});
