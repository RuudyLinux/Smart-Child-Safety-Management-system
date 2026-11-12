import 'package:flutter/material.dart';
import '../core/api_client.dart';
import '../core/theme.dart';
import 'cg_glass.dart';
import 'cg_states.dart';

// Mobile counterparts of the Admin Web components (site.css): page head, hero, KPI tile, card with
// head/body/foot, definition list, segmented control, alert, timeline and pickup-state badge. Screens
// compose these so the Guardian app reads the same way the admin console does.

/// Tone shared by KPI icons, alerts and timeline markers (cg-kpi__icon--*, cg-alert--*, cg-timeline__marker--*).
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

/// cg-page-head: large title, muted subtitle, actions on the right.
class CgPageHead extends StatelessWidget {
  final String title;
  final String? subtitle;
  final List<Widget> actions;
  final Widget? leading;
  const CgPageHead({required this.title, this.subtitle, this.actions = const [], this.leading, super.key});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(2, 4, 2, 16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (leading != null) ...[leading!, const SizedBox(width: 12)],
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title,
                      style: const TextStyle(fontSize: 25, fontWeight: FontWeight.w800, letterSpacing: -0.75, height: 1.2, color: CgColors.text)),
                  if (subtitle != null) ...[
                    const SizedBox(height: 3),
                    Text(subtitle!, style: const TextStyle(color: CgColors.textMuted, fontSize: 13.5, height: 1.35)),
                  ],
                ],
              ),
            ),
            ...actions,
          ],
        ),
      );
}

/// cg-hero: gradient welcome panel with frosted stat chips and a live status pill.
class CgHero extends StatelessWidget {
  final String greeting;
  final String subtitle;
  final List<(String, String)> stats;
  final Widget? status;
  const CgHero({required this.greeting, required this.subtitle, this.stats = const [], this.status, super.key});

  @override
  Widget build(BuildContext context) => CgGlass(
        radius: 24,
        padding: const EdgeInsets.fromLTRB(20, 22, 20, 20),
        shadow: CgShadows.md,
        gradient: const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xEBDCEBFF), Color(0xD9EEF4FF), Color(0xE6EFEDFD)],
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(greeting, style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w800, letterSpacing: -0.7, color: CgColors.text)),
            const SizedBox(height: 4),
            Text(subtitle, style: const TextStyle(color: CgColors.text2, fontSize: 13.5, height: 1.4)),
            if (stats.isNotEmpty) ...[
              const SizedBox(height: 16),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [for (final s in stats) _HeroStat(label: s.$1, value: s.$2)],
              ),
            ],
            if (status != null) ...[const SizedBox(height: 14), status!],
          ],
        ),
      );
}

class _HeroStat extends StatelessWidget {
  final String label;
  final String value;
  const _HeroStat({required this.label, required this.value});

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.62),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: CgColors.glassBorder),
          boxShadow: CgShadows.sm,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(label.toUpperCase(),
                style: const TextStyle(fontSize: 10.5, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: CgColors.textMuted)),
            const SizedBox(height: 2),
            Text(value, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800, letterSpacing: -0.4, color: CgColors.text)),
          ],
        ),
      );
}

/// cg-sysstatus: pulsing dot plus a two-line status, used inside the hero.
class CgStatusPill extends StatelessWidget {
  final CgTone tone;
  final String label;
  final String headline;
  const CgStatusPill({required this.tone, required this.label, required this.headline, super.key});

  @override
  Widget build(BuildContext context) {
    final c = CgToneColors.of(tone);
    return Container(
      padding: const EdgeInsets.fromLTRB(12, 9, 16, 9),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.72),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: c.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          _Pulse(color: tone == CgTone.success ? CgColors.success : c.fg),
          const SizedBox(width: 10),
          Flexible(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: const TextStyle(fontSize: 11, color: CgColors.textMuted, fontWeight: FontWeight.w600)),
                Text(headline, style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w800, color: c.fg)),
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
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 1800));

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
          width: 10,
          height: 10,
          decoration: BoxDecoration(
            color: widget.color,
            shape: BoxShape.circle,
            boxShadow: [BoxShadow(color: widget.color.withValues(alpha: 0.4 * (1 - _c.value)), spreadRadius: 7 * _c.value)],
          ),
        ),
      );
}

