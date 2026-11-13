import 'dart:io';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../services/guardian_api_service.dart';
import '../../widgets/photo_capture.dart';

/// Face registration with several reference photos, from the camera or the gallery. Each photo is
/// checked by the server first (one clear face, sharp, well lit, facing the camera) and can only be
/// added once it passes. Only a numeric representation is stored, never the photo.
class FaceRegistrationScreen extends StatefulWidget {
  const FaceRegistrationScreen({super.key});
  @override
  State<FaceRegistrationScreen> createState() => _FaceRegistrationScreenState();
}

class _FaceRegistrationScreenState extends State<FaceRegistrationScreen> {
  final _label = TextEditingController();
  FaceReferences? _refs;
  XFile? _photo;
  Uint8List? _bytes;
  FaceImageAnalysis? _analysis;
  bool _analyzing = false;
  bool _submitting = false;
  bool _loadingList = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadList();
  }

  @override
  void dispose() {
    _label.dispose();
    super.dispose();
  }

  GuardianApiService get _api => context.read<AuthProvider>().api;

  Future<void> _loadList() async {
    try {
      final refs = await _api.getFaceReferences();
      if (mounted) setState(() { _refs = refs; _loadingList = false; });
    } catch (_) {
      if (mounted) setState(() => _loadingList = false);
    }
  }

  Future<void> _pick(ImageSource source) async {
    // 1280 px keeps the face sharp for recognition without uploading a full 12 MP photo.
    final picked = await pickPhoto(context, source, preferredCamera: CameraDevice.front, maxSize: 1280, quality: 90);
    if (picked == null) return;
    final bytes = await picked.readAsBytes();
    setState(() { _photo = picked; _bytes = bytes; _analysis = null; _error = null; });
    await _analyze();
  }

  Future<void> _analyze() async {
    if (_bytes == null) return;
    setState(() { _analyzing = true; _error = null; });
    try {
      final result = await _api.analyzeFaceImage(encodeImageBytes(_bytes!));
      if (mounted) setState(() => _analysis = result);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not reach the server. Check your connection and try again.');
    } finally {
      if (mounted) setState(() => _analyzing = false);
    }
  }

  void _clearPhoto() => setState(() { _photo = null; _bytes = null; _analysis = null; _error = null; _label.clear(); });

  Future<void> _submit() async {
    if (_bytes == null || _analysis?.passed != true) return;
    final auth = context.read<AuthProvider>();
    setState(() { _submitting = true; _error = null; });
    try {
      final label = _label.text.trim();
      final result = await auth.api.addFaceReference(encodeImageBytes(_bytes!), label.isEmpty ? null : label);
      if (!mounted) return;
      if (result.added) {
        await auth.refreshProfile();
        _clearPhoto();
        await _loadList();
        if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(result.message)));
      } else {
        setState(() => _error = result.message);
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not reach the server. Check your connection and try again.');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _remove(FaceReference ref) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Remove this photo?'),
        content: const Text('You can still be verified with your other registered photos.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Keep')),
          TextButton(onPressed: () => Navigator.pop(c, true), child: const Text('Remove')),
        ],
      ),
    );
    if (ok != true) return;
    try {
      await _api.removeFaceReference(ref.id);
      await _loadList();
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Widget _checkRow(String label, bool pass) => Padding(
        padding: const EdgeInsets.only(bottom: 4),
        child: Row(children: [
          Icon(pass ? Icons.check_circle : Icons.cancel,
              size: 18, color: pass ? CgColors.successText : CgColors.errorText),
          const SizedBox(width: 8),
          Text(label),
        ]),
      );

  Widget _analysisCard() {
    final a = _analysis;
    if (_analyzing) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 12),
        child: Row(children: [
          SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2)),
          SizedBox(width: 12),
          Text('Checking the photo…'),
        ]),
      );
    }
    if (a == null) return const SizedBox.shrink();
    final detectionFailed = a.issues.any((i) => const ['NO_FACE', 'MULTIPLE_FACES', 'FACE_CUT_OFF', 'BAD_FORMAT', 'TOO_LARGE', 'LOW_RESOLUTION'].contains(i));
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      const SizedBox(height: 12),
      _checkRow('Face detected', !detectionFailed),
      _checkRow('Photo quality acceptable', a.issues.isEmpty),
      for (final m in a.messages)
        Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text(m, style: TextStyle(color: a.passed ? CgColors.textMuted : CgColors.errorText)),
        ),
      if (a.passed)
        const Padding(
          padding: EdgeInsets.only(top: 6),
          child: Text('This photo is suitable. Add it to register it.', style: TextStyle(color: CgColors.successText)),
        ),
    ]);
  }

  Widget _registeredList() {
    final refs = _refs;
    if (_loadingList) return const Padding(padding: EdgeInsets.all(12), child: Center(child: CircularProgressIndicator()));
    if (refs == null) return const SizedBox.shrink();
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text('Registered photos (${refs.references.length} of ${refs.maxReferences})',
          style: const TextStyle(fontWeight: FontWeight.w700)),
      const SizedBox(height: 8),
      if (refs.references.isEmpty)
        const Text('No face registered yet.', style: TextStyle(color: CgColors.textMuted)),
      for (final r in refs.references)
        ListTile(
          contentPadding: EdgeInsets.zero,
          leading: Icon(r.isPrimary ? Icons.verified_user_outlined : Icons.photo_outlined, color: CgColors.primaryDark),
          title: Text(r.label ?? (r.isPrimary ? 'First registered photo' : 'Reference photo')),
          subtitle: Text('Added ${r.createdAt.toLocal().toString().substring(0, 10)}'),
          trailing: r.isPrimary
              ? null
              : IconButton(
                  tooltip: 'Remove photo',
                  icon: const Icon(Icons.delete_outline),
                  onPressed: _submitting ? null : () => _remove(r),
                ),
        ),
    ]);
  }

  @override
  Widget build(BuildContext context) {
    final canAdd = _refs?.canAdd ?? true;
    final busy = _analyzing || _submitting;
    return Scaffold(
      appBar: AppBar(title: const Text('Face Registration')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            const Text(
              'Register your face so you can be verified for student pickup. Add a few photos in different '
              'light or from slightly different angles so verification copes with changes. '
              'Only a secure numeric representation is stored — your photos are not kept.',
              style: TextStyle(color: CgColors.textMuted),
            ),
            const SizedBox(height: 16),
            _registeredList(),
            const SizedBox(height: 16),
            if (!canAdd)
              const Text('You have reached the limit. Remove a photo to add another.',
                  style: TextStyle(color: CgColors.textMuted))
            else ...[
              AspectRatio(
                aspectRatio: 1,
                child: Container(
                  decoration: BoxDecoration(color: CgColors.primaryDark, borderRadius: BorderRadius.circular(12)),
                  child: _photo == null
                      ? const Center(child: Icon(Icons.face_retouching_natural, color: Colors.white54, size: 64))
                      : ClipRRect(borderRadius: BorderRadius.circular(12), child: Image.file(File(_photo!.path), fit: BoxFit.cover)),
                ),
              ),
              _analysisCard(),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Text(_error!, style: const TextStyle(color: CgColors.errorText)),
                ),
              const SizedBox(height: 12),
              Row(children: [
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: busy ? null : () => _pick(ImageSource.camera),
                    icon: const Icon(Icons.camera_alt_outlined),
                    label: Text(_photo == null ? 'Take photo' : 'Retake'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: busy ? null : () => _pick(ImageSource.gallery),
                    icon: const Icon(Icons.photo_library_outlined),
                    label: const Text('Choose photo'),
                  ),
                ),
              ]),
              if (_photo != null && !_analyzing && _analysis == null && _error != null)
                TextButton(onPressed: _analyze, child: const Text('Retry check')),
              if (_photo != null)
                TextButton(onPressed: busy ? null : _clearPhoto, child: const Text('Remove this photo')),
              const SizedBox(height: 8),
              TextField(
                controller: _label,
                maxLength: 60,
                decoration: const InputDecoration(labelText: 'Label (optional)', hintText: 'e.g. Outdoors, side light'),
              ),
              const SizedBox(height: 8),
              ElevatedButton(
                onPressed: (busy || _analysis?.passed != true) ? null : _submit,
                child: _submitting
                    ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : Text((_refs?.references.isEmpty ?? true) ? 'Register face' : 'Add photo'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
