import 'package:intl/intl.dart';
import 'models.dart';

// Models for the Guardian app read endpoints (api/me/dashboard, students, report, pickups).
// Field names follow the API's camelCase JSON; every field tolerates absence so an older server
// never crashes the app.

DateTime? _instant(dynamic v) => v == null ? null : DateTime.tryParse(v.toString())?.toLocal();
DateTime? _date(dynamic v) => v == null ? null : DateTime.tryParse(v.toString());
List<T> _list<T>(dynamic v, T Function(Map<String, dynamic>) f) =>
    (v as List? ?? []).map((e) => f((e as Map).cast<String, dynamic>())).toList();

/// "2026-11-12" for query strings.
String apiDate(DateTime d) => DateFormat('yyyy-MM-dd').format(d);

class GuardianDashboard {
  final String guardianName;
  final DateTime? today;
  final int childrenCount;
  final int presentToday;
  final int lateToday;
  final int atSchool;
  final int onBus;
  final int leftSchoolOrHome;
  final int unreadNotifications;
  final int pendingPickups;
  final List<StudentSummary> children;
  final List<JourneyEvent> recentEvents;

  GuardianDashboard({
    required this.guardianName,
    required this.today,
    required this.childrenCount,
    required this.presentToday,
    required this.lateToday,
    required this.atSchool,
    required this.onBus,
    required this.leftSchoolOrHome,
    required this.unreadNotifications,
    required this.pendingPickups,
    required this.children,
    required this.recentEvents,
  });

  factory GuardianDashboard.fromJson(Map<String, dynamic> j) => GuardianDashboard(
        guardianName: j['guardianName']?.toString() ?? '',
        today: _date(j['today']),
        childrenCount: j['childrenCount'] ?? 0,
        presentToday: j['presentToday'] ?? 0,
        lateToday: j['lateToday'] ?? 0,
        atSchool: j['atSchool'] ?? 0,
        onBus: j['onBus'] ?? 0,
        leftSchoolOrHome: j['leftSchoolOrHome'] ?? 0,
        unreadNotifications: j['unreadNotifications'] ?? 0,
        pendingPickups: j['pendingPickups'] ?? 0,
        children: _list(j['children'], StudentSummary.fromJson),
        recentEvents: _list(j['recentEvents'], JourneyEvent.fromJson),
      );
}

class TransportInfo {
  final String busNumber;
  final int capacity;
  final String? routeName;
  final String? routeDescription;
  final String? driverName;
  final String? driverPhone;
  final DateTime? assignedAt;

  TransportInfo({
    required this.busNumber,
    required this.capacity,
    this.routeName,
    this.routeDescription,
    this.driverName,
    this.driverPhone,
    this.assignedAt,
  });

  factory TransportInfo.fromJson(Map<String, dynamic> j) => TransportInfo(
        busNumber: j['busNumber']?.toString() ?? '',
        capacity: j['capacity'] ?? 0,
        routeName: j['routeName']?.toString(),
        routeDescription: j['routeDescription']?.toString(),
        driverName: j['driverName']?.toString(),
        driverPhone: j['driverPhone']?.toString(),
        assignedAt: _instant(j['assignedAt']),
      );
}

class CoGuardian {
  final int guardianId;
  final String fullName;
  final String? relationship;
  final bool isAuthorizedForPickup;
  final bool isYou;

  CoGuardian({required this.guardianId, required this.fullName, this.relationship, required this.isAuthorizedForPickup, required this.isYou});

  factory CoGuardian.fromJson(Map<String, dynamic> j) => CoGuardian(
        guardianId: j['guardianId'] ?? 0,
        fullName: j['fullName']?.toString() ?? '',
        relationship: j['relationship']?.toString(),
        isAuthorizedForPickup: j['isAuthorizedForPickup'] ?? false,
        isYou: j['isYou'] ?? false,
      );
}

class AttendanceStats {
  final int windowDays;
  final int daysPresent;
  final int daysLate;
  final int schoolDaysObserved;
  final double presentRate;

  AttendanceStats({required this.windowDays, required this.daysPresent, required this.daysLate, required this.schoolDaysObserved, required this.presentRate});

  factory AttendanceStats.fromJson(Map<String, dynamic> j) => AttendanceStats(
        windowDays: j['windowDays'] ?? 30,
        daysPresent: j['daysPresent'] ?? 0,
        daysLate: j['daysLate'] ?? 0,
        schoolDaysObserved: j['schoolDaysObserved'] ?? 0,
        presentRate: (j['presentRate'] as num?)?.toDouble() ?? 0,
      );
}

