import 'package:flutter/material.dart';

/// ChildGuard design system · Light Glass — shared visual language with the
/// Admin Web. Off-white canvas with faint blue/lavender ambient light, frosted
/// white surfaces, one brand blue. Light only. Status always pairs a colour
/// with a label or icon, never colour alone. Every colour used for text keeps
/// 4.5:1 on white and on its own soft tint (WCAG AA).
class CgColors {
  static const primary = Color(0xFF3978D5);
  static const primaryDark = Color(0xFF2F68C0); // solid buttons + links (5.4:1 with white)
  static const primaryDeep = Color(0xFF2558A6); // text on soft blue tints
  static const primarySoft = Color(0xFFEEF5FF);
  static const primarySoft2 = Color(0xFFDCEBFF);
  static const primaryBorder = Color(0xFFC4DAF8);

  static const lavender = Color(0xFF8B7FE8);
  static const lavenderSoft = Color(0xFFEFEDFD);

  /// Kept for older call sites: now a deep text tone, never a surface.
  static const navy = Color(0xFF172B4D);
  static const navy2 = Color(0xFF23395E);

  static const background = Color(0xFFF7F9FC);
  static const card = Color(0xFFFFFFFF);
  static const glass = Color(0xBDFFFFFF); // rgba(255,255,255,.74)
  static const glassStrong = Color(0xE0FFFFFF); // .88
  static const glassBorder = Color(0xD9FFFFFF); // .85
  static const glassEdge = Color(0x12172B4D); // hairline outline
  static const surface2 = Color(0xFFF3F6FB);
  static const surface3 = Color(0xFFE6ECF5);

  static const text = Color(0xFF172B4D);
  static const text2 = Color(0xFF3A4F72);
  static const textMuted = Color(0xFF5E6D84);
  static const textMuted2 = Color(0xFF8A97AB);
  static const border = Color(0xFFE3E9F2);
  static const borderStrong = Color(0xFFCDD6E4);

  static const success = Color(0xFF16A36A);
  static const successText = Color(0xFF0F7A4F);
  static const successSoft = Color(0xFFECF9F2);
  static const successBorder = Color(0xFFBDE9D3);

  static const warning = Color(0xFF9A5B0B); // warning text
  static const warningAccent = Color(0xFFE99A32); // warning fill
  static const warningSoft = Color(0xFFFFF6EA);
  static const warningBorder = Color(0xFFF8DDB4);

  static const error = Color(0xFFDC5252);
  static const errorText = Color(0xFFB53434);
  static const errorSoft = Color(0xFFFDF0F0);
  static const errorBorder = Color(0xFFF6CACA);

  static const info = Color(0xFF2B8FC9);
  static const infoText = Color(0xFF1D6A99);
  static const infoSoft = Color(0xFFEDF7FD);
  static const infoBorder = Color(0xFFBFE2F5);

  static const guardianSoft = lavenderSoft;
  static const guardianText = Color(0xFF5146B8);

  // Backward-compatible aliases used by not-yet-migrated screens.
  static const primaryLight = primarySoft;
  static const successBg = successSoft;
  static const warningBg = warningSoft;
  static const errorBg = errorSoft;

  static const brandGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF4A88E3), primary, Color(0xFF6F7EE6)],
    stops: [0, 0.45, 1],
  );
}

/// Shared shadows: soft, blue-tinted, never grey-black. Painted only OUTSIDE
/// the shape (BlurStyle.outer), so translucent glass never shows a shadow
/// through its own fill.
class CgShadows {
  static const sm = [
    BoxShadow(color: Color(0x0F172B4D), blurRadius: 3, blurStyle: BlurStyle.outer),
    BoxShadow(color: Color(0x1A294880), blurRadius: 18, blurStyle: BlurStyle.outer),
  ];
  static const md = [
    BoxShadow(color: Color(0x12172B4D), blurRadius: 5, blurStyle: BlurStyle.outer),
    BoxShadow(color: Color(0x2E294880), blurRadius: 30, blurStyle: BlurStyle.outer),
  ];
  static List<BoxShadow> glow([Color color = CgColors.primary]) =>
      [BoxShadow(color: color.withValues(alpha: 0.40), blurRadius: 18, offset: const Offset(0, 6), spreadRadius: -4)];
}

