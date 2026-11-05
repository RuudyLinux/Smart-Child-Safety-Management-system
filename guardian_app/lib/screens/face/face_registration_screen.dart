import 'dart:io';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../services/guardian_api_service.dart';

/// Real device camera capture via image_picker — no simulated/mock face
/// image. The captured photo is sent to ChildGuard.Api, which forwards it
/// to the Python face-recognition service; only the resulting embedding is
/// stored, never the photo itself.
class FaceRegistrationScreen extends StatefulWidget {
  const FaceRegistrationScreen({super.key});
  @override
  State<FaceRegistrationScreen> createState() => _FaceRegistrationScreenState();
}

class _FaceRegistrationScreenState extends State<FaceRegistrationScreen> {
  XFile? _photo;
  bool _submitting = false;
  String? _error;

  Future<void> _capture() async {
    final picked = await ImagePicker().pickImage(source: ImageSource.camera, preferredCameraDevice: CameraDevice.front, imageQuality: 85);
    if (picked != null) setState(() { _photo = picked; _error = null; });
  }

  Future<void> _submit() async {
    if (_photo == null) return;
    final auth = context.read<AuthProvider>();
    setState(() { _submitting = true; _error = null; });
    try {
      final bytes = await _photo!.readAsBytes();
      final b64 = encodeImageBytes(bytes);
      await auth.api.registerFace(b64);
      await auth.refreshProfile();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Face registered successfully.')));
        Navigator.of(context).pop();
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not reach server. Check your connection and try again.');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Face Registration')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            const Text(
              'Register your face so you can be verified for student pickup. '
              'Only a secure numeric representation is stored — your photo is not kept.',
              style: TextStyle(color: CgColors.textMuted),
            ),
            const SizedBox(height: 20),
            AspectRatio(
              aspectRatio: 1,
              child: Container(
                decoration: BoxDecoration(color: CgColors.primaryDark, borderRadius: BorderRadius.circular(12)),
                child: _photo == null
                    ? const Center(child: Icon(Icons.face_retouching_natural, color: Colors.white54, size: 64))
                    : ClipRRect(borderRadius: BorderRadius.circular(12), child: Image.file(File(_photo!.path), fit: BoxFit.cover)),
              ),
            ),
            const SizedBox(height: 16),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text(_error!, style: const TextStyle(color: CgColors.error)),
              ),
            OutlinedButton.icon(onPressed: _capture, icon: const Icon(Icons.camera_alt_outlined), label: Text(_photo == null ? 'Capture Photo' : 'Retake Photo')),
            const SizedBox(height: 12),
            ElevatedButton(
              onPressed: (_photo == null || _submitting) ? null : _submit,
              child: _submitting ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white)) : const Text('Register Face'),
            ),
          ],
        ),
      ),
    );
  }
}
