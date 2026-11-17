import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_ui.dart';

/// Bus and transport for one child: assigned bus, route and driver (as on the Admin Web
/// Transportation page) and the bus checkpoints of the last 7 days.
class TransportScreen extends StatelessWidget {
  final StudentSummary student;
  const TransportScreen({required this.student, super.key});

  @override
  Widget build(BuildContext context) {
    final api = context.read<AuthProvider>().api;
    final to = DateTime.now();
    final from = to.subtract(const Duration(days: 6));

    return Scaffold(
      appBar: AppBar(title: const Text('Transport')),
      body: CgAsync<(StudentDetail, List<JourneyEvent>)>(
        load: () async {
          final r = await Future.wait([api.getStudentDetail(student.studentId), api.getJourneyRange(student.studentId, from, to)]);
          return (r[0] as StudentDetail, r[1] as List<JourneyEvent>);
        },
        builder: (context, data, reload) {
          final (detail, events) = data;
          final t = detail.transport;
          final bus = events.where((e) => e.eventType == 'BusBoarding' || e.eventType == 'BusDropoff').toList();
          return CgPage(
            maxWidth: CgWidths.detail,
            onRefresh: reload,
            children: [
              CgPageHead(
                title: t?.busNumber ?? 'No bus assigned',
                subtitle: t == null
                    ? '${student.fullName} has no active bus assignment.'
                    : '${student.fullName}${t.routeName == null ? '' : ' · ${t.routeName}'}',
                leading: const CgIconTile(icon: Icons.directions_bus_rounded, color: CgColors.infoText, background: CgColors.infoSoft, size: 44),
              ),
              if (t == null)
                const CgAlert(
                  tone: CgTone.info,
                  title: 'No bus on record',
                  message: 'If your child should travel by school bus, contact the school office to have a bus assigned.',
                )
              else
                Padding(
                  padding: const EdgeInsets.only(bottom: CgSpace.md),
                  child: CgGrid(minItemWidth: 260, maxColumns: 3, children: [
                    CgCard(
                      bottomSpacing: 0,
                      title: 'Bus',
                      child: CgDefList([
                        CgDefList.text('Bus number', t.busNumber),
                        CgDefList.text('Capacity', '${t.capacity} seats'),
                        CgDefList.text('Assigned', t.assignedAt == null ? null : CgFormat.date(t.assignedAt!)),
                      ]),
                    ),
                    CgCard(
                      bottomSpacing: 0,
                      title: 'Route',
                      child: CgDefList([
                        CgDefList.text('Name', t.routeName),
                        CgDefList.text('Description', t.routeDescription),
                      ]),
                    ),
                    CgCard(
                      bottomSpacing: 0,
                      title: 'Driver',
                      child: CgDefList([
                        CgDefList.text('Name', t.driverName),
                        (
                          'Phone',
                          t.driverPhone == null || t.driverPhone!.isEmpty
                              ? const Text('—')
                              : SelectableText(t.driverPhone!, style: const TextStyle(fontWeight: FontWeight.w700, color: CgColors.primaryDeep))
                        ),
                      ]),
                    ),
                  ]),
                ),
              CgCard(
                title: 'Bus checkpoints',
                subtitle: 'Last 7 days',
                child: bus.isEmpty
                    ? const Text('No bus boardings or drop-offs recorded.', style: CgText.small)
                    : CgTimeline(CgFormat.timeline(bus, withDate: true)),
              ),
            ],
          );
        },
      ),
    );
  }
}
