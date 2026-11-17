import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'core/theme.dart';
import 'providers/auth_provider.dart';
import 'screens/splash/splash_screen.dart';
import 'widgets/cg_glass.dart';

void main() {
  runApp(const ChildGuardApp());
}

class ChildGuardApp extends StatelessWidget {
  const ChildGuardApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (_) => AuthProvider(),
      child: MaterialApp(
        title: 'ChildGuard',
        debugShowCheckedModeBanner: false,
        theme: buildCgTheme(),
        themeMode: ThemeMode.light,
        // One shared ambient canvas behind every route; scaffolds are transparent.
        builder: (context, child) => CgAmbientBackground(child: child ?? const SizedBox.shrink()),
        home: const SplashScreen(),
      ),
    );
  }
}
