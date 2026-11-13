import 'package:flutter/material.dart';
import '../core/theme.dart';
import '../models/models.dart';
import 'cg_avatar.dart';
import 'cg_glass.dart';

/// A child's live safety picture in one tappable card: photo, name, current status, check-in time and
/// bus — the core unit of the dashboard and the children screen.
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
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(CgSpace.md, CgSpace.md, CgSpace.sm, CgSpace.sm),
              child: Row(
                children: [
                  CgAvatar(
                    name: student.fullName,
                    photoUrl: photoUrl,
                    headers: photoHeaders,
                    size: 48,
                    square: true,
                    statusDot: CgSafety.color(status),
                  ),
                  const SizedBox(width: CgSpace.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(student.fullName, maxLines: 1, overflow: TextOverflow.ellipsis, style: CgText.h3),
                        const SizedBox(height: 1),
                        Text(student.grade == null ? 'No grade on file' : 'Grade ${student.grade}', style: CgText.caption.copyWith(fontWeight: FontWeight.w400)),
                      ],
                    ),
                  ),
                  if (onTap != null) const Icon(Icons.chevron_right_rounded, color: CgColors.textMuted2, size: 20),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: CgSpace.md),
              child: Align(alignment: Alignment.centerLeft, child: CgBadge.safety(status)),
            ),
            const SizedBox(height: CgSpace.sm),
            Container(
              padding: const EdgeInsets.fromLTRB(CgSpace.md, 10, CgSpace.md, 12),
              decoration: const BoxDecoration(border: Border(top: BorderSide(color: CgColors.border))),
              child: Column(
                children: [
                  _factRow(
                    Icons.event_available_rounded,
                    student.presentToday && student.checkInTime != null
                        ? 'Checked in at ${formatCheckInTime(student.checkInTime)}${student.lateToday ? ' (late)' : ''}'
                        : 'Not checked in today',
                  ),
                  const SizedBox(height: 4),
                  _factRow(
                    Icons.directions_bus_rounded,
                    student.busNumber == null
                        ? 'No bus assigned'
                        : (student.routeName != null ? '${student.busNumber} · ${student.routeName}' : student.busNumber!),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _factRow(IconData icon, String text) => Row(
        children: [
          Icon(icon, size: 15, color: CgColors.textMuted2),
          const SizedBox(width: CgSpace.xs),
          Expanded(child: Text(text, style: const TextStyle(color: CgColors.text2, fontSize: 13), overflow: TextOverflow.ellipsis)),
        ],
      );
}

/// Compact chip for a multi-child switcher strip.
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
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(right: CgSpace.xs),
        child: Material(
          color: selected ? CgColors.primarySoft : CgColors.card,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(CgRadius.pill),
            side: BorderSide(color: selected ? CgColors.primaryBorder : CgColors.border),
          ),
          child: InkWell(
            onTap: onTap,
            customBorder: const StadiumBorder(),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(6, 5, 14, 5),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  CgAvatar(name: student.fullName, photoUrl: photoUrl, headers: photoHeaders, size: 24),
                  const SizedBox(width: CgSpace.xs),
                  Text(
                    student.fullName.split(' ').first,
                    style: TextStyle(
                      color: selected ? CgColors.primaryDeep : CgColors.text2,
                      fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                      fontSize: 13.5,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
}
