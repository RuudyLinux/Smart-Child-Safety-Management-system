import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'core/theme.dart';
import 'providers/auth_provider.dart';
import 'screens/splash/splash_screen.dart';

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
        home: const SplashScreen(),
      ),
    );
  }
}
