import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:guardian_app/core/theme.dart';
import 'package:guardian_app/models/portal_models.dart';
import 'package:guardian_app/widgets/cg_ui.dart';

void main() {
  temporaryPickupTests();
  test('dashboard parses counts, children and recent events', () {
    final d = GuardianDashboard.fromJson({
      'guardianName': 'Asha Rao',
      'today': '2026-11-12',
      'childrenCount': 2,
      'presentToday': 1,
      'lateToday': 1,
      'atSchool': 1,
      'onBus': 0,
      'leftSchoolOrHome': 1,
      'unreadNotifications': 3,
      'pendingPickups': 1,
      'children': [
        {'studentId': 4, 'fullName': 'Mira Rao', 'safetyStatus': 'AtSchool', 'presentToday': true, 'lateToday': true, 'checkInTime': '08:41:00'},
      ],
      'recentEvents': [
        {'journeyEventId': 9, 'studentId': 4, 'studentName': 'Mira Rao', 'eventType': 'SchoolArrival', 'timestamp': '2026-11-12T03:11:00Z', 'source': 'Rfid'},
      ],
    });

    expect(d.guardianName, 'Asha Rao');
    expect(d.today, DateTime(2026, 11, 12));
    expect(d.pendingPickups, 1);
    expect(d.children.single.safetyStatus, CgSafetyStatus.atSchool);
    expect(d.children.single.lateToday, isTrue);
    expect(d.recentEvents.single.studentName, 'Mira Rao');
  });

  test('student detail tolerates a child with no transport', () {
    final s = StudentDetail.fromJson({
      'child': {'studentId': 4, 'fullName': 'Mira Rao', 'safetyStatus': 'NoActivityToday'},
      'dateOfBirth': '2016-05-01T00:00:00',
      'hasRfidCard': true,
      'attendanceStats': {'windowDays': 30, 'daysPresent': 18, 'daysLate': 2, 'schoolDaysObserved': 20, 'presentRate': 90.0},
      'transport': null,
      'guardians': [
        {'guardianId': 1, 'fullName': 'Asha Rao', 'relationship': 'Mother', 'isAuthorizedForPickup': true, 'isYou': true},
      ],
      'todaysEvents': [],
    });

    expect(s.transport, isNull);
    expect(s.stats.presentRate, 90.0);
    expect(s.guardians.single.isYou, isTrue);
  });

  test('report and pickup records parse dates, times and kinds', () {
    final r = StudentReport.fromJson({
      'studentId': 4,
      'studentName': 'Mira',
      'from': '2026-11-01',
      'to': '2026-11-12',
      'daysPresent': 1,
      'presentRate': 50,
      'days': [
        {'date': '2026-11-12', 'checkIn': '08:41:00', 'checkOut': null, 'late': true},
      ],
    });
    final p = PickupRecord.fromJson({
      'kind': 'Temporary',
      'id': 3,
      'studentId': 4,
      'studentName': 'Mira',
      'pickedUpBy': 'Ravi Kumar',
      'attemptedAt': '2026-11-12T10:00:00Z',
      'state': 'Approved',
    });

    expect(r.days.single.late, isTrue);
    expect(r.presentRate, 50.0);
    expect(p.isTemporary, isTrue);
    expect(apiDate(DateTime(2026, 1, 5)), '2026-01-05');
  });

  testWidgets('pickup state badges always show text, never colour alone', (tester) async {
    await tester.pumpWidget(const MaterialApp(
      home: Scaffold(
        body: Column(children: [
          CgPickupStateBadge('Pending'),
          CgPickupStateBadge('Approved'),
          CgPickupStateBadge('Expired'),
        ]),
      ),
    ));
    expect(find.text('Awaiting approval'), findsOneWidget);
    expect(find.text('Picked up'), findsOneWidget);
    expect(find.text('Expired'), findsOneWidget);
  });

  testWidgets('KPI tile and timeline render their content', (tester) async {
    await tester.pumpWidget(const MaterialApp(
      home: Scaffold(
        body: Column(children: [
          CgKpi(label: 'At school now', value: '2', icon: Icons.verified_user_rounded, note: 'Checked in'),
          CgTimeline([CgTimelineItem(title: 'Arrived at school', time: '8:41 AM', icon: Icons.login_rounded, current: true)]),
        ]),
      ),
    ));
    expect(find.text('At school now'), findsOneWidget);
    expect(find.text('Arrived at school'), findsOneWidget);
    expect(find.text('8:41 AM'), findsOneWidget);
  });
}

void temporaryPickupTests() {
  test('temporary pickup parses state and flags; open states are cancellable windows', () {
    final p = TemporaryPickup.fromJson({
      'id': 7,
      'studentId': 4,
      'studentName': 'Mira Rao',
      'requestedByName': 'Asha Rao',
      'requestedByYou': true,
      'fullName': 'Ravi Kumar',
      'phone': '9820012345',
      'relationship': 'Uncle',
      'validFromUtc': '2026-11-12T09:00:00Z',
      'validUntilUtc': '2026-11-12T12:00:00Z',
      'state': 'Active',
      'hasPhoto': true,
      'canCancel': true,
      'qrAvailable': true,
    });
    expect(p.isOpen, isTrue);
    expect(p.qrAvailable, isTrue);
    expect(p.validUntil.difference(p.validFrom), const Duration(hours: 3));
    expect(TemporaryPickup.fromJson({...{'state': 'Completed'}}).isOpen, isFalse);
  });

  test('detail keeps the audit order and the QR model carries the token', () {
    final d = TemporaryPickupDetail.fromJson({
      'pickup': {'id': 1, 'state': 'Completed'},
      'audit': [
        {'occurredAt': '2026-11-12T09:00:00Z', 'action': 'Requested', 'actorRole': 'Guardian'},
        {'occurredAt': '2026-11-12T09:05:00Z', 'action': 'Approved', 'actorRole': 'Admin'},
      ],
    });
    final qr = TemporaryPickupQr.fromJson({'token': 'CGTP1.1.abc.def', 'validFromUtc': '2026-11-12T09:00:00Z', 'validUntilUtc': '2026-11-12T12:00:00Z'});
    expect(d.audit.map((a) => a.action), ['Requested', 'Approved']);
    expect(qr.token, startsWith('CGTP1.'));
  });
}