/// cg-kpi: label, tinted icon tile, big number and a note.
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
        radius: 18,
        padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(label, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted, fontWeight: FontWeight.w600)),
                ),
                Container(
                  width: 34,
                  height: 34,
                  decoration: BoxDecoration(
                    color: c.soft,
                    borderRadius: BorderRadius.circular(11),
                    border: Border.all(color: c.border.withValues(alpha: 0.7)),
                  ),
                  child: Icon(icon, size: 18, color: c.fg),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Text.rich(
              TextSpan(children: [
                TextSpan(text: value, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w800, letterSpacing: -0.9, color: CgColors.text)),
                if (unit != null)
                  TextSpan(text: ' $unit', style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: CgColors.textMuted)),
              ]),
            ),
            if (note != null) ...[
              const SizedBox(height: 2),
              Text(note!, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12, color: CgColors.textMuted)),
            ],
          ],
        ),
      ),
    );
  }
}

/// Two-column KPI grid that stays two columns on phones (cg-grid--kpi).
class CgKpiGrid extends StatelessWidget {
  final List<Widget> children;
  const CgKpiGrid({required this.children, super.key});

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          final columns = c.maxWidth > 720 ? 4 : 2;
          const gap = 12.0;
          final w = (c.maxWidth - gap * (columns - 1)) / columns;
          return Wrap(
            spacing: gap,
            runSpacing: gap,
            children: [for (final k in children) SizedBox(width: w, child: k)],
          );
        },
      );
}

/// cg-card with optional head (title, subtitle, trailing), body and foot.
class CgCard extends StatelessWidget {
  final String? title;
  final String? subtitle;
  final Widget? trailing;
  final Widget child;
  final Widget? foot;
  final EdgeInsetsGeometry padding;
  const CgCard({
    required this.child,
    this.title,
    this.subtitle,
    this.trailing,
    this.foot,
    this.padding = const EdgeInsets.all(16),
    super.key,
  });

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 14),
        child: CgGlass(
          padding: EdgeInsets.zero,
          radius: 20,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (title != null)
                Container(
                  padding: const EdgeInsets.fromLTRB(16, 14, 12, 12),
                  decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: Color(0xCCE3E9F2)))),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(title!, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w800, letterSpacing: -0.15, color: CgColors.text)),
                            if (subtitle != null) ...[
                              const SizedBox(height: 2),
                              Text(subtitle!, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted)),
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
                  padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
                  decoration: const BoxDecoration(
                    color: Color(0xB3F3F6FB),
                    border: Border(top: BorderSide(color: Color(0xCCE3E9F2))),
                    borderRadius: BorderRadius.vertical(bottom: Radius.circular(20)),
                  ),
                  child: foot!,
                ),
            ],
          ),
        ),
      );
}

/// cg-deflist: muted label column, value column.
class CgDefList extends StatelessWidget {
  final List<(String, Widget)> rows;
  const CgDefList(this.rows, {super.key});

  /// Convenience for plain text values; null or empty shows a dash.
  static (String, Widget) text(String label, String? value) => (
        label,
        Text(value == null || value.isEmpty ? '—' : value,
            style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: CgColors.text)),
      );

  @override
  Widget build(BuildContext context) => Column(
        children: [
          for (var i = 0; i < rows.length; i++)
            Padding(
              padding: EdgeInsets.only(bottom: i == rows.length - 1 ? 0 : 11),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SizedBox(
                    width: 112,
                    child: Text(rows[i].$1, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted, height: 1.5)),
                  ),
                  Expanded(child: rows[i].$2),
                ],
              ),
            ),
        ],
      );
}

