import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../face/face_registration_screen.dart';
import '../pickup/pickup_verification_screen.dart';
import '../profile/profile_screen.dart';
import 'student_detail_screen.dart';

class HomeTab extends StatelessWidget {
  const HomeTab({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;

    return Scaffold(
      appBar: AppBar(
        title: const Text('ChildGuard'),
        actions: [
          IconButton(
            icon: const Icon(Icons.person_outline),
            onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ProfileScreen())),
          ),
        ],
      ),
      body: profile == null
          ? const Center(child: CircularProgressIndicator())
          : RefreshIndicator(
              onRefresh: () => context.read<AuthProvider>().refreshProfile(),
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  Text('Welcome, ${profile.fullName.split(' ').first}', style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700, color: CgColors.text)),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      profile.faceRegistered ? CgBadge.success('Face Registered') : CgBadge.warning('Face Not Registered'),
                    ],
                  ),
                  const SizedBox(height: 16),
                  if (!profile.faceRegistered)
                    Card(
                      child: ListTile(
                        leading: const Icon(Icons.face_retouching_natural, color: CgColors.primary),
                        title: const Text('Register your face'),
                        subtitle: const Text('Required before you can be verified for student pickup.'),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const FaceRegistrationScreen())),
                      ),
                    ),
                  Card(
                    child: ListTile(
                      leading: const Icon(Icons.verified_user_outlined, color: CgColors.primary),
                      title: const Text('Pickup Verification'),
                      subtitle: const Text('View your recent pickup verification history.'),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const PickupVerificationScreen())),
                    ),
                  ),
                  const SizedBox(height: 12),
                  const Text('Your Students', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: CgColors.text)),
                  const SizedBox(height: 8),
                  if (profile.students.isEmpty)
                    const Padding(padding: EdgeInsets.all(12), child: Text('No students linked to your account yet.', style: TextStyle(color: CgColors.textMuted))),
                  ...profile.students.map((s) => Card(
                        child: ListTile(
                          leading: const CircleAvatar(backgroundColor: CgColors.primaryLight, child: Icon(Icons.child_care, color: CgColors.primary)),
                          title: Text(s.fullName, style: const TextStyle(fontWeight: FontWeight.w600)),
                          subtitle: Text('${s.grade ?? "—"} · ${s.relationship ?? "Guardian"}'),
                          trailing: s.isAuthorizedForPickup ? CgBadge.success('Pickup OK') : CgBadge.neutral('View'),
                          onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => StudentDetailScreen(student: s))),
                        ),
                      )),
                ],
              ),
            ),
    );
  }
}
