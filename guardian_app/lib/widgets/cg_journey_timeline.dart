import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../core/theme.dart';
import '../models/models.dart';

IconData _eventIcon(String type) => switch (type) {
      'BusBoarding' => Icons.directions_bus_rounded,
      'SchoolArrival' => Icons.login_rounded,
      'SchoolDeparture' => Icons.logout_rounded,
      'BusDropoff' => Icons.home_rounded,
      _ => Icons.circle,
    };

String _eventLabel(String type) => switch (type) {
      'BusBoarding' => 'Bus boarding',
      'SchoolArrival' => 'School arrival',
      'SchoolDeparture' => 'School departure',
      'BusDropoff' => 'Bus drop-off',
      _ => type,
    };

/// Vertical checkpoint timeline — bus boarding, school arrival, departure,
/// drop-off. ChildGuard records checkpoints only, never continuous location,
/// so this is the entire "where has my child been" picture, honestly framed.
class CgJourneyTimeline extends StatelessWidget {
  final List<JourneyEvent> events;
  const CgJourneyTimeline({required this.events, super.key});

  @override
  Widget build(BuildContext context) {
    final sorted = [...events]..sort((a, b) => a.timestamp.compareTo(b.timestamp));
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (var i = 0; i < sorted.length; i++) _row(sorted[i], isLast: i == sorted.length - 1),
      ],
    );
  }

  Widget _row(JourneyEvent e, {required bool isLast}) => IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Column(
              children: [
                Container(
                  width: 32,
                  height: 32,
                  decoration: const BoxDecoration(color: CgColors.successSoft, shape: BoxShape.circle),
                  child: Icon(_eventIcon(e.eventType), size: 16, color: CgColors.success),
                ),
                if (!isLast) Expanded(child: Container(width: 2, color: CgColors.border)),
              ],
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Padding(
                padding: EdgeInsets.only(bottom: isLast ? 0 : 20, top: 4),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(_eventLabel(e.eventType), style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14, color: CgColors.text)),
                    const SizedBox(height: 2),
                    Text(
                      DateFormat('d MMM, h:mm a').format(e.timestamp.toLocal()),
                      style: const TextStyle(color: CgColors.textMuted, fontSize: 12.5),
                    ),
                    const SizedBox(height: 4),
                    CgBadge.neutral(e.source == 'Rfid' ? 'RFID verification' : 'Recorded by staff'),
                  ],
                ),
              ),
            ),
          ],
        ),
      );
}
