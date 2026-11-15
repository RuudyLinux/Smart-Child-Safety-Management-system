import { test as base, expect, request as pwRequest, APIRequestContext, Page, ConsoleMessage } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { readState, URLS, E2E_DIR, StackState } from './stack';

export { expect };

/** Unique, readable names so tests never collide and their data is easy to find. */
let counter = 0;
export function uid(prefix: string) {
  counter += 1;
  return `${prefix} ${Date.now().toString(36).slice(-5)}${counter}${Math.random().toString(36).slice(2, 5)}`;
}

/** Signs in to the API as the seeded E2E admin; used only to arrange test data. */
export async function adminApi(state: StackState): Promise<APIRequestContext> {
  const anon = await pwRequest.newContext({ baseURL: URLS.api });
  const res = await anon.post('/api/auth/admin/login', { data: { email: state.adminEmail, password: state.adminPassword } });
  if (!res.ok()) throw new Error(`Admin API login failed: ${res.status()} ${await res.text()}`);
  const { token } = await res.json();
  await anon.dispose();
  return pwRequest.newContext({ baseURL: URLS.api, extraHTTPHeaders: { Authorization: `Bearer ${token}` } });
}

async function ok<T = any>(res: import('@playwright/test').APIResponse, what: string): Promise<T> {
  if (!res.ok()) throw new Error(`${what} failed: ${res.status()} ${await res.text()}`);
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** Test-data builders. Each creates fresh records through the real admin API. */
export class Data {
  constructor(private api: APIRequestContext) {}

  async student(over: Partial<{ fullName: string; dateOfBirth: string; grade: string }> = {}) {
    const body = { fullName: uid('Student'), dateOfBirth: '2015-04-12', grade: '4B', ...over };
    const s = await ok(await this.api.post('/api/students', { data: body }), 'create student');
    return { ...body, id: s.studentId as number };
  }

  async guardian(over: Partial<{ fullName: string; email: string; contactNumber: string }> = {}) {
    const n = Math.floor(Math.random() * 1e8).toString().padStart(8, '0');
    const body = { fullName: uid('Guardian'), email: `g${n}@childguard.test`, contactNumber: `98${n}`, ...over };
    const g = await ok(await this.api.post('/api/guardians', { data: body }), 'create guardian');
    return { ...body, id: g.guardianId as number };
  }

  async link(studentId: number, guardianId: number, relationship = 'Mother', isAuthorizedForPickup = true) {
    await ok(await this.api.post(`/api/students/${studentId}/guardians`, { data: { guardianId, relationship, isAuthorizedForPickup } }), 'link guardian');
  }

  async card(studentId: number, cardUid = 'E2E' + Math.random().toString(16).slice(2, 12).toUpperCase()) {
    await ok(await this.api.post('/api/rfid/cards', { data: { cardUid, studentId } }), 'assign card');
    // The assign endpoint answers with a message only; find the card by its student.
    const cards = await ok<any[]>(await this.api.get('/api/rfid/cards'), 'list cards');
    const card = cards.find(x => x.studentId === studentId && x.status === 'Active');
    return { id: card.rfidCardId as number, cardUid };
  }

  async device() {
    const devices = await ok<any[]>(await this.api.get('/api/rfid/devices'), 'list devices');
    return devices[0] as { rfidDeviceId: number; deviceLabel: string };
  }

  async scan(cardUid: string, checkpoint: 'BusBoarding' | 'SchoolArrival' | 'SchoolDeparture' | 'BusDropoff') {
    const d = await this.device();
    return ok(await this.api.post('/api/rfid/scan', { data: { cardUid, rfidDeviceId: d.rfidDeviceId, checkpoint } }), `scan ${checkpoint}`);
  }

  async setGuardianPassword(guardianId: number, newPassword: string) {
    await ok(await this.api.patch(`/api/guardians/${guardianId}/credentials`, { data: { newPassword } }), 'set guardian password');
  }

  async driver() { const b = { fullName: uid('Driver'), phone: '9876500000', licenseNumber: 'DL-' + Date.now() }; const r = await ok(await this.api.post('/api/transportation/drivers', { data: b }), 'driver'); return { ...b, id: r.driverId as number }; }
  async route() { const b = { routeName: uid('Route'), description: 'E2E route' }; const r = await ok(await this.api.post('/api/transportation/routes', { data: b }), 'route'); return { ...b, id: r.routeId as number }; }
  async bus(driverId?: number, routeId?: number) { const b = { busNumber: 'BUS-' + Math.random().toString(36).slice(2, 7).toUpperCase(), capacity: 40, driverId, routeId }; const r = await ok(await this.api.post('/api/transportation/buses', { data: b }), 'bus'); return { ...b, id: r.busId as number }; }
}

/**
 * Guardian sign-in exactly as the app does it: password -> OTP -> (first login) reset.
 * The OTP is read from the test API's own log: Development logs it when email is off.
 * The API logs it by guardian id ("Login OTP for guardian 42 (dev-only log): 123456"), never by
 * phone number, so the id is needed to pick the right code while tests run in parallel.
 */
export async function guardianSession(guardianId: number, contactNumber: string, password: string): Promise<{ token: string; password: string; api: APIRequestContext }> {
  const anon = await pwRequest.newContext({ baseURL: URLS.api });
  const logPath = path.join(E2E_DIR, '.e2e', 'api.log');

  const passwordAndOtp = async (pw: string) => {
    const before = fs.readFileSync(logPath, 'utf8').length;
    const login = await ok(await anon.post('/api/auth/guardian/login', { data: { contactNumber, password: pw } }), 'guardian login');
    let code: string | undefined;
    for (let i = 0; i < 40 && !code; i++) {
      const fresh = fs.readFileSync(logPath, 'utf8').slice(before);
      code = [...fresh.matchAll(new RegExp(`Login OTP for guardian ${guardianId} \\(dev-only log\\): (\\d+)`, 'g'))].at(-1)?.[1];
      if (!code) await new Promise(r => setTimeout(r, 250));
    }
    if (!code) throw new Error('OTP not found in API log');
    return ok(await anon.post('/api/auth/guardian/verify-otp', { data: { challengeToken: login.challengeToken, code } }), 'verify otp');
  };

  let current = password;
  let auth = await passwordAndOtp(current);
  if (auth.stage === 'PasswordResetRequired') {
    // First sign-in on an admin-issued password: reset it, then sign in again (as the app does).
    current = password + 'X1';
    await ok(await anon.post('/api/auth/guardian/reset-password', { data: { resetTicket: auth.resetTicket, newPassword: current, confirmPassword: current } }), 'guardian reset');
    auth = await passwordAndOtp(current);
  }
  if (auth.stage !== 'Authenticated') throw new Error(`Guardian sign-in ended at stage ${auth.stage}`);
  const token = auth.session.token;
  await anon.dispose();
  return { token, password: current, api: await pwRequest.newContext({ baseURL: URLS.api, extraHTTPHeaders: { Authorization: `Bearer ${token}` } }) };
}

export const face = {
  async willMatch(match: boolean) { await fetch(`${URLS.face}/__stub/verify`, { method: 'POST', body: JSON.stringify({ match }) }); },
  async failNext(detail = 'No face detected', status = 422) { await fetch(`${URLS.face}/__stub/fail`, { method: 'POST', body: JSON.stringify({ detail, status }) }); },
  async calls(): Promise<any[]> { return (await fetch(`${URLS.face}/__stub/calls`)).json(); },
};

/** A tiny valid PNG (1x1) for photo uploads. */
export const PNG_1PX = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');

type WorkerFixtures = { stack: StackState; api: APIRequestContext };

type Fixtures = {
  data: Data;
  /** Regexes for console/network errors a test expects (e.g. a deliberate 400). */
  allowErrors: RegExp[];
  errorWatch: void;
};

export const test = base.extend<Fixtures, WorkerFixtures>({
  stack: [async ({}, use) => { await use(readState()); }, { scope: 'worker' }],
  // One admin sign-in per worker: keeps the suite well under the login rate limit.
  api: [async ({ stack }, use) => { const api = await adminApi(stack); await use(api); await api.dispose(); }, { scope: 'worker' }],
  data: async ({ api }, use) => { await use(new Data(api)); },
  allowErrors: [[], { option: true }],

  // Fails any test whose page logged a console error, threw, or got a 5xx / failed request.
  errorWatch: [async ({ page, allowErrors }, use, testInfo) => {
    const problems: string[] = [];
    const allowed = (s: string) => allowErrors.some(r => r.test(s));
    const onConsole = (m: ConsoleMessage) => { if (m.type() === 'error' && !allowed(m.text())) problems.push(`console: ${m.text()}`); };
    page.on('console', onConsole);
    page.on('pageerror', e => { if (!allowed(e.message)) problems.push(`uncaught: ${e.message}`); });
    page.on('response', r => {
      const type = r.request().resourceType();
      const asset = ['image', 'script', 'stylesheet', 'font', 'media'].includes(type);
      // Server errors anywhere, plus broken assets (images, scripts, styles...).
      const missing = ['fetch', 'xhr'].includes(type) && r.status() === 404;
      if ((r.status() >= 500 || (asset && r.status() >= 400) || missing) && !allowed(r.url())) problems.push(`HTTP ${r.status()} ${type} ${r.request().method()} ${r.url()}`);
    });
    page.on('requestfailed', r => {
      const f = r.failure()?.errorText ?? '';
      // Navigating away cancels in-flight requests, and WebKit reports a link that turns
      // into a file download as "Frame load interrupted"; neither is a failure.
      if (/ERR_ABORTED|NS_BINDING_ABORTED|cancelled|Load request cancelled|Frame load interrupted/i.test(f)) return;
      if (!allowed(r.url())) problems.push(`request failed: ${r.url()} ${f}`);
    });
    await use();
    if (problems.length) {
      await testInfo.attach('browser-errors', { body: problems.join('\n'), contentType: 'text/plain' });
      expect(problems, 'browser console / network errors').toEqual([]);
    }
  }, { auto: true }],
});

/** Waits for the toast shown after a form post-redirect-get and returns its text. */
export async function toast(page: Page) {
  const t = page.locator('.cg-toast').first();
  await expect(t).toBeVisible();
  return (await t.innerText()).trim();
}
