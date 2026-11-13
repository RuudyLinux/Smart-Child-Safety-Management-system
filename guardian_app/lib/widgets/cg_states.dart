import 'package:flutter/material.dart';
import '../core/theme.dart';

/// Centred icon + message + optional action — the one empty-state pattern used on every screen.
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
          padding: const EdgeInsets.symmetric(horizontal: CgSpace.xl, vertical: CgSpace.xxl),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 380),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 56,
                  height: 56,
                  decoration: BoxDecoration(
                    color: CgColors.primarySoft,
                    borderRadius: BorderRadius.circular(CgRadius.lg),
                    border: Border.all(color: CgColors.primaryBorder.withValues(alpha: 0.6)),
                  ),
                  child: Icon(icon, size: 26, color: CgColors.primaryDark),
                ),
                const SizedBox(height: CgSpace.md),
                Text(title, textAlign: TextAlign.center, style: CgText.h3),
                const SizedBox(height: 4),
                Text(message, textAlign: TextAlign.center, style: CgText.small),
                if (actionLabel != null && onAction != null) ...[
                  const SizedBox(height: CgSpace.md),
                  ElevatedButton(onPressed: onAction, child: Text(actionLabel!)),
                ],
              ],
            ),
          ),
        ),
      );
}

/// Skeleton for a screen still loading its first payload.
class CgLoadingState extends StatelessWidget {
  final int lines;
  const CgLoadingState({this.lines = 3, super.key});

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          final side = c.maxWidth > 1012 ? (c.maxWidth - 980) / 2 : (c.maxWidth >= 600 ? CgSpace.xl : CgSpace.md);
          return ListView.builder(
            padding: EdgeInsets.fromLTRB(side, CgSpace.lg, side, CgSpace.lg),
            itemCount: lines,
            itemBuilder: (context, i) => Padding(
              padding: const EdgeInsets.only(bottom: CgSpace.sm),
              child: _ShimmerCard(height: i == 0 ? 112 : 84),
            ),
          );
        },
      );
}

class _ShimmerCard extends StatefulWidget {
  final double height;
  const _ShimmerCard({required this.height});
  @override
  State<_ShimmerCard> createState() => _ShimmerCardState();
}

class _ShimmerCardState extends State<_ShimmerCard> with SingleTickerProviderStateMixin {
  late final AnimationController _controller =
      AnimationController(vsync: this, duration: const Duration(milliseconds: 1400))..repeat();

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
            height: widget.height,
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(CgRadius.lg),
              border: Border.all(color: CgColors.border),
              gradient: LinearGradient(
                begin: Alignment(-1 + t * 3, 0),
                end: Alignment(1 + t * 3, 0),
                colors: const [Color(0xFFF2F4F7), Color(0xFFFAFBFC), Color(0xFFF2F4F7)],
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
          padding: const EdgeInsets.all(CgSpace.xl),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 380),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 56,
                  height: 56,
                  decoration: BoxDecoration(
                    color: CgColors.errorSoft,
                    borderRadius: BorderRadius.circular(CgRadius.lg),
                    border: Border.all(color: CgColors.errorBorder),
                  ),
                  child: const Icon(Icons.cloud_off_rounded, size: 26, color: CgColors.errorText),
                ),
                const SizedBox(height: CgSpace.md),
                const Text('Could not load this', style: CgText.h3),
                const SizedBox(height: 4),
                Text(message, textAlign: TextAlign.center, style: CgText.small),
                const SizedBox(height: CgSpace.md),
                OutlinedButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh_rounded, size: 18), label: const Text('Try again')),
              ],
            ),
          ),
        ),
      );
}

/// Small uppercase label above a group of cards.
class CgSectionHeader extends StatelessWidget {
  final String title;
  final Widget? trailing;
  const CgSectionHeader(this.title, {this.trailing, super.key});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(2, CgSpace.xs, 2, CgSpace.sm),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Flexible(child: Text(title.toUpperCase(), style: CgText.overline)),
            if (trailing != null) trailing!,
          ],
        ),
      );
}
