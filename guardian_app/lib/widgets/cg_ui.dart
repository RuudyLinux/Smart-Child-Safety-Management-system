import 'package:flutter/material.dart';
import '../core/api_client.dart';
import '../core/theme.dart';
import 'cg_glass.dart';
import 'cg_states.dart';

// Shared components: page head, status summary, KPI tile, card with head/body/foot, definition list,
// segmented control, alert, timeline, list row, async loader and the pickup-state badge. Every screen
// composes these, so the whole app reads as one product.

/// Tone shared by KPI icons, alerts and timeline markers.
enum CgTone { primary, success, warning, danger, info, neutral }

class CgToneColors {
  final Color fg;
  final Color soft;
  final Color border;
  const CgToneColors(this.fg, this.soft, this.border);

  static CgToneColors of(CgTone t) => switch (t) {
        CgTone.primary => const CgToneColors(CgColors.primaryDeep, CgColors.primarySoft, CgColors.primaryBorder),
        CgTone.success => const CgToneColors(CgColors.successText, CgColors.successSoft, CgColors.successBorder),
        CgTone.warning => const CgToneColors(CgColors.warning, CgColors.warningSoft, CgColors.warningBorder),
        CgTone.danger => const CgToneColors(CgColors.errorText, CgColors.errorSoft, CgColors.errorBorder),
        CgTone.info => const CgToneColors(CgColors.infoText, CgColors.infoSoft, CgColors.infoBorder),
        CgTone.neutral => const CgToneColors(CgColors.textMuted, CgColors.surface2, CgColors.border),
      };
}

/// Page title, muted subtitle, actions on the right.
class CgPageHead extends StatelessWidget {
  final String title;
  final String? subtitle;
  final List<Widget> actions;
  final Widget? leading;
  const CgPageHead({required this.title, this.subtitle, this.actions = const [], this.leading, super.key});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: CgSpace.lg),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            if (leading != null) ...[leading!, const SizedBox(width: CgSpace.sm)],
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: CgText.h1),
                  if (subtitle != null) ...[
                    const SizedBox(height: 4),
                    Text(subtitle!, style: CgText.small),
                  ],
                ],
              ),
            ),
            if (actions.isNotEmpty) ...[const SizedBox(width: CgSpace.sm), ...actions],
          ],
        ),
      );
}

/// Status line with a live dot: a label above a coloured headline.
class CgStatusPill extends StatelessWidget {
  final CgTone tone;
  final String label;
  final String headline;
  const CgStatusPill({required this.tone, required this.label, required this.headline, super.key});

  @override
  Widget build(BuildContext context) {
    final c = CgToneColors.of(tone);
    return Container(
      padding: const EdgeInsets.fromLTRB(12, 8, 14, 8),
      decoration: BoxDecoration(color: c.soft, borderRadius: BorderRadius.circular(CgRadius.md), border: Border.all(color: c.border)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          _Pulse(color: tone == CgTone.success ? CgColors.success : c.fg),
          const SizedBox(width: 10),
          Flexible(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: CgText.caption),
                Text(headline, style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: c.fg)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Pulse extends StatefulWidget {
  final Color color;
  const _Pulse({required this.color});
  @override
  State<_Pulse> createState() => _PulseState();
}

class _PulseState extends State<_Pulse> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 2200));

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.maybeDisableAnimationsOf(context) ?? false) {
      _c.stop();
    } else if (!_c.isAnimating) {
      _c.repeat();
    }
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
        animation: _c,
        builder: (context, _) => Container(
          width: 9,
          height: 9,
          decoration: BoxDecoration(
            color: widget.color,
            shape: BoxShape.circle,
            boxShadow: [BoxShadow(color: widget.color.withValues(alpha: 0.35 * (1 - _c.value)), spreadRadius: 6 * _c.value)],
          ),
        ),
      );
}