/// Motion tokens: quick, eased out, never in the way.
class CgMotion {
  static const fast = Duration(milliseconds: 160);
  static const base = Duration(milliseconds: 260);
  static const slow = Duration(milliseconds: 480);
  static const ease = Curves.easeOutCubic;
}

/// Safety-status colour + label in one place, so every screen agrees on what
/// "safe at school" looks like. Mirrors ChildGuard.AdminWeb's SafetyUi.
enum CgSafetyStatus { noActivity, onBus, atSchool, leftSchool, droppedOff }

class CgSafety {
  static CgSafetyStatus fromApi(String? raw) => switch (raw) {
        'OnBusToSchool' => CgSafetyStatus.onBus,
        'AtSchool' => CgSafetyStatus.atSchool,
        'LeftSchool' => CgSafetyStatus.leftSchool,
        'DroppedOffHome' => CgSafetyStatus.droppedOff,
        _ => CgSafetyStatus.noActivity,
      };

  static String label(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.onBus => 'On the bus',
        CgSafetyStatus.atSchool => 'Safe at school',
        CgSafetyStatus.leftSchool => 'Left school',
        CgSafetyStatus.droppedOff => 'Dropped off',
        CgSafetyStatus.noActivity => 'No activity today',
      };

  static String explanation(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.onBus => 'Boarded the bus — school arrival not yet recorded.',
        CgSafetyStatus.atSchool => 'Checked in at school and has not departed.',
        CgSafetyStatus.leftSchool => 'Departed school — drop-off not yet recorded.',
        CgSafetyStatus.droppedOff => "Bus drop-off recorded — today's journey is complete.",
        CgSafetyStatus.noActivity => 'No checkpoint has been recorded today.',
      };

  /// Fill colour: dots, icons, rings.
  static Color color(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.atSchool || CgSafetyStatus.droppedOff => CgColors.success,
        CgSafetyStatus.onBus => CgColors.info,
        CgSafetyStatus.leftSchool => CgColors.warningAccent,
        CgSafetyStatus.noActivity => CgColors.textMuted2,
      };

  /// Text colour that stays readable on [soft].
  static Color textColor(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.atSchool || CgSafetyStatus.droppedOff => CgColors.successText,
        CgSafetyStatus.onBus => CgColors.infoText,
        CgSafetyStatus.leftSchool => CgColors.warning,
        CgSafetyStatus.noActivity => CgColors.textMuted,
      };

  static Color soft(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.atSchool || CgSafetyStatus.droppedOff => CgColors.successSoft,
        CgSafetyStatus.onBus => CgColors.infoSoft,
        CgSafetyStatus.leftSchool => CgColors.warningSoft,
        CgSafetyStatus.noActivity => CgColors.surface2,
      };

  static IconData icon(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.onBus => Icons.directions_bus_rounded,
        CgSafetyStatus.atSchool => Icons.verified_user_rounded,
        CgSafetyStatus.leftSchool => Icons.logout_rounded,
        CgSafetyStatus.droppedOff => Icons.home_rounded,
        CgSafetyStatus.noActivity => Icons.help_outline_rounded,
      };
}

/// Fade-through page transition. Scaffolds are transparent so the ambient
/// canvas shows through; the outgoing page fades out before the incoming one
/// rises in, so two translucent pages never overlap.
class CgPageTransitionsBuilder extends PageTransitionsBuilder {
  const CgPageTransitionsBuilder();

