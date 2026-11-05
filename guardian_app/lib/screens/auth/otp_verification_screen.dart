import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../home/home_shell.dart';
import 'first_login_password_screen.dart';

/// Step 2 of Guardian sign-in. Reached only after credentials validate, and the
/// server is what decides whether the next screen is the dashboard or the
/// mandatory first-login password reset.
class OtpVerificationScreen extends StatefulWidget {
  const OtpVerificationScreen({super.key});
  @override
  State<OtpVerificationScreen> createState() => _OtpVerificationScreenState();
}

class _OtpVerificationScreenState extends State<OtpVerificationScreen> {
  final _codeCtrl = TextEditingController();
  Timer? _ticker;
  int _resendIn = 0;
  int _expiresIn = 0;
  bool _loading = false;

  @override
  void initState() {
    super.initState();
    // Seed from the server-issued challenge once; _syncCountdowns only ever
    // counts down from here, it never re-reads resendAvailableInSeconds.
    _resendIn = context.read<AuthProvider>().challenge?.resendAvailableInSeconds ?? 0;
    _syncCountdowns();
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) => _syncCountdowns());
  }

  @override
  void dispose() {
    _ticker?.cancel();
    _codeCtrl.dispose();
    super.dispose();
  }

  void _syncCountdowns() {
    final challenge = context.read<AuthProvider>().challenge;
    if (challenge == null) {
      setState(() { _resendIn = 0; _expiresIn = 0; });
      return;
    }
    final expiresIn = challenge.expiresAtUtc.difference(DateTime.now().toUtc()).inSeconds;
    setState(() {
      _expiresIn = expiresIn > 0 ? expiresIn : 0;
      if (_resendIn > 0) _resendIn--;
    });
  }

  Future<void> _verify() async {
    setState(() => _loading = true);
    final auth = context.read<AuthProvider>();
    final stage = await auth.verifyOtp(_codeCtrl.text.trim());
    if (!mounted) return;
    setState(() => _loading = false);

    switch (stage) {
      case GuardianAuthStage.authenticated:
        Navigator.of(context).pushAndRemoveUntil(
            MaterialPageRoute(builder: (_) => const HomeShell()), (_) => false);
      case GuardianAuthStage.passwordResetRequired:
        Navigator.of(context).pushReplacement(
            MaterialPageRoute(builder: (_) => const FirstLoginPasswordScreen()));
      case GuardianAuthStage.otpRequired:
      case null:
        break; // error is shown from auth.lastError
    }
  }

  Future<void> _resend() async {
    setState(() => _loading = true);
    final auth = context.read<AuthProvider>();
    final ok = await auth.resendOtp();
    if (!mounted) return;
    setState(() {
      _loading = false;
      _resendIn = ok ? (auth.challenge?.resendAvailableInSeconds ?? 60) : _resendIn;
    });
    if (ok) {
      ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('A new code has been sent to your contact number.')));
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final challenge = auth.challenge;
    final codeLength = challenge?.codeLength ?? 6;
    final expired = _expiresIn == 0;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Verify OTP'),
        leading: BackButton(onPressed: () {
          context.read<AuthProvider>().clearAuthFlow();
          Navigator.of(context).pop();
        }),
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            const Text('Enter the code we sent to your registered contact number.',
                style: TextStyle(color: CgColors.textMuted)),
            const SizedBox(height: 20),
            if (auth.lastError != null)
              Container(
                padding: const EdgeInsets.all(12),
                margin: const EdgeInsets.only(bottom: 16),
                decoration: BoxDecoration(color: CgColors.errorBg, borderRadius: BorderRadius.circular(8)),
                child: Text(auth.lastError!, style: const TextStyle(color: CgColors.error)),
              ),
            if (expired && auth.lastError == null)
              Container(
                padding: const EdgeInsets.all(12),
                margin: const EdgeInsets.only(bottom: 16),
                decoration: BoxDecoration(color: CgColors.errorBg, borderRadius: BorderRadius.circular(8)),
                child: const Text('This code has expired. Request a new one.',
                    style: TextStyle(color: CgColors.error)),
              ),
            TextField(
              controller: _codeCtrl,
              keyboardType: TextInputType.number,
              maxLength: codeLength,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly],
              decoration: InputDecoration(
                labelText: 'OTP',
                counterText: '',
                helperText: expired ? null : 'Expires in ${_formatSeconds(_expiresIn)}',
              ),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              onPressed: _loading ? null : _verify,
              child: _loading
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Text('Verify OTP'),
            ),
            const SizedBox(height: 8),
            TextButton(
              onPressed: (_loading || _resendIn > 0 || challenge == null) ? null : _resend,
              child: Text(_resendIn > 0 ? 'Resend OTP in ${_formatSeconds(_resendIn)}' : 'Resend OTP'),
            ),
            if (challenge != null)
              Center(
                child: Text('${challenge.resendsRemaining} resend(s) remaining',
                    style: const TextStyle(color: CgColors.textMuted, fontSize: 12)),
              ),
          ],
        ),
      ),
    );
  }

  static String _formatSeconds(int seconds) {
    final m = seconds ~/ 60;
    final s = seconds % 60;
    return m > 0 ? '${m}m ${s}s' : '${s}s';
  }
}