class StudentDetail {
  final StudentSummary child;
  final DateTime? dateOfBirth;
  final bool hasRfidCard;
  final AttendanceStats stats;
  final TransportInfo? transport;
  final List<CoGuardian> guardians;
  final List<JourneyEvent> todaysEvents;

  StudentDetail({
    required this.child,
    required this.dateOfBirth,
    required this.hasRfidCard,
    required this.stats,
    required this.transport,
    required this.guardians,
    required this.todaysEvents,
  });

  factory StudentDetail.fromJson(Map<String, dynamic> j) => StudentDetail(
        child: StudentSummary.fromJson((j['child'] as Map).cast<String, dynamic>()),
        dateOfBirth: _date(j['dateOfBirth']),
        hasRfidCard: j['hasRfidCard'] ?? false,
        stats: AttendanceStats.fromJson((j['attendanceStats'] as Map? ?? {}).cast<String, dynamic>()),
        transport: j['transport'] == null ? null : TransportInfo.fromJson((j['transport'] as Map).cast<String, dynamic>()),
        guardians: _list(j['guardians'], CoGuardian.fromJson),
        todaysEvents: _list(j['todaysEvents'], JourneyEvent.fromJson),
      );
}

class ReportDay {
  final DateTime date;
  final String? checkIn;
  final String? checkOut;
  final bool late;
  final DateTime? busBoardingAt;
  final DateTime? busDropoffAt;

  ReportDay({required this.date, this.checkIn, this.checkOut, required this.late, this.busBoardingAt, this.busDropoffAt});

  factory ReportDay.fromJson(Map<String, dynamic> j) => ReportDay(
        date: _date(j['date']) ?? DateTime.now(),
        checkIn: j['checkIn']?.toString(),
        checkOut: j['checkOut']?.toString(),
        late: j['late'] ?? false,
        busBoardingAt: _instant(j['busBoardingAt']),
        busDropoffAt: _instant(j['busDropoffAt']),
      );
}

class StudentReport {
  final int studentId;
  final String studentName;
  final DateTime? from;
  final DateTime? to;
  final int schoolDaysObserved;
  final int daysPresent;
  final int daysLate;
  final double presentRate;
  final int busBoardings;
  final int busDropoffs;
  final int pickupsApproved;
  final List<ReportDay> days;

  StudentReport({
    required this.studentId,
    required this.studentName,
    required this.from,
    required this.to,
    required this.schoolDaysObserved,
    required this.daysPresent,
    required this.daysLate,
    required this.presentRate,
    required this.busBoardings,
    required this.busDropoffs,
    required this.pickupsApproved,
    required this.days,
  });

  factory StudentReport.fromJson(Map<String, dynamic> j) => StudentReport(
        studentId: j['studentId'] ?? 0,
        studentName: j['studentName']?.toString() ?? '',
        from: _date(j['from']),
        to: _date(j['to']),
        schoolDaysObserved: j['schoolDaysObserved'] ?? 0,
        daysPresent: j['daysPresent'] ?? 0,
        daysLate: j['daysLate'] ?? 0,
        presentRate: (j['presentRate'] as num?)?.toDouble() ?? 0,
        busBoardings: j['busBoardings'] ?? 0,
        busDropoffs: j['busDropoffs'] ?? 0,
        pickupsApproved: j['pickupsApproved'] ?? 0,
        days: _list(j['days'], ReportDay.fromJson),
      );
}

/// One pickup attempt. [kind] is "Guardian" or "Temporary".
class PickupRecord {
  final String kind;
  final int id;
  final int studentId;
  final String studentName;
  final String pickedUpBy;
  final bool isYou;
  final DateTime attemptedAt;
  final String state;
  final DateTime? expiresAt;

  PickupRecord({
    required this.kind,
    required this.id,
    required this.studentId,
    required this.studentName,
    required this.pickedUpBy,
    required this.isYou,
    required this.attemptedAt,
    required this.state,
    this.expiresAt,
  });

  bool get isTemporary => kind == 'Temporary';

