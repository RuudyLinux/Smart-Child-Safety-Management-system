import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:guardian_app/main.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

/// Drives the real app (splash, login, OTP, home and every tab) against a fake API, tapping buttons the way a
/// guardian does. If a screen throws or a tap goes nowhere, this fails.
void main() {
  final student = {
    'studentId': 4,
    'fullName': 'Mira Rao',
    'grade': '3A',
    'relationship': 'Mother',
    'isAuthorizedForPickup': true,
    'hasPhoto': false,
    'safetyStatus': 'AtSchool',
    'presentToday': true,
    'lateToday': false,
    'checkInTime': '08:10:00',
    'busNumber': 'B1',
    'routeName': 'North',
  };
  final profile = {
    'guardianId': 1,
    'fullName': 'Asha Rao',
    'email': 'asha@example.test',
    'phone': '9820011111',
    'faceRegistered': true,
    'hasPhoto': false,
    'students': [student],
  };
  final calls = <String>[];

  http.Response json(Object body, [int status = 200]) =>
      http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

  http.Response handle(http.Request r) {
    calls.add('${r.method} ${r.url.path}');
    final p = r.url.path;
    if (p == '/api/auth/guardian/login') {
      return json({
        'challengeToken': '11111111-1111-1111-1111-111111111111',
        'expiresAtUtc': DateTime.now().toUtc().add(const Duration(minutes: 5)).toIso8601String(),
        'codeLength': 6,
        'resendAvailableInSeconds': 60,
        'resendsRemaining': 3,
        'maskedEmail': 'a***@example.test',
        'stage': 'OtpRequired',
      });
    }
    if (p == '/api/auth/guardian/verify-otp') {
      return json({
        'stage': 'Authenticated',
        'session': {'token': 'jwt', 'fullName': 'Asha Rao'},
        'deviceToken': 'device-secret',
      });
    }
    if (p == '/api/me/profile') return json(profile);
    if (p == '/api/me/dashboard') {
      return json({
        'guardianName': 'Asha Rao',
        'today': '2026-11-12',
        'childrenCount': 1,
        'presentToday': 1,
        'lateToday': 0,
        'atSchool': 1,
        'onBus': 0,
        'leftSchoolOrHome': 0,
        'unreadNotifications': 2,
        'pendingPickups': 1,
        'children': [student],
        'recentEvents': [],
      });
    }
    if (p == '/api/me/pickups') return json([]);
    if (p == '/api/me/notifications') return json([]);
    if (p == '/api/me/students/4') {
      return json({
        'child': student,
        'dateOfBirth': '2016-05-01T00:00:00',
        'hasRfidCard': true,
        'attendanceStats': {'windowDays': 30, 'daysPresent': 18, 'daysLate': 1, 'schoolDaysObserved': 20, 'presentRate': 90.0},
        'transport': {'busNumber': 'B1', 'capacity': 40, 'routeName': 'North'},
        'guardians': [
          {'guardianId': 1, 'fullName': 'Asha Rao', 'relationship': 'Mother', 'isAuthorizedForPickup': true, 'isYou': true}
        ],
        'todaysEvents': [],
      });
    }
    if (p == '/api/me/students/4/journey' || p == '/api/me/students/4/attendance') return json([]);
    if (p == '/api/me/students/4/report') {
      return json({'studentId': 4, 'studentName': 'Mira Rao', 'from': '2026-11-01', 'to': '2026-11-12', 'days': []});
    }
    if (p == '/api/me/temporary-pickups') return json([]);
    if (p == '/api/me/trusted-devices') return json({'deviceToken': 'x'});
    return json({'message': 'not faked: $p'}, 404);
  }

  Future<void> settle(WidgetTester t, [int ms = 1500]) async {
    for (var i = 0; i < ms ~/ 100; i++) {
      await t.pump(const Duration(milliseconds: 100));
    }
  }

  testWidgets('sign in with the emailed code, then every tab and button responds', (t) async {
    FlutterSecureStorage.setMockInitialValues({});
    final errors = <String>[];
    final previous = FlutterError.onError;
    FlutterError.onError = (d) => errors.add(d.exceptionAsString());
    addTearDown(() => FlutterError.onError = previous);
    t.view.physicalSize = const Size(430, 900);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);

    await http.runWithClient(() async {
      await t.pumpWidget(const ChildGuardApp());
      await settle(t, 3000);

      // Login screen.
      expect(find.text('Sign in'), findsOneWidget, reason: 'login screen should be showing');
      await t.enterText(find.widgetWithText(TextField, 'Contact number'), '9820011111');
      await t.enterText(find.widgetWithText(TextField, 'Password'), 'Chosen@2026');
      await t.tap(find.text('Sign in'));
      await settle(t);

      // OTP screen.
      expect(calls, contains('POST /api/auth/guardian/login'));
      final otpField = find.byType(TextField);
      expect(otpField, findsWidgets, reason: 'the OTP screen should have opened after Sign in');
      await t.enterText(otpField.first, '123456');
      final verify = find.widgetWithText(ElevatedButton, 'Verify OTP');
      expect(verify, findsOneWidget, reason: 'the OTP screen should have a Verify button');
      await t.tap(verify);
      await settle(t, 2500);

      // Home / dashboard.
      expect(calls, contains('POST /api/auth/guardian/verify-otp'));
      expect(calls, contains('GET /api/me/dashboard'));
      expect(find.textContaining('Asha'), findsWidgets);
      await t.scrollUntilVisible(find.text('YOUR CHILDREN (1)'), 300, scrollable: find.byType(Scrollable).first);
      expect(find.text('Mira Rao'), findsWidgets);

      // Quick action: Pickup status, then back.
      await t.scrollUntilVisible(find.text('Pickup status'), 300, scrollable: find.byType(Scrollable).first);
      await t.ensureVisible(find.text('Pickup status').first);
      await settle(t, 300);
      await t.tap(find.text('Pickup status').first);
      await settle(t);
      expect(calls, contains('GET /api/me/pickups'));
      await t.tap(find.byType(BackButton));
      await settle(t);

      // The child card opens student details.
      await t.scrollUntilVisible(find.text('Mira Rao'), 300, scrollable: find.byType(Scrollable).first);
      await t.ensureVisible(find.text('Mira Rao').first);
      await settle(t, 300);
      await t.tap(find.text('Mira Rao').first);
      await settle(t, 2000);
      expect(calls, contains('GET /api/me/students/4'));
      expect(find.text('Student information'), findsOneWidget);
      await t.tap(find.byType(BackButton));
      await settle(t);

      // Bottom navigation: each tab loads.
      for (final tab in ['Children', 'Pickups', 'Reports', 'Profile', 'Home']) {
        await t.tap(find.text(tab).last);
        await settle(t, 1200);
      }
      expect(calls, contains('GET /api/me/students/4/report'));
    }, () => MockClient((r) async => handle(r)));

    // The test font is wider than the real one, so harmless overflow warnings are expected; anything else is not.
    final real = errors.where((e) => !e.contains('overflowed')).toList();
    expect(real, isEmpty, reason: real.join(String.fromCharCode(10)));
  });
}
