import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
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

/// Dashboard: today's safety summary, things that need attention, KPIs, each child's live status,
/// today's checkpoints, recent alerts and quick actions. One column on phones; a two-column dashboard
/// on wide screens.
class HomeTab extends StatefulWidget {
  const HomeTab({super.key});

  @override
  State<HomeTab> createState() => _HomeTabState();
}

class _HomeTabState extends State<HomeTab> {
  final _key = GlobalKey<CgAsyncState<GuardianDashboard>>();
  Key _alertsKey = UniqueKey();

  void _push(Widget screen) => Navigator.of(context).push(MaterialPageRoute(builder: (_) => screen)).then((_) {
        _key.currentState?.reload();
        if (mounted) setState(() => _alertsKey = UniqueKey());
      });

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;
    final compact = context.isCompact;

    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: CgAsync<GuardianDashboard>(
          key: _key,
          load: () => auth.api.getDashboard(),
          builder: (context, d, reload) {
            final avatar = CgAvatar(
              name: profile?.fullName ?? d.guardianName,
              photoUrl: (profile?.hasPhoto ?? false) ? auth.api.myPhotoUrl : null,
              headers: auth.photoHeaders,
              size: 36,
            );
            return CgPage(
              maxWidth: CgWidths.dashboard,
              top: compact ? CgSpace.sm : CgSpace.xl,
              onRefresh: () async {
                await Future.wait([reload(), auth.refreshProfile()]);
                if (mounted) setState(() => _alertsKey = UniqueKey());
              },
              children: [
                _Header(
                  compact: compact,
                  greeting: _greeting(d),
                  date: DateFormat('EEEE, d MMMM').format(d.today ?? DateTime.now()),
                  unread: d.unreadNotifications,
                  avatar: avatar,
                  onBell: () => _push(const NotificationsScreen()),
                  onAvatar: () => _push(const ProfileScreen()),
                ),
                CgFadeIn(child: _SummaryCard(dashboard: d, headline: _headline(d))),
                const SizedBox(height: CgSpace.md),
                if (profile != null && !profile.faceRegistered)
                  CgAlert(
                    tone: CgTone.warning,
                    title: 'Register your face',
                    message: 'Staff verify your face before handing over your child. Register it once from this device.',
                    action: TextButton.icon(
                      style: TextButton.styleFrom(padding: EdgeInsets.zero, foregroundColor: CgColors.warning),
                      onPressed: () => _push(const FaceRegistrationScreen()),
                      icon: const Icon(Icons.arrow_forward_rounded, size: 16),
                      label: const Text('Register now'),
                    ),
                  ),
                if (d.pendingPickups > 0)
                  CgAlert(
                    tone: CgTone.info,
                    icon: Icons.hourglass_top_rounded,
                    title: '${d.pendingPickups} pickup${d.pendingPickups == 1 ? '' : 's'} awaiting approval',
                    message: 'A face check matched at the school desk. Staff will confirm the handover shortly.',
                    action: TextButton.icon(
                      style: TextButton.styleFrom(padding: EdgeInsets.zero),
                      onPressed: () => _push(const PickupsScreen()),
                      icon: const Icon(Icons.arrow_forward_rounded, size: 16),
                      label: const Text('View pickup status'),
                    ),
                  ),
                CgFadeIn(
                  index: 1,
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
                const SizedBox(height: CgSpace.xl),
                CgSplit(
                  breakAt: 900,
                  primaryFlex: 3,
                  secondaryFlex: 2,
                  primary: [
                    CgSectionHeader('Your children (${d.children.length})'),
                    if (d.children.isEmpty)
                      const CgCard(
                        child: CgEmptyState(
                          icon: Icons.family_restroom_rounded,
                          title: 'No children linked yet',
                          message: 'Once the school links a child to your account, they will appear here.',
                        ),
                      )
                    else
                      Padding(
                        padding: const EdgeInsets.only(bottom: CgSpace.md),
                        child: CgGrid(
                          minItemWidth: 300,
                          maxColumns: 2,
                          gap: CgSpace.sm,
                          children: [
                            for (final s in d.children)
                              CgChildCard(
                                student: s,
                                photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                                photoHeaders: auth.photoHeaders,
                                onTap: () => _push(StudentDetailScreen(student: s)),
                              ),
                          ],
                        ),
                      ),
                    CgCard(
                      title: "Today's activity",
                      subtitle: 'Checkpoints recorded today',
                      trailing: TextButton(onPressed: () => _push(const JourneyScreen()), child: const Text('All journeys')),
                      child: d.recentEvents.isEmpty
                          ? const _InlineEmpty(icon: Icons.route_rounded, text: 'No checkpoints recorded yet today.')
                          : CgTimeline(CgFormat.timeline(d.recentEvents, withStudent: d.children.length > 1)),
                    ),
                  ],
                  secondary: [
                    const CgSectionHeader('Shortcuts'),
                    CgCard(
                      title: 'Quick actions',
                      padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.sm, 0),
                      child: Column(children: [
                        CgRow(
                          leading: const CgIconTile(icon: Icons.how_to_reg_rounded, color: CgColors.primaryDeep, background: CgColors.primarySoft, size: 36),
                          title: 'Pickup status',
                          subtitle: 'Every pickup attempt and its outcome',
                          onTap: () => _push(const PickupsScreen()),
                        ),
                        CgRow(
                          leading: const CgIconTile(icon: Icons.badge_rounded, color: CgColors.guardianText, background: CgColors.guardianSoft, size: 36),
                          title: 'Temporary pickup',
                          subtitle: 'Let someone else collect your child for up to 24 hours',
                          onTap: () => _push(const TemporaryPickupsScreen()),
                        ),
                        CgRow(
                          leading: const CgIconTile(icon: Icons.insights_rounded, color: CgColors.successText, background: CgColors.successSoft, size: 36),
                          title: 'Attendance reports',
                          subtitle: 'Days present, late arrivals and bus trips',
                          onTap: () => _push(const ReportsScreen()),
                        ),
                        CgRow(
                          leading: const CgIconTile(icon: Icons.verified_user_rounded, color: CgColors.infoText, background: CgColors.infoSoft, size: 36),
                          title: 'How pickup verification works',
                          subtitle: 'What happens at the school desk',
                          divider: false,
                          onTap: () => _push(const PickupVerificationScreen()),
                        ),
                      ]),
                    ),
                    _RecentAlerts(key: _alertsKey, unread: d.unreadNotifications, onOpen: () => _push(const NotificationsScreen())),
                  ],
                ),
              ],
            );
          },
        ),
      ),
    );
  }

  String _greeting(GuardianDashboard d) {
    final hour = DateTime.now().hour;
    final greeting = hour < 12 ? 'Good morning' : (hour < 17 ? 'Good afternoon' : 'Good evening');
    final first = d.guardianName.split(' ').first;
    return first.isEmpty ? greeting : '$greeting, $first';
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

/// Top of the dashboard. Phones: brand, bell and avatar, then the greeting. Wider: greeting with the
/// bell and avatar beside it (the sidebar already carries the brand).
class _Header extends StatelessWidget {
  final bool compact;
  final String greeting;
  final String date;
  final int unread;
  final Widget avatar;
  final VoidCallback onBell;
  final VoidCallback onAvatar;
  const _Header({
    required this.compact,
    required this.greeting,
    required this.date,
    required this.unread,
    required this.avatar,
    required this.onBell,
    required this.onAvatar,
  });

  @override
  Widget build(BuildContext context) {
    final actions = [
      IconButton(
        tooltip: unread > 0 ? '$unread unread alerts' : 'Alerts',
        onPressed: onBell,
        icon: Badge(
          isLabelVisible: unread > 0,
          label: Text(unread > 9 ? '9+' : '$unread'),
          backgroundColor: CgColors.error,
          child: const Icon(Icons.notifications_outlined),
        ),
      ),
      const SizedBox(width: 4),
      Semantics(
        button: true,
        label: 'Profile',
        child: MouseRegion(cursor: SystemMouseCursors.click, child: GestureDetector(onTap: onAvatar, child: avatar)),
      ),
    ];
    final titles = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(greeting, style: compact ? CgText.h1 : CgText.display),
        const SizedBox(height: 2),
        Text("$date · here is your children's day so far", style: CgText.small),
      ],
    );

    if (compact) {
      return Padding(
        padding: const EdgeInsets.only(bottom: CgSpace.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(children: [const CgBrandLockup(markSize: 32), const Spacer(), ...actions]),
            const SizedBox(height: CgSpace.lg),
            titles,
          ],
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: CgSpace.xl),
      child: Row(children: [Expanded(child: titles), ...actions]),
    );
  }
}

