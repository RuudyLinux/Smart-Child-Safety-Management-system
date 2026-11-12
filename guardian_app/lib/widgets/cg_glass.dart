import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import '../core/theme.dart';

/// The app canvas: off-white with two faint, slowly drifting light washes
/// (blue top-left, lavender top-right). Painted once behind the Navigator, so
/// every page shares it and scrolling never repaints it.
class CgAmbientBackground extends StatefulWidget {
  final Widget child;
  const CgAmbientBackground({required this.child, super.key});

  @override
  State<CgAmbientBackground> createState() => _CgAmbientBackgroundState();
}

class _CgAmbientBackgroundState extends State<CgAmbientBackground> with SingleTickerProviderStateMixin {
  late final AnimationController _drift =
      AnimationController(vsync: this, duration: const Duration(seconds: 18))..repeat(reverse: true);

  @override
  void dispose() {
    _drift.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final still = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    return ColoredBox(
      color: CgColors.background,
      child: Stack(
        children: [
          Positioned.fill(
            child: RepaintBoundary(
              child: AnimatedBuilder(
                animation: _drift,
                builder: (context, _) {
                  final t = still ? 0.0 : Curves.easeInOut.transform(_drift.value);
                  return CustomPaint(painter: _AmbientPainter(t));
                },
              ),
            ),
          ),
          widget.child,
        ],
      ),
    );
  }
}

class _AmbientPainter extends CustomPainter {
  final double t;
  _AmbientPainter(this.t);

  void _orb(Canvas canvas, Offset c, double r, Color color) {
    final paint = Paint()
      ..shader = RadialGradient(colors: [color, color.withValues(alpha: 0)]).createShader(Rect.fromCircle(center: c, radius: r));
    canvas.drawCircle(c, r, paint);
  }

  @override
  void paint(Canvas canvas, Size size) {
    final w = size.width, h = size.height;
    _orb(canvas, Offset(w * (0.05 + 0.08 * t), -h * 0.04 + 30 * t), w * 0.95, const Color(0x293978D5));
    _orb(canvas, Offset(w * (1.0 - 0.1 * t), h * (0.12 + 0.04 * t)), w * 0.8, const Color(0x248B7FE8));
    _orb(canvas, Offset(w * 0.5, h * (1.05 - 0.04 * t)), w * 0.9, const Color(0x143978D5));
  }

  @override
  bool shouldRepaint(_AmbientPainter old) => old.t != t;
}

/// Frosted white surface: translucent fill, white rim, hairline outline and a
/// soft tinted shadow. [blur] adds a real backdrop blur — keep it for chrome
/// that floats over content (nav bar, sheets), not for long lists.
class CgGlass extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final bool blur;
  final bool strong;
  final Gradient? gradient;
  final List<BoxShadow>? shadow;

  const CgGlass({
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.radius = 20,
    this.blur = false,
    this.strong = false,
    this.gradient,
    this.shadow,
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    final r = BorderRadius.circular(radius);
    Widget surface = Container(
      padding: padding,
      decoration: BoxDecoration(
        color: gradient == null ? (strong ? CgColors.glassStrong : CgColors.glass) : null,
        gradient: gradient,
        borderRadius: r,
        border: Border.all(color: CgColors.glassBorder),
      ),
      child: child,
    );
    if (blur) {
      surface = ClipRRect(
        borderRadius: r,
        child: BackdropFilter(filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18), child: surface),
      );
    }
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: r,
        boxShadow: shadow ?? CgShadows.sm,
        border: Border.all(color: CgColors.glassEdge, width: 0.6),
      ),
      child: surface,
    );
  }
}

/// Scales down a touch while pressed — tactile feedback for custom cards.
class CgPressable extends StatefulWidget {
  final Widget child;
  final VoidCallback? onTap;
  final double radius;
  const CgPressable({required this.child, this.onTap, this.radius = 20, super.key});

  @override
  State<CgPressable> createState() => _CgPressableState();
}

class _CgPressableState extends State<CgPressable> {
  bool _down = false;

  @override
  Widget build(BuildContext context) => AnimatedScale(
        scale: _down ? 0.975 : 1,
        duration: CgMotion.fast,
        curve: CgMotion.ease,
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: BorderRadius.circular(widget.radius),
            onTap: widget.onTap,
            onHighlightChanged: (v) => setState(() => _down = v),
            splashColor: CgColors.primary.withValues(alpha: 0.08),
            highlightColor: CgColors.primary.withValues(alpha: 0.04),
            child: widget.child,
          ),
        ),
      );
}

/// Fades and lifts its child into place once, after [index] × 60 ms — used to
/// cascade a screen's sections on first build.
class CgFadeIn extends StatefulWidget {
  final Widget child;
  final int index;
  final double offset;
  const CgFadeIn({required this.child, this.index = 0, this.offset = 16, super.key});

