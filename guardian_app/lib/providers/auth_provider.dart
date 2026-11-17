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

  /// Open OTP challenge for the sign-in attempt in progress, if any.
  OtpChallenge? challenge;

  /// Ticket that authorises the mandatory first-login password reset. Issued by
  /// the server after OTP verification — the app never decides this itself.
  String? resetTicket;

  late final PushNotificationService _pushService;

  AuthProvider({ApiClient? client}) : client = client ?? ApiClient() {
    api = GuardianApiService(this.client);
    _pushService = PushNotificationService(api);
    this.client.onUnauthorized = _handleUnauthorized;
  }

  /// The API already cleared the token (expired, or the account was
  /// deactivated) — drop the stale profile and send the guardian back to
  /// login instead of leaving every screen stuck retrying a 401.
  void _handleUnauthorized() {
    if (status != AuthStatus.authenticated) return;
    profile = null;
    challenge = null;
    resetTicket = null;
    status = AuthStatus.unauthenticated;
    lastError = 'Your session has expired. Please sign in again.';
    notifyListeners();
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
        unawaited(api.enrolTrustedDeviceIfNeeded());
      } catch (_) {
        await client.setToken(null);
        status = AuthStatus.unauthenticated;
      }
    }
    notifyListeners();
  }

  /// Step 1: validate credentials. Usually raises an OTP challenge (no session yet). On a phone the server
  /// already trusts it finishes here instead: authenticated, or a first-login reset ticket. Returns the stage
  /// the server chose, or null when the attempt failed (the reason is in [lastError]).
  Future<GuardianAuthStage?> login(String contactNumber, String password) async {
    GuardianAuthStage? stage;
    await _guard(() async {
      final result = await api.login(contactNumber, password);
      stage = result.stage;
      resetTicket = null;
      challenge = null;
      switch (result.stage) {
        case GuardianAuthStage.otpRequired:
          challenge = result.challenge;
        case GuardianAuthStage.passwordResetRequired:
          resetTicket = result.auth?.resetTicket;
        case GuardianAuthStage.authenticated:
          profile = await api.getProfile();
          status = AuthStatus.authenticated;
          unawaited(_pushService.registerForPushNotifications());
      }
      return true;
    });
    return stage;
  }

  /// Step 2: verify the code. Returns the stage the server put the flow in.
  Future<GuardianAuthStage?> verifyOtp(String code) async {
    final token = challenge?.challengeToken;
    if (token == null) {
      lastError = 'Your sign-in session has expired. Please sign in again.';
      notifyListeners();
      return null;
    }

    GuardianAuthStage? stage;
    await _guard(() async {
      final result = await api.verifyOtp(token, code);
      stage = result.stage;
      if (result.stage == GuardianAuthStage.authenticated) {
        challenge = null;
        resetTicket = null;
        profile = await api.getProfile();
        status = AuthStatus.authenticated;
        unawaited(_pushService.registerForPushNotifications());
      } else if (result.stage == GuardianAuthStage.passwordResetRequired) {
        challenge = null;
        resetTicket = result.resetTicket;
      }
      return true;
    });
    return stage;
  }

  Future<bool> resendOtp() async {
    final token = challenge?.challengeToken;
    if (token == null) {
      lastError = 'Your sign-in session has expired. Please sign in again.';
      notifyListeners();
      return false;
    }
    return _guard(() async {
      challenge = await api.resendOtp(token);
      return true;
    });
  }

  /// Completes the forced first-login reset. The Guardian then signs in again.
  Future<bool> completeFirstLoginReset(String newPassword, String confirmPassword) async {
    final ticket = resetTicket;
    if (ticket == null) {
      lastError = 'Your reset session has expired. Please sign in again.';
      notifyListeners();
      return false;
    }
    return _guard(() async {
      await api.resetFirstLoginPassword(ticket, newPassword, confirmPassword);
      resetTicket = null;
      challenge = null;
      return true;
    });
  }

  /// Drops any half-finished sign-in state (back button, abandoned OTP step).
  void clearAuthFlow() {
    challenge = null;
    resetTicket = null;
    lastError = null;
    notifyListeners();
  }

  Future<void> logout() async {
    await api.logout();
    profile = null;
    challenge = null;
    resetTicket = null;
    status = AuthStatus.unauthenticated;
    notifyListeners();
  }

  /// Auth header for authenticated `Image.network` photo requests.
  Map<String, String> get photoHeaders => client.cachedAuthHeaders;

  Future<void> refreshProfile() async {
    try {
      profile = await api.getProfile();
      lastError = null;
    } on ApiException catch (e) {
      lastError = e.message;
    } catch (_) {
      lastError = 'Could not reach server. Check your connection and try again.';
    }
    notifyListeners();
  }

  Future<bool> _guard(Future<bool> Function() action) async {
    try {
      final ok = await action();
      lastError = null;
      notifyListeners();
      return ok;
    } on ApiException catch (e) {
      lastError = e.message;
      notifyListeners();
      return false;
    } catch (_) {
      lastError = 'Could not reach server. Check your connection and try again.';
      notifyListeners();
      return false;
    }
  }
}
