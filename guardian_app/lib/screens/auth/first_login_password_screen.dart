import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import 'login_screen.dart';

/// Mandatory reset of the admin-supplied default password. Reachable only with a
/// reset ticket issued after OTP verification; the server rejects the submission
/// without one, so this screen cannot be used to bypass anything.
class FirstLoginPasswordScreen extends StatefulWidget {
  const FirstLoginPasswordScreen({super.key});
  @override
  State<FirstLoginPasswordScreen> createState() => _FirstLoginPasswordScreenState();
}

class _FirstLoginPasswordScreenState extends State<FirstLoginPasswordScreen> {
  final _newPasswordCtrl = TextEditingController();
  final _confirmCtrl = TextEditingController();
  bool _loading = false;
  String? _validationError;

  @override
  void dispose() {
    _newPasswordCtrl.dispose();
    _confirmCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _validationError = _localValidationError();
      _loading = _validationError == null;
    });
    if (_validationError != null) return;

    final auth = context.read<AuthProvider>();
    final ok = await auth.completeFirstLoginReset(_newPasswordCtrl.text, _confirmCtrl.text);
    if (!mounted) return;
    setState(() => _loading = false);
    if (!ok) return;

    // Rule: after a successful reset the Guardian signs in again with the new password.
    ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Password updated. Please sign in with your new password.')));
    Navigator.of(context).pushAndRemoveUntil(
        MaterialPageRoute(builder: (_) => const LoginScreen()), (_) => false);
  }

  String? _localValidationError() {
    final password = _newPasswordCtrl.text;
    if (password.length < 8) return 'Password must be at least 8 characters.';
    if (!password.contains(RegExp('[A-Z]'))) return 'Password must contain an uppercase letter.';
    if (!password.contains(RegExp('[0-9]'))) return 'Password must contain a digit.';
    if (password != _confirmCtrl.text) return 'Passwords do not match.';
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final error = _validationError ?? auth.lastError;

    return Scaffold(
      appBar: AppBar(title: const Text('Set a New Password'), automaticallyImplyLeading: false),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            const Text('You signed in with a default password. Choose your own password to continue.',
                style: TextStyle(color: CgColors.textMuted)),
            const SizedBox(height: 20),
            if (error != null)
              Container(
                padding: const EdgeInsets.all(12),
                margin: const EdgeInsets.only(bottom: 16),
                decoration: BoxDecoration(color: CgColors.errorBg, borderRadius: BorderRadius.circular(8)),
                child: Text(error, style: const TextStyle(color: CgColors.error)),
              ),
            TextField(
              controller: _newPasswordCtrl,
              obscureText: true,
              decoration: const InputDecoration(
                labelText: 'New Password',
                helperText: 'At least 8 characters, one uppercase, one digit',
              ),
            ),
            const SizedBox(height: 14),
            TextField(
              controller: _confirmCtrl,
              obscureText: true,
              decoration: const InputDecoration(labelText: 'Confirm Password'),
            ),
            const SizedBox(height: 20),
            ElevatedButton(
              onPressed: _loading ? null : _submit,
              child: _loading
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Text('Submit'),
            ),
          ],
        ),
      ),
    );
  }
}
