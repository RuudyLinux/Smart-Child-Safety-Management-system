@echo off
REM Runs the Guardian app in Chrome against the local API.
REM
REM The web port is pinned to 5090 on purpose: the API only sends CORS headers to
REM origins listed in Cors:AllowedOrigins (appsettings.Development.json), and 5090
REM is the one reserved for this app. On a random port the browser blocks every
REM API call and the app reports "Could not reach server".
REM
REM API_BASE_URL must also be set, because the built-in default (10.0.2.2) is the
REM Android emulator address and is unreachable from a desktop browser.
setlocal
set API_BASE_URL=http://localhost:5275
flutter run -d chrome --web-port=5090 --dart-define=API_BASE_URL=%API_BASE_URL% %*
