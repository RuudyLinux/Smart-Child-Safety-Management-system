import 'dart:async';
import 'package:flutter/foundation.dart';
import '../core/api_client.dart';
import '../models/models.dart';
import '../services/guardian_api_service.dart';
import '../services/push_notification_service.dart';

enum AuthStatus { unknown, authenticated, unauthenticated }

class AuthProvider extends ChangeNotifier {
  final ApiClient client;
  late final GuardianApiService api;

  AuthStatus status = AuthStatus.unknown;
  GuardianProfile? profile;
  String? lastError;

  late final PushNotificationService _pushService;

  AuthProvider({ApiClient? client}) : client = client ?? ApiClient() {
    api = GuardianApiService(this.client);
    _pushService = PushNotificationService(api);
  }

  Future<void> bootstrap() async {
    final token = await client.token;
    if (token == null) {
      status = AuthStatus.unauthenticated;
    } else {
      try {
        profile = await api.getProfile();
        status = AuthStatus.authenticated;
        unawaited(_pushService.registerForPushNotifications());
      } catch (_) {
        await client.setToken(null);
        status = AuthStatus.unauthenticated;
      }
    }
    notifyListeners();
  }

  Future<bool> login(String email, String password) async {
    try {
      await api.login(email, password);
      profile = await api.getProfile();
      status = AuthStatus.authenticated;
      lastError = null;
      unawaited(_pushService.registerForPushNotifications());
      notifyListeners();
      return true;
    } on ApiException catch (e) {
      lastError = e.message;
      notifyListeners();
      return false;
    }
  }

  Future<void> logout() async {
    await api.logout();
    profile = null;
    status = AuthStatus.unauthenticated;
    notifyListeners();
  }

  Future<void> refreshProfile() async {
    profile = await api.getProfile();
    notifyListeners();
  }
}
