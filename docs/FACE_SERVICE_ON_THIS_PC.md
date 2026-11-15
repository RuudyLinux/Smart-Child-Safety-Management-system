# Face service on the school's own PC (behind a free tunnel)

Why: Render's free tier (0.1 CPU, 512 MB) cannot run the face service, which needs about 600-700 MB and real CPU. So it
runs on a PC you control and the Render API reaches it through a tunnel. Cost: ₹0.

```
Guardian app / AdminWeb -> API (Render) --HTTPS + X-Internal-Key--> Tailscale Funnel --> 127.0.0.1:8001 (this PC)
```

## Security model (the URL is public, so the key is the gate)
- The service listens on **127.0.0.1 only**; the tunnel is the single way in.
- `FACE_SERVICE_REQUIRE_KEY=true` (set by `scripts/run-face-service.ps1`): the service **refuses to start** without a
  `FACE_SERVICE_KEY` of at least 32 characters and answers 503, never "open", if the key goes missing.
- Every endpoint except `/health` needs `X-Internal-Key`, compared in constant time. `/health` shows only `{"status":"ok"}`
  to a caller without the key. `/docs` and `/openapi.json` are not served.
- **Tailscale Funnel ends TLS on this PC**, so the tunnel provider cannot read face images in transit.
- The key is a User-level Windows environment variable here and a secret environment variable in Render
  (`FaceService__ApiKey`). It is never in the repository or logs. Rotate it by generating a new one in both places.
- Funnel has bandwidth limits that are not published (beta).

## Availability: what "free" costs
The PC must be on, awake and online. Face registration, live checks and pickup face matching stop while it is off.
Everything else in ChildGuard keeps working. Turn off sleep while on mains power:
`powercfg /change standby-timeout-ac 0`.

## Run it
1. Set the key once (PowerShell): generate 48 random bytes, base64, and store as the User variable `FACE_SERVICE_KEY`.
2. `scripts/run-face-service.ps1` starts it and restarts it if it stops. `scripts/install-face-service-task.ps1`
   registers a Windows scheduled task so it starts when you sign in. Logs: `logs/face-service.log`.
   - The task runs the watchdog in a **headless conhost**. Without that, Windows 11 hosts it in Windows Terminal, and
     closing that Terminal window killed the watchdog (task result `0xC000013A`).
   - The task also fires every 5 minutes; a dead watchdog is replaced, a running one ignores the extra start.
   - If the face service is already answering on 8001, the watchdog watches it instead of starting a second copy
     (two copies used to fight over the port). `start.bat` starts this task rather than its own copy.
   - If the task was first created from an elevated PowerShell, re-running the installer needs one too.
3. Funnel: install Tailscale, sign in, allow Funnel in the admin console, then
   `tailscale funnel --bg 8001`. The command prints the public `https://<machine>.<tailnet>.ts.net` address.
4. In Render, on the API service set `FaceService__BaseUrl` (that address) and `FaceService__ApiKey` (the same key),
   then deploy the API.

## Check it
`face-service/tests/test_security.py` covers the key rules. End to end: `POST /api/face/analyze-image` through the API
should return a result instead of an error.
