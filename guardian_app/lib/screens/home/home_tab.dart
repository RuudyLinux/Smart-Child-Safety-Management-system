import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_states.dart';
import '../face/face_registration_screen.dart';
import '../pickup/pickup_verification_screen.dart';
import '../profile/profile_screen.dart';
import 'student_detail_screen.dart';

/// "I want to know my child is safe" — the first thing a guardian sees.
class HomeTab extends StatelessWidget {
  const HomeTab({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;

    final hour = DateTime.now().hour;
    final greeting = hour < 12 ? 'Good morning' : (hour < 17 ? 'Good afternoon' : 'Good evening');
    final firstName = (profile?.fullName ?? '').split(' ').firstOrNull ?? '';

    return Scaffold(
      body: profile == null
          ? const CgLoadingState()
          : RefreshIndicator(
              onRefresh: () => context.read<AuthProvider>().refreshProfile(),
              child: CustomScrollView(
                physics: const AlwaysScrollableScrollPhysics(),
                slivers: [
                  SliverAppBar(
                    pinned: false,
                    floating: true,
                    backgroundColor: CgColors.background,
                    elevation: 0,
                    toolbarHeight: 64,
                    title: Text('$greeting${firstName.isEmpty ? '' : ', $firstName'}',
                        style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18)),
                    actions: [
                      IconButton(
                        icon: CgAvatar(
                          name: profile.fullName,
                          photoUrl: profile.hasPhoto ? auth.api.myPhotoUrl : null,
                          headers: auth.photoHeaders,
                          size: 34,
                        ),
                        onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ProfileScreen())),
                      ),
                      const SizedBox(width: 6),
                    ],
                  ),
                  SliverPadding(
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
                    sliver: SliverList(
                      delegate: SliverChildListDelegate([
                        _SafetyBanner(profile: profile),
                        const SizedBox(height: 20),
                        if (!profile.faceRegistered)
                          _ActionCard(
                            icon: Icons.face_retouching_natural_rounded,
                            iconBg: CgColors.warningSoft,
                            iconColor: CgColors.warning,
                            title: 'Register your face',
                            subtitle: 'Required before you can be verified for student pickup.',
                            onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const FaceRegistrationScreen())),
                          ),
                        _ActionCard(
                          icon: Icons.verified_user_rounded,
                          iconBg: CgColors.primarySoft,
                          iconColor: CgColors.primary,
                          title: 'Pickup verification',
                          subtitle: 'See how you will be verified to collect your child.',
                          onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const PickupVerificationScreen())),
                        ),
                        const SizedBox(height: 12),
                        CgSectionHeader('Your children (${profile.students.length})'),
                        if (profile.students.isEmpty)
                          const Padding(
                            padding: EdgeInsets.symmetric(vertical: 24),
                            child: CgEmptyState(
                              icon: Icons.family_restroom_rounded,
                              title: 'No children linked yet',
                              message: 'Once a school administrator links a child to your account, they will appear here.',
                            ),
                          )
                        else
                          ...profile.students.map((s) => Padding(
                                padding: const EdgeInsets.only(bottom: 10),
                                child: CgChildCard(
                                  student: s,
                                  photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                                  photoHeaders: auth.photoHeaders,
                                  onTap: () => Navigator.of(context)
                                      .push(MaterialPageRoute(builder: (_) => StudentDetailScreen(student: s))),
                                ),
                              )),
                      ]),
                    ),
                  ),
                ],
              ),
            ),
    );
  }
}

class _SafetyBanner extends StatelessWidget {
  final GuardianProfile profile;
  const _SafetyBanner({required this.profile});

  @override
  Widget build(BuildContext context) {
    final hasChildren = profile.students.isNotEmpty;
    final allSafe = hasChildren && profile.allChildrenSafe;
    final needsAttention = hasChildren &&
        profile.students.any((s) => s.safetyStatus == CgSafetyStatus.noActivity && DateTime.now().hour >= 10);

    final Color bg;
    final Color fg;
    final IconData icon;
    final String headline;
    final String detail;

    if (!hasChildren) {
      bg = CgColors.surface2;
      fg = CgColors.textMuted;
      icon = Icons.family_restroom_rounded;
      headline = 'No children linked yet';
      detail = 'Nothing to report until a child is linked to your account.';
    } else if (allSafe) {
      bg = CgColors.successSoft;
      fg = CgColors.success;
      icon = Icons.verified_rounded;
      headline = 'All children are safe';
      detail = '${profile.students.length} of ${profile.students.length} accounted for right now.';
    } else if (needsAttention) {
      bg = CgColors.warningSoft;
      fg = CgColors.warning;
      icon = Icons.error_outline_rounded;
      headline = 'Attention needed';
      detail = 'One or more children have no activity recorded today.';
    } else {
      bg = CgColors.infoSoft;
      fg = CgColors.info;
      icon = Icons.schedule_rounded;
      headline = 'Journey in progress';
      detail = "Your children's day is still underway.";
    }

    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          Container(
            width: 46,
            height: 46,
            decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.6), shape: BoxShape.circle),
            child: Icon(icon, color: fg, size: 24),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(headline, style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16, color: fg)),
                const SizedBox(height: 3),
                Text(detail, style: TextStyle(color: fg.withValues(alpha: 0.85), fontSize: 12.5, height: 1.4)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ActionCard extends StatelessWidget {
  final IconData icon;
  final Color iconBg;
  final Color iconColor;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  const _ActionCard({
    required this.icon,
    required this.iconBg,
    required this.iconColor,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: Card(
          child: ListTile(
            contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
            leading: Container(
              width: 42,
              height: 42,
              decoration: BoxDecoration(color: iconBg, borderRadius: BorderRadius.circular(12)),
              child: Icon(icon, color: iconColor, size: 22),
            ),
            title: Text(title, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14.5)),
            subtitle: Text(subtitle, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted)),
            trailing: const Icon(Icons.chevron_right_rounded, color: CgColors.textMuted2),
            onTap: onTap,
          ),
        ),
      );
}

extension _FirstOrNull<T> on List<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