/// Today's headline status with the three counts that matter.
class _SummaryCard extends StatelessWidget {
  final GuardianDashboard dashboard;
  final (CgTone, String, String) headline;
  const _SummaryCard({required this.dashboard, required this.headline});

  @override
  Widget build(BuildContext context) {
    final (tone, label, text) = headline;
    final c = CgToneColors.of(tone);
    final icon = switch (tone) {
      CgTone.success => Icons.verified_user_rounded,
      CgTone.warning => Icons.priority_high_rounded,
      CgTone.info => Icons.directions_bus_rounded,
      _ => Icons.shield_outlined,
    };
    final stats = [
      ('Children', '${dashboard.childrenCount}'),
      ('At school', '${dashboard.atSchool}'),
      ('Home / left', '${dashboard.leftSchoolOrHome}'),
    ];

    return CgGlass(
      padding: const EdgeInsets.all(CgSpace.lg),
      child: LayoutBuilder(
        builder: (context, box) {
          final status = Row(
            children: [
              Container(
                width: 48,
                height: 48,
                decoration: BoxDecoration(color: c.soft, borderRadius: BorderRadius.circular(CgRadius.lg), border: Border.all(color: c.border)),
                child: Icon(icon, color: c.fg, size: 24),
              ),
              const SizedBox(width: CgSpace.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label.toUpperCase(), style: CgText.overline.copyWith(color: c.fg)),
                    const SizedBox(height: 2),
                    Text(text, style: CgText.h2),
                  ],
                ),
              ),
            ],
          );
          final figures = Row(
            mainAxisSize: box.maxWidth >= 640 ? MainAxisSize.min : MainAxisSize.max,
            children: [
              for (final (i, s) in stats.indexed) ...[
                if (i > 0) Container(width: 1, height: 34, color: CgColors.border, margin: const EdgeInsets.symmetric(horizontal: CgSpace.md)),
                box.maxWidth >= 640 ? _Figure(label: s.$1, value: s.$2) : Expanded(child: _Figure(label: s.$1, value: s.$2)),
              ],
            ],
          );
          if (box.maxWidth >= 640) {
            return Row(children: [Expanded(child: status), const SizedBox(width: CgSpace.lg), figures]);
          }
          return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            status,
            const Padding(padding: EdgeInsets.symmetric(vertical: CgSpace.md), child: Divider()),
            figures,
          ]);
        },
      ),
    );
  }
}