/// KPI tile: label, tinted icon, big number with unit, and a note.
class CgKpi extends StatelessWidget {
  final String label;
  final String value;
  final String? unit;
  final String? note;
  final IconData icon;
  final CgTone tone;
  final VoidCallback? onTap;
  const CgKpi({
    required this.label,
    required this.value,
    required this.icon,
    this.unit,
    this.note,
    this.tone = CgTone.primary,
    this.onTap,
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    final c = CgToneColors.of(tone);
    return CgPressable(
      onTap: onTap,
      child: CgGlass(
        padding: const EdgeInsets.all(CgSpace.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text(label, maxLines: 2, overflow: TextOverflow.ellipsis, style: CgText.caption)),
                const SizedBox(width: CgSpace.xs),
                CgIconTile(icon: icon, color: c.fg, background: c.soft, size: 30),
              ],
            ),
            const SizedBox(height: CgSpace.xs),
            Text.rich(
              TextSpan(children: [
                TextSpan(text: value, style: CgText.metric),
                if (unit != null) TextSpan(text: ' $unit', style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500, color: CgColors.textMuted)),
              ]),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            const SizedBox(height: 2),
            // The note line is always reserved so tiles in one row keep the same height.
            Text(note ?? ' ', maxLines: 2, overflow: TextOverflow.ellipsis, style: CgText.caption.copyWith(fontWeight: FontWeight.w400)),
          ],
        ),
      ),
    );
  }
}

/// KPI grid: two columns on narrow widths, four when there is room.
class CgKpiGrid extends StatelessWidget {
  final List<Widget> children;
  const CgKpiGrid({required this.children, super.key});

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          final columns = c.maxWidth >= 680 ? (children.length >= 4 ? 4 : children.length) : 2;
          const gap = CgSpace.sm;
          final w = (c.maxWidth - gap * (columns - 1)) / columns;
          return Wrap(
            spacing: gap,
            runSpacing: gap,
            children: [for (final k in children) SizedBox(width: w, child: k)],
          );
        },
      );
}

/// Card with optional head (title, subtitle, trailing), body and foot.
class CgCard extends StatelessWidget {
  final String? title;
  final String? subtitle;
  final Widget? trailing;
  final Widget child;
  final Widget? foot;
  final EdgeInsetsGeometry padding;
  final double bottomSpacing;
  const CgCard({
    required this.child,
    this.title,
    this.subtitle,
    this.trailing,
    this.foot,
    this.padding = const EdgeInsets.all(CgSpace.md),
    this.bottomSpacing = CgSpace.md,
    super.key,
  });

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.only(bottom: bottomSpacing),
        child: CgGlass(
          padding: EdgeInsets.zero,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (title != null)
                Container(
                  constraints: const BoxConstraints(minHeight: 52),
                  padding: const EdgeInsets.fromLTRB(CgSpace.md, CgSpace.sm, CgSpace.sm, CgSpace.sm),
                  decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: CgColors.border))),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(title!, style: CgText.h3),
                            if (subtitle != null) ...[
                              const SizedBox(height: 1),
                              Text(subtitle!, style: CgText.caption.copyWith(fontWeight: FontWeight.w400)),
                            ],
                          ],
                        ),
                      ),
                      if (trailing != null) trailing!,
                    ],
                  ),
                ),
              Padding(padding: padding, child: child),
              if (foot != null)
                Container(
                  padding: const EdgeInsets.fromLTRB(CgSpace.xs, 6, CgSpace.xs, 6),
                  decoration: const BoxDecoration(
                    color: CgColors.surface2,
                    border: Border(top: BorderSide(color: CgColors.border)),
                    borderRadius: BorderRadius.vertical(bottom: Radius.circular(CgRadius.lg)),
                  ),
                  child: foot!,
                ),
            ],
          ),
        ),
      );
}

/// Definition list: muted label column, value column. Narrow widths stack label over value.
class CgDefList extends StatelessWidget {
  final List<(String, Widget)> rows;
  const CgDefList(this.rows, {super.key});

  /// Convenience for plain text values; null or empty shows a dash.
  static (String, Widget) text(String label, String? value) => (
        label,
        Text(value == null || value.isEmpty ? '—' : value, style: CgText.bodyStrong),
      );

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          final labelWidth = c.maxWidth >= 460 ? 160.0 : 116.0;
          return Column(
            children: [
              for (var i = 0; i < rows.length; i++)
                Container(
                  padding: EdgeInsets.only(top: i == 0 ? 0 : 10, bottom: i == rows.length - 1 ? 0 : 10),
                  decoration: BoxDecoration(
                    border: i == rows.length - 1 ? null : const Border(bottom: BorderSide(color: CgColors.surface2)),
                  ),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SizedBox(width: labelWidth, child: Text(rows[i].$1, style: CgText.small.copyWith(height: 1.55))),
                      Expanded(child: Align(alignment: Alignment.centerLeft, child: rows[i].$2)),
                    ],
                  ),
                ),
            ],
          );
        },
      );
}

