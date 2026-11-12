import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_glass.dart';
import '../auth/login_screen.dart';
import '../home/home_shell.dart';

class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});
  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> with SingleTickerProviderStateMixin {
  late final AnimationController _intro = AnimationController(vsync: this, duration: const Duration(milliseconds: 900))..forward();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _bootstrap());
  }

  @override
  void dispose() {
    _intro.dispose();
    super.dispose();
  }

  Future<void> _bootstrap() async {
    final auth = context.read<AuthProvider>();
    await auth.bootstrap();
    if (!mounted) return;
    Navigator.of(context).pushReplacement(MaterialPageRoute(
      builder: (_) => auth.status == AuthStatus.authenticated ? const HomeShell() : const LoginScreen(),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final mark = CurvedAnimation(parent: _intro, curve: const Interval(0, 0.7, curve: Curves.easeOutBack));
    final text = CurvedAnimation(parent: _intro, curve: const Interval(0.3, 1, curve: Curves.easeOutCubic));
    return Scaffold(
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ScaleTransition(
              scale: Tween(begin: 0.6, end: 1.0).animate(mark),
              child: FadeTransition(
                opacity: _intro,
                child: Container(
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: Colors.white.withValues(alpha: 0.6),
                    border: Border.all(color: Colors.white),
                    boxShadow: CgShadows.md,
                  ),
                  child: const CgBrandMark(size: 78),
                ),
              ),
            ),
            const SizedBox(height: 22),
            FadeTransition(
              opacity: text,
              child: SlideTransition(
                position: Tween(begin: const Offset(0, 0.4), end: Offset.zero).animate(text),
                child: Column(
                  children: [
                    RichText(
                      text: const TextSpan(
                        style: TextStyle(fontSize: 30, fontWeight: FontWeight.w800, letterSpacing: -0.8),
                        children: [
                          TextSpan(text: 'Child', style: TextStyle(color: CgColors.text)),
                          TextSpan(text: 'Guard', style: TextStyle(color: CgColors.primary)),
                        ],
                      ),
                    ),
                    const SizedBox(height: 6),
                    const Text('Your child, always accounted for',
                        style: TextStyle(color: CgColors.textMuted, fontSize: 13.5, fontWeight: FontWeight.w600)),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 36),
            const SizedBox(
              width: 26,
              height: 26,
              child: CircularProgressIndicator(strokeWidth: 2.6, strokeCap: StrokeCap.round),
            ),
          ],
        ),
      ),
    );
  }
}
