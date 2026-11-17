import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'theme.dart';

/// Window size classes, decided from the available width (never from the platform).
enum CgSize { compact, medium, expanded }

class CgBreakpoints {
  /// Below this: phone layout, bottom navigation.
  static const double medium = 600;

  /// From this up: desktop layout, sidebar.
  static const double expanded = 1024;

  static CgSize of(double width) => width >= expanded
      ? CgSize.expanded
      : width >= medium
          ? CgSize.medium
          : CgSize.compact;

  /// Page gutter for a given available width.
  static double gutter(double width) => width >= expanded ? CgSpace.xxl : (width >= medium ? CgSpace.xl : CgSpace.md);
}

/// Content widths. Targets, not cages: each page picks the one that suits its content.
class CgWidths {
  static const double auth = 440;
  static const double form = 680;
  static const double reading = 820;
  static const double detail = 940;
  static const double list = 980;
  static const double dashboard = 1180;
}

extension CgResponsiveContext on BuildContext {
  /// Size class of the whole window.
  CgSize get windowSize => CgBreakpoints.of(MediaQuery.sizeOf(this).width);
  bool get isCompact => windowSize == CgSize.compact;
  bool get isExpanded => windowSize == CgSize.expanded;
}

/// A scrolling page: content centred at [maxWidth] with consistent gutters, optional pull-to-refresh.
/// The scrollable itself spans the full width, so the scrollbar and wheel scrolling work anywhere on the page.
class CgPage extends StatelessWidget {
  final List<Widget> children;
  final double maxWidth;
  final double top;
  final double bottom;
  final Future<void> Function()? onRefresh;

  const CgPage({
    required this.children,
    this.maxWidth = CgWidths.list,
    this.top = CgSpace.lg,
    this.bottom = CgSpace.xxl,
    this.onRefresh,
    super.key,
  });

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          final gutter = CgBreakpoints.gutter(c.maxWidth);
          final side = math.max(gutter, (c.maxWidth - maxWidth) / 2);
          final list = ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: EdgeInsets.fromLTRB(side, top, side, bottom),
            children: children,
          );
          return onRefresh == null ? list : RefreshIndicator(onRefresh: onRefresh!, child: list);
        },
      );
}

/// Centres a single box (a form, a message) at [maxWidth] without scrolling.
class CgCentered extends StatelessWidget {
  final Widget child;
  final double maxWidth;
  const CgCentered({required this.child, this.maxWidth = CgWidths.list, super.key});

  @override
  Widget build(BuildContext context) => Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(constraints: BoxConstraints(maxWidth: maxWidth), child: child),
      );
}

/// Two columns side by side when there is room ([breakAt] of available width), stacked otherwise.
/// [primary] goes first when stacked.
class CgSplit extends StatelessWidget {
  final List<Widget> primary;
  final List<Widget> secondary;
  final int primaryFlex;
  final int secondaryFlex;
  final double breakAt;
  final double gap;

  const CgSplit({
    required this.primary,
    required this.secondary,
    this.primaryFlex = 3,
    this.secondaryFlex = 2,
    this.breakAt = 860,
    this.gap = CgSpace.lg,
    super.key,
  });

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          if (c.maxWidth < breakAt) {
            return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [...primary, ...secondary]);
          }
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(flex: primaryFlex, child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: primary)),
              SizedBox(width: gap),
              Expanded(flex: secondaryFlex, child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: secondary)),
            ],
          );
        },
      );
}

/// Responsive grid: as many columns of at least [minItemWidth] as fit, capped at [maxColumns].
class CgGrid extends StatelessWidget {
  final List<Widget> children;
  final double minItemWidth;
  final int maxColumns;
  final double gap;

  const CgGrid({required this.children, this.minItemWidth = 300, this.maxColumns = 3, this.gap = CgSpace.md, super.key});

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, c) {
          final columns = ((c.maxWidth + gap) / (minItemWidth + gap)).floor().clamp(1, maxColumns);
          final w = (c.maxWidth - gap * (columns - 1)) / columns;
          return Wrap(
            spacing: gap,
            runSpacing: gap,
            children: [for (final child in children) SizedBox(width: w, child: child)],
          );
        },
      );
}
