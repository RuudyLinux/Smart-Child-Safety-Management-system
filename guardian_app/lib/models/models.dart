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

  OtpChallenge({
    required this.challengeToken,
    required this.expiresAtUtc,
    required this.codeLength,
    required this.resendAvailableInSeconds,
    required this.resendsRemaining,
  });

  factory OtpChallenge.fromJson(Map<String, dynamic> j) => OtpChallenge(
        challengeToken: j['challengeToken'],
        expiresAtUtc: DateTime.parse(j['expiresAtUtc']).toUtc(),
        codeLength: j['codeLength'] ?? 6,
        resendAvailableInSeconds: j['resendAvailableInSeconds'] ?? 0,
        resendsRemaining: j['resendsRemaining'] ?? 0,
      );
}

/// Result of OTP verification: a session, or a first-login password-reset ticket.
class GuardianAuthResult {
  final GuardianAuthStage stage;
  final Map<String, dynamic>? session;
  final String? resetTicket;

  GuardianAuthResult({required this.stage, this.session, this.resetTicket});

  factory GuardianAuthResult.fromJson(Map<String, dynamic> j) => GuardianAuthResult(
        stage: _stageFromJson(j['stage']?.toString()),
        session: (j['session'] as Map?)?.cast<String, dynamic>(),
        resetTicket: j['resetTicket'],
      );
}

class GuardianProfile {
  final int guardianId;
  final String fullName;
  final String email;
  final String? phone;
  final bool faceRegistered;
  final List<StudentSummary> students;

  GuardianProfile({
    required this.guardianId,
    required this.fullName,
    required this.email,
    required this.phone,
    required this.faceRegistered,
    required this.students,
  });

  factory GuardianProfile.fromJson(Map<String, dynamic> j) => GuardianProfile(
        guardianId: j['guardianId'],
        fullName: j['fullName'],
        email: j['email'],
        phone: j['phone'],
        faceRegistered: j['faceRegistered'] ?? false,
        students: (j['students'] as List? ?? []).map((s) => StudentSummary.fromJson(s)).toList(),
      );
}

class StudentSummary {
  final int studentId;
  final String fullName;
  final String? grade;
  final String? relationship;
  final bool isAuthorizedForPickup;

  StudentSummary({
    required this.studentId,
    required this.fullName,
    required this.grade,
    required this.relationship,
    required this.isAuthorizedForPickup,
  });

  factory StudentSummary.fromJson(Map<String, dynamic> j) => StudentSummary(
        studentId: j['studentId'],
        fullName: j['fullName'],
        grade: j['grade'],
        relationship: j['relationship'],
        isAuthorizedForPickup: j['isAuthorizedForPickup'] ?? false,
      );
}

class AttendanceRecord {
  final String eventType;
  final DateTime date;
  final String time;

  AttendanceRecord({required this.eventType, required this.date, required this.time});

  factory AttendanceRecord.fromJson(Map<String, dynamic> j) => AttendanceRecord(
        eventType: j['eventType'],
        date: DateTime.parse(j['date']),
        time: j['time'],
      );
}

class JourneyEvent {
  final String eventType;
  final DateTime timestamp;
  final String source;

  JourneyEvent({required this.eventType, required this.timestamp, required this.source});

  factory JourneyEvent.fromJson(Map<String, dynamic> j) => JourneyEvent(
        eventType: j['eventType'],
        timestamp: DateTime.parse(j['timestamp']),
        source: j['source'],
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
        notificationId: j['notificationId'],
        title: j['title'],
        message: j['message'],
        type: j['type'],
        isRead: j['isRead'] ?? false,
        sentAt: DateTime.parse(j['sentAt']),
      );
}
