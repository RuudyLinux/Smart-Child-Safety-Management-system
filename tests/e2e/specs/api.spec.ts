// API contract, validation and authorization, against the isolated test API.
import { test, expect, request as pwRequest } from '@playwright/test';
import { readState, URLS } from '../support/stack';
import { adminApi, Data, guardianSession } from '../support/fixtures';

test.describe('API', () => {
  test('health reports API and database OK @smoke', async ({ request }) => {
    const res = await request.get(`${URLS.api}/api/health`);
    expect(res.status()).toBe(200);
    expect(await res.json()).toMatchObject({ status: 'ok', database: 'ok' });
  });

  test('every admin endpoint refuses anonymous callers', async ({ request }) => {
    for (const path of ['/api/students', '/api/guardians', '/api/attendance', '/api/dashboard/summary', '/api/journey/live',
      '/api/rfid/cards', '/api/rfid/devices', '/api/transportation/buses', '/api/verification/history', '/api/reports/attendance']) {
      expect((await request.get(`${URLS.api}${path}`)).status(), path).toBe(401);
    }
    expect((await request.post(`${URLS.api}/api/students`, { data: { fullName: 'x', dateOfBirth: '2015-01-01' } })).status()).toBe(401);
  });

  test('a tampered token is refused', async ({ request }) => {
    const api = await adminApi(readState());
    const token = (await api.storageState(), (api as any)._options?.extraHTTPHeaders?.Authorization) as string | undefined;
    await api.dispose();
    const forged = 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwicm9sZSI6IkFkbWluIn0.' + 'x'.repeat(43);
    expect((await request.get(`${URLS.api}/api/students`, { headers: { Authorization: forged } })).status()).toBe(401);
    expect(token === undefined || token.startsWith('Bearer ')).toBeTruthy();
  });

  test('admin list endpoints return JSON @smoke', async () => {
    const api = await adminApi(readState());
    for (const path of ['/api/students', '/api/guardians', '/api/attendance', '/api/dashboard/summary', '/api/journey/live', '/api/journey/recent',
      '/api/rfid/cards', '/api/rfid/devices', '/api/transportation/buses', '/api/transportation/drivers', '/api/transportation/routes',
      '/api/transportation/assignments', '/api/verification/history']) {
      const res = await api.get(path);
      expect(res.status(), path).toBe(200);
      expect(res.headers()['content-type'], path).toContain('application/json');
    }
    await api.dispose();
  });

  test('server-side validation: future birth date, empty name, unknown ids', async () => {
    const api = await adminApi(readState());
    expect((await api.post('/api/students', { data: { fullName: 'Future', dateOfBirth: '2099-01-01', grade: '1A' } })).status()).toBe(400);
    expect((await api.post('/api/students', { data: { fullName: '', dateOfBirth: '2015-01-01' } })).status()).toBe(400);
    expect((await api.get('/api/students/999999')).status()).toBe(404);
    expect((await api.post('/api/rfid/cards', { data: { cardUid: 'ABC123', studentId: 999999 } })).status()).toBeGreaterThanOrEqual(400);
    await api.dispose();
  });

  test('a guardian can only see their own children', async () => {
    const admin = await adminApi(readState());
    const data = new Data(admin);
    const mine = await data.student();
    const other = await data.student();
    const g = await data.guardian();
    await data.link(mine.id, g.id);
    await data.setGuardianPassword(g.id, 'Scope-Pass-1!');
    const session = await guardianSession(g.id, g.contactNumber, 'Scope-Pass-1!');
    expect((await session.api.get(`/api/me/students/${mine.id}/journey`)).status()).toBe(200);
    expect((await session.api.get(`/api/me/students/${other.id}/journey`)).status()).toBe(403);
    // Guardians cannot use admin endpoints.
    expect((await session.api.get('/api/students')).status()).toBe(403);
    await session.api.dispose();
    await admin.dispose();
  });

  test('an RFID scan without a device key or admin token is refused', async ({ request }) => {
    const res = await request.post(`${URLS.api}/api/rfid/scan`, { data: { cardUid: 'X', rfidDeviceId: 1, checkpoint: 'SchoolArrival' } });
    expect(res.status()).toBe(401);
  });

  test('CORS allows the Admin Web origin and not others', async () => {
    const ctx = await pwRequest.newContext();
    const ok = await ctx.fetch(`${URLS.api}/api/health`, { method: 'OPTIONS', headers: { Origin: URLS.web, 'Access-Control-Request-Method': 'GET' } });
    expect(ok.headers()['access-control-allow-origin']).toBe(URLS.web);
    const bad = await ctx.fetch(`${URLS.api}/api/health`, { method: 'OPTIONS', headers: { Origin: 'https://evil.example', 'Access-Control-Request-Method': 'GET' } });
    expect(bad.headers()['access-control-allow-origin']).toBeUndefined();
    await ctx.dispose();
  });
});
