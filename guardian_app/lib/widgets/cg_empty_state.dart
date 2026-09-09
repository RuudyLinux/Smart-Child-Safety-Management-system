import 'package:flutter/material.dart';
import '../core/theme.dart';

/// Centered muted-text placeholder for an empty list — the one empty-state
/// pattern repeated across Notifications, Attendance, Journey and Student
/// Profile screens.
class CgEmptyState extends StatelessWidget {
  final String message;
  const CgEmptyState(this.message, {super.key});

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(message, style: const TextStyle(color: CgColors.textMuted)),
        ),
      );
}