/// cg-segment: pill segmented control.
class CgSegment<T> extends StatelessWidget {
  final List<(T, String)> options;
  final T value;
  final ValueChanged<T> onChanged;
  const CgSegment({required this.options, required this.value, required this.onChanged, super.key});

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(3),
        decoration: BoxDecoration(
          color: const Color(0xB3E6ECF5),
          borderRadius: BorderRadius.circular(13),
          border: Border.all(color: CgColors.glassEdge),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (final o in options)
              Semantics(
                button: true,
                selected: o.$1 == value,
                child: GestureDetector(
                  onTap: () => onChanged(o.$1),
                  child: AnimatedContainer(
                    duration: CgMotion.fast,
                    padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 7),
                    decoration: BoxDecoration(
                      color: o.$1 == value ? CgColors.card : Colors.transparent,
                      borderRadius: BorderRadius.circular(10),
                      boxShadow: o.$1 == value ? CgShadows.sm : null,
                    ),
                    child: Text(o.$2,
                        style: TextStyle(
                          fontSize: 12.5,
                          fontWeight: FontWeight.w700,
                          color: o.$1 == value ? CgColors.primaryDeep : CgColors.textMuted,
                        )),
                  ),
                ),
              ),
          ],
        ),
      );
}

/// cg-alert: tinted box with icon, title and body.
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
      margin: const EdgeInsets.only(bottom: 14),
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      decoration: BoxDecoration(color: c.soft, borderRadius: BorderRadius.circular(14), border: Border.all(color: c.border)),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(i, color: c.fg, size: 20),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: TextStyle(fontWeight: FontWeight.w800, fontSize: 13.5, color: c.fg)),
                if (message != null) ...[
                  const SizedBox(height: 2),
                  Text(message!, style: const TextStyle(fontSize: 13, color: CgColors.text2, height: 1.4)),
                ],
                if (action != null) ...[const SizedBox(height: 8), action!],
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

/// cg-timeline: marker column with a connecting rail.
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
                  color: item.current ? CgColors.card : c.soft,
                  shape: BoxShape.circle,
                  border: Border.all(color: item.current ? CgColors.primary : CgColors.card, width: 2),
                  boxShadow: [BoxShadow(color: c.border, spreadRadius: 1)],
                ),
                child: Icon(item.icon, size: 14, color: c.fg),
              ),
              if (!last) Expanded(child: Container(width: 2, margin: const EdgeInsets.symmetric(vertical: 2), color: CgColors.border)),
            ],
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(top: 4, bottom: last ? 0 : 18),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Wrap(
                    spacing: 8,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      Text(item.title,
                          style: TextStyle(
                              fontWeight: FontWeight.w700,
                              fontSize: 13.5,
                              color: item.current ? CgColors.primaryDeep : CgColors.text)),
                      if (item.time != null)
                        Text(item.time!, style: const TextStyle(fontSize: 12, color: CgColors.textMuted, fontFeatures: [FontFeature.tabularFigures()])),
                    ],
                  ),
                  if (item.body != null) ...[
                    const SizedBox(height: 2),
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

/// A list row inside a card (cg-table row on mobile): leading, title/subtitle, trailing.
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
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 11),
          decoration: BoxDecoration(border: divider ? const Border(bottom: BorderSide(color: Color(0xCCE3E9F2))) : null),
          child: Row(
            children: [
              if (leading != null) ...[leading!, const SizedBox(width: 12)],
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14, color: CgColors.text)),
                    if (subtitle != null) ...[
                      const SizedBox(height: 2),
                      Text(subtitle!, style: const TextStyle(fontSize: 12.5, color: CgColors.textMuted, height: 1.35)),
                    ],
                  ],
                ),
              ),
              if (trailing != null) ...[const SizedBox(width: 8), trailing!],
              if (onTap != null) const Icon(Icons.chevron_right_rounded, color: CgColors.textMuted2, size: 20),
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

/// Pickup state chip, matching the Admin Web's _PickupStateBadge.
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