  @override
  Widget buildTransitions<T>(PageRoute<T> route, BuildContext context, Animation<double> animation,
      Animation<double> secondaryAnimation, Widget child) {
    final incoming = CurvedAnimation(parent: animation, curve: const Interval(0.3, 1, curve: Curves.easeOutCubic));
    final outgoing = CurvedAnimation(parent: secondaryAnimation, curve: const Interval(0, 0.35, curve: Curves.easeIn));
    return FadeTransition(
      opacity: ReverseAnimation(outgoing),
      child: FadeTransition(
        opacity: incoming,
        child: SlideTransition(
          position: Tween(begin: const Offset(0, 0.035), end: Offset.zero).animate(incoming),
          child: child,
        ),
      ),
    );
  }
}

ThemeData buildCgTheme() {
  final base = ThemeData(useMaterial3: true, fontFamily: 'Roboto');
  OutlineInputBorder field(Color c, [double w = 1]) =>
      OutlineInputBorder(borderRadius: BorderRadius.circular(14), borderSide: BorderSide(color: c, width: w));

  return base.copyWith(
    // Transparent: the ambient canvas (CgAmbientBackground) lives behind every page.
    scaffoldBackgroundColor: Colors.transparent,
    canvasColor: CgColors.background,
    colorScheme: ColorScheme.fromSeed(
      seedColor: CgColors.primary,
      primary: CgColors.primaryDark,
      secondary: CgColors.lavender,
      surface: CgColors.card,
      error: CgColors.errorText,
      brightness: Brightness.light,
    ).copyWith(surfaceTint: Colors.transparent),
    pageTransitionsTheme: const PageTransitionsTheme(builders: {
      TargetPlatform.android: CgPageTransitionsBuilder(),
      TargetPlatform.iOS: CgPageTransitionsBuilder(),
      TargetPlatform.windows: CgPageTransitionsBuilder(),
      TargetPlatform.macOS: CgPageTransitionsBuilder(),
      TargetPlatform.linux: CgPageTransitionsBuilder(),
    }),
    appBarTheme: const AppBarTheme(
      backgroundColor: Colors.transparent,
      surfaceTintColor: Colors.transparent,
      foregroundColor: CgColors.text,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(color: CgColors.text, fontSize: 19, fontWeight: FontWeight.w800, letterSpacing: -0.3),
      iconTheme: IconThemeData(color: CgColors.text2),
    ),
    cardTheme: CardThemeData(
      color: CgColors.glass,
      elevation: 0,
      shadowColor: const Color(0x33294880),
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(20),
        side: const BorderSide(color: CgColors.glassBorder),
      ),
      margin: const EdgeInsets.symmetric(vertical: 6),
      clipBehavior: Clip.antiAlias,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: const Color(0xD9FFFFFF),
      labelStyle: const TextStyle(color: CgColors.textMuted),
      floatingLabelStyle: const TextStyle(color: CgColors.primaryDark, fontWeight: FontWeight.w700),
      hintStyle: const TextStyle(color: CgColors.textMuted2),
      prefixIconColor: CgColors.textMuted,
      suffixIconColor: CgColors.textMuted,
      border: field(CgColors.borderStrong),
      enabledBorder: field(CgColors.border),
      focusedBorder: field(CgColors.primary, 1.8),
      errorBorder: field(CgColors.error),
      focusedErrorBorder: field(CgColors.error, 1.8),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 15),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: CgColors.primaryDark,
        foregroundColor: Colors.white,
        disabledBackgroundColor: CgColors.primary.withValues(alpha: 0.35),
        disabledForegroundColor: Colors.white,
        elevation: 0,
        shadowColor: CgColors.primary.withValues(alpha: 0.5),
        padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 20),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        textStyle: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15.5, letterSpacing: 0.1),
      ).copyWith(
        elevation: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.disabled) ? 0 : (s.contains(WidgetState.pressed) ? 0 : 2)),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: CgColors.primaryDark,
        foregroundColor: Colors.white,
        padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 20),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        textStyle: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: CgColors.primaryDark,
        backgroundColor: const Color(0xB3FFFFFF),
        side: const BorderSide(color: CgColors.border),
        padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 18),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14.5),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: CgColors.primaryDark,
        textStyle: const TextStyle(fontWeight: FontWeight.w700),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
    ),
    listTileTheme: const ListTileThemeData(iconColor: CgColors.primary, textColor: CgColors.text),
    bottomNavigationBarTheme: const BottomNavigationBarThemeData(
      backgroundColor: Colors.transparent,
      selectedItemColor: CgColors.primaryDark,
      unselectedItemColor: CgColors.textMuted,
      type: BottomNavigationBarType.fixed,
      elevation: 0,
      selectedLabelStyle: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w800),
      unselectedLabelStyle: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600),
    ),
    dividerTheme: const DividerThemeData(color: CgColors.border, thickness: 1, space: 1),
    snackBarTheme: SnackBarThemeData(
      backgroundColor: CgColors.card,
      contentTextStyle: const TextStyle(color: CgColors.text, fontSize: 13.5, fontWeight: FontWeight.w600),
      actionTextColor: CgColors.primaryDark,
      behavior: SnackBarBehavior.floating,
      elevation: 8,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(14),
        side: const BorderSide(color: CgColors.border),
      ),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: CgColors.glassStrong,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: CgColors.glassStrong,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(26))),
    ),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: CgColors.primary, linearTrackColor: CgColors.surface3),
    textSelectionTheme: const TextSelectionThemeData(cursorColor: CgColors.primary, selectionHandleColor: CgColors.primary),
    checkboxTheme: CheckboxThemeData(shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(5))),
  );
}

