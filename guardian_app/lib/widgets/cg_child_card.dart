import 'package:flutter/material.dart';
import '../core/theme.dart';
import '../models/models.dart';
import 'cg_avatar.dart';
import 'cg_glass.dart';

/// A child's live safety picture in one tappable card: photo, name, current
/// status, check-in time and bus — the core unit of the Guardian app's home
/// and children screens.
class CgChildCard extends StatelessWidget {
  final StudentSummary student;
  final String? photoUrl;
  final Map<String, String>? photoHeaders;
  final VoidCallback? onTap;

  const CgChildCard({
    required this.student,
    this.photoUrl,
    this.photoHeaders,
    this.onTap,
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    final status = student.safetyStatus;
    return CgPressable(
      onTap: onTap,
      child: CgGlass(
        padding: EdgeInsets.zero,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              CgAvatar(
                name: student.fullName,
                photoUrl: photoUrl,
                headers: photoHeaders,
                size: 58,
                square: true,
                statusDot: CgSafety.color(status),
              ),
              const SizedBox(width: 13),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(student.fullName,
                              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800, color: CgColors.text, letterSpacing: -0.2)),
                        ),
                        const Icon(Icons.chevron_right_rounded, color: CgColors.textMuted2, size: 20),
                      ],
                    ),
                    const SizedBox(height: 3),
                    Text(
                      student.grade ?? 'No grade on file',
                      style: const TextStyle(color: CgColors.textMuted, fontSize: 12.5),
                    ),
                    const SizedBox(height: 10),
                    CgBadge.safety(status),
                    const SizedBox(height: 8),
                    _factRow(
                      Icons.event_available_rounded,
                      student.presentToday && student.checkInTime != null
                          ? 'Checked in at ${formatCheckInTime(student.checkInTime)}'
                          : 'Not checked in today',
                    ),
                    if (student.busNumber != null) ...[
                      const SizedBox(height: 3),
                      _factRow(Icons.directions_bus_rounded,
                          student.routeName != null ? '${student.busNumber} · ${student.routeName}' : student.busNumber!),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _factRow(IconData icon, String text) => Row(
        children: [
          Icon(icon, size: 14, color: CgColors.textMuted2),
          const SizedBox(width: 6),
          Expanded(
            child: Text(text, style: const TextStyle(color: CgColors.text2, fontSize: 12.5), overflow: TextOverflow.ellipsis),
          ),
        ],
      );
}

/// Compact horizontal variant for a multi-child switcher strip.
class CgChildChip extends StatelessWidget {
  final StudentSummary student;
  final String? photoUrl;
  final Map<String, String>? photoHeaders;
  final bool selected;
  final VoidCallback onTap;

  const CgChildChip({
    required this.student,
    required this.selected,
    required this.onTap,
    this.photoUrl,
    this.photoHeaders,
    super.key,
  });

  @override
  Widget build(BuildContext context) => GestureDetector(
        onTap: onTap,
        child: AnimatedContainer(
          duration: CgMotion.base,
          curve: CgMotion.ease,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          margin: const EdgeInsets.only(right: 8),
          decoration: BoxDecoration(
            color: selected ? null : CgColors.glassStrong,
            gradient: selected ? CgColors.brandGradient : null,
            borderRadius: BorderRadius.circular(999),
            border: Border.all(color: selected ? Colors.white.withValues(alpha: 0.4) : CgColors.border),
            boxShadow: selected ? CgShadows.glow() : CgShadows.sm,
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              CgAvatar(name: student.fullName, photoUrl: photoUrl, headers: photoHeaders, size: 22),
              const SizedBox(width: 7),
              Text(
                student.fullName.split(' ').first,
                style: TextStyle(
                  color: selected ? Colors.white : CgColors.text2,
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
            ],
          ),
        ),
      );
}
