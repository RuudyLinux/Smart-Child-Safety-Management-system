import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../providers/auth_provider.dart';

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

  Future<void> _requestToken() async {
    setState(() => _loading = true);
    try {
      await context.read<AuthProvider>().api.forgotPassword(_emailCtrl.text.trim());
      setState(() { _requested = true; _message = 'If the account exists, a reset token has been sent.'; });
    } on ApiException catch (e) {
      setState(() => _message = e.message);
    } finally {
      setState(() => _loading = false);
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
      setState(() => _message = e.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Forgot Password')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            if (_message != null) Padding(padding: const EdgeInsets.only(bottom: 16), child: Text(_message!)),
            TextField(controller: _emailCtrl, decoration: const InputDecoration(labelText: 'Email'), enabled: !_requested),
            const SizedBox(height: 14),
            if (!_requested)
              ElevatedButton(
                onPressed: _loading ? null : _requestToken,
                child: const Text('Send Reset Token'),
              )
            else ...[
              TextField(controller: _tokenCtrl, decoration: const InputDecoration(labelText: 'Reset Token')),
              const SizedBox(height: 14),
              TextField(controller: _newPasswordCtrl, decoration: const InputDecoration(labelText: 'New Password'), obscureText: true),
              const SizedBox(height: 20),
              ElevatedButton(
                onPressed: _loading ? null : _resetPassword,
                child: const Text('Reset Password'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
