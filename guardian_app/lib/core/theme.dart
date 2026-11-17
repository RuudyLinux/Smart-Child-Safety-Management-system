import 'package:flutter/material.dart';

/// ChildGuard design system.
///
/// Calm, trustworthy and plain: an off-white canvas, solid white surfaces with a hairline border and a
/// barely-there shadow, one brand blue, and status colours that always pair with a label or an icon.
/// Every colour used for text keeps at least 4.5:1 on white and on its own soft tint (WCAG AA).
///
/// Tokens live here (colour, type, spacing, radius, elevation, motion); layout rules live in
/// `responsive.dart`; components live in `widgets/`.
class CgColors {
  // Brand
  static const primary = Color(0xFF2F6FDB);
  static const primaryDark = Color(0xFF1F5BC4); // solid buttons, links (5.9:1 with white)
  static const primaryDeep = Color(0xFF1A4A9E); // text on soft blue tints
  static const primarySoft = Color(0xFFEFF4FE);
  static const primarySoft2 = Color(0xFFDDE8FC);
  static const primaryBorder = Color(0xFFC5D7F7);

  /// Secondary accent, used only to tell other people (co-guardians, temporary pickups) apart from you.
  static const lavender = Color(0xFF6E62D9);
  static const lavenderSoft = Color(0xFFF1EFFD);
  static const guardianSoft = lavenderSoft;
  static const guardianText = Color(0xFF4F44B5);

  // Neutrals
  static const navy = Color(0xFF101828);
  static const navy2 = Color(0xFF1D2939);
  static const background = Color(0xFFF5F7FA);
  static const card = Color(0xFFFFFFFF);
  static const surface2 = Color(0xFFF2F4F7);
  static const surface3 = Color(0xFFE4E8EE);
  static const text = Color(0xFF101828);
  static const text2 = Color(0xFF344054);
  static const textMuted = Color(0xFF5B677A);
  static const textMuted2 = Color(0xFF8B95A5); // decorative only (icons, dividers), never body text
  static const border = Color(0xFFE4E8EE);
  static const borderStrong = Color(0xFFD0D5DD);

  // Status
  static const success = Color(0xFF12A15E);
  static const successText = Color(0xFF0B7443);
  static const successSoft = Color(0xFFECF8F1);
  static const successBorder = Color(0xFFBCE6CF);

  static const warning = Color(0xFF93530A); // warning text
  static const warningAccent = Color(0xFFE49A2E); // warning fill
  static const warningSoft = Color(0xFFFFF6E9);
  static const warningBorder = Color(0xFFF6DDB5);

  static const error = Color(0xFFD94848);
  static const errorText = Color(0xFFB42318);
  static const errorSoft = Color(0xFFFEF2F1);
  static const errorBorder = Color(0xFFF6CBC7);

  static const info = Color(0xFF2483C5);
  static const infoText = Color(0xFF175F93);
  static const infoSoft = Color(0xFFEDF6FC);
  static const infoBorder = Color(0xFFC2E0F3);

  // Former "glass" names, kept so every call site resolves: surfaces are now solid.
  static const glass = card;
  static const glassStrong = card;
  static const glassBorder = border;
  static const glassEdge = border;

  // Older aliases.
  static const primaryLight = primarySoft;
  static const successBg = successSoft;
  static const warningBg = warningSoft;
  static const errorBg = errorSoft;

  /// The brand mark only. Two close blues, so it reads as one colour with a little depth.
  static const brandGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF3A7BE6), primaryDark],
  );
}

/// Spacing scale (4-point). Use these instead of ad-hoc numbers.
class CgSpace {
  static const double xxs = 4;
  static const double xs = 8;
  static const double sm = 12;
  static const double md = 16;
  static const double lg = 20;
  static const double xl = 24;
  static const double xxl = 32;
  static const double xxxl = 48;
}

/// Corner radius scale: one value per kind of element, used everywhere.
class CgRadius {
  static const double xs = 6; // badges inside rows, small marks
  static const double sm = 8; // icon tiles, segment items
  static const double md = 10; // inputs, buttons
  static const double lg = 14; // cards, panels
  static const double xl = 18; // sheets, dialogs
  static const double pill = 999;

  static BorderRadius get card => BorderRadius.circular(lg);
  static BorderRadius get control => BorderRadius.circular(md);
}

