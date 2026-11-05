import 'package:flutter/material.dart';
import '../core/theme.dart';

/// One avatar for the whole app. Shows the photo when a URL is given; falls
/// back to initials on a soft brand background when there is none, or when
/// the image fails to load — so a network hiccup never shows a broken icon
/// on a child's record.
class CgAvatar extends StatelessWidget {
  final String name;
  final String? photoUrl;
  final Map<String, String>? headers;
  final double size;
  final bool guardian;
  final bool square;
  final Color? statusDot;

  const CgAvatar({
    required this.name,
    this.photoUrl,
    this.headers,
    this.size = 44,
    this.guardian = false,
    this.square = false,
    this.statusDot,
    super.key,
  });

  String get _initials {
    final parts = name.trim().split(RegExp(r'\s+')).where((p) => p.isNotEmpty).toList();
    if (parts.isEmpty) return '?';
    if (parts.length == 1) return parts.first.substring(0, 1).toUpperCase();
    return (parts.first.substring(0, 1) + parts.last.substring(0, 1)).toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    final radius = square ? size * 0.28 : size / 2;
    final bg = guardian ? CgColors.guardianSoft : CgColors.primarySoft2;
    final fg = guardian ? CgColors.guardianText : CgColors.primaryDeep;

    Widget content = Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(radius),
        border: Border.all(color: CgColors.border),
      ),
      child: Text(
        _initials,
        style: TextStyle(color: fg, fontWeight: FontWeight.w800, fontSize: size * 0.36),
      ),
    );

    if (photoUrl != null && photoUrl!.isNotEmpty) {
      final cachePx = (size * MediaQuery.devicePixelRatioOf(context)).round();
      content = ClipRRect(
        borderRadius: BorderRadius.circular(radius),
        child: Stack(
          fit: StackFit.expand,
          children: [
            content,
            Image.network(
              photoUrl!,
              headers: headers,
              width: size,
              height: size,
              fit: BoxFit.cover,
              cacheWidth: cachePx,
              cacheHeight: cachePx,
              loadingBuilder: (context, child, progress) => progress == null ? child : const SizedBox.shrink(),
              errorBuilder: (context, error, stack) => const SizedBox.shrink(),
            ),
          ],
        ),
      );
    }

    if (statusDot == null) return content;

    return Stack(
      clipBehavior: Clip.none,
      children: [
        content,
        Positioned(
          right: -1,
          bottom: -1,
          child: Container(
            width: size * 0.28,
            height: size * 0.28,
            decoration: BoxDecoration(
              color: statusDot,
              shape: BoxShape.circle,
              border: Border.all(color: CgColors.background, width: 2),
            ),
          ),
        ),
      ],
    );
  }
}
