import 'dart:convert';
import '../core/api_client.dart';
import '../models/models.dart';

/// Wraps all ChildGuard.Api calls the Guardian app needs. One place to see
/// every endpoint this app depends on.
class GuardianApiService {
  final ApiClient _client;
  GuardianApiService(this._client);

  Future<Map<String, dynamic>> login(String email, String password) async {
    final result = await _client.post('/api/auth/guardian/login', {'email': email, 'password': password}, auth: false);
    await _client.setToken(result['token']);
    return result;
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
}

String encodeImageBytes(List<int> bytes) => base64Encode(bytes);
