import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/responsive.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_ui.dart';
import '../face/face_registration_screen.dart';

/// Guardian-side pickup verification info screen. The actual RFID scan and
/// live face-verification capture happen at the school's operational
/// verification desk (Admin Web demo page) — this screen explains the
/// process and confirms the guardian's own readiness (face registered).
class PickupVerificationScreen extends StatelessWidget {
  const PickupVerificationScreen({super.key});

  static const _steps = [
    (Icons.contactless_rounded, 'Your child is identified', 'The student is identified by their RFID card at school.'),
    (Icons.how_to_reg_rounded, 'Your authorisation is checked', 'Staff confirm you are an authorised guardian for that student.'),
    (Icons.face_retouching_natural_rounded, 'Your face is compared', 'Your face is captured and compared with your registered face.'),
    (Icons.verified_rounded, 'Staff approve the pickup', 'On a successful match, staff approve the handover.'),
    (Icons.notifications_active_outlined, 'You are notified', 'You receive a notification once the pickup is recorded.'),
  ];

  @override
  Widget build(BuildContext context) {
    final profile = context.watch<AuthProvider>().profile;
    final registered = profile?.faceRegistered ?? false;

    return Scaffold(
      appBar: AppBar(title: const Text('Pickup verification')),
      body: CgPage(
        maxWidth: CgWidths.reading,
        children: [
          const CgPageHead(title: 'How pickup verification works', subtitle: 'What happens at the school desk when you collect your child.'),
          CgAlert(
            tone: registered ? CgTone.success : CgTone.warning,
            title: registered ? 'Your face is registered' : 'Face not registered yet',
            message: registered ? 'You can be verified for pickup at school.' : 'Register your face so staff can verify you at pickup time.',
            action: registered
                ? null
                : ElevatedButton(
                    onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const FaceRegistrationScreen())),
                    child: const Text('Register face now'),
                  ),
          ),
          CgCard(
            title: 'The steps',
            child: CgTimeline([
              for (final (i, s) in _steps.indexed)
                CgTimelineItem(title: '${i + 1}. ${s.$2}', body: s.$3, icon: s.$1, tone: CgTone.primary),
            ]),
          ),
        ],
      ),
    );
  }
}
