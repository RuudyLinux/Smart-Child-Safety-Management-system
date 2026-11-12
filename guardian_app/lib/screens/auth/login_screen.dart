import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_glass.dart';
import '../../models/models.dart';
import '../home/home_shell.dart';
import 'first_login_password_screen.dart';
import 'otp_verification_screen.dart';
import 'register_screen.dart';
import 'forgot_password_screen.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _contactCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  bool _loading = false;
  bool _obscure = true;

  @override
  void dispose() {
    _contactCtrl.dispose();
    _passwordCtrl.dispose();
    super.dispose();
  }

  /// The password step. The server decides what happens next: an emailed code (new phone), or on a phone
  /// it already trusts straight in / to the first-login password reset.
  Future<void> _submit() async {
    setState(() => _loading = true);
    final auth = context.read<AuthProvider>();
    final stage = await auth.login(_contactCtrl.text.trim(), _passwordCtrl.text);
    if (!mounted) return;
    setState(() => _loading = false);
    switch (stage) {
      case GuardianAuthStage.otpRequired:
        Navigator.of(context).push(MaterialPageRoute(builder: (_) => const OtpVerificationScreen()));
      case GuardianAuthStage.passwordResetRequired:
        Navigator.of(context).push(MaterialPageRoute(builder: (_) => const FirstLoginPasswordScreen()));
      case GuardianAuthStage.authenticated:
        Navigator.of(context).pushAndRemoveUntil(MaterialPageRoute(builder: (_) => const HomeShell()), (_) => false);
      case null:
        break; // the reason is shown from auth.lastError
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 400),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const CgFadeIn(child: Align(alignment: Alignment.centerLeft, child: CgBrandMark(size: 58))),
                  const SizedBox(height: 20),
                  const Text('Welcome back',
                      style: TextStyle(fontSize: 28, fontWeight: FontWeight.w800, color: CgColors.text, letterSpacing: -0.4)),
                  const SizedBox(height: 4),
                  const Text('Sign in to see your children\'s safety status.', style: TextStyle(color: CgColors.textMuted, fontSize: 13.5)),
                  const SizedBox(height: 28),
                  CgFadeIn(
                    index: 1,
                    child: CgGlass(
                      strong: true,
                      radius: 24,
                      padding: const EdgeInsets.fromLTRB(18, 20, 18, 18),
                      shadow: CgShadows.md,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          if (auth.lastError != null)
                            Container(
                              padding: const EdgeInsets.all(13),
                              margin: const EdgeInsets.only(bottom: 16),
                              decoration: BoxDecoration(
                                  color: CgColors.errorSoft,
                                  borderRadius: BorderRadius.circular(12),
                                  border: Border.all(color: CgColors.errorBorder)),
                              child: Row(
                                children: [
                                  const Icon(Icons.error_outline_rounded, color: CgColors.errorText, size: 18),
                                  const SizedBox(width: 10),
                                  Expanded(child: Text(auth.lastError!, style: const TextStyle(color: CgColors.errorText, fontSize: 13))),
                                ],
                              ),
                            ),
                          TextField(
                            controller: _contactCtrl,
                            decoration: const InputDecoration(labelText: 'Contact number', prefixIcon: Icon(Icons.phone_outlined)),
                            keyboardType: TextInputType.phone,
                            autofillHints: const [AutofillHints.telephoneNumber],
                          ),
                          const SizedBox(height: 14),
                          TextField(
                            controller: _passwordCtrl,
                            decoration: InputDecoration(
                              labelText: 'Password',
                              prefixIcon: const Icon(Icons.lock_outline_rounded),
                              suffixIcon: IconButton(
                                icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined, size: 20),
                                tooltip: _obscure ? 'Show password' : 'Hide password',
                                onPressed: () => setState(() => _obscure = !_obscure),
                              ),
                            ),
                            obscureText: _obscure,
                            onSubmitted: (_) => _loading ? null : _submit(),
                          ),
                          Align(
                            alignment: Alignment.centerRight,
                            child: TextButton(
                              onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ForgotPasswordScreen())),
                              child: const Text('Forgot password?'),
                            ),
                          ),
                          const SizedBox(height: 6),
                          ElevatedButton(
                            onPressed: _loading ? null : _submit,
                            child: _loading
                                ? const SizedBox(
                                    height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                                : const Text('Sign in'),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 20),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Text("Don't have an account?", style: TextStyle(color: CgColors.textMuted, fontSize: 13.5)),
                      TextButton(
                        onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const RegisterScreen())),
                        child: const Text('Register'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