  factory PickupRecord.fromJson(Map<String, dynamic> j) => PickupRecord(
        kind: j['kind']?.toString() ?? 'Guardian',
        id: j['id'] ?? 0,
        studentId: j['studentId'] ?? 0,
        studentName: j['studentName']?.toString() ?? '',
        pickedUpBy: j['pickedUpBy']?.toString() ?? '',
        isYou: j['isYou'] ?? false,
        attemptedAt: _instant(j['attemptedAt']) ?? DateTime.now(),
        state: j['state']?.toString() ?? '',
        expiresAt: _instant(j['expiresAt']),
      );
}

/// A temporary pickup request as the guardian sees it. [state] is the server's effective state
/// (PendingApproval, Scheduled, Active, Completed, Rejected, Cancelled, Revoked, Expired).
class TemporaryPickup {
  final int id;
  final int studentId;
  final String studentName;
  final String requestedByName;
  final bool requestedByYou;
  final String fullName;
  final String phone;
  final String relationship;
  final String? identityNote;
  final String? notes;
  final DateTime validFrom;
  final DateTime validUntil;
  final String state;
  final DateTime? decidedAt;
  final String? decisionNote;
  final DateTime? completedAt;
  final bool hasPhoto;
  final bool canCancel;
  final bool qrAvailable;

  TemporaryPickup({
    required this.id,
    required this.studentId,
    required this.studentName,
    required this.requestedByName,
    required this.requestedByYou,
    required this.fullName,
    required this.phone,
    required this.relationship,
    this.identityNote,
    this.notes,
    required this.validFrom,
    required this.validUntil,
    required this.state,
    this.decidedAt,
    this.decisionNote,
    this.completedAt,
    required this.hasPhoto,
    required this.canCancel,
    required this.qrAvailable,
  });

  factory TemporaryPickup.fromJson(Map<String, dynamic> j) => TemporaryPickup(
        id: j['id'] ?? 0,
        studentId: j['studentId'] ?? 0,
        studentName: j['studentName']?.toString() ?? '',
        requestedByName: j['requestedByName']?.toString() ?? '',
        requestedByYou: j['requestedByYou'] ?? false,
        fullName: j['fullName']?.toString() ?? '',
        phone: j['phone']?.toString() ?? '',
        relationship: j['relationship']?.toString() ?? '',
        identityNote: j['identityNote']?.toString(),
        notes: j['notes']?.toString(),
        validFrom: _instant(j['validFromUtc']) ?? DateTime.now(),
        validUntil: _instant(j['validUntilUtc']) ?? DateTime.now(),
        state: j['state']?.toString() ?? '',
        decidedAt: _instant(j['decidedAt']),
        decisionNote: j['decisionNote']?.toString(),
        completedAt: _instant(j['completedAt']),
        hasPhoto: j['hasPhoto'] ?? false,
        canCancel: j['canCancel'] ?? false,
        qrAvailable: j['qrAvailable'] ?? false,
      );

  bool get isOpen => const {'PendingApproval', 'Scheduled', 'Active'}.contains(state);
}

class TemporaryPickupAuditEntry {
  final DateTime at;
  final String action;
  final String actorRole;
  final String? detail;

  TemporaryPickupAuditEntry({required this.at, required this.action, required this.actorRole, this.detail});

  factory TemporaryPickupAuditEntry.fromJson(Map<String, dynamic> j) => TemporaryPickupAuditEntry(
        at: _instant(j['occurredAt']) ?? DateTime.now(),
        action: j['action']?.toString() ?? '',
        actorRole: j['actorRole']?.toString() ?? '',
        detail: j['detail']?.toString(),
      );
}

class TemporaryPickupDetail {
  final TemporaryPickup pickup;
  final List<TemporaryPickupAuditEntry> audit;

  TemporaryPickupDetail({required this.pickup, required this.audit});

  factory TemporaryPickupDetail.fromJson(Map<String, dynamic> j) => TemporaryPickupDetail(
        pickup: TemporaryPickup.fromJson((j['pickup'] as Map).cast<String, dynamic>()),
        audit: _list(j['audit'], TemporaryPickupAuditEntry.fromJson),
      );
}

class TemporaryPickupQr {
  final String token;
  final DateTime validFrom;
  final DateTime validUntil;

  TemporaryPickupQr({required this.token, required this.validFrom, required this.validUntil});

  factory TemporaryPickupQr.fromJson(Map<String, dynamic> j) => TemporaryPickupQr(
        token: j['token']?.toString() ?? '',
        validFrom: _instant(j['validFromUtc']) ?? DateTime.now(),
        validUntil: _instant(j['validUntilUtc']) ?? DateTime.now(),
      );
}
