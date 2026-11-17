import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';

/// Attendance and journey report for one child over 7, 30 or 90 school days — the guardian's
/// slice of the Admin Web Reports page.
class ReportsScreen extends StatefulWidget {
  final bool embedded;
  final int? initialStudentId;
  const ReportsScreen({this.embedded = false, this.initialStudentId, super.key});

  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> {
  int? _studentId;
  int _days = 30;
  Key _reloadKey = UniqueKey();

  @override
  void initState() {
    super.initState();
    _studentId = widget.initialStudentId;
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final students = auth.profile?.students ?? const <StudentSummary>[];
    if (_studentId == null && students.isNotEmpty) _studentId = students.first.studentId;

    final to = DateTime.now();
    final from = to.subtract(Duration(days: _days - 1));

    return Scaffold(
      appBar: widget.embedded ? null : AppBar(title: const Text('Reports')),
      body: SafeArea(
        bottom: false,
        child: students.isEmpty
            ? const CgPage(children: [
                CgEmptyState(
                  icon: Icons.insights_rounded,
                  title: 'No reports yet',
                  message: 'Reports appear once a child is linked to your account.',
                ),
              ])
            : CgAsync<(StudentReport, List<JourneyEvent>)>(
                key: _reloadKey,
                load: () async {
                  final r = await Future.wait([
                    auth.api.getStudentReport(_studentId!, from, to),
                    auth.api.getJourneyRange(_studentId!, from, to),
                  ]);
                  return (r[0] as StudentReport, r[1] as List<JourneyEvent>);
                },
                builder: (context, data, reload) {
                  final (report, journey) = data;
                  return CgPage(
                    maxWidth: CgWidths.dashboard,
                    top: widget.embedded ? (context.isCompact ? 12 : 24) : CgSpace.lg,
                    onRefresh: reload,
                    children: [
                      if (widget.embedded) const CgPageHead(title: 'Reports', subtitle: 'Attendance, punctuality and bus trips by date range.'),
                      if (students.length > 1) ...[
                        SizedBox(
                          height: 40,
                          child: ListView(
                            scrollDirection: Axis.horizontal,
                            children: [
                              for (final s in students)
                                CgChildChip(
                                  student: s,
                                  selected: s.studentId == _studentId,
                                  photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                                  photoHeaders: auth.photoHeaders,
                                  onTap: () => _set(() => _studentId = s.studentId),
                                ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 12),
                      ],
                      Wrap(
                        spacing: 12,
                        runSpacing: 8,
                        crossAxisAlignment: WrapCrossAlignment.center,
                        children: [
                          CgSegment<int>(
                            options: const [(7, '7 days'), (30, '30 days'), (90, '90 days')],
                            value: _days,
                            onChanged: (v) => _set(() => _days = v),
                          ),
                          Text('${CgFormat.shortDate(from)} – ${CgFormat.shortDate(to)}', style: CgText.caption),
                        ],
                      ),
                      const SizedBox(height: CgSpace.md),
                      CgKpiGrid(children: [
                        CgKpi(
                          label: 'Attendance rate',
                          value: report.presentRate.toStringAsFixed(report.presentRate % 1 == 0 ? 0 : 1),
                          unit: '%',
                          icon: Icons.donut_large_rounded,
                          tone: report.presentRate >= 90 ? CgTone.success : (report.presentRate >= 75 ? CgTone.warning : CgTone.danger),
                          note: '${report.daysPresent} of ${report.schoolDaysObserved} school days',
                        ),
                        CgKpi(
                          label: 'Late arrivals',
                          value: '${report.daysLate}',
                          icon: Icons.schedule_rounded,
                          tone: report.daysLate == 0 ? CgTone.success : CgTone.warning,
                          note: 'Checked in after the late cutoff',
                        ),
                        CgKpi(
                          label: 'Bus trips',
                          value: '${report.busBoardings + report.busDropoffs}',
                          icon: Icons.directions_bus_rounded,
                          tone: CgTone.info,
                          note: '${report.busBoardings} boardings · ${report.busDropoffs} drop-offs',
                        ),
                        CgKpi(
                          label: 'Approved pickups',
                          value: '${report.pickupsApproved}',
                          icon: Icons.how_to_reg_rounded,
                          tone: CgTone.primary,
                          note: 'Face-verified handovers',
                        ),
                      ]),
                      const SizedBox(height: CgSpace.lg),
                      CgSplit(
                        breakAt: 900,
                        primaryFlex: 1,
                        secondaryFlex: 1,
                        primary: [
                          CgCard(
                            title: 'Daily attendance',
                            subtitle: report.days.isEmpty
                                ? 'No school days recorded in this range'
                                : '${report.days.length} day${report.days.length == 1 ? '' : 's'} with activity',
                            padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.md, 0),
                            child: report.days.isEmpty
                                ? const Padding(
                                    padding: EdgeInsets.symmetric(vertical: 14),
                                    child: Text('Nothing recorded for this period.', style: CgText.small),
                                  )
                                : Column(children: [
                                    for (final (i, d) in report.days.indexed)
                                      CgRow(
                                        divider: i != report.days.length - 1,
                                        title: CgFormat.date(d.date),
                                        subtitle: [
                                          'In ${formatCheckInTime(d.checkIn) ?? '—'}',
                                          'Out ${formatCheckInTime(d.checkOut) ?? '—'}',
                                          if (d.busBoardingAt != null) 'Bus ${CgFormat.time(d.busBoardingAt!)}',
                                        ].join(' · '),
                                        trailing: d.checkIn == null
                                            ? CgBadge.neutral('Bus only')
                                            : d.late
                                                ? CgBadge.warning('Late')
                                                : CgBadge.success('On time'),
                                      ),
                                  ]),
                          ),
                        ],
                        secondary: [
                          CgCard(
                            title: 'Journey history',
                            subtitle: '${journey.length} checkpoint${journey.length == 1 ? '' : 's'}',
                            child: journey.isEmpty
                                ? const Text('No checkpoints in this period.', style: CgText.small)
                                : CgTimeline(CgFormat.timeline(journey.take(60).toList(), withDate: true)),
                          ),
                        ],
                      ),
                    ],
                  );
                },
              ),
      ),
    );
  }

  void _set(VoidCallback change) => setState(() {
        change();
        _reloadKey = UniqueKey();
      });
}
