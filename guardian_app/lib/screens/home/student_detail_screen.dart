import 'dart:math' as math;

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
        appBar: AppBar(title: Text(s.fullName)),
        body: NestedScrollView(
          headerSliverBuilder: (context, _) => [
            SliverToBoxAdapter(child: _Identity(student: s, photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null, headers: auth.photoHeaders)),
            const SliverPersistentHeader(pinned: true, delegate: _TabsHeader()),
          ],
          body: _loading
              ? const CgLoadingState()
              : _error != null
                  ? CgErrorState(message: _error!, onRetry: _load)
                  : TabBarView(children: [
                      _OverviewTab(detail: _detail!, onRefresh: _load),
                      _AttendanceTab(records: _attendance ?? [], onRefresh: _load),
                      _JourneyTab(events: _journey ?? [], onRefresh: _load),
                    ]),
        ),
      ),
    );
  }
}

/// Photo, name, status and grade, on the card surface under the app bar.
class _Identity extends StatelessWidget {
  final StudentSummary student;
  final String? photoUrl;
  final Map<String, String>? headers;
  const _Identity({required this.student, required this.photoUrl, required this.headers});

  @override
  Widget build(BuildContext context) => ColoredBox(
        color: CgColors.card,
        child: LayoutBuilder(
          builder: (context, c) {
            final side = math.max(CgBreakpoints.gutter(c.maxWidth), (c.maxWidth - CgWidths.detail) / 2);
            return Padding(
              padding: EdgeInsets.fromLTRB(side, CgSpace.lg, side, CgSpace.md),
              child: Row(
                children: [
                  CgAvatar(
                    name: student.fullName,
                    photoUrl: photoUrl,
                    headers: headers,
                    size: 64,
                    square: true,
                    statusDot: CgSafety.color(student.safetyStatus),
                  ),
                  const SizedBox(width: CgSpace.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(student.fullName, maxLines: 2, overflow: TextOverflow.ellipsis, style: CgText.h1),
                        const SizedBox(height: 6),
                        Wrap(spacing: 6, runSpacing: 6, children: [
                          CgBadge.safety(student.safetyStatus),
                          if (student.grade != null) CgBadge.neutral('Grade ${student.grade}'),
                        ]),
                      ],
                    ),
                  ),
                ],
              ),
            );
          },
        ),
      );
}

/// Pinned tab strip, aligned with the page content.
class _TabsHeader extends SliverPersistentHeaderDelegate {
  const _TabsHeader();

  @override
  double get minExtent => 48;
  @override
  double get maxExtent => 48;

  @override
  Widget build(BuildContext context, double shrinkOffset, bool overlapsContent) => DecoratedBox(
        decoration: const BoxDecoration(color: CgColors.card, border: Border(bottom: BorderSide(color: CgColors.border))),
        child: LayoutBuilder(
          builder: (context, c) {
            final side = math.max(CgBreakpoints.gutter(c.maxWidth), (c.maxWidth - CgWidths.detail) / 2);
            return Padding(
              padding: EdgeInsets.symmetric(horizontal: side - 12 < 0 ? 0 : side - 12),
              child: const TabBar(
                isScrollable: true,
                tabAlignment: TabAlignment.start,
                dividerColor: Colors.transparent,
                tabs: [Tab(text: 'Overview'), Tab(text: 'Attendance'), Tab(text: 'Journey')],
              ),
            );
          },
        ),
      );

  @override
  bool shouldRebuild(covariant _TabsHeader oldDelegate) => false;
}

class _OverviewTab extends StatelessWidget {
  final StudentDetail detail;
  final Future<void> Function() onRefresh;
  const _OverviewTab({required this.detail, required this.onRefresh});

