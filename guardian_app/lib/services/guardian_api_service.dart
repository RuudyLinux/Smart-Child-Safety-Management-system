import 'dart:convert';
import '../core/api_client.dart';
import '../models/models.dart';

/// Wraps all ChildGuard.Api calls the Guardian app needs. One place to see
/// every endpoint this app depends on.
class GuardianApiService {
  final ApiClient _client;
  GuardianApiService(this._client);

  /// Step 1 of sign-in. Returns an OTP challenge; no session is issued here.
  Future<OtpChallenge> login(String contactNumber, String password) async {
    final result = await _client.post('/api/auth/guardian/login',
        {'contactNumber': contactNumber, 'password': password}, auth: false);
    return OtpChallenge.fromJson(result);
  }

  /// Step 2. Stores the token only when the server returns a session.
  Future<GuardianAuthResult> verifyOtp(String challengeToken, String code) async {
    final result = await _client.post('/api/auth/guardian/verify-otp',
        {'challengeToken': challengeToken, 'code': code}, auth: false);
    final auth = GuardianAuthResult.fromJson(result);
    if (auth.session != null) await _client.setToken(auth.session!['token']);
    return auth;
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
