import 'dart:convert';
import 'package:flutter/foundation.dart' show defaultTargetPlatform;
import '../core/api_client.dart';
import '../models/models.dart';
import '../models/portal_models.dart';

/// Wraps all ChildGuard.Api calls the Guardian app needs. One place to see
/// every endpoint this app depends on.
class GuardianApiService {
  final ApiClient _client;
  GuardianApiService(this._client);

  /// Step 1 of sign-in. Sends this phone's trusted-device secret when it has one: the server then either
  /// raises an OTP challenge as before, or (trusted phone, correct password) answers with the final outcome.
  Future<GuardianSignIn> login(String contactNumber, String password) async {
    final device = await _client.deviceToken;
    final result = await _client.post('/api/auth/guardian/login', {
      'contactNumber': contactNumber,
      'password': password,
      if (device != null && device.isNotEmpty) 'deviceToken': device,
    }, auth: false);
    final signIn = GuardianSignIn.fromJson((result as Map).cast<String, dynamic>());
    final auth = signIn.auth;
    if (auth?.session != null) await _client.setToken(auth!.session!['token']);
    return signIn;
  }

  /// Step 2. Asks the server to trust this phone, so the next sign-in here needs only the password.
  /// Stores the session token when the server returns one, and the device secret when it issues one.
  Future<GuardianAuthResult> verifyOtp(String challengeToken, String code) async {
    final result = await _client.post('/api/auth/guardian/verify-otp', {
      'challengeToken': challengeToken,
      'code': code,
      'trustDevice': true,
      'deviceLabel': defaultTargetPlatform.name,
    }, auth: false);
    final auth = GuardianAuthResult.fromJson(result);
    if (auth.deviceToken != null) await _client.setDeviceToken(auth.deviceToken);
    if (auth.session != null) await _client.setToken(auth.session!['token']);
    return auth;
  }

  /// A phone that is already signed in (an install from before trusted devices existed) trusts itself once,
  /// without another code. Silent: if it fails the guardian simply gets the OTP at the next sign-in.
  Future<void> enrolTrustedDeviceIfNeeded() async {
    final existing = await _client.deviceToken;
    if (existing != null && existing.isNotEmpty) return;
    try {
      final json = await _client.post('/api/me/trusted-devices', {'label': defaultTargetPlatform.name});
      await _client.setDeviceToken((json as Map)['deviceToken']?.toString());
    } catch (_) {}
  }

  /// Stops trusting every phone of this guardian (including this one): each needs the OTP at its next sign-in.
  Future<void> forgetTrustedDevices() async {
    await _client.delete('/api/me/trusted-devices');
    await _client.setDeviceToken(null);
  }

  Future<OtpChallenge> resendOtp(String challengeToken) async {
    final result = await _client.post('/api/auth/guardian/resend-otp',
        {'challengeToken': challengeToken}, auth: false);
    return OtpChallenge.fromJson(result);
  }

  /// Completes the mandatory first-login password reset using the ticket from [verifyOtp].
  Future<void> resetFirstLoginPassword(String resetTicket, String newPassword, String confirmPassword) async {
    await _client.post('/api/auth/guardian/reset-password',
        {'resetTicket': resetTicket, 'newPassword': newPassword, 'confirmPassword': confirmPassword}, auth: false);
  }

  Future<void> register(String fullName, String email, String phone, String password) async {
    await _client.post('/api/auth/guardian/register',
        {'fullName': fullName, 'email': email, 'phone': phone, 'password': password}, auth: false);
  }

  Future<void> forgotPassword(String email) async {
    await _client.post('/api/auth/forgot-password', {'email': email, 'role': 'Guardian'}, auth: false);
  }

  Future<void> resetPassword(String email, String resetToken, String newPassword) async {
    await _client.post('/api/auth/reset-password',
        {'email': email, 'role': 'Guardian', 'resetToken': resetToken, 'newPassword': newPassword}, auth: false);
  }

  Future<void> changePassword(String currentPassword, String newPassword) async {
    await _client.post('/api/auth/change-password', {'currentPassword': currentPassword, 'newPassword': newPassword});
  }

  Future<void> logout() => _client.setToken(null);

  Future<GuardianProfile> getProfile() async {
    final json = await _client.get('/api/me/profile');
    return GuardianProfile.fromJson(json);
  }

  Future<void> updateProfile(String fullName, String? phone) async {
    await _client.put('/api/me/profile', {'fullName': fullName, 'phone': phone});
  }

  Future<List<AttendanceRecord>> getAttendance(int studentId) async {
    final json = await _client.get('/api/me/students/$studentId/attendance');
    return (json as List).map((e) => AttendanceRecord.fromJson(e)).toList();
  }

  Future<List<JourneyEvent>> getJourney(int studentId) async {
    final json = await _client.get('/api/me/students/$studentId/journey');
    return (json as List).map((e) => JourneyEvent.fromJson(e)).toList();
  }

  // ---- Dashboard, student details, transport, reports and pickup status ----

  Future<GuardianDashboard> getDashboard() async =>
      GuardianDashboard.fromJson((await _client.get('/api/me/dashboard') as Map).cast<String, dynamic>());

  Future<List<StudentSummary>> getStudents() async =>
      (await _client.get('/api/me/students') as List).map((e) => StudentSummary.fromJson(e)).toList();

  Future<StudentDetail> getStudentDetail(int studentId) async =>
      StudentDetail.fromJson((await _client.get('/api/me/students/$studentId') as Map).cast<String, dynamic>());

