import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../face/face_registration_screen.dart';

/// Guardian-side pickup verification info screen. The actual RFID scan and
/// live face-verification capture happen at the school's operational
/// verification desk (Admin Web demo page) — this screen explains the
/// process and confirms the guardian's own readiness (face registered).
class PickupVerificationScreen extends StatelessWidget {
  const PickupVerificationScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final profile = context.watch<AuthProvider>().profile;
    final registered = profile?.faceRegistered ?? false;

    return Scaffold(
      appBar: AppBar(title: const Text('Pickup Verification')),
      body: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(children: [
                    Icon(registered ? Icons.check_circle : Icons.error_outline, color: registered ? CgColors.success : CgColors.warning),
                    const SizedBox(width: 8),
                    Text(registered ? 'Your face is registered' : 'Face not registered yet', style: const TextStyle(fontWeight: FontWeight.w600)),
                  ]),
                  const SizedBox(height: 8),
                  Text(
                    registered
                        ? 'You can be verified for pickup at school.'
                        : 'Register your face so staff can verify you at pickup time.',
                    style: const TextStyle(color: CgColors.textMuted),
                  ),
                  if (!registered) ...[
                    const SizedBox(height: 12),
                    ElevatedButton(
                      onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const FaceRegistrationScreen())),
                      child: const Text('Register Face Now'),
                    ),
                  ],
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          const Text('How pickup verification works', style: TextStyle(fontWeight: FontWeight.w700, color: CgColors.text)),
          const SizedBox(height: 8),
          _step('1', 'Student is identified via RFID at school.'),
          _step('2', 'Staff confirms you are an authorized guardian for that student.'),
          _step('3', 'Your face is captured and compared to your registered face.'),
          _step('4', 'On a successful match, staff approves the pickup.'),
          _step('5', 'You receive a notification once the pickup is recorded.'),
        ],
      ),
    );
  }

  Widget _step(String n, String text) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CircleAvatar(radius: 12, backgroundColor: CgColors.primaryLight, child: Text(n, style: const TextStyle(fontSize: 12, color: CgColors.primary, fontWeight: FontWeight.w700))),
            const SizedBox(width: 10),
            Expanded(child: Text(text)),
          ],
        ),
      );
}
