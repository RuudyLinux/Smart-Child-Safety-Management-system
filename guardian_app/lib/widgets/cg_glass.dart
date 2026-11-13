import 'package:flutter/material.dart';
import '../core/theme.dart';

// Base surfaces and small building blocks. (The file keeps its old name so imports stay stable; the
// translucent "glass" look is gone: surfaces are solid white with a hairline border.)

/// The app canvas behind every route: one flat off-white colour.
class CgAmbientBackground extends StatelessWidget {
  final Widget child;
  const CgAmbientBackground({required this.child, super.key});

  @override
  Widget build(BuildContext context) => ColoredBox(color: CgColors.background, child: child);
}

/// A surface: white, hairline border, soft shadow. [strong] adds a slightly stronger lift (used for
/// floating panels such as the sign-in card). [gradient] is honoured for the rare tinted panel.
class CgGlass extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final bool blur; // kept for call-site compatibility; surfaces are opaque, nothing to blur
  final bool strong;
  final Gradient? gradient;
  final Color? color;
  final Color? borderColor;
  final List<BoxShadow>? shadow;

  const CgGlass({
    required this.child,
    this.padding = const EdgeInsets.all(CgSpace.md),
    this.radius = CgRadius.lg,
    this.blur = false,
    this.strong = false,
    this.gradient,
    this.color,
    this.borderColor,
    this.shadow,
    super.key,
  });

  @override
  Widget build(BuildContext context) => Container(
        padding: padding,
        decoration: BoxDecoration(
          color: gradient == null ? (color ?? CgColors.card) : null,
          gradient: gradient,
          borderRadius: BorderRadius.circular(radius),
          border: Border.all(color: borderColor ?? CgColors.border),
          boxShadow: shadow ?? (strong ? CgShadows.md : CgShadows.sm),
        ),
        child: child,
      );
}

/// Tappable wrapper for custom cards: ink ripple, pointer cursor, and a gentle lift on hover (desktop).
class CgPressable extends StatefulWidget {
  final Widget child;
  final VoidCallback? onTap;
  final double radius;
  const CgPressable({required this.child, this.onTap, this.radius = CgRadius.lg, super.key});

  @override
  State<CgPressable> createState() => _CgPressableState();
}

class _CgPressableState extends State<CgPressable> {
  bool _hover = false;

  @override
  Widget build(BuildContext context) {
    if (widget.onTap == null) return widget.child;
    final r = BorderRadius.circular(widget.radius);
    return MouseRegion(
      onEnter: (_) => setState(() => _hover = true),
      onExit: (_) => setState(() => _hover = false),
      child: AnimatedContainer(
        duration: CgMotion.fast,
        curve: CgMotion.ease,
        transform: Matrix4.translationValues(0, _hover ? -1 : 0, 0),
        decoration: BoxDecoration(borderRadius: r, boxShadow: _hover ? CgShadows.md : CgShadows.none),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: r,
            onTap: widget.onTap,
            splashColor: CgColors.primary.withValues(alpha: 0.06),
            highlightColor: CgColors.primary.withValues(alpha: 0.03),
            hoverColor: Colors.transparent,
            child: widget.child,
          ),
        ),
      ),
    );
  }
}

/// Fades its child in once, after [index] × 40 ms — a light cascade for a screen's first build.
class CgFadeIn extends StatefulWidget {
  final Widget child;
  final int index;
  final double offset;
  const CgFadeIn({required this.child, this.index = 0, this.offset = 6, super.key});

  @override
  State<CgFadeIn> createState() => _CgFadeInState();
}

class _CgFadeInState extends State<CgFadeIn> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: CgMotion.slow);
  late final Animation<double> _a = CurvedAnimation(parent: _c, curve: CgMotion.ease);

  @override
  void initState() {
    super.initState();
    final delay = Duration(milliseconds: 40 * widget.index.clamp(0, 6));
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

/// The ChildGuard mark: a white shield on brand blue.
class CgBrandMark extends StatelessWidget {
  final double size;
  final bool onDark;
  const CgBrandMark({this.size = 40, this.onDark = false, super.key});

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          gradient: onDark ? null : CgColors.brandGradient,
          color: onDark ? Colors.white.withValues(alpha: 0.14) : null,
          borderRadius: BorderRadius.circular(size * 0.28),
          border: onDark ? Border.all(color: Colors.white.withValues(alpha: 0.22)) : null,
        ),
        child: Icon(Icons.shield_rounded, color: Colors.white, size: size * 0.55),
      );
}

/// Brand mark plus the product name, for headers and the sidebar.
class CgBrandLockup extends StatelessWidget {
  final double markSize;
  final String? caption;
  final bool onDark;
  const CgBrandLockup({this.markSize = 34, this.caption, this.onDark = false, super.key});

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          CgBrandMark(size: markSize, onDark: onDark),
          const SizedBox(width: 10),
          Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('ChildGuard',
                  style: TextStyle(
                      fontSize: 16.5, fontWeight: FontWeight.w700, letterSpacing: -0.3, color: onDark ? Colors.white : CgColors.text)),
              if (caption != null)
                Text(caption!,
                    style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w500, color: onDark ? Colors.white70 : CgColors.textMuted)),
            ],
          ),
        ],
      );
}

/// Square tinted icon tile used for leading icons in rows and cards.
class CgIconTile extends StatelessWidget {
  final IconData icon;
  final Color color;
  final Color background;
  final double size;
  const CgIconTile({required this.icon, required this.color, required this.background, this.size = 40, super.key});

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(size * 0.26)),
        child: Icon(icon, color: color, size: size * 0.5),
      );
}

/// One destination in the app's main navigation.
class CgNavItem {
  final IconData icon;
  final IconData activeIcon;
  final String label;
  const CgNavItem(this.icon, this.activeIcon, this.label);
}
