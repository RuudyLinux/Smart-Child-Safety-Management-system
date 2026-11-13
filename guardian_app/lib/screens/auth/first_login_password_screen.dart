import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import 'login_screen.dart';
import '../../widgets/cg_auth_layout.dart';

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

    return CgAuthLayout(
      title: 'Set a new password',
      subtitle: 'You signed in with a default password. Choose your own password to continue.',
      error: error,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextField(
            controller: _newPasswordCtrl,
            obscureText: true,
            autofillHints: const [AutofillHints.newPassword],
            decoration: const InputDecoration(
              labelText: 'New password',
              helperText: 'At least 8 characters, one uppercase, one digit',
              prefixIcon: Icon(Icons.lock_outline_rounded),
            ),
          ),
          const SizedBox(height: CgSpace.md),
          TextField(
            controller: _confirmCtrl,
            obscureText: true,
            onSubmitted: (_) => _loading ? null : _submit(),
            decoration: const InputDecoration(labelText: 'Confirm password', prefixIcon: Icon(Icons.lock_outline_rounded)),
          ),
          const SizedBox(height: CgSpace.xl),
          ElevatedButton(
            onPressed: _loading ? null : _submit,
            child: _loading
                ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                : const Text('Save and continue'),
          ),
        ],
      ),
    );
  }
}