/// Type scale. Roboto, tight headings, comfortable body.
class CgText {
  static const display = TextStyle(fontSize: 28, height: 1.2, fontWeight: FontWeight.w700, letterSpacing: -0.6, color: CgColors.text);
  static const h1 = TextStyle(fontSize: 24, height: 1.25, fontWeight: FontWeight.w700, letterSpacing: -0.45, color: CgColors.text);
  static const h2 = TextStyle(fontSize: 18, height: 1.3, fontWeight: FontWeight.w700, letterSpacing: -0.2, color: CgColors.text);
  static const h3 = TextStyle(fontSize: 15, height: 1.35, fontWeight: FontWeight.w600, letterSpacing: -0.1, color: CgColors.text);
  static const body = TextStyle(fontSize: 14, height: 1.5, fontWeight: FontWeight.w400, color: CgColors.text2);
  static const bodyStrong = TextStyle(fontSize: 14, height: 1.45, fontWeight: FontWeight.w600, color: CgColors.text);
  static const small = TextStyle(fontSize: 13, height: 1.45, fontWeight: FontWeight.w400, color: CgColors.textMuted);
  static const caption = TextStyle(fontSize: 12, height: 1.4, fontWeight: FontWeight.w500, color: CgColors.textMuted);
  static const overline =
      TextStyle(fontSize: 11.5, height: 1.3, fontWeight: FontWeight.w600, letterSpacing: 0.7, color: CgColors.textMuted);
  static const metric = TextStyle(
      fontSize: 26, height: 1.15, fontWeight: FontWeight.w700, letterSpacing: -0.7, color: CgColors.text, fontFeatures: [FontFeature.tabularFigures()]);
}

/// Elevation: neutral and very soft. Cards rely on their border; the shadow only lifts them off the canvas.
class CgShadows {
  static const none = <BoxShadow>[];
  static const sm = [
    BoxShadow(color: Color(0x0A101828), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x08101828), blurRadius: 6, offset: Offset(0, 2), spreadRadius: -1),
  ];
  static const md = [
    BoxShadow(color: Color(0x0D101828), blurRadius: 4, offset: Offset(0, 2), spreadRadius: -1),
    BoxShadow(color: Color(0x14101828), blurRadius: 16, offset: Offset(0, 8), spreadRadius: -4),
  ];
  static List<BoxShadow> glow([Color color = CgColors.primary]) =>
      [BoxShadow(color: color.withValues(alpha: 0.22), blurRadius: 10, offset: const Offset(0, 3), spreadRadius: -3)];
}

/// Motion tokens: quick, eased out, never in the way.
class CgMotion {
  static const fast = Duration(milliseconds: 140);
  static const base = Duration(milliseconds: 220);
  static const slow = Duration(milliseconds: 320);
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

  static Color borderColor(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.atSchool || CgSafetyStatus.droppedOff => CgColors.successBorder,
        CgSafetyStatus.onBus => CgColors.infoBorder,
        CgSafetyStatus.leftSchool => CgColors.warningBorder,
        CgSafetyStatus.noActivity => CgColors.border,
      };

  static IconData icon(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.onBus => Icons.directions_bus_rounded,
        CgSafetyStatus.atSchool => Icons.verified_user_rounded,
        CgSafetyStatus.leftSchool => Icons.logout_rounded,
        CgSafetyStatus.droppedOff => Icons.home_rounded,
        CgSafetyStatus.noActivity => Icons.help_outline_rounded,
      };
}

/// Fade-through page transition: the incoming page fades in with a short rise.
class CgPageTransitionsBuilder extends PageTransitionsBuilder {
  const CgPageTransitionsBuilder();

  @override
  Widget buildTransitions<T>(PageRoute<T> route, BuildContext context, Animation<double> animation,
      Animation<double> secondaryAnimation, Widget child) {
    final incoming = CurvedAnimation(parent: animation, curve: const Interval(0.2, 1, curve: Curves.easeOutCubic));
    return FadeTransition(
      opacity: incoming,
      child: SlideTransition(
        position: Tween(begin: const Offset(0, 0.015), end: Offset.zero).animate(incoming),
        child: child,
      ),
    );
  }
}

