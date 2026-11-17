import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';
import '../../core/theme.dart';
import '../../widgets/cg_auth_layout.dart';
import '../../widgets/cg_ui.dart';

class ForgotPasswordScreen extends StatefulWidget {
  const ForgotPasswordScreen({super.key});
  @override
  State<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends State<ForgotPasswordScreen> {
  final _emailCtrl = TextEditingController();
  final _tokenCtrl = TextEditingController();
  final _newPasswordCtrl = TextEditingController();
  bool _requested = false;
  bool _loading = false;
  String? _message;

  @override
  void dispose() {
    _emailCtrl.dispose();
    _tokenCtrl.dispose();
    _newPasswordCtrl.dispose();
    super.dispose();
  }

  Future<void> _requestToken() async {
    setState(() => _loading = true);
    try {
      await context.read<AuthProvider>().api.forgotPassword(_emailCtrl.text.trim());
      if (mounted) setState(() { _requested = true; _message = 'If the account exists, a reset token has been sent.'; });
    } on ApiException catch (e) {
      if (mounted) setState(() => _message = e.message);
    } catch (_) {
      if (mounted) setState(() => _message = 'Could not reach server. Check your connection and try again.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _resetPassword() async {
    setState(() => _loading = true);
    try {
      await context.read<AuthProvider>().api.resetPassword(_emailCtrl.text.trim(), _tokenCtrl.text.trim(), _newPasswordCtrl.text);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Password reset. Please sign in.')));
        Navigator.of(context).pop();
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _message = e.message);
    } catch (_) {
      if (mounted) setState(() => _message = 'Could not reach server. Check your connection and try again.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return CgAuthLayout(
      title: 'Reset your password',
      subtitle: _requested
          ? 'Enter the reset token from your email and choose a new password.'
          : 'Enter the email on your account and we will send you a reset token.',
      showBack: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (_message != null) CgAlert(tone: _requested ? CgTone.info : CgTone.neutral, title: _message!),
          TextField(
            controller: _emailCtrl,
            enabled: !_requested,
            keyboardType: TextInputType.emailAddress,
            autofillHints: const [AutofillHints.email],
            decoration: const InputDecoration(labelText: 'Email', prefixIcon: Icon(Icons.mail_outline_rounded)),
          ),
          const SizedBox(height: CgSpace.md),
          if (!_requested)
            ElevatedButton(
              onPressed: _loading ? null : _requestToken,
              child: _loading
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Text('Send reset token'),
            )
          else ...[
            TextField(
              controller: _tokenCtrl,
              decoration: const InputDecoration(labelText: 'Reset token', prefixIcon: Icon(Icons.key_outlined)),
            ),
            const SizedBox(height: CgSpace.md),
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
            const SizedBox(height: CgSpace.xl),
            ElevatedButton(
              onPressed: _loading ? null : _resetPassword,
              child: _loading
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Text('Reset password'),
            ),
          ],
        ],
      ),
    );
  }
}