class _Figure extends StatelessWidget {
  final String label;
  final String value;
  const _Figure({required this.label, required this.value});

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(value, style: CgText.metric.copyWith(fontSize: 22)),
          Text(label, style: CgText.caption),
        ],
      );
}

/// The latest few alerts, newest first, with a link to all of them.
class _RecentAlerts extends StatefulWidget {
  final int unread;
  final VoidCallback onOpen;
  const _RecentAlerts({required this.unread, required this.onOpen, super.key});

  @override
  State<_RecentAlerts> createState() => _RecentAlertsState();
}

class _RecentAlertsState extends State<_RecentAlerts> {
  // Fetched once per instance; the dashboard gives this widget a new key to refresh it.
  late final Future<List<AppNotification>> _future = context.read<AuthProvider>().api.getNotifications();

  @override
  Widget build(BuildContext context) {
    return CgCard(
      title: 'Recent alerts',
      subtitle: widget.unread > 0 ? '${widget.unread} unread' : 'From the school',
      trailing: TextButton(onPressed: widget.onOpen, child: const Text('View all')),
      padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.md, 0),
      child: FutureBuilder<List<AppNotification>>(
        future: _future,
        builder: (context, snap) {
          if (snap.connectionState != ConnectionState.done) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: CgSpace.lg),
              child: Center(child: SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2))),
            );
          }
          final items = (snap.data ?? const <AppNotification>[]).take(4).toList();
          if (snap.hasError) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: CgSpace.md),
              child: _InlineEmpty(icon: Icons.cloud_off_rounded, text: 'Alerts could not be loaded right now.'),
            );
          }
          if (items.isEmpty) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: CgSpace.md),
              child: _InlineEmpty(icon: Icons.notifications_none_rounded, text: 'No alerts yet.'),
            );
          }
          return Column(children: [
            for (final (i, n) in items.indexed)
              CgRow(
                divider: i != items.length - 1,
                leading: Container(
                  width: 8,
                  height: 8,
                  decoration: BoxDecoration(color: n.isRead ? CgColors.border : CgColors.primary, shape: BoxShape.circle),
                ),
                title: n.title,
                subtitle: '${n.message}\n${DateFormat('d MMM, h:mm a').format(n.sentAt.toLocal())}',
              ),
          ]);
        },
      ),
    );
  }
}

/// One-line muted placeholder inside a card.
class _InlineEmpty extends StatelessWidget {
  final IconData icon;
  final String text;
  const _InlineEmpty({required this.icon, required this.text});

  @override
  Widget build(BuildContext context) => Row(children: [
        Icon(icon, size: 18, color: CgColors.textMuted2),
        const SizedBox(width: CgSpace.xs),
        Expanded(child: Text(text, style: CgText.small)),
      ]);
}
