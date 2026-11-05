import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_journey_timeline.dart';
import '../../widgets/cg_states.dart';

class StudentDetailScreen extends StatefulWidget {
  final StudentSummary student;
  const StudentDetailScreen({required this.student, super.key});

  @override
  State<StudentDetailScreen> createState() => _StudentDetailScreenState();
}

class _StudentDetailScreenState extends State<StudentDetailScreen> {
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
    setState(() { _loading = true; _error = null; });
    try {
      final api = context.read<AuthProvider>().api;
      final results = await Future.wait([
        api.getJourney(widget.student.studentId),
        api.getAttendance(widget.student.studentId),
      ]);
      if (mounted) {
        setState(() {
          _journey = results[0] as List<JourneyEvent>;
          _attendance = results[1] as List<AttendanceRecord>;
          _loading = false;
        });
      }
    } catch (e) {
      if (mounted) setState(() { _error = 'Could not load this child\'s record.'; _loading = false; });
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthProvider>();
    final s = widget.student;

    return DefaultTabController(
      length: 3,
      child: Scaffold(
        body: NestedScrollView(
          headerSliverBuilder: (context, innerBoxIsScrolled) => [
            SliverAppBar(
              pinned: true,
              backgroundColor: CgColors.background,
              elevation: 0,
              expandedHeight: 210,
              title: Text(s.fullName, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
              flexibleSpace: FlexibleSpaceBar(
                background: Container(
                  padding: const EdgeInsets.fromLTRB(20, 70, 20, 16),
                  color: CgColors.primarySoft,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.end,
                    children: [
                      CgAvatar(
                        name: s.fullName,
                        photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                        headers: auth.photoHeaders,
                        size: 72,
                        square: true,
                        statusDot: CgSafety.color(s.safetyStatus),
                      ),
                      const SizedBox(height: 12),
                      Text(s.fullName, style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w800)),
                      const SizedBox(height: 6),
                      Wrap(
                        spacing: 8,
                        runSpacing: 6,
                        children: [
                          CgBadge.safety(s.safetyStatus),
                          CgBadge.neutral(s.grade ?? 'No grade'),
                          if (s.isAuthorizedForPickup)
                            CgBadge.success('You can collect', icon: Icons.check_circle_rounded)
                          else
                            CgBadge.neutral('Not a pickup guardian'),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
              bottom: const TabBar(
                labelColor: CgColors.primary,
                unselectedLabelColor: CgColors.textMuted,
                indicatorColor: CgColors.primary,
                labelStyle: TextStyle(fontWeight: FontWeight.w700, fontSize: 13),
                tabs: [
                  Tab(text: 'Overview'),
                  Tab(text: 'Attendance'),
                  Tab(text: 'Journey'),
                ],
              ),
            ),
          ],
          body: _loading
              ? const CgLoadingState()
              : _error != null
                  ? CgErrorState(message: _error!, onRetry: _load)
                  : TabBarView(
                      children: [
                        _OverviewTab(student: s, journey: _journey ?? [], attendance: _attendance ?? []),
                        _AttendanceTab(records: _attendance ?? []),
                        RefreshIndicator(
                          onRefresh: _load,
                          child: ListView(
                            padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                            physics: const AlwaysScrollableScrollPhysics(),
                            children: [
                              if ((_journey ?? []).isEmpty)
                                const CgEmptyState(
                                  icon: Icons.route_rounded,
                                  title: 'No journey events yet',
                                  message: 'Checkpoint scans will appear here as they happen.',
                                )
                              else
                                CgJourneyTimeline(events: _journey!),
                            ],
                          ),
                        ),
                      ],
                    ),
        ),
      ),
    );
  }
}

class _OverviewTab extends StatelessWidget {
  final StudentSummary student;
  final List<JourneyEvent> journey;
  final List<AttendanceRecord> attendance;
  const _OverviewTab({required this.student, required this.journey, required this.attendance});

  @override
  Widget build(BuildContext context) {
    final recentJourney = journey.take(3).toList();
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Icon(CgSafety.icon(student.safetyStatus), color: CgSafety.color(student.safetyStatus), size: 22),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(CgSafety.label(student.safetyStatus),
                          style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15.5)),
                    ),
                  ],
                ),
                const SizedBox(height: 6),
                Text(CgSafety.explanation(student.safetyStatus),
                    style: const TextStyle(color: CgColors.textMuted, fontSize: 13, height: 1.5)),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(
              child: _statTile(
                icon: Icons.event_available_rounded,
                label: 'Today',
                value: student.presentToday && student.checkInTime != null ? student.checkInTime! : 'Not in',
                color: student.presentToday ? CgColors.success : CgColors.textMuted,
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: _statTile(
                icon: Icons.directions_bus_rounded,
                label: 'Transport',
                value: student.busNumber ?? 'No bus',
                color: CgColors.info,
              ),
            ),
          ],
        ),
        const SizedBox(height: 16),
        CgSectionHeader('Recent activity'),
        if (recentJourney.isEmpty)
          const CgEmptyState(
            icon: Icons.route_rounded,
            title: 'No activity yet',
            message: 'Checkpoint scans will appear here.',
          )
        else
          Card(child: Padding(padding: const EdgeInsets.all(16), child: CgJourneyTimeline(events: recentJourney))),
      ],
    );
  }

  Widget _statTile({required IconData icon, required String label, required String value, required Color color}) => Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, color: color, size: 20),
              const SizedBox(height: 8),
              Text(label, style: const TextStyle(color: CgColors.textMuted, fontSize: 11.5)),
              const SizedBox(height: 2),
              Text(value, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15)),
            ],
          ),
        ),
      );
}

class _AttendanceTab extends StatelessWidget {
  final List<AttendanceRecord> records;
  const _AttendanceTab({required this.records});

  @override
  Widget build(BuildContext context) {
    if (records.isEmpty) {
      return const CgEmptyState(
        icon: Icons.event_available_rounded,
        title: 'No attendance recorded',
        message: 'Attendance is created automatically when your child scans in or out at school.',
      );
    }
    final present = records.where((r) => r.eventType == 'SchoolArrival').length;
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      children: [
        Row(
          children: [
            Expanded(
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Text('Days present', style: TextStyle(color: CgColors.textMuted, fontSize: 11.5)),
                      const SizedBox(height: 4),
                      Text('$present', style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 22)),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 14),
        ...records.map((r) {
          final isArrival = r.eventType == 'SchoolArrival';
          return Card(
            child: ListTile(
              leading: Icon(isArrival ? Icons.login_rounded : Icons.logout_rounded, color: CgColors.primary),
              title: Text(isArrival ? 'School arrival' : 'School departure', style: const TextStyle(fontWeight: FontWeight.w600)),
              subtitle: Text(DateFormat('EEE, d MMM yyyy').format(r.date)),
              trailing: Text(r.time, style: const TextStyle(color: CgColors.textMuted, fontWeight: FontWeight.w600)),
            ),
          );
        }),
      ],
    );
  }
}