ThemeData buildCgTheme() {
  final base = ThemeData(useMaterial3: true, fontFamily: 'Roboto');
  OutlineInputBorder field(Color c, [double w = 1]) =>
      OutlineInputBorder(borderRadius: CgRadius.control, borderSide: BorderSide(color: c, width: w));
  final controlShape = RoundedRectangleBorder(borderRadius: CgRadius.control);

  return base.copyWith(
    scaffoldBackgroundColor: CgColors.background,
    canvasColor: CgColors.background,
    colorScheme: ColorScheme.fromSeed(
      seedColor: CgColors.primary,
      primary: CgColors.primaryDark,
      secondary: CgColors.lavender,
      surface: CgColors.card,
      error: CgColors.errorText,
      brightness: Brightness.light,
    ).copyWith(surfaceTint: Colors.transparent, outline: CgColors.borderStrong, outlineVariant: CgColors.border),
    textTheme: base.textTheme.apply(bodyColor: CgColors.text2, displayColor: CgColors.text),
    pageTransitionsTheme: const PageTransitionsTheme(builders: {
      TargetPlatform.android: CgPageTransitionsBuilder(),
      TargetPlatform.iOS: CgPageTransitionsBuilder(),
      TargetPlatform.windows: CgPageTransitionsBuilder(),
      TargetPlatform.macOS: CgPageTransitionsBuilder(),
      TargetPlatform.linux: CgPageTransitionsBuilder(),
    }),
    appBarTheme: const AppBarTheme(
      backgroundColor: CgColors.card,
      surfaceTintColor: Colors.transparent,
      foregroundColor: CgColors.text,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      toolbarHeight: 60,
      shape: Border(bottom: BorderSide(color: CgColors.border)),
      titleTextStyle: TextStyle(color: CgColors.text, fontSize: 17, fontWeight: FontWeight.w700, letterSpacing: -0.2),
      iconTheme: IconThemeData(color: CgColors.text2),
    ),
    cardTheme: CardThemeData(
      color: CgColors.card,
      elevation: 0,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: CgRadius.card, side: const BorderSide(color: CgColors.border)),
      margin: EdgeInsets.zero,
      clipBehavior: Clip.antiAlias,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: CgColors.card,
      labelStyle: const TextStyle(color: CgColors.textMuted, fontSize: 14),
      floatingLabelStyle: const TextStyle(color: CgColors.primaryDark, fontWeight: FontWeight.w600),
      hintStyle: const TextStyle(color: CgColors.textMuted2, fontSize: 14),
      helperStyle: const TextStyle(color: CgColors.textMuted, fontSize: 12),
      prefixIconColor: CgColors.textMuted,
      suffixIconColor: CgColors.textMuted,
      border: field(CgColors.borderStrong),
      enabledBorder: field(CgColors.borderStrong),
      focusedBorder: field(CgColors.primary, 1.6),
      errorBorder: field(CgColors.error),
      focusedErrorBorder: field(CgColors.error, 1.6),
      disabledBorder: field(CgColors.border),
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      isDense: false,
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: CgColors.primaryDark,
        foregroundColor: Colors.white,
        disabledBackgroundColor: CgColors.primary.withValues(alpha: 0.38),
        disabledForegroundColor: Colors.white,
        elevation: 0,
        minimumSize: const Size(64, 46),
        padding: const EdgeInsets.symmetric(vertical: 13, horizontal: 20),
        shape: controlShape,
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14.5, letterSpacing: 0.1),
      ).copyWith(
        overlayColor: WidgetStatePropertyAll(Colors.white.withValues(alpha: 0.08)),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: CgColors.primaryDark,
        foregroundColor: Colors.white,
        minimumSize: const Size(64, 46),
        padding: const EdgeInsets.symmetric(vertical: 13, horizontal: 20),
        shape: controlShape,
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14.5),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: CgColors.text,
        backgroundColor: CgColors.card,
        side: const BorderSide(color: CgColors.borderStrong),
        minimumSize: const Size(64, 46),
        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 16),
        shape: controlShape,
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: CgColors.primaryDark,
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(CgRadius.sm)),
      ),
    ),
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(foregroundColor: CgColors.text2, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(CgRadius.md))),
    ),
    floatingActionButtonTheme: FloatingActionButtonThemeData(
      backgroundColor: CgColors.primaryDark,
      foregroundColor: Colors.white,
      elevation: 2,
      highlightElevation: 2,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(CgRadius.lg)),
      extendedTextStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14.5),
    ),
    listTileTheme: const ListTileThemeData(iconColor: CgColors.primaryDark, textColor: CgColors.text),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: CgColors.card,
      surfaceTintColor: Colors.transparent,
      indicatorColor: CgColors.primarySoft2,
      elevation: 0,
      height: 66,
      labelTextStyle: WidgetStateProperty.resolveWith((s) => TextStyle(
            fontSize: 12,
            fontWeight: s.contains(WidgetState.selected) ? FontWeight.w700 : FontWeight.w500,
            color: s.contains(WidgetState.selected) ? CgColors.primaryDeep : CgColors.textMuted,
          )),
      iconTheme: WidgetStateProperty.resolveWith(
          (s) => IconThemeData(size: 23, color: s.contains(WidgetState.selected) ? CgColors.primaryDeep : CgColors.textMuted)),
    ),
    navigationRailTheme: const NavigationRailThemeData(
      backgroundColor: CgColors.card,
      indicatorColor: CgColors.primarySoft2,
      selectedIconTheme: IconThemeData(color: CgColors.primaryDeep, size: 23),
      unselectedIconTheme: IconThemeData(color: CgColors.textMuted, size: 23),
      selectedLabelTextStyle: TextStyle(color: CgColors.primaryDeep, fontWeight: FontWeight.w700, fontSize: 12),
      unselectedLabelTextStyle: TextStyle(color: CgColors.textMuted, fontWeight: FontWeight.w500, fontSize: 12),
    ),
    tabBarTheme: const TabBarThemeData(
      labelColor: CgColors.primaryDeep,
      unselectedLabelColor: CgColors.textMuted,
      dividerColor: CgColors.border,
      indicatorSize: TabBarIndicatorSize.label,
      indicator: UnderlineTabIndicator(
        borderSide: BorderSide(width: 2.5, color: CgColors.primaryDark),
        borderRadius: BorderRadius.vertical(top: Radius.circular(2)),
      ),
      labelStyle: TextStyle(fontWeight: FontWeight.w700, fontSize: 14),
      unselectedLabelStyle: TextStyle(fontWeight: FontWeight.w500, fontSize: 14),
    ),
    dividerTheme: const DividerThemeData(color: CgColors.border, thickness: 1, space: 1),
    snackBarTheme: SnackBarThemeData(
      backgroundColor: CgColors.navy2,
      contentTextStyle: const TextStyle(color: Colors.white, fontSize: 13.5, fontWeight: FontWeight.w500),
      actionTextColor: CgColors.primarySoft2,
      behavior: SnackBarBehavior.floating,
      width: null,
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(CgRadius.md)),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: CgColors.card,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(CgRadius.xl)),
      titleTextStyle: CgText.h2,
      contentTextStyle: CgText.body,
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: CgColors.card,
      surfaceTintColor: Colors.transparent,
      constraints: BoxConstraints(maxWidth: 560),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(CgRadius.xl))),
    ),
    dropdownMenuTheme: const DropdownMenuThemeData(textStyle: CgText.bodyStrong),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: CgColors.primary, linearTrackColor: CgColors.surface3),
    textSelectionTheme: const TextSelectionThemeData(cursorColor: CgColors.primary, selectionHandleColor: CgColors.primary),
    checkboxTheme: CheckboxThemeData(shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(5))),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(color: CgColors.navy2, borderRadius: BorderRadius.circular(CgRadius.xs)),
      textStyle: const TextStyle(color: Colors.white, fontSize: 12),
    ),
    scrollbarTheme: ScrollbarThemeData(
      thumbColor: WidgetStatePropertyAll(CgColors.textMuted2.withValues(alpha: 0.5)),
      radius: const Radius.circular(8),
      thickness: const WidgetStatePropertyAll(6),
    ),
  );
}

/// Status pill. Always icon/dot + text, never colour alone.
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
      CgBadge(text, fg: CgColors.primaryDeep, bg: CgColors.primarySoft, borderColor: CgColors.primaryBorder, icon: icon);

  factory CgBadge.safety(CgSafetyStatus status) => CgBadge(CgSafety.label(status),
      fg: CgSafety.textColor(status), bg: CgSafety.soft(status), borderColor: CgSafety.borderColor(status), icon: CgSafety.icon(status));

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3.5),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(CgRadius.pill),
        border: Border.all(color: borderColor ?? fg.withValues(alpha: 0.18)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: fg),
            const SizedBox(width: 4),
          ],
          Flexible(
            child: Text(text,
                overflow: TextOverflow.ellipsis,
                maxLines: 1,
                style: TextStyle(color: fg, fontSize: 12, height: 1.35, fontWeight: FontWeight.w600)),
          ),
        ],
      ),
    );
  }
}