  Future<List<JourneyEvent>> getJourneyRange(int studentId, DateTime from, DateTime to) async {
    final json = await _client.get('/api/me/students/$studentId/journey?from=${apiDate(from)}&to=${apiDate(to)}');
    return (json as List).map((e) => JourneyEvent.fromJson(e)).toList();
  }

  Future<StudentReport> getStudentReport(int studentId, DateTime from, DateTime to) async => StudentReport.fromJson(
      (await _client.get('/api/me/students/$studentId/report?from=${apiDate(from)}&to=${apiDate(to)}') as Map)
          .cast<String, dynamic>());

  Future<List<PickupRecord>> getPickups({int? studentId, DateTime? from, DateTime? to}) async {
    final q = <String>[
      if (studentId != null) 'studentId=$studentId',
      if (from != null) 'from=${apiDate(from)}',
      if (to != null) 'to=${apiDate(to)}',
    ];
    final json = await _client.get('/api/me/pickups${q.isEmpty ? '' : '?${q.join('&')}'}');
    return (json as List).map((e) => PickupRecord.fromJson(e)).toList();
  }

  // ---- Temporary pickup guardians ----

  Future<List<TemporaryPickup>> getTemporaryPickups() async =>
      (await _client.get('/api/me/temporary-pickups') as List).map((e) => TemporaryPickup.fromJson(e)).toList();

  Future<TemporaryPickupDetail> getTemporaryPickup(int id) async =>
      TemporaryPickupDetail.fromJson((await _client.get('/api/me/temporary-pickups/$id') as Map).cast<String, dynamic>());

  Future<TemporaryPickup> createTemporaryPickup({
    required int studentId,
    required String fullName,
    required String phone,
    required String relationship,
    required DateTime validFrom,
    required DateTime validUntil,
    required String photoBase64,
    String? identityNote,
    String? notes,
  }) async {
    final json = await _client.post('/api/me/temporary-pickups', {
      'studentId': studentId,
      'fullName': fullName,
      'phone': phone,
      'relationship': relationship,
      'validFromUtc': validFrom.toUtc().toIso8601String(),
      'validUntilUtc': validUntil.toUtc().toIso8601String(),
      'photoBase64': photoBase64,
      'identityNote': identityNote,
      'notes': notes,
    });
    return TemporaryPickup.fromJson((json as Map).cast<String, dynamic>());
  }

  Future<TemporaryPickup> cancelTemporaryPickup(int id, {String? reason}) async => TemporaryPickup.fromJson(
      ((await _client.post('/api/me/temporary-pickups/$id/cancel', {'reason': reason})) as Map).cast<String, dynamic>());

  Future<TemporaryPickupQr> getTemporaryPickupQr(int id) async =>
      TemporaryPickupQr.fromJson((await _client.get('/api/me/temporary-pickups/$id/qr') as Map).cast<String, dynamic>());

  String temporaryPickupPhotoUrl(int id) => _client.imageUrl('/api/me/temporary-pickups/$id/photo');

  Future<List<AppNotification>> getNotifications() async {
    final json = await _client.get('/api/me/notifications');
    return (json as List).map((e) => AppNotification.fromJson(e)).toList();
  }

  Future<void> markNotificationRead(int id) async {
    await _client.patch('/api/me/notifications/$id/read', {});
  }

  Future<void> registerFace(String imageBase64) async {
    await _client.post('/api/me/face-registration', {'imageBase64': imageBase64});
  }

  // ---- Reference faces ----
  // Several photos (different light and angle) make pickup verification more reliable. Photos are
  // checked on the server first; only a numeric representation is stored, never the photo.

  Future<FaceReferences> getFaceReferences() async {
    final json = await _client.get('/api/me/face-references');
    return FaceReferences.fromJson(json);
  }

  Future<FaceImageAnalysis> analyzeFaceImage(String imageBase64) async {
    final json = await _client.post('/api/me/face-references/analyze',
        {'imageBase64': imageBase64, 'purpose': 'registration'});
    return FaceImageAnalysis.fromJson(json);
  }

  Future<AddFaceReferenceResult> addFaceReference(String imageBase64, String? label) async {
    final json = await _client.post('/api/me/face-references', {'imageBase64': imageBase64, 'label': label});
    return AddFaceReferenceResult.fromJson(json);
  }

  Future<void> removeFaceReference(int id) async {
    await _client.delete('/api/me/face-references/$id');
  }

  Future<void> registerDeviceToken(String fcmToken) async {
    await _client.post('/api/me/device-token', {'fcmToken': fcmToken});
  }

  // ---- Profile photo ----
  // Images are fetched with Image.network(url, headers: authHeaders) rather
  // than a plain URL, so the bearer token travels with the request and the
  // API's authorization is never bypassed from the app side.

  String get myPhotoUrl => _client.imageUrl('/api/me/photo');
  String studentPhotoUrl(int studentId) => _client.imageUrl('/api/me/students/$studentId/photo');
  Future<Map<String, String>> photoHeaders() => _client.authHeaders();

  Future<void> uploadMyPhoto(List<int> bytes, String filename, String contentType) async {
    await _client.postFile('/api/me/photo', bytes, filename, contentType);
  }

  Future<void> deleteMyPhoto() async {
    await _client.delete('/api/me/photo');
  }
}

String encodeImageBytes(List<int> bytes) => base64Encode(bytes);
