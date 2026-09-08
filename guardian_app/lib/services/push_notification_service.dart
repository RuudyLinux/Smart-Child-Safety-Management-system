import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'guardian_api_service.dart';

/// Registers this device for Firebase Cloud Messaging and sends the token to
/// ChildGuard.Api so the backend can push real notifications (see
/// FcmPushNotificationSender on the API side).
///
/// This only activates once the app has real Firebase project config —
/// `firebase_options.dart` generated via `flutterfire configure`, plus
/// google-services.json / GoogleService-Info.plist. No fake keys are shipped
/// here; without that config, every step below fails fast and is swallowed
/// so the app keeps working with in-app notifications only.
class PushNotificationService {
  final GuardianApiService _api;
  PushNotificationService(this._api);

  Future<void> registerForPushNotifications() async {
    try {
      await Firebase.initializeApp();
      final messaging = FirebaseMessaging.instance;
      await messaging.requestPermission();
      final token = await messaging.getToken();
      if (token != null) {
        await _api.registerDeviceToken(token);
      }
    } catch (e) {
      // No Firebase project configured yet, or platform config files missing —
      // expected in dev until `flutterfire configure` is run. Not fatal.
      debugPrint('Push notification registration skipped: $e');
    }
  }
}
