// Boots an isolated ChildGuard stack for the E2E suite:
//   throwaway MySQL database -> migrations -> stub face service -> API -> Admin Web.
// Nothing here can reach the developer's childguard_db: the database name is generated
// with a fixed prefix and checked before every CREATE/DROP, and the server must be local.
import { spawn, execFileSync, ChildProcess } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

export const ROOT = path.resolve(__dirname, '..', '..', '..');
export const E2E_DIR = path.resolve(__dirname, '..');
export const STATE_FILE = path.join(E2E_DIR, '.e2e', 'state.json');
export const DB_PREFIX = 'childguard_e2e_';

export const PORTS = { api: 5375, web: 5180, face: 8101 };
export const URLS = {
  api: `http://localhost:${PORTS.api}`,
  web: `http://localhost:${PORTS.web}`,
  face: `http://127.0.0.1:${PORTS.face}`,
};

const MYSQL = process.env.CG_E2E_MYSQL_CLI ?? 'C:\\xampp\\mysql\\bin\\mysql.exe';
const MYSQL_HOST = '127.0.0.1';

export interface StackState {
  database: string;
  adminEmail: string;
  adminPassword: string;
  pids: number[];
}

function guardDatabaseName(name: string) {
  if (!/^childguard_e2e_[a-f0-9]{12}$/.test(name)) throw new Error(`Refusing to touch database '${name}'.`);
}

export function mysql(sql: string) {
  execFileSync(MYSQL, ['-u', 'root', '-h', MYSQL_HOST, '-P', '3306', '--batch'], { input: sql, stdio: ['pipe', 'pipe', 'pipe'] });
}

export function createDatabase(name: string) {
  guardDatabaseName(name);
  mysql(`CREATE DATABASE \`${name}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;`);
}

export function dropDatabase(name: string) {
  guardDatabaseName(name);
  mysql(`DROP DATABASE IF EXISTS \`${name}\`;`);
}

export function connectionString(db: string) {
  return `Server=${MYSQL_HOST};Port=3306;Database=${db};User=root;Password=;`;
}

async function waitFor(url: string, timeoutMs: number, proc?: ChildProcess, logFile?: string) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    if (proc && proc.exitCode !== null) {
      throw new Error(`${url}: process exited (${proc.exitCode}). Log: ${logFile}`);
    }
    try {
      const res = await fetch(url);
      if (res.ok) return;
    } catch { /* not up yet */ }
    await new Promise(r => setTimeout(r, 1000));
  }
  throw new Error(`Timed out waiting for ${url}. Log: ${logFile}`);
}

function start(name: string, cmd: string, args: string[], cwd: string, env: Record<string, string>): ChildProcess {
  const logFile = path.join(E2E_DIR, '.e2e', `${name}.log`);
  const out = fs.openSync(logFile, 'w');
  const proc = spawn(cmd, args, { cwd, env: { ...process.env, ...env }, stdio: ['ignore', out, out], windowsHide: true });
  (proc as any).logFile = logFile;
  return proc;
}

/**
 * Builds into .e2e/bin/<project>, not the project's own bin: a dev instance started by
 * start.bat locks that folder, and the suite must always run the current source.
 */
function build(project: string): string {
  const out = path.join(E2E_DIR, '.e2e', 'bin', project);
  try {
    execFileSync('dotnet', ['build', path.join(ROOT, 'src', project), '-o', out, '-v', 'q', '-nologo'], { stdio: 'pipe' });
  } catch (e: any) {
    const log = String(e.stdout ?? '') + String(e.stderr ?? '');
    throw new Error(`Build of ${project} failed:\n${log.split('\n').filter(l => / error /.test(l)).join('\n')}`);
  }
  return path.join(out, `${project}.dll`);
}

export async function startStack(): Promise<StackState> {
  fs.mkdirSync(path.join(E2E_DIR, '.e2e'), { recursive: true });
  const database = DB_PREFIX + randomBytes(6).toString('hex');
  const adminEmail = 'e2e-admin@childguard.test';
  const adminPassword = 'E2e-' + randomBytes(9).toString('hex') + '!a';
  const jwtSecret = randomBytes(48).toString('hex');

  const apiDll = build('ChildGuard.Api');
  const webDll = build('ChildGuard.AdminWeb');

  createDatabase(database);
  const state: StackState = { database, adminEmail, adminPassword, pids: [] };
  // Written now so an interrupted setup still gets its database dropped next time.
  const save = () => fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
  save();

  // dotnet-ef applies migrations from the project's regular build. Refresh it unless a
  // running dev instance holds it locked (then it is the build start.bat just made).
  try {
    execFileSync('dotnet', ['build', path.join(ROOT, 'src', 'ChildGuard.Api'), '-v', 'q', '-nologo'], { stdio: 'pipe' });
  } catch { /* locked by a running dev API */ }
  try {
    execFileSync('dotnet', ['ef', 'database', 'update', '--no-build',
      '--project', path.join(ROOT, 'src', 'ChildGuard.Infrastructure'),
      '--startup-project', path.join(ROOT, 'src', 'ChildGuard.Api'),
      '--connection', connectionString(database)], { stdio: 'pipe' });
  } catch (e: any) {
    throw new Error(`Migrations failed:\n${String(e.stdout ?? '')}${String(e.stderr ?? '')}`.slice(0, 3000));
  }

  const track = (p: ChildProcess) => { state.pids.push(p.pid!); save(); return p; };
  const face = start('face-stub', process.execPath, [path.join(__dirname, 'face-stub.js'), String(PORTS.face)], E2E_DIR, {});
  track(face);
  await waitFor(`${URLS.face}/health`, 15_000, face, (face as any).logFile);

  // Run the built DLL with the project folder as working directory, so the content
  // root (appsettings*.json, wwwroot) is the project's own.
  const api = start('api', 'dotnet', [apiDll, '--urls', URLS.api], path.join(ROOT, 'src', 'ChildGuard.Api'), {
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__ChildGuardDb: connectionString(database),
    Jwt__Secret: jwtSecret,
    SeedAdmin__Email: adminEmail,
    SeedAdmin__Password: adminPassword,
    SeedAdmin__FullName: 'E2E Administrator',
    SeedDemoData: 'false',
    FaceService__BaseUrl: URLS.face,
    FaceService__ApiKey: '',
    // Never send real email or push from tests, whatever user-secrets hold.
    Email__Resend__ApiKey: '',
    Firebase__CredentialsPath: '',
    Rfid__DeviceApiKey: '',
    // The suite signs in a handful of times per minute from one IP. Raised from 10 so
    // parallel files do not trip it; the limiter itself is covered by integration tests.
    RateLimiting__AuthPerMinute: '60',
    Cors__AllowedOrigins__0: URLS.web,
  });
  track(api);
  await waitFor(`${URLS.api}/api/health`, 120_000, api, (api as any).logFile);

  const web = start('adminweb', 'dotnet', [webDll, '--urls', URLS.web], path.join(ROOT, 'src', 'ChildGuard.AdminWeb'), {
    ASPNETCORE_ENVIRONMENT: 'Development',
    Api__BaseUrl: URLS.api,
  });
  track(web);
  await waitFor(`${URLS.web}/Account/Login`, 120_000, web, (web as any).logFile);
  return state;
}

export function readState(): StackState {
  return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
}

export function stopStack() {
  if (!fs.existsSync(STATE_FILE)) return;
  const state = readState();
  for (const pid of state.pids) {
    try { execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* already gone */ }
  }
  if (process.env.CG_E2E_KEEP_DB !== '1') dropDatabase(state.database);
  fs.rmSync(STATE_FILE, { force: true });
}
