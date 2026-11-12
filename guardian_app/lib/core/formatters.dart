import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/models.dart';
import '../widgets/cg_ui.dart';

/// Shared wording, icons and tones for journey checkpoints, and date/time formats, so every screen
/// labels the same event the same way (mirrors the Admin Web's JourneyUi).
class CgFormat {
  static String eventLabel(String type) => switch (type) {
        'BusBoarding' => 'Boarded the bus',
        'SchoolArrival' => 'Arrived at school',
        'SchoolDeparture' => 'Left school',
        'BusDropoff' => 'Dropped off home',
        _ => type,
      };

  static IconData eventIcon(String type) => switch (type) {
        'BusBoarding' => Icons.directions_bus_rounded,
        'SchoolArrival' => Icons.login_rounded,
        'SchoolDeparture' => Icons.logout_rounded,
        'BusDropoff' => Icons.home_rounded,
        _ => Icons.circle,
      };

  static CgTone eventTone(String type) => switch (type) {
        'BusBoarding' => CgTone.info,
        'SchoolArrival' => CgTone.success,
        'SchoolDeparture' => CgTone.warning,
        'BusDropoff' => CgTone.success,
        _ => CgTone.neutral,
      };

  static String source(String s) => switch (s) {
        'Rfid' => 'RFID card scan',
        'Manual' => 'Recorded by school staff',
        _ => s,
      };

  static String time(DateTime t) => DateFormat('h:mm a').format(t.toLocal());
  static String dayTime(DateTime t) => DateFormat('d MMM, h:mm a').format(t.toLocal());
  static String date(DateTime d) => DateFormat('EEE, d MMM yyyy').format(d);
  static String shortDate(DateTime d) => DateFormat('d MMM').format(d);

  /// Journey events as timeline entries, oldest first; the newest is marked current.
  static List<CgTimelineItem> timeline(List<JourneyEvent> events, {bool withStudent = false, bool withDate = false}) {
    final sorted = [...events]..sort((a, b) => a.timestamp.compareTo(b.timestamp));
    return [
      for (var i = 0; i < sorted.length; i++)
        CgTimelineItem(
          title: withStudent && sorted[i].studentName.isNotEmpty
              ? '${sorted[i].studentName.split(' ').first} · ${eventLabel(sorted[i].eventType).toLowerCase()}'
              : eventLabel(sorted[i].eventType),
          time: withDate ? dayTime(sorted[i].timestamp) : time(sorted[i].timestamp),
          body: source(sorted[i].source),
          icon: eventIcon(sorted[i].eventType),
          tone: eventTone(sorted[i].eventType),
          current: i == sorted.length - 1,
        ),
    ];
  }
}