/// Status pill matching the Admin Web's cg-badge component. Always icon/dot + text.
class CgBadge extends StatelessWidget {
  final String text;
  final Color fg;
  final Color bg;
  final Color? borderColor;
  final IconData? icon;
  const CgBadge(this.text, {required this.fg, required this.bg, this.icon, this.borderColor, super.key});

  factory CgBadge.success(String text, {IconData? icon}) =>
      CgBadge(text, fg: CgColors.successText, bg: CgColors.successSoft, borderColor: CgColors.successBorder, icon: icon);
  factory CgBadge.warning(String text, {IconData? icon}) =>
      CgBadge(text, fg: CgColors.warning, bg: CgColors.warningSoft, borderColor: CgColors.warningBorder, icon: icon);
  factory CgBadge.error(String text, {IconData? icon}) =>
      CgBadge(text, fg: CgColors.errorText, bg: CgColors.errorSoft, borderColor: CgColors.errorBorder, icon: icon);
  factory CgBadge.info(String text, {IconData? icon}) =>
      CgBadge(text, fg: CgColors.infoText, bg: CgColors.infoSoft, borderColor: CgColors.infoBorder, icon: icon);
  factory CgBadge.neutral(String text, {IconData? icon}) =>
      CgBadge(text, fg: CgColors.text2, bg: CgColors.surface2, borderColor: CgColors.border, icon: icon);
  factory CgBadge.primary(String text, {IconData? icon}) =>
      CgBadge(text, fg: CgColors.primaryDeep, bg: CgColors.primarySoft2, borderColor: CgColors.primaryBorder, icon: icon);

  factory CgBadge.safety(CgSafetyStatus status) => CgBadge(CgSafety.label(status),
      fg: CgSafety.textColor(status), bg: CgSafety.soft(status), icon: CgSafety.icon(status));

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 5),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: borderColor ?? fg.withValues(alpha: 0.18)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: fg),
            const SizedBox(width: 5),
          ],
          Flexible(
            child: Text(text,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(color: fg, fontSize: 12.5, fontWeight: FontWeight.w700)),
          ),
        ],
      ),
    );
  }
}