/// Segmented control.
class CgSegment<T> extends StatelessWidget {
  final List<(T, String)> options;
  final T value;
  final ValueChanged<T> onChanged;
  const CgSegment({required this.options, required this.value, required this.onChanged, super.key});

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(3),
        decoration: BoxDecoration(
          color: CgColors.surface2,
          borderRadius: BorderRadius.circular(CgRadius.md),
          border: Border.all(color: CgColors.border),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (final o in options)
              Semantics(
                button: true,
                selected: o.$1 == value,
                child: MouseRegion(
                  cursor: SystemMouseCursors.click,
                  child: GestureDetector(
                    onTap: () => onChanged(o.$1),
                    child: AnimatedContainer(
                      duration: CgMotion.fast,
                      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                      decoration: BoxDecoration(
                        color: o.$1 == value ? CgColors.card : Colors.transparent,
                        borderRadius: BorderRadius.circular(CgRadius.sm),
                        boxShadow: o.$1 == value ? CgShadows.sm : null,
                      ),
                      child: Text(o.$2,
                          style: TextStyle(
                            fontSize: 13,
                            fontWeight: o.$1 == value ? FontWeight.w600 : FontWeight.w500,
                            color: o.$1 == value ? CgColors.text : CgColors.textMuted,
                          )),
                    ),
                  ),
                ),
              ),
          ],
        ),
      );
}

/// Tinted message box with icon, title and body.
class CgAlert extends StatelessWidget {
  final CgTone tone;
  final String title;
  final String? message;
  final IconData? icon;
  final Widget? action;
  const CgAlert({required this.tone, required this.title, this.message, this.icon, this.action, super.key});

  @override
  Widget build(BuildContext context) {
    final c = CgToneColors.of(tone);
    final i = icon ??
        switch (tone) {
          CgTone.success => Icons.check_circle_rounded,
          CgTone.warning => Icons.warning_amber_rounded,
          CgTone.danger => Icons.error_outline_rounded,
          _ => Icons.info_outline_rounded,
        };
    return Container(
      margin: const EdgeInsets.only(bottom: CgSpace.md),
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      decoration: BoxDecoration(color: c.soft, borderRadius: BorderRadius.circular(CgRadius.md), border: Border.all(color: c.border)),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(padding: const EdgeInsets.only(top: 1), child: Icon(i, color: c.fg, size: 19)),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: TextStyle(fontWeight: FontWeight.w700, fontSize: 13.5, color: c.fg, height: 1.4)),
                if (message != null && message!.isNotEmpty) ...[
                  const SizedBox(height: 2),
                  Text(message!, style: const TextStyle(fontSize: 13, color: CgColors.text2, height: 1.45)),
                ],
                if (action != null) ...[const SizedBox(height: 6), action!],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// One entry for [CgTimeline].
class CgTimelineItem {
  final String title;
  final String? time;
  final String? body;
  final IconData icon;
  final CgTone tone;
  final bool current;
  const CgTimelineItem({required this.title, required this.icon, this.time, this.body, this.tone = CgTone.primary, this.current = false});
}

/// Vertical timeline: marker column with a connecting rail.
class CgTimeline extends StatelessWidget {
  final List<CgTimelineItem> items;
  const CgTimeline(this.items, {super.key});

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (var i = 0; i < items.length; i++) _row(items[i], last: i == items.length - 1),
        ],
      );

