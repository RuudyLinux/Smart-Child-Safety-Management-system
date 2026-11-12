import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import '../reports/reports_screen.dart';
import '../temporary/temporary_pickups_screen.dart';
import '../transport/transport_screen.dart';

/// Student details — mirrors the Admin Web student profile: identity header, live status, attendance
/// KPIs, transport, linked guardians, and Attendance / Journey tabs.
class StudentDetailScreen extends StatefulWidget {
  final StudentSummary student;
  const StudentDetailScreen({required this.student, super.key});

  @override
  State<StudentDetailScreen> createState() => _StudentDetailScreenState();
}

class _StudentDetailScreenState extends State<StudentDetailScreen> {
  StudentDetail? _detail;
  List<JourneyEvent>? _journey;
  List<AttendanceRecord>? _attendance;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = _detail == null;
      _error = null;
    });
    try {
      final api = context.read<AuthProvider>().api;
      final id = widget.student.studentId;
      final r = await Future.wait([api.getStudentDetail(id), api.getJourney(id), api.getAttendance(id)]);
      if (mounted) {
        setState(() {
          _detail = r[0] as StudentDetail;
          _journey = r[1] as List<JourneyEvent>;
          _attendance = r[2] as List<AttendanceRecord>;
          _loading = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() { _error = "Could not load this child's record."; _loading = false; });
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthProvider>();
    final s = _detail?.child ?? widget.student;

    return DefaultTabController(
      length: 3,
      child: Scaffold(
        body: NestedScrollView(
          headerSliverBuilder: (context, _) => [
            SliverAppBar(
              pinned: true,
              backgroundColor: CgColors.glassStrong,
              surfaceTintColor: Colors.transparent,
              expandedHeight: 236,
              title: Text(s.fullName, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
              flexibleSpace: FlexibleSpaceBar(
                background: Container(
                  padding: const EdgeInsets.fromLTRB(20, 76, 20, 58),
                  decoration: const BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [Color(0xF2DCEBFF), Color(0xD9FFFFFF), Color(0xF2EFEDFD)],
                    ),
                  ),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      DecoratedBox(
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(24),
                          border: Border.all(color: Colors.white, width: 3),
                          boxShadow: CgShadows.md,
                        ),
                        child: CgAvatar(
                          name: s.fullName,
                          photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                          headers: auth.photoHeaders,
                          size: 84,
                          square: true,
                          statusDot: CgSafety.color(s.safetyStatus),
                        ),
                      ),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisAlignment: MainAxisAlignment.end,
                          children: [
                            Text(s.fullName,
                                maxLines: 2,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w800, letterSpacing: -0.4)),
                            const SizedBox(height: 6),
                            Wrap(spacing: 6, runSpacing: 6, children: [
                              CgBadge.safety(s.safetyStatus),
                              if (s.grade != null) CgBadge.neutral('Grade ${s.grade}'),
                            ]),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              bottom: const TabBar(
                labelColor: CgColors.primaryDeep,
                unselectedLabelColor: CgColors.textMuted,
                dividerColor: CgColors.border,
                indicatorSize: TabBarIndicatorSize.label,
                indicator: UnderlineTabIndicator(
                  borderSide: BorderSide(width: 3, color: CgColors.primary),
                  borderRadius: BorderRadius.vertical(top: Radius.circular(3)),
                ),
                labelStyle: TextStyle(fontWeight: FontWeight.w800, fontSize: 13.5),
                unselectedLabelStyle: TextStyle(fontWeight: FontWeight.w600, fontSize: 13.5),
                tabs: [Tab(text: 'Overview'), Tab(text: 'Attendance'), Tab(text: 'Journey')],
              ),
            ),
          ],
          body: _loading
              ? const CgLoadingState()
              : _error != null
                  ? CgErrorState(message: _error!, onRetry: _load)
                  : TabBarView(children: [
                      RefreshIndicator(onRefresh: _load, child: _OverviewTab(detail: _detail!)),
                      RefreshIndicator(onRefresh: _load, child: _AttendanceTab(records: _attendance ?? [])),
                      RefreshIndicator(onRefresh: _load, child: _JourneyTab(events: _journey ?? [])),
                    ]),
        ),
      ),
    );
  }
}

class _OverviewTab extends StatelessWidget {
  final StudentDetail detail;
  const _OverviewTab({required this.detail});

  @override
  Widget build(BuildContext context) {
    final s = detail.child;
    final stats = detail.stats;
    final t = detail.transport;
    void push(Widget w) => Navigator.of(context).push(MaterialPageRoute(builder: (_) => w));

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
      children: [
        CgFadeIn(
          child: CgAlert(
            tone: switch (s.safetyStatus) {
              CgSafetyStatus.atSchool || CgSafetyStatus.droppedOff => CgTone.success,
              CgSafetyStatus.onBus => CgTone.info,
              CgSafetyStatus.leftSchool => CgTone.warning,
              CgSafetyStatus.noActivity => CgTone.neutral,
            },
            icon: CgSafety.icon(s.safetyStatus),
            title: CgSafety.label(s.safetyStatus),
            message: [
              CgSafety.explanation(s.safetyStatus),
              if (s.lastEventAt != null) 'Last checkpoint ${CgFormat.time(s.lastEventAt!)}.',
            ].join(' '),
          ),
        ),
        CgFadeIn(
          index: 1,
          child: CgKpiGrid(children: [
            CgKpi(
              label: 'Today',
              value: s.presentToday ? (formatCheckInTime(s.checkInTime) ?? 'In') : 'Not in',
              icon: Icons.event_available_rounded,
              tone: s.presentToday ? (s.lateToday ? CgTone.warning : CgTone.success) : CgTone.neutral,
              note: s.presentToday ? (s.lateToday ? 'Checked in late' : 'Checked in on time') : 'No school arrival yet',
            ),
            CgKpi(
              label: 'Attendance',
              value: stats.presentRate.toStringAsFixed(stats.presentRate % 1 == 0 ? 0 : 1),
              unit: '%',
              icon: Icons.donut_large_rounded,
              tone: stats.presentRate >= 90 ? CgTone.success : CgTone.warning,
              note: '${stats.daysPresent} of ${stats.schoolDaysObserved} days · ${stats.daysLate} late',
              onTap: () => push(ReportsScreen(initialStudentId: s.studentId)),
            ),
          ]),
        ),
        const SizedBox(height: 14),
        CgCard(
          title: 'Student information',
          child: CgDefList([
            CgDefList.text('Full name', s.fullName),
            CgDefList.text('Grade', s.grade),
            CgDefList.text('Date of birth', detail.dateOfBirth == null ? null : DateFormat('d MMMM yyyy').format(detail.dateOfBirth!)),
            CgDefList.text('Your relationship', s.relationship),
            ('RFID card', detail.hasRfidCard ? CgBadge.success('Card issued', icon: Icons.contactless_rounded) : CgBadge.warning('No card')),
            (
              'Pickup',
              s.isAuthorizedForPickup
                  ? CgBadge.success('You can collect', icon: Icons.check_circle_rounded)
                  : CgBadge.neutral('Not authorised for pickup'),
            ),
          ]),
        ),
        CgCard(
          title: 'Transport',
          subtitle: t == null ? 'No bus assigned' : 'Assigned bus and route',
          trailing: t == null ? null : TextButton(onPressed: () => push(TransportScreen(student: s)), child: const Text('Details')),
          child: t == null
              ? const Text('Your child travels independently, or the school has not assigned a bus yet.',
                  style: TextStyle(color: CgColors.textMuted, height: 1.4))
              : CgDefList([
                  CgDefList.text('Bus', t.busNumber),
                  CgDefList.text('Route', t.routeName),
                  CgDefList.text('Driver', t.driverName),
                ]),
        ),
        CgCard(
          title: 'Guardians',
          subtitle: 'People linked to ${s.fullName.split(' ').first}',
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 2),
          child: Column(children: [
            for (final (i, g) in detail.guardians.indexed)
              CgRow(
                divider: i != detail.guardians.length - 1,
                leading: CgAvatar(name: g.fullName, size: 36, guardian: true),
                title: g.isYou ? '${g.fullName} (you)' : g.fullName,
                subtitle: g.relationship ?? 'Guardian',
                trailing: g.isAuthorizedForPickup ? CgBadge.success('Pickup') : CgBadge.neutral('No pickup'),
              ),
          ]),
        ),
        if (s.isAuthorizedForPickup)
          CgCard(
            title: 'Temporary pickup',
            padding: const EdgeInsets.fromLTRB(16, 2, 12, 2),
            child: CgRow(
              divider: false,
              leading: const CgIconTile(icon: Icons.badge_rounded, color: CgColors.guardianText, background: CgColors.guardianSoft, size: 38),
              title: 'Let someone else collect ${s.fullName.split(' ').first}',
              subtitle: 'Needs school approval; QR code and face check at the desk',
              onTap: () => push(const TemporaryPickupsScreen()),
            ),
          ),
        CgCard(
          title: "Today's checkpoints",
          child: detail.todaysEvents.isEmpty
              ? const Text('No checkpoints recorded yet today.', style: TextStyle(color: CgColors.textMuted))
              : CgTimeline(CgFormat.timeline(detail.todaysEvents)),
        ),
      ],
    );
  }
}

