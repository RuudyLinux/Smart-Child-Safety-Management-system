import 'package:intl/intl.dart';
import '../core/theme.dart' show CgSafety, CgSafetyStatus;

/// The API sends times of day as a .NET TimeOnly ("16:50:03.0878150"). Shows it as a
/// short clock time ("4:50 PM"); returns anything unexpected unchanged.
String? formatCheckInTime(String? raw) {
  if (raw == null) return null;
  final m = RegExp(r'^(\d{1,2}):(\d{2})').firstMatch(raw.trim());
  if (m == null) return raw;
  final hour = int.parse(m.group(1)!), minute = int.parse(m.group(2)!);
  if (hour > 23 || minute > 59) return raw;
  return DateFormat('h:mm a').format(DateTime(2000, 1, 1, hour, minute));
}

/// What the server says the app must do next in the sign-in flow.
enum GuardianAuthStage { otpRequired, passwordResetRequired, authenticated }

GuardianAuthStage _stageFromJson(String? raw) => switch (raw) {
      'PasswordResetRequired' => GuardianAuthStage.passwordResetRequired,
      'Authenticated' => GuardianAuthStage.authenticated,
      _ => GuardianAuthStage.otpRequired,
    };

/// An open OTP challenge. Carries no code — only the handle and its timings.
class OtpChallenge {
  final String challengeToken;
  final DateTime expiresAtUtc;
  final int codeLength;
  final int resendAvailableInSeconds;
  final int resendsRemaining;
  /// The inbox the code was sent to, masked (a***@gmail.com). Null from an older server.
  final String? maskedEmail;
  /// Seconds the code had left on the server's clock when it answered. Null from an older server.
  final int? expiresInSeconds;

  /// Started when this challenge was received. Measures elapsed time without
  /// reading the phone's wall clock, which can be minutes off the server's.
  final Stopwatch _sinceReceived = Stopwatch()..start();

  OtpChallenge({
    required this.challengeToken,
    required this.expiresAtUtc,
    required this.codeLength,
    required this.resendAvailableInSeconds,
    required this.resendsRemaining,
    this.maskedEmail,
    this.expiresInSeconds,
  });

  /// Seconds until the code expires. Uses the server-measured [expiresInSeconds]
  /// minus time elapsed since receipt, so a wrong phone clock cannot make a
  /// valid code look expired. Falls back to the wall clock for an older server.
  int get secondsUntilExpiry {
    final serverSeconds = expiresInSeconds;
    final left = (serverSeconds != null && serverSeconds > 0)
        ? serverSeconds - _sinceReceived.elapsed.inSeconds
        : expiresAtUtc.difference(DateTime.now().toUtc()).inSeconds;
    return left > 0 ? left : 0;
  }

  factory OtpChallenge.fromJson(Map<String, dynamic> j) => OtpChallenge(
        challengeToken: j['challengeToken']?.toString() ?? '',
        expiresAtUtc: (DateTime.tryParse(j['expiresAtUtc']?.toString() ?? '') ?? DateTime.now()).toUtc(),
        codeLength: j['codeLength'] ?? 6,
        resendAvailableInSeconds: j['resendAvailableInSeconds'] ?? 0,
        resendsRemaining: j['resendsRemaining'] ?? 0,
        maskedEmail: j['maskedEmail']?.toString(),
        expiresInSeconds: (j['expiresInSeconds'] as num?)?.toInt(),
      );
}

/// Result of OTP verification: a session, or a first-login password-reset ticket.
class GuardianAuthResult {
  final GuardianAuthStage stage;
  final Map<String, dynamic>? session;
  final String? resetTicket;
  /// Secret for this phone, present only when the OTP step trusted it. Stored, never shown.
  final String? deviceToken;

  GuardianAuthResult({required this.stage, this.session, this.resetTicket, this.deviceToken});

  factory GuardianAuthResult.fromJson(Map<String, dynamic> j) => GuardianAuthResult(
        stage: _stageFromJson(j['stage']?.toString()),
        session: (j['session'] as Map?)?.cast<String, dynamic>(),
        resetTicket: j['resetTicket'],
        deviceToken: j['deviceToken']?.toString(),
      );
}

