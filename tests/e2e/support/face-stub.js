// Stand-in for face-service with the same HTTP contract (/register-face, /verify-face,
// /health). No real faces are available to the suite, so tests choose the outcome:
//   POST /__stub/verify  {"match": true|false, "score": 0.9}  -> next /verify-face answers
//   POST /__stub/fail    {"status": 422, "detail": "No face detected"} -> next call fails
//   GET  /__stub/calls   -> calls received so far
const http = require('node:http');
const port = Number(process.argv[2] || 8101);

let verifyOutcome = { match: true, score: 0.93 };
let nextFailure = null;
const calls = [];

function send(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(body));
}

http.createServer((req, res) => {
  let raw = '';
  req.on('data', c => (raw += c));
  req.on('end', () => {
    let body = {};
    try { body = raw ? JSON.parse(raw) : {}; } catch { return send(res, 400, { detail: 'invalid json' }); }

    if (req.method === 'GET' && req.url === '/health') return send(res, 200, { status: 'ok', model: 'stub', detector: 'stub', threshold: 0.4, anti_spoofing: true });
    if (req.method === 'POST' && req.url === '/__stub/verify') { verifyOutcome = { match: !!body.match, score: body.score ?? (body.match ? 0.93 : 0.21) }; return send(res, 200, verifyOutcome); }
    if (req.method === 'POST' && req.url === '/__stub/fail') { nextFailure = body; return send(res, 200, body); }
    if (req.method === 'GET' && req.url === '/__stub/calls') return send(res, 200, calls);

    if (req.method === 'POST' && (req.url === '/register-face' || req.url === '/verify-face')) {
      calls.push({ path: req.url, at: new Date().toISOString(), hasImage: typeof body.image_base64 === 'string' && body.image_base64.length > 0 });
      if (typeof body.image_base64 !== 'string' || !body.image_base64) return send(res, 422, { detail: 'image_base64 is required' });
      if (nextFailure) { const f = nextFailure; nextFailure = null; return send(res, f.status || 422, { detail: f.detail || 'No face detected' }); }
      if (req.url === '/register-face') return send(res, 200, { embedding_json: JSON.stringify(Array.from({ length: 8 }, (_, i) => i / 10)), model: 'stub' });
      return send(res, 200, verifyOutcome);
    }
    send(res, 404, { detail: 'not found' });
  });
}).listen(port, '127.0.0.1', () => console.log(`face stub listening on ${port}`));
