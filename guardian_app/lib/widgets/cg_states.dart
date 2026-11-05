import 'package:flutter/material.dart';
import '../core/theme.dart';

/// Centered icon + message + optional action — the one empty-state pattern
/// reused across Home, Children, Journeys, Alerts and Attendance.
class CgEmptyState extends StatelessWidget {
  final IconData icon;
  final String title;
  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  const CgEmptyState({
    required this.title,
    required this.message,
    this.icon = Icons.inbox_outlined,
    this.actionLabel,
    this.onAction,
    super.key,
  });

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 64,
                height: 64,
                decoration: BoxDecoration(color: CgColors.surface2, borderRadius: BorderRadius.circular(18)),
                child: Icon(icon, size: 30, color: CgColors.textMuted2),
              ),
              const SizedBox(height: 16),
              Text(title, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800, color: CgColors.text)),
              const SizedBox(height: 6),
              Text(message, textAlign: TextAlign.center, style: const TextStyle(color: CgColors.textMuted, fontSize: 13.5, height: 1.5)),
              if (actionLabel != null && onAction != null) ...[
                const SizedBox(height: 18),
                ElevatedButton(onPressed: onAction, child: Text(actionLabel!)),
              ],
            ],
          ),
        ),
      );
}

/// Full-bleed shimmer skeleton for a screen still loading its first payload.
class CgLoadingState extends StatelessWidget {
  final int lines;
  const CgLoadingState({this.lines = 3, super.key});

  @override
  Widget build(BuildContext context) => ListView.builder(
        padding: const EdgeInsets.all(16),
        itemCount: lines,
        itemBuilder: (context, i) => const Padding(
          padding: EdgeInsets.only(bottom: 12),
          child: _ShimmerCard(),
        ),
      );
}

class _ShimmerCard extends StatefulWidget {
  const _ShimmerCard();
  @override
  State<_ShimmerCard> createState() => _ShimmerCardState();
}

class _ShimmerCardState extends State<_ShimmerCard> with SingleTickerProviderStateMixin {
  late final AnimationController _controller =
      AnimationController(vsync: this, duration: const Duration(milliseconds: 1300))..repeat();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
        animation: _controller,
        builder: (context, _) {
          final t = _controller.value;
          return Container(
            height: 84,
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(16),
              border: Border.all(color: CgColors.border),
              gradient: LinearGradient(
                begin: Alignment(-1 + t * 3, 0),
                end: Alignment(1 + t * 3, 0),
                colors: const [CgColors.surface2, Color(0xFFE8EDF3), CgColors.surface2],
              ),
            ),
          );
        },
      );
}

/// Clear failure explanation with a retry — never a bare error string.
class CgErrorState extends StatelessWidget {
  final String message;
  final VoidCallback onRetry;
  const CgErrorState({required this.message, required this.onRetry, super.key});

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 64,
                height: 64,
                decoration: BoxDecoration(color: CgColors.errorSoft, borderRadius: BorderRadius.circular(18)),
                child: const Icon(Icons.wifi_off_rounded, size: 30, color: CgColors.error),
              ),
              const SizedBox(height: 16),
              const Text('Could not load this', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w800, color: CgColors.text)),
              const SizedBox(height: 6),
              Text(message, textAlign: TextAlign.center, style: const TextStyle(color: CgColors.textMuted, fontSize: 13.5, height: 1.5)),
              const SizedBox(height: 18),
              OutlinedButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh_rounded, size: 18), label: const Text('Try again')),
            ],
          ),
        ),
      );
}

/// Uppercase section label used above a list of cards.
class CgSectionHeader extends StatelessWidget {
  final String title;
  final Widget? trailing;
  const CgSectionHeader(this.title, {this.trailing, super.key});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(2, 4, 2, 10),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              title.toUpperCase(),
              style: const TextStyle(fontSize: 11.5, fontWeight: FontWeight.w800, letterSpacing: 0.6, color: CgColors.textMuted),
            ),
            if (trailing != null) trailing!,
          ],
        ),
      );
}