class _AttendanceTab extends StatelessWidget {
  final List<AttendanceRecord> records;
  const _AttendanceTab({required this.records});

  @override
  Widget build(BuildContext context) {
    if (records.isEmpty) {
      return ListView(physics: const AlwaysScrollableScrollPhysics(), children: const [
        SizedBox(height: 60),
        CgEmptyState(
          icon: Icons.event_available_rounded,
          title: 'No attendance recorded',
          message: 'Attendance is recorded automatically when your child scans in or out at school.',
        ),
      ]);
    }
    final days = records.where((r) => r.eventType == 'SchoolArrival').map((r) => r.date).toSet().length;
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
      children: [
        CgKpiGrid(children: [
          CgKpi(label: 'Days present', value: '$days', icon: Icons.event_available_rounded, tone: CgTone.success),
          CgKpi(label: 'Records', value: '${records.length}', icon: Icons.list_alt_rounded, tone: CgTone.neutral),
        ]),
        const SizedBox(height: 14),
        CgCard(
          title: 'Attendance log',
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 2),
          child: Column(children: [
            for (final (i, r) in records.indexed)
              CgRow(
                divider: i != records.length - 1,
                leading: CgIconTile(
                  icon: r.eventType == 'SchoolArrival' ? Icons.login_rounded : Icons.logout_rounded,
                  color: r.eventType == 'SchoolArrival' ? CgColors.successText : CgColors.primaryDark,
                  background: r.eventType == 'SchoolArrival' ? CgColors.successSoft : CgColors.primarySoft,
                  size: 36,
                ),
                title: r.eventType == 'SchoolArrival' ? 'School arrival' : 'School departure',
                subtitle: CgFormat.date(r.date),
                trailing: Text(formatCheckInTime(r.time) ?? r.time,
                    style: const TextStyle(color: CgColors.text2, fontWeight: FontWeight.w700)),
              ),
          ]),
        ),
      ],
    );
  }
}

class _JourneyTab extends StatelessWidget {
  final List<JourneyEvent> events;
  const _JourneyTab({required this.events});

  @override
  Widget build(BuildContext context) => ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
        children: [
          if (events.isEmpty)
            const CgEmptyState(
              icon: Icons.route_rounded,
              title: 'No journey events yet',
              message: 'Checkpoint scans will appear here as they happen.',
            )
          else
            CgCard(
              title: 'Journey history',
              subtitle: '${events.length} checkpoint${events.length == 1 ? '' : 's'}',
              child: CgTimeline(CgFormat.timeline(events.take(80).toList(), withDate: true)),
            ),
        ],
      );
}
