// Stand-in for face-service with the same HTTP contract as the real one (face-service/app):
//   GET  /health, POST /register-face, /verify-face, /analyze-image, /identify,
//   /register-reference, /liveness/analyze
// No real faces are available to the suite, so tests choose the outcome:
//   POST /__stub/verify  {"match": true|false, "score": 0.9}  -> identity outcome of later checks
//                         (/verify-face, /identify and the identification inside /liveness/analyze)
//   POST /__stub/fail    {"status": 422, "detail": "No face detected"} -> the next face call fails
//   GET  /__stub/calls   -> calls received so far
// The response values mirror the integration tests' FakeFaceServiceClient (PassingLiveness, Clean),
// which are known to pass every verification layer in the API.
// The control state above is GLOBAL to the stub process. Only specs/pickup.spec.ts may set it: its tests
// run one after another in a single worker (fullyParallel: false), so they never race each other. A second
// spec setting it, or --repeat-each across workers, would let one test consume another's failure.
const http = require('node:http');
const port = Number(process.argv[2] || 8101);

let verifyOutcome = { match: true, score: 0.93 };
let nextFailure = null;
const calls = [];

const THRESHOLD = 0.4;
const CLEAN_ANALYSIS = (purpose) => ({
  purpose: purpose || 'verification', face_count: 1, issues: [], warnings: [], messages: [],
  metrics: { sharpness: 300, brightness: 150, contrast: 40, face_px: 200, yaw_deg_est: 0, detection_confidence: 0.99 },
});
const EMBEDDING = () => JSON.stringify(Array.from({ length: 8 }, (_, i) => i / 10));

function send(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(body));
}

/** The first candidate is "the person at the camera": it scores per /__stub/verify, every other one low. */
function identification(candidates) {
  const score = verifyOutcome.match ? verifyOutcome.score : 0.21;
  return {
    threshold: THRESHOLD, model: 'stub',
    results: (candidates || []).map((c, i) => {
      const s = i === 0 ? score : 0.05;
      const refs = c.references || [];
      return {
        guardian_id: c.guardian_id, best_score: s, best_reference_id: refs.length ? refs[0].reference_id : null,
        scores: refs.map(r => ({ reference_id: r.reference_id, score: s })), unreadable_reference_ids: [],
      };
    }),
  };
}

function liveness(body) {
  const steps = body.steps || [];
  return {
    frames_usable: 10, face_found_frac: 1.0, neutral_frame_index: 2,
    steps: steps.map((step, i) => ({ step, completed: true, frame_index: 4 + i * 3, peak_rel_yaw: step === 'turn_left' ? 0.35 : -0.35 })),
    challenge_completed: true,
    passive: { available: true, frames_scored: 5, real_scores: [0.97, 0.98, 0.96, 0.97, 0.98], real_frac: 1.0, mean: 0.972, min: 0.96 },
    continuity: { checked: 2, min_cosine: 0.7, same_person: true },
    duplicate_frame_frac: 0.0, issues: [],
    identification: body.candidates ? identification(body.candidates) : null,
    neutral_quality: CLEAN_ANALYSIS('verification'),
  };
}

const FACE_ROUTES = ['/register-face', '/verify-face', '/analyze-image', '/identify', '/register-reference', '/liveness/analyze'];

http.createServer((req, res) => {
  let raw = '';
  req.on('data', c => (raw += c));
  req.on('end', () => {
    let body = {};
    try { body = raw ? JSON.parse(raw) : {}; } catch { return send(res, 400, { detail: 'invalid json' }); }

    if (req.method === 'GET' && req.url === '/health') return send(res, 200, { status: 'ok', model: 'stub', detector: 'stub', threshold: THRESHOLD, anti_spoofing: true });
    if (req.method === 'POST' && req.url === '/__stub/verify') { verifyOutcome = { match: !!body.match, score: body.score ?? (body.match ? 0.93 : 0.21) }; return send(res, 200, verifyOutcome); }
    if (req.method === 'POST' && req.url === '/__stub/fail') { nextFailure = body; return send(res, 200, body); }
    if (req.method === 'GET' && req.url === '/__stub/calls') return send(res, 200, calls);

    if (req.method === 'POST' && FACE_ROUTES.includes(req.url)) {
      const isLive = req.url === '/liveness/analyze';
      const hasImage = isLive
        ? Array.isArray(body.frames) && body.frames.length > 0 && body.frames.every(f => typeof f.image_base64 === 'string' && f.image_base64)
        : typeof body.image_base64 === 'string' && body.image_base64.length > 0;
      calls.push({ path: req.url, at: new Date().toISOString(), hasImage, frames: isLive ? (body.frames || []).length : undefined, steps: isLive ? body.steps : undefined });
      if (!hasImage) return send(res, 422, { detail: isLive ? 'frames are required' : 'image_base64 is required' });
      if (nextFailure) { const f = nextFailure; nextFailure = null; return send(res, f.status || 422, { detail: f.detail || 'No face detected' }); }

      switch (req.url) {
        case '/register-face': return send(res, 200, { embedding_json: EMBEDDING(), model: 'stub' });
        case '/verify-face': return send(res, 200, verifyOutcome);
        case '/analyze-image': return send(res, 200, CLEAN_ANALYSIS(body.purpose));
        case '/identify': return send(res, 200, identification(body.candidates));
        case '/register-reference': return send(res, 200, {
          embedding_json: EMBEDDING(), model: 'stub', duplicate_of: null,
          best_existing_score: (body.existing || []).length ? 0.5 : null, threshold: THRESHOLD,
        });
        case '/liveness/analyze': return send(res, 200, liveness(body));
      }
    }
    send(res, 404, { detail: 'not found' });
  });
}).listen(port, '127.0.0.1', () => console.log(`face stub listening on ${port}`));