/// What the password step returned: an OTP challenge, or (a phone the server already trusts) the final
/// outcome with no code. The server's "stage" field says which.
class GuardianSignIn {
  final GuardianAuthStage stage;
  final OtpChallenge? challenge;
  final GuardianAuthResult? auth;

  GuardianSignIn({required this.stage, this.challenge, this.auth});

  factory GuardianSignIn.fromJson(Map<String, dynamic> j) {
    final stage = _stageFromJson(j['stage']?.toString());
    return stage == GuardianAuthStage.otpRequired
        ? GuardianSignIn(stage: stage, challenge: OtpChallenge.fromJson(j))
        : GuardianSignIn(stage: stage, auth: GuardianAuthResult.fromJson(j));
  }
}

class GuardianProfile {
  final int guardianId;
  final String fullName;
  final String email;
  final String? phone;
  final bool faceRegistered;
  final bool hasPhoto;
  final List<StudentSummary> students;

  GuardianProfile({
    required this.guardianId,
    required this.fullName,
    required this.email,
    required this.phone,
    required this.faceRegistered,
    required this.hasPhoto,
    required this.students,
  });

  factory GuardianProfile.fromJson(Map<String, dynamic> j) => GuardianProfile(
        guardianId: j['guardianId'] ?? 0,
        fullName: j['fullName']?.toString() ?? '',
        email: j['email']?.toString() ?? '',
        phone: j['phone'],
        faceRegistered: j['faceRegistered'] ?? false,
        hasPhoto: j['hasPhoto'] ?? false,
        students: (j['students'] as List? ?? []).map((s) => StudentSummary.fromJson(s)).toList(),
      );

  /// True while every linked child's safety status reads as fine — the
  /// headline the Guardian app leads with. A guardian with no children yet
  /// is not "all safe": there is nothing to report, so callers should check
  /// students.isEmpty first.
  bool get allChildrenSafe => students.every((s) =>
      s.safetyStatus == CgSafetyStatus.atSchool || s.safetyStatus == CgSafetyStatus.droppedOff);
}

class StudentSummary {
  final int studentId;
  final String fullName;
  final String? grade;
  final String? relationship;
  final bool isAuthorizedForPickup;
  final bool hasPhoto;
  final CgSafetyStatus safetyStatus;
  final DateTime? lastEventAt;
  final String? lastEventType;
  final String? checkInTime;
  final bool presentToday;
  final bool lateToday;
  final String? busNumber;
  final String? routeName;

  StudentSummary({
    required this.studentId,
    required this.fullName,
    required this.grade,
    required this.relationship,
    required this.isAuthorizedForPickup,
    required this.hasPhoto,
    required this.safetyStatus,
    required this.lastEventAt,
    required this.lastEventType,
    required this.checkInTime,
    required this.presentToday,
    this.lateToday = false,
    required this.busNumber,
    required this.routeName,
  });

  factory StudentSummary.fromJson(Map<String, dynamic> j) => StudentSummary(
        studentId: j['studentId'] ?? 0,
        fullName: j['fullName']?.toString() ?? '',
        grade: j['grade'],
        relationship: j['relationship'],
        isAuthorizedForPickup: j['isAuthorizedForPickup'] ?? false,
        hasPhoto: j['hasPhoto'] ?? false,
        safetyStatus: CgSafety.fromApi(j['safetyStatus']?.toString()),
        lastEventAt: j['lastEventAt'] != null ? DateTime.tryParse(j['lastEventAt']) : null,
        lastEventType: j['lastEventType']?.toString(),
        checkInTime: j['checkInTime']?.toString(),
        presentToday: j['presentToday'] ?? false,
        lateToday: j['lateToday'] ?? false,
        busNumber: j['busNumber'],
        routeName: j['routeName'],
      );
}

class AttendanceRecord {
  final String eventType;
  final DateTime date;
  final String time;

  AttendanceRecord({required this.eventType, required this.date, required this.time});

