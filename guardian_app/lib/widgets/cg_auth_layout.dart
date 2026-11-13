import 'package:flutter/material.dart';
import '../core/responsive.dart';
import '../core/theme.dart';
import 'cg_glass.dart';
import 'cg_ui.dart';

/// Shared frame for every signed-out screen (sign in, register, code, password reset).
///
/// Phones and tablets: one centred column, at most [CgWidths.auth] wide. Desktop: a brand panel on the
/// left and the same form column centred on the right.
class CgAuthLayout extends StatelessWidget {
  final String title;
  final String? subtitle;
  final Widget child;
  final Widget? footer;
  final String? error;
  final bool showBack;
  final VoidCallback? onBack;

  const CgAuthLayout({
    required this.title,
    required this.child,
    this.subtitle,
    this.footer,
    this.error,
    this.showBack = false,
    this.onBack,
    super.key,
  });

  @override
  Widget build(BuildContext context) => Scaffold(
        backgroundColor: CgColors.background,
        body: LayoutBuilder(
          builder: (context, c) {
            final wide = c.maxWidth >= 960;
            final form = SafeArea(
              child: Center(
                child: SingleChildScrollView(
                  padding: EdgeInsets.symmetric(horizontal: CgBreakpoints.gutter(c.maxWidth), vertical: CgSpace.xl),
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: CgWidths.auth),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (showBack)
                          Align(
                            alignment: Alignment.centerLeft,
                            child: Padding(
                              padding: const EdgeInsets.only(bottom: CgSpace.md),
                              child: BackButton(onPressed: onBack),
                            ),
                          ),
                        if (!wide) ...[
                          const Align(alignment: Alignment.centerLeft, child: CgBrandMark(size: 44)),
                          const SizedBox(height: CgSpace.lg),
                        ],
                        Text(title, style: CgText.display),
                        if (subtitle != null) ...[
                          const SizedBox(height: 6),
                          Text(subtitle!, style: CgText.body.copyWith(color: CgColors.textMuted)),
                        ],
                        const SizedBox(height: CgSpace.xl),
                        CgGlass(
                          strong: true,
                          padding: const EdgeInsets.all(CgSpace.xl),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              if (error != null) CgAlert(tone: CgTone.danger, title: error!),
                              child,
                            ],
                          ),
                        ),
                        if (footer != null) ...[const SizedBox(height: CgSpace.lg), footer!],
                      ],
                    ),
                  ),
                ),
              ),
            );
            if (!wide) return form;
            return Row(children: [
              SizedBox(width: (c.maxWidth * 0.42).clamp(400.0, 560.0), child: const _BrandPanel()),
              Expanded(child: form),
            ]);
          },
        ),
      );
}

/// Desktop-only panel: what ChildGuard does for a guardian, in three lines.
class _BrandPanel extends StatelessWidget {
  const _BrandPanel();

  static const _features = [
    (Icons.contactless_rounded, 'RFID check-ins', 'Arrival and departure are recorded the moment your child scans in or out.'),
    (Icons.directions_bus_rounded, 'Bus checkpoints', 'See when your child boards the school bus and when they are dropped off.'),
    (Icons.face_retouching_natural_rounded, 'Face-verified pickup', 'A child is handed over only after staff verify an authorised guardian.'),
  ];

  @override
  Widget build(BuildContext context) => Container(
        color: const Color(0xFF173E86),
        padding: const EdgeInsets.fromLTRB(48, 40, 48, 40),
        child: SafeArea(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const CgBrandLockup(onDark: true, caption: 'Guardian portal'),
              const Spacer(),
              const Text(
                'Know your child is safe, from the bus stop to the school gate.',
                style: TextStyle(color: Colors.white, fontSize: 30, height: 1.25, fontWeight: FontWeight.w700, letterSpacing: -0.6),
              ),
              const SizedBox(height: CgSpace.xxl),
              for (final f in _features)
                Padding(
                  padding: const EdgeInsets.only(bottom: CgSpace.lg),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        width: 38,
                        height: 38,
                        decoration: BoxDecoration(
                          color: Colors.white.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(CgRadius.md),
                          border: Border.all(color: Colors.white.withValues(alpha: 0.16)),
                        ),
                        child: Icon(f.$1, color: Colors.white, size: 19),
                      ),
                      const SizedBox(width: CgSpace.md),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(f.$2, style: const TextStyle(color: Colors.white, fontSize: 15, fontWeight: FontWeight.w600)),
                            const SizedBox(height: 2),
                            Text(f.$3, style: TextStyle(color: Colors.white.withValues(alpha: 0.75), fontSize: 13.5, height: 1.45)),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              const Spacer(),
              Text('For guardians of students enrolled by their school.',
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.6), fontSize: 12.5)),
            ],
          ),
        ),
      );
}
