import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/theme.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import '../face/face_registration_screen.dart';
import '../journey/journey_screen.dart';
import '../notifications/notifications_screen.dart';
import '../pickup/pickup_verification_screen.dart';
import '../pickup/pickups_screen.dart';
import '../profile/profile_screen.dart';
import '../reports/reports_screen.dart';
import '../temporary/temporary_pickups_screen.dart';
import 'student_detail_screen.dart';

/// Dashboard — the guardian's version of the Admin Web dashboard: hero with today's headline,
/// KPI tiles, quick actions, each child's live status and today's checkpoints.
class HomeTab extends StatefulWidget {
  const HomeTab({super.key});

  @override
  State<HomeTab> createState() => _HomeTabState();
}

class _HomeTabState extends State<HomeTab> {
  final _key = GlobalKey<CgAsyncState<GuardianDashboard>>();

  void _push(Widget screen) =>
      Navigator.of(context).push(MaterialPageRoute(builder: (_) => screen)).then((_) => _key.currentState?.reload());

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;

    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: CgAsync<GuardianDashboard>(
          key: _key,
          load: () => auth.api.getDashboard(),
          builder: (context, d, reload) => RefreshIndicator(
            onRefresh: () async {
              await Future.wait([reload(), auth.refreshProfile()]);
            },
            child: ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 110),
              children: [
                _TopBar(
                  unread: d.unreadNotifications,
                  avatar: CgAvatar(
                    name: profile?.fullName ?? d.guardianName,
                    photoUrl: (profile?.hasPhoto ?? false) ? auth.api.myPhotoUrl : null,
                    headers: auth.photoHeaders,
                    size: 38,
                  ),
                  onBell: () => _push(const NotificationsScreen()),
                  onAvatar: () => _push(const ProfileScreen()),
                ),
                const SizedBox(height: 12),
                CgFadeIn(child: _hero(d)),
                const SizedBox(height: 16),
                if (profile != null && !profile.faceRegistered)
                  CgFadeIn(
                    index: 1,
                    child: CgAlert(
                      tone: CgTone.warning,
                      title: 'Register your face',
                      message: 'Staff verify your face before handing over your child. Register it once from this phone.',
                      action: OutlinedButton.icon(
                        onPressed: () => _push(const FaceRegistrationScreen()),
                        icon: const Icon(Icons.face_retouching_natural_rounded, size: 18),
                        label: const Text('Register now'),
                      ),
                    ),
                  ),
                if (d.pendingPickups > 0)
                  CgFadeIn(
                    index: 1,
                    child: CgAlert(
                      tone: CgTone.info,
                      icon: Icons.hourglass_top_rounded,
                      title: '${d.pendingPickups} pickup${d.pendingPickups == 1 ? '' : 's'} awaiting approval',
                      message: 'A face check matched at the school desk. Staff will confirm the handover shortly.',
                      action: TextButton(onPressed: () => _push(const PickupsScreen()), child: const Text('View pickup status')),
                    ),
                  ),
                CgFadeIn(
                  index: 2,
                  child: CgKpiGrid(children: [
                    CgKpi(
                      label: 'Present today',
                      value: '${d.presentToday}',
                      unit: 'of ${d.childrenCount}',
                      icon: Icons.event_available_rounded,
                      tone: CgTone.success,
                      note: d.lateToday > 0 ? '${d.lateToday} arrived late' : 'Checked in at school',
                    ),
                    CgKpi(
                      label: 'At school now',
                      value: '${d.atSchool}',
                      icon: Icons.verified_user_rounded,
                      tone: CgTone.primary,
                      note: 'Checked in, not departed',
                    ),
                    CgKpi(
                      label: 'On the bus',
                      value: '${d.onBus}',
                      icon: Icons.directions_bus_rounded,
                      tone: CgTone.info,
                      note: 'Boarded, not yet arrived',
                    ),
                    CgKpi(
                      label: 'Pickups pending',
                      value: '${d.pendingPickups}',
                      icon: Icons.how_to_reg_rounded,
                      tone: d.pendingPickups > 0 ? CgTone.warning : CgTone.neutral,
                      note: 'Tap for pickup status',
                      onTap: () => _push(const PickupsScreen()),
                    ),
                  ]),
                ),
                const SizedBox(height: 18),
                CgFadeIn(
                  index: 3,
                  child: CgCard(
                    title: 'Quick actions',
                    padding: const EdgeInsets.fromLTRB(16, 2, 12, 2),
                    child: Column(children: [
                      CgRow(
                        leading: const CgIconTile(icon: Icons.how_to_reg_rounded, color: CgColors.primaryDeep, background: CgColors.primarySoft, size: 38),
                        title: 'Pickup status',
                        subtitle: 'Every pickup attempt and its outcome',
                        onTap: () => _push(const PickupsScreen()),
                      ),
                      CgRow(
                        leading: const CgIconTile(icon: Icons.badge_rounded, color: CgColors.guardianText, background: CgColors.guardianSoft, size: 38),
                        title: 'Temporary pickup',
                        subtitle: 'Let someone else collect your child for up to 24 hours',
                        onTap: () => _push(const TemporaryPickupsScreen()),
                      ),
                      CgRow(
                        leading: const CgIconTile(icon: Icons.insights_rounded, color: CgColors.successText, background: CgColors.successSoft, size: 38),
                        title: 'Attendance reports',
                        subtitle: 'Days present, late arrivals and bus trips',
                        onTap: () => _push(const ReportsScreen()),
                      ),
                      CgRow(
                        leading: const CgIconTile(icon: Icons.verified_user_rounded, color: CgColors.infoText, background: CgColors.infoSoft, size: 38),
                        title: 'How pickup verification works',
                        subtitle: 'What happens at the school desk',
                        divider: false,
                        onTap: () => _push(const PickupVerificationScreen()),
                      ),
                    ]),
                  ),
                ),
                CgSectionHeader('Your children (${d.children.length})'),
                if (d.children.isEmpty)
                  const Padding(
                    padding: EdgeInsets.symmetric(vertical: 16),
                    child: CgEmptyState(
                      icon: Icons.family_restroom_rounded,
                      title: 'No children linked yet',
                      message: 'Once the school links a child to your account, they will appear here.',
                    ),
                  )
                else
                  for (final (i, s) in d.children.indexed)
                    CgFadeIn(
                      index: 4 + i,
                      child: Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: CgChildCard(
                          student: s,
                          photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                          photoHeaders: auth.photoHeaders,
                          onTap: () => _push(StudentDetailScreen(student: s)),
                        ),
                      ),
                    ),
                const SizedBox(height: 6),
                CgCard(
                  title: "Today's activity",
                  subtitle: 'Checkpoints recorded today',
                  trailing: TextButton(onPressed: () => _push(const JourneyScreen()), child: const Text('All journeys')),
                  child: d.recentEvents.isEmpty
                      ? const Padding(
                          padding: EdgeInsets.symmetric(vertical: 8),
                          child: Text('No checkpoints recorded yet today.', style: TextStyle(color: CgColors.textMuted)),
                        )
                      : CgTimeline(CgFormat.timeline(d.recentEvents, withStudent: d.children.length > 1)),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _hero(GuardianDashboard d) {
    final hour = DateTime.now().hour;
    final greeting = hour < 12 ? 'Good morning' : (hour < 17 ? 'Good afternoon' : 'Good evening');
    final first = d.guardianName.split(' ').first;
    final date = DateFormat('EEEE, d MMMM').format(d.today ?? DateTime.now());

    final (tone, label, headline) = _headline(d);
    return CgHero(
      greeting: first.isEmpty ? greeting : '$greeting, $first',
      subtitle: "$date — here is your children's day so far.",
      stats: [
        ('Children', '${d.childrenCount}'),
        ('At school', '${d.atSchool}'),
        ('Home / left', '${d.leftSchoolOrHome}'),
      ],
      status: CgStatusPill(tone: tone, label: label, headline: headline),
    );
  }

  (CgTone, String, String) _headline(GuardianDashboard d) {
    if (d.children.isEmpty) return (CgTone.neutral, 'Nothing to report', 'No children linked');
    if (d.pendingPickups > 0) return (CgTone.warning, 'Needs your attention', 'Pickup awaiting approval');
    final quiet = d.children.where((c) => c.safetyStatus == CgSafetyStatus.noActivity).length;
    if (quiet > 0 && DateTime.now().hour >= 10) {
      return (CgTone.warning, 'Check with the school', '$quiet with no activity today');
    }
    final safe = d.children.every((c) => c.safetyStatus == CgSafetyStatus.atSchool || c.safetyStatus == CgSafetyStatus.droppedOff);
    if (safe) return (CgTone.success, 'Live status', 'All children accounted for');
    return (CgTone.info, 'Live status', 'Journey in progress');
  }
}

class _TopBar extends StatelessWidget {
  final int unread;
  final Widget avatar;
  final VoidCallback onBell;
  final VoidCallback onAvatar;
  const _TopBar({required this.unread, required this.avatar, required this.onBell, required this.onAvatar});

  @override
  Widget build(BuildContext context) => Row(
        children: [
          const CgBrandMark(size: 36),
          const SizedBox(width: 10),
          const Expanded(
            child: Text('ChildGuard', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 18, letterSpacing: -0.4, color: CgColors.text)),
          ),
          IconButton(
            tooltip: unread > 0 ? '$unread unread alerts' : 'Alerts',
            onPressed: onBell,
            icon: Badge(
              isLabelVisible: unread > 0,
              label: Text(unread > 9 ? '9+' : '$unread'),
              backgroundColor: CgColors.error,
              child: const Icon(Icons.notifications_outlined, color: CgColors.text2),
            ),
          ),
          const SizedBox(width: 2),
          Semantics(button: true, label: 'Profile', child: GestureDetector(onTap: onAvatar, child: avatar)),
        ],
      );
}