  @override
  Widget build(BuildContext context) {
    final s = detail.child;
    final stats = detail.stats;
    final t = detail.transport;
    void push(Widget w) => Navigator.of(context).push(MaterialPageRoute(builder: (_) => w));

    return CgPage(
      maxWidth: CgWidths.detail,
      onRefresh: onRefresh,
      children: [
        CgAlert(
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
        CgKpiGrid(children: [
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
        const SizedBox(height: CgSpace.md),
        CgSplit(
          breakAt: 760,
          primaryFlex: 1,
          secondaryFlex: 1,
          primary: [
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
                  ? const Text('Your child travels independently, or the school has not assigned a bus yet.', style: CgText.small)
                  : CgDefList([
                      CgDefList.text('Bus', t.busNumber),
                      CgDefList.text('Route', t.routeName),
                      CgDefList.text('Driver', t.driverName),
                    ]),
            ),
          ],
          secondary: [
            CgCard(
              title: 'Guardians',
              subtitle: 'People linked to ${s.fullName.split(' ').first}',
              padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.md, 0),
              child: Column(children: [
                for (final (i, g) in detail.guardians.indexed)
                  CgRow(
                    divider: i != detail.guardians.length - 1,
                    leading: CgAvatar(name: g.fullName, size: 34, guardian: true),
                    title: g.isYou ? '${g.fullName} (you)' : g.fullName,
                    subtitle: g.relationship ?? 'Guardian',
                    trailing: g.isAuthorizedForPickup ? CgBadge.success('Pickup') : CgBadge.neutral('No pickup'),
                  ),
              ]),
            ),
            if (s.isAuthorizedForPickup)
              CgCard(
                padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.sm, 0),
                child: CgRow(
                  divider: false,
                  leading: const CgIconTile(icon: Icons.badge_rounded, color: CgColors.guardianText, background: CgColors.guardianSoft, size: 36),
                  title: 'Let someone else collect ${s.fullName.split(' ').first}',
                  subtitle: 'Needs school approval; QR code and face check at the desk',
                  onTap: () => push(const TemporaryPickupsScreen()),
                ),
              ),
            CgCard(
              title: "Today's checkpoints",
              child: detail.todaysEvents.isEmpty
                  ? const Text('No checkpoints recorded yet today.', style: CgText.small)
                  : CgTimeline(CgFormat.timeline(detail.todaysEvents)),
            ),
          ],
        ),
      ],
    );
  }
}

class _AttendanceTab extends StatelessWidget {
  final List<AttendanceRecord> records;
  final Future<void> Function() onRefresh;
  const _AttendanceTab({required this.records, required this.onRefresh});

  @override
  Widget build(BuildContext context) {
    if (records.isEmpty) {
      return CgPage(maxWidth: CgWidths.detail, onRefresh: onRefresh, children: const [
        CgCard(
          child: CgEmptyState(
            icon: Icons.event_available_rounded,
            title: 'No attendance recorded',
            message: 'Attendance is recorded automatically when your child scans in or out at school.',
          ),
        ),
      ]);
    }
    final days = records.where((r) => r.eventType == 'SchoolArrival').map((r) => r.date).toSet().length;
    return CgPage(
      maxWidth: CgWidths.detail,
      onRefresh: onRefresh,
      children: [
        CgKpiGrid(children: [
          CgKpi(label: 'Days present', value: '$days', icon: Icons.event_available_rounded, tone: CgTone.success),
          CgKpi(label: 'Records', value: '${records.length}', icon: Icons.list_alt_rounded, tone: CgTone.neutral),
        ]),
        const SizedBox(height: CgSpace.md),
        CgCard(
          title: 'Attendance log',
          padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.md, 0),
          child: Column(children: [
            for (final (i, r) in records.indexed)
              CgRow(
                divider: i != records.length - 1,
                leading: CgIconTile(
                  icon: r.eventType == 'SchoolArrival' ? Icons.login_rounded : Icons.logout_rounded,
                  color: r.eventType == 'SchoolArrival' ? CgColors.successText : CgColors.primaryDark,
                  background: r.eventType == 'SchoolArrival' ? CgColors.successSoft : CgColors.primarySoft,
                  size: 34,
                ),
                title: r.eventType == 'SchoolArrival' ? 'School arrival' : 'School departure',
                subtitle: CgFormat.date(r.date),
                trailing: Text(formatCheckInTime(r.time) ?? r.time,
                    style: const TextStyle(color: CgColors.text, fontWeight: FontWeight.w600, fontFeatures: [FontFeature.tabularFigures()])),
              ),
          ]),
        ),
      ],
    );
  }
}

class _JourneyTab extends StatelessWidget {
  final List<JourneyEvent> events;
  final Future<void> Function() onRefresh;
  const _JourneyTab({required this.events, required this.onRefresh});

  @override
  Widget build(BuildContext context) => CgPage(
        maxWidth: CgWidths.detail,
        onRefresh: onRefresh,
        children: [
          if (events.isEmpty)
            const CgCard(
              child: CgEmptyState(
                icon: Icons.route_rounded,
                title: 'No journey events yet',
                message: 'Checkpoint scans will appear here as they happen.',
              ),
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
