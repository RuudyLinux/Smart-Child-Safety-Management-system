import 'package:flutter/material.dart';

/// ChildGuard design system — shared visual language with the Admin Web.
/// Deep blue primary, light neutral background, white cards, dark navy text.
/// No gradients, no decorative graphics.
class CgColors {
  static const primary = Color(0xFF1E3A5F);
  static const primaryDark = Color(0xFF142A46);
  static const primaryLight = Color(0xFFEAF0F7);
  static const background = Color(0xFFF4F6F8);
  static const card = Color(0xFFFFFFFF);
  static const text = Color(0xFF14213D);
  static const textMuted = Color(0xFF5B6B82);
  static const border = Color(0xFFE1E6ED);
  static const success = Color(0xFF1E7A34);
  static const successBg = Color(0xFFE7F5EA);
  static const warning = Color(0xFFB5590A);
  static const warningBg = Color(0xFFFCF0E3);
  static const error = Color(0xFFB3261E);
  static const errorBg = Color(0xFFFBEAE9);
}

ThemeData buildCgTheme() {
  return ThemeData(
    useMaterial3: true,
    scaffoldBackgroundColor: CgColors.background,
    colorScheme: ColorScheme.fromSeed(
      seedColor: CgColors.primary,
      primary: CgColors.primary,
      surface: CgColors.card,
      error: CgColors.error,
    ),
    appBarTheme: const AppBarTheme(
      backgroundColor: CgColors.card,
      foregroundColor: CgColors.text,
      elevation: 0,
      scrolledUnderElevation: 1,
      titleTextStyle: TextStyle(color: CgColors.text, fontSize: 17, fontWeight: FontWeight.w700),
    ),
    cardTheme: CardThemeData(
      color: CgColors.card,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: const BorderSide(color: CgColors.border),
      ),
      margin: const EdgeInsets.symmetric(vertical: 6),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: CgColors.card,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: const BorderSide(color: CgColors.border)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: const BorderSide(color: CgColors.border)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: const BorderSide(color: CgColors.primary, width: 1.5)),
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: CgColors.primary,
        foregroundColor: Colors.white,
        elevation: 0,
        padding: const EdgeInsets.symmetric(vertical: 14),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15),
      ),
    ),
    bottomNavigationBarTheme: const BottomNavigationBarThemeData(
      backgroundColor: CgColors.card,
      selectedItemColor: CgColors.primary,
      unselectedItemColor: CgColors.textMuted,
      type: BottomNavigationBarType.fixed,
      elevation: 8,
    ),
    fontFamily: 'Roboto',
  );
}

/// Status badge matching the Admin Web's cg-badge component.
class CgBadge extends StatelessWidget {
  final String text;
  final Color fg;
  final Color bg;
  const CgBadge(this.text, {required this.fg, required this.bg, super.key});

  factory CgBadge.success(String text) => CgBadge(text, fg: CgColors.success, bg: CgColors.successBg);
  factory CgBadge.warning(String text) => CgBadge(text, fg: CgColors.warning, bg: CgColors.warningBg);
  factory CgBadge.error(String text) => CgBadge(text, fg: CgColors.error, bg: CgColors.errorBg);
  factory CgBadge.neutral(String text) => CgBadge(text, fg: CgColors.primaryDark, bg: CgColors.primaryLight);

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(999)),
      child: Text(text, style: TextStyle(color: fg, fontSize: 12, fontWeight: FontWeight.w600)),
    );
  }
}