  factory AttendanceRecord.fromJson(Map<String, dynamic> j) => AttendanceRecord(
        eventType: j['eventType']?.toString() ?? '',
        date: DateTime.tryParse(j['date']?.toString() ?? '') ?? DateTime.now(),
        time: j['time']?.toString() ?? '',
      );
}

class JourneyEvent {
  final String eventType;
  final DateTime timestamp;
  final String source;
  final int studentId;
  final String studentName;

  JourneyEvent({required this.eventType, required this.timestamp, required this.source, this.studentId = 0, this.studentName = ''});

  factory JourneyEvent.fromJson(Map<String, dynamic> j) => JourneyEvent(
        eventType: j['eventType']?.toString() ?? '',
        timestamp: DateTime.tryParse(j['timestamp']?.toString() ?? '') ?? DateTime.now(),
        source: j['source']?.toString() ?? '',
        studentId: j['studentId'] ?? 0,
        studentName: j['studentName']?.toString() ?? '',
      );
}

class AppNotification {
  final int notificationId;
  final String title;
  final String message;
  final String type;
  final bool isRead;
  final DateTime sentAt;

  AppNotification({
    required this.notificationId,
    required this.title,
    required this.message,
    required this.type,
    required this.isRead,
    required this.sentAt,
  });

  factory AppNotification.fromJson(Map<String, dynamic> j) => AppNotification(
        notificationId: j['notificationId'] ?? 0,
        title: j['title']?.toString() ?? '',
        message: j['message']?.toString() ?? '',
        type: j['type']?.toString() ?? '',
        isRead: j['isRead'] ?? false,
        sentAt: DateTime.tryParse(j['sentAt']?.toString() ?? '') ?? DateTime.now(),
      );
}

/// One registered reference face. Metadata only; the server never sends biometric data.
class FaceReference {
  final int id; // 0 is the first (primary) face
  final String source; // "Primary" or "Additional"
  final String? label;
  final DateTime createdAt;

  FaceReference({required this.id, required this.source, required this.label, required this.createdAt});

  bool get isPrimary => source == 'Primary';

  factory FaceReference.fromJson(Map<String, dynamic> j) => FaceReference(
        id: j['id'] ?? 0,
        source: j['source']?.toString() ?? 'Additional',
        label: j['label']?.toString(),
        createdAt: DateTime.tryParse(j['createdAt']?.toString() ?? '') ?? DateTime.now(),
      );
}

class FaceReferences {
  final List<FaceReference> references;
  final int maxReferences;
  final bool canAdd;

  FaceReferences({required this.references, required this.maxReferences, required this.canAdd});

  factory FaceReferences.fromJson(Map<String, dynamic> j) => FaceReferences(
        references: (j['references'] as List? ?? []).map((r) => FaceReference.fromJson(r)).toList(),
        maxReferences: j['maxReferences'] ?? 5,
        canAdd: j['canAdd'] ?? true,
      );
}

/// The server's verdict on one photo (image quality + face detection), shown before it is submitted.
class FaceImageAnalysis {
  final bool passed;
  final int faceCount;
  final List<String> issues;
  final List<String> messages;

  FaceImageAnalysis({required this.passed, required this.faceCount, required this.issues, required this.messages});

  factory FaceImageAnalysis.fromJson(Map<String, dynamic> j) => FaceImageAnalysis(
        passed: j['passed'] == true,
        faceCount: j['faceCount'] ?? 0,
        issues: (j['issues'] as List? ?? []).map((e) => e.toString()).toList(),
        messages: (j['messages'] as List? ?? []).map((e) => e.toString()).toList(),
      );
}

class AddFaceReferenceResult {
  final bool added;
  final bool duplicate;
  final String message;

  AddFaceReferenceResult({required this.added, required this.duplicate, required this.message});

  factory AddFaceReferenceResult.fromJson(Map<String, dynamic> j) => AddFaceReferenceResult(
        added: j['added'] == true,
        duplicate: j['duplicate'] == true,
        message: j['message']?.toString() ?? '',
      );
}
