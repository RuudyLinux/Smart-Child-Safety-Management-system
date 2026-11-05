import 'package:flutter/material.dart';

/// ChildGuard design system — shared visual language with the Admin Web.
/// Primary blue, deep navy, light neutral background, white cards. Status
/// always pairs a colour with a label or icon, never colour alone.
class CgColors {
  static const primary = Color(0xFF2563EB);
  static const primaryDark = Color(0xFF1D4ED8);
  static const primaryDeep = Color(0xFF1E40AF);
  static const primarySoft = Color(0xFFEFF6FF);
  static const primarySoft2 = Color(0xFFDBEAFE);

  static const navy = Color(0xFF0F172A);
  static const navy2 = Color(0xFF1E293B);

  static const background = Color(0xFFF8FAFC);
  static const card = Color(0xFFFFFFFF);
  static const surface2 = Color(0xFFF1F5F9);
  static const surface3 = Color(0xFFE2E8F0);

  static const text = Color(0xFF0F172A);
  static const text2 = Color(0xFF334155);
  static const textMuted = Color(0xFF64748B);
  static const textMuted2 = Color(0xFF94A3B8);
  static const border = Color(0xFFE2E8F0);
  static const borderStrong = Color(0xFFCBD5E1);

  static const success = Color(0xFF16A34A);
  static const successSoft = Color(0xFFF0FDF4);
  static const successBorder = Color(0xFFBBF7D0);

  static const warning = Color(0xFFB45309);
  static const warningAccent = Color(0xFFF59E0B);
  static const warningSoft = Color(0xFFFFFBEB);
  static const warningBorder = Color(0xFFFDE68A);

  static const error = Color(0xFFDC2626);
  static const errorSoft = Color(0xFFFEF2F2);
  static const errorBorder = Color(0xFFFECACA);

  static const info = Color(0xFF0EA5E9);
  static const infoSoft = Color(0xFFF0F9FF);
  static const infoBorder = Color(0xFFBAE6FD);

  static const guardianSoft = Color(0xFFE0E7FF);
  static const guardianText = Color(0xFF4338CA);

  // Backward-compatible aliases used by not-yet-migrated screens.
  static const primaryLight = primarySoft;
  static const successBg = successSoft;
  static const warningBg = warningSoft;
  static const errorBg = errorSoft;
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

  static Color color(CgSafetyStatus s) => switch (s) {
        CgSafetyStatus.atSchool || CgSafetyStatus.droppedOff => CgColors.success,
        CgSafetyStatus.onBus => CgColors.info,
        CgSafetyStatus.leftSchool => CgColors.warningAccent,
        CgSafetyStatus.noActivity => CgColors.textMuted2,
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

ThemeData buildCgTheme() {
  final base = ThemeData(useMaterial3: true, fontFamily: 'Roboto');
  return base.copyWith(
    scaffoldBackgroundColor: CgColors.background,
    colorScheme: ColorScheme.fromSeed(
      seedColor: CgColors.primary,
      primary: CgColors.primary,
      surface: CgColors.card,
      error: CgColors.error,
      brightness: Brightness.light,
    ),
    appBarTheme: const AppBarTheme(
      backgroundColor: CgColors.background,
      foregroundColor: CgColors.text,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(color: CgColors.text, fontSize: 19, fontWeight: FontWeight.w800, letterSpacing: -0.3),
      iconTheme: IconThemeData(color: CgColors.text2),
    ),
    cardTheme: CardThemeData(
      color: CgColors.card,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: CgColors.border),
      ),
      margin: const EdgeInsets.symmetric(vertical: 6),
      clipBehavior: Clip.antiAlias,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: CgColors.card,
      labelStyle: const TextStyle(color: CgColors.textMuted),
      hintStyle: const TextStyle(color: CgColors.textMuted2),
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: CgColors.border)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: CgColors.border)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: CgColors.primary, width: 1.6)),
      errorBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: CgColors.error)),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: CgColors.primary,
        foregroundColor: Colors.white,
        disabledBackgroundColor: CgColors.primary.withValues(alpha: 0.4),
        elevation: 0,
        padding: const EdgeInsets.symmetric(vertical: 16),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15.5),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: CgColors.primary,
        side: const BorderSide(color: CgColors.borderStrong),
        padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 18),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14.5),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(foregroundColor: CgColors.primary, textStyle: const TextStyle(fontWeight: FontWeight.w600)),
    ),
    listTileTheme: const ListTileThemeData(iconColor: CgColors.primary, textColor: CgColors.text),
    bottomNavigationBarTheme: const BottomNavigationBarThemeData(
      backgroundColor: CgColors.card,
      selectedItemColor: CgColors.primary,
      unselectedItemColor: CgColors.textMuted2,
      type: BottomNavigationBarType.fixed,
      elevation: 0,
      selectedLabelStyle: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w700),
      unselectedLabelStyle: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600),
    ),
    dividerTheme: const DividerThemeData(color: CgColors.border, thickness: 1, space: 1),
    snackBarTheme: SnackBarThemeData(
      backgroundColor: CgColors.navy,
      contentTextStyle: const TextStyle(color: Colors.white, fontSize: 13.5),
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
    ),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: CgColors.primary),
    textSelectionTheme: const TextSelectionThemeData(cursorColor: CgColors.primary, selectionHandleColor: CgColors.primary),
  );
}

/// Status pill matching the Admin Web's cg-badge component. Always icon/dot + text.
class CgBadge extends StatelessWidget {
  final String text;
  final Color fg;
  final Color bg;
  final IconData? icon;
  const CgBadge(this.text, {required this.fg, required this.bg, this.icon, super.key});

  factory CgBadge.success(String text, {IconData? icon}) => CgBadge(text, fg: CgColors.success, bg: CgColors.successSoft, icon: icon);
  factory CgBadge.warning(String text, {IconData? icon}) => CgBadge(text, fg: CgColors.warning, bg: CgColors.warningSoft, icon: icon);
  factory CgBadge.error(String text, {IconData? icon}) => CgBadge(text, fg: CgColors.error, bg: CgColors.errorSoft, icon: icon);
  factory CgBadge.info(String text, {IconData? icon}) => CgBadge(text, fg: const Color(0xFF0369A1), bg: CgColors.infoSoft, icon: icon);
  factory CgBadge.neutral(String text, {IconData? icon}) => CgBadge(text, fg: CgColors.text2, bg: CgColors.surface2, icon: icon);
  factory CgBadge.primary(String text, {IconData? icon}) => CgBadge(text, fg: CgColors.primaryDeep, bg: CgColors.primarySoft2, icon: icon);

  factory CgBadge.safety(CgSafetyStatus status) =>
      CgBadge(CgSafety.label(status), fg: CgSafety.color(status), bg: CgSafety.soft(status), icon: CgSafety.icon(status));

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 5),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(999)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: fg),
            const SizedBox(width: 5),
          ],
          Text(text, style: TextStyle(color: fg, fontSize: 12.5, fontWeight: FontWeight.w700)),
        ],
      ),
    );
  }
}