  @override
  State<CgFadeIn> createState() => _CgFadeInState();
}

class _CgFadeInState extends State<CgFadeIn> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: CgMotion.slow);
  late final Animation<double> _a = CurvedAnimation(parent: _c, curve: CgMotion.ease);

  @override
  void initState() {
    super.initState();
    final delay = Duration(milliseconds: 60 * widget.index.clamp(0, 8));
    Future.delayed(delay, () {
      if (mounted) _c.forward();
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.maybeDisableAnimationsOf(context) ?? false) _c.value = 1;
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FadeTransition(
        opacity: _a,
        child: AnimatedBuilder(
          animation: _a,
          builder: (context, child) => Transform.translate(offset: Offset(0, widget.offset * (1 - _a.value)), child: child),
          child: widget.child,
        ),
      );
}

/// The ChildGuard mark: shield on the brand gradient.
class CgBrandMark extends StatelessWidget {
  final double size;
  const CgBrandMark({this.size = 56, super.key});

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          gradient: CgColors.brandGradient,
          borderRadius: BorderRadius.circular(size * 0.3),
          boxShadow: CgShadows.glow(),
          border: Border.all(color: Colors.white.withValues(alpha: 0.35)),
        ),
        child: Icon(Icons.shield_rounded, color: Colors.white, size: size * 0.54),
      );
}

/// Round frosted icon tile used for leading icons in cards.
class CgIconTile extends StatelessWidget {
  final IconData icon;
  final Color color;
  final Color background;
  final double size;
  const CgIconTile({required this.icon, required this.color, required this.background, this.size = 44, super.key});

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(size * 0.32),
          border: Border.all(color: Colors.white.withValues(alpha: 0.9)),
          boxShadow: [BoxShadow(color: color.withValues(alpha: 0.18), blurRadius: 12, offset: const Offset(0, 4), spreadRadius: -4)],
        ),
        child: Icon(icon, color: color, size: size * 0.5),
      );
}

class CgNavItem {
  final IconData icon;
  final IconData activeIcon;
  final String label;
  const CgNavItem(this.icon, this.activeIcon, this.label);
}

/// Floating frosted bottom navigation with a sliding highlight pill.
class CgGlassNavBar extends StatelessWidget {
  final int index;
  final ValueChanged<int> onTap;
  final List<CgNavItem> items;
  const CgGlassNavBar({required this.index, required this.onTap, required this.items, super.key});

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      top: false,
      minimum: const EdgeInsets.fromLTRB(12, 0, 12, 10),
      child: CgGlass(
        blur: true,
        strong: true,
        radius: 26,
        padding: const EdgeInsets.all(6),
        shadow: CgShadows.md,
        child: LayoutBuilder(
          builder: (context, c) {
            final slot = c.maxWidth / items.length;
            return SizedBox(
              height: 60,
              child: Stack(
                children: [
                  AnimatedPositioned(
                    duration: CgMotion.base,
                    curve: Curves.easeOutBack,
                    left: slot * index + 4,
                    top: 2,
                    bottom: 2,
                    width: slot - 8,
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        gradient: const LinearGradient(colors: [Color(0xFFDCEBFF), Color(0xFFEFEDFD)]),
                        borderRadius: BorderRadius.circular(20),
                        border: Border.all(color: Colors.white),
                        boxShadow: [BoxShadow(color: CgColors.primary.withValues(alpha: 0.18), blurRadius: 12, offset: const Offset(0, 4))],
                      ),
                    ),
                  ),
                  Row(
                    children: [
                      for (var i = 0; i < items.length; i++)
                        Expanded(child: _NavButton(item: items[i], selected: i == index, onTap: () => onTap(i))),
                    ],
                  ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

class _NavButton extends StatelessWidget {
  final CgNavItem item;
  final bool selected;
  final VoidCallback onTap;
  const _NavButton({required this.item, required this.selected, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final color = selected ? CgColors.primaryDeep : CgColors.textMuted;
    return Semantics(
      button: true,
      selected: selected,
      label: item.label,
      excludeSemantics: true,
      child: InkResponse(
        onTap: onTap,
        radius: 32,
        highlightShape: BoxShape.rectangle,
        containedInkWell: false,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            AnimatedScale(
              scale: selected ? 1.12 : 1,
              duration: CgMotion.base,
              curve: Curves.easeOutBack,
              child: Icon(selected ? item.activeIcon : item.icon, color: color, size: 23),
            ),
            const SizedBox(height: 3),
            AnimatedDefaultTextStyle(
              duration: CgMotion.fast,
              style: TextStyle(fontSize: 11, fontWeight: selected ? FontWeight.w800 : FontWeight.w600, color: color),
              child: Text(item.label, maxLines: 1, overflow: TextOverflow.fade, softWrap: false),
            ),
          ],
        ),
      ),
    );
  }
}
