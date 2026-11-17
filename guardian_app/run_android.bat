@echo off
REM Runs the Guardian app on a USB-connected Android phone (or an emulator) against the local API.
REM
REM A real phone cannot use 10.0.2.2 (that address only exists inside the Android emulator), and the PC's
REM firewall/Wi-Fi are easy to trip over. "adb reverse" instead makes the phone's own localhost:5275 reach
REM this PC's API over the USB cable, so nothing else needs configuring. Start the API first (start.bat).
REM
REM Wi-Fi instead of USB: run  flutter run --dart-define=API_BASE_URL=http://<this PC's IPv4>:5275
REM and allow inbound TCP 5275 in Windows Firewall.
setlocal
set API_PORT=5275
adb reverse tcp:%API_PORT% tcp:%API_PORT%
if errorlevel 1 (
  echo.
  echo Could not set up the USB tunnel. Check that the phone is connected, USB debugging is on,
  echo and "adb devices" lists it as "device" ^(not "unauthorized"^).
  exit /b 1
)
flutter run --dart-define=API_BASE_URL=http://localhost:%API_PORT% %*