  Widget _row(CgTimelineItem item, {required bool last}) {
    final c = CgToneColors.of(item.tone);
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Column(
            children: [
              Container(
                width: 28,
                height: 28,
                decoration: BoxDecoration(
                  color: c.soft,
                  shape: BoxShape.circle,
                  border: Border.all(color: item.current ? c.fg : c.border, width: item.current ? 1.6 : 1),
                ),
                child: Icon(item.icon, size: 14, color: c.fg),
              ),
              if (!last) Expanded(child: Container(width: 1.5, margin: const EdgeInsets.symmetric(vertical: 3), color: CgColors.border)),
            ],
          ),
          const SizedBox(width: CgSpace.sm),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(top: 4, bottom: last ? 0 : CgSpace.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: Text(item.title,
                            style: TextStyle(fontWeight: FontWeight.w600, fontSize: 13.5, height: 1.4, color: item.current ? c.fg : CgColors.text)),
                      ),
                      if (item.time != null) ...[
                        const SizedBox(width: CgSpace.xs),
                        Text(item.time!,
                            style: const TextStyle(fontSize: 12, height: 1.6, color: CgColors.textMuted, fontFeatures: [FontFeature.tabularFigures()])),
                      ],
                    ],
                  ),
                  if (item.body != null) ...[
                    const SizedBox(height: 1),
                    Text(item.body!, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted, height: 1.4)),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A list row inside a card: leading, title/subtitle, trailing; hover highlight when tappable.
class CgRow extends StatelessWidget {
  final Widget? leading;
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final VoidCallback? onTap;
  final bool divider;
  const CgRow({required this.title, this.leading, this.subtitle, this.trailing, this.onTap, this.divider = true, super.key});

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        hoverColor: CgColors.surface2.withValues(alpha: 0.6),
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 12),
          decoration: BoxDecoration(border: divider ? const Border(bottom: BorderSide(color: CgColors.border)) : null),
          child: Row(
            children: [
              if (leading != null) ...[leading!, const SizedBox(width: CgSpace.sm)],
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: CgText.bodyStrong),
                    if (subtitle != null) ...[
                      const SizedBox(height: 2),
                      Text(subtitle!, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted, height: 1.4)),
                    ],
                  ],
                ),
              ),
              if (trailing != null) ...[const SizedBox(width: CgSpace.xs), trailing!],
              if (onTap != null) const Padding(padding: EdgeInsets.only(left: 4), child: Icon(Icons.chevron_right_rounded, color: CgColors.textMuted2, size: 20)),
            ],
          ),
        ),
      );
}

/// Loads [load] once (and on pull-to-refresh), showing the shared loading, error and data states.
class CgAsync<T> extends StatefulWidget {
  final Future<T> Function() load;
  final Widget Function(BuildContext context, T data, Future<void> Function() reload) builder;
  final int skeletonLines;
  const CgAsync({required this.load, required this.builder, this.skeletonLines = 4, super.key});

  @override
  State<CgAsync<T>> createState() => CgAsyncState<T>();
}

class CgAsyncState<T> extends State<CgAsync<T>> {
  T? _data;
  String? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    reload();
  }

  Future<void> reload() async {
    if (_data == null) setState(() => _loading = true);
    try {
      final d = await widget.load();
      if (mounted) setState(() { _data = d; _error = null; _loading = false; });
    } on ApiException catch (e) {
      if (mounted) setState(() { _error = e.message; _loading = false; });
    } catch (_) {
      if (mounted) setState(() { _error = 'Could not reach the server. Check your connection and try again.'; _loading = false; });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading && _data == null) return CgLoadingState(lines: widget.skeletonLines);
    if (_error != null && _data == null) return CgErrorState(message: _error!, onRetry: reload);
    return widget.builder(context, _data as T, reload);
  }
}

/// Pickup state chip, matching the Admin Web wording.
class CgPickupStateBadge extends StatelessWidget {
  final String state;
  const CgPickupStateBadge(this.state, {super.key});

  @override
  Widget build(BuildContext context) => switch (state) {
        'Pending' => CgBadge.warning('Awaiting approval', icon: Icons.hourglass_top_rounded),
        'Approved' => CgBadge.success('Picked up', icon: Icons.check_circle_rounded),
        'Rejected' => CgBadge.error('Rejected', icon: Icons.block_rounded),
        'Expired' => CgBadge.neutral('Expired', icon: Icons.timer_off_outlined),
        'NotMatched' => CgBadge.error('Face not matched', icon: Icons.face_retouching_off_rounded),
        _ => CgBadge.neutral(state),
      };
}
