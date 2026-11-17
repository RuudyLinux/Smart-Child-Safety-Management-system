import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show FilteringTextInputFormatter;
import 'package:image_picker/image_picker.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../services/guardian_api_service.dart';
import '../../widgets/cg_ui.dart';
import '../../widgets/photo_capture.dart';

/// Request form for a temporary pickup person. The photo is mandatory; the window is limited to 24 hours
/// (the server enforces the same limits, this just fails early and says why).
class NewTemporaryPickupScreen extends StatefulWidget {
  const NewTemporaryPickupScreen({super.key});

  @override
  State<NewTemporaryPickupScreen> createState() => _NewTemporaryPickupScreenState();
}

class _NewTemporaryPickupScreenState extends State<NewTemporaryPickupScreen> {
  static const _maxHours = 24;

  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _relationship = TextEditingController();
  final _idNote = TextEditingController();
  final _notes = TextEditingController();

  int? _studentId;
  late DateTime _from = _roundUp(DateTime.now());
  late DateTime _until = _from.add(const Duration(hours: 3));
  List<int>? _photoBytes;
  bool _saving = false;
  String? _error;

  static DateTime _roundUp(DateTime t) {
    final r = DateTime(t.year, t.month, t.day, t.hour, t.minute).add(Duration(minutes: 15 - t.minute % 15));
    return r;
  }

  @override
  void dispose() {
    for (final c in [_name, _phone, _relationship, _idNote, _notes]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<DateTime?> _pick(DateTime initial) async {
    final date = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime.now().subtract(const Duration(days: 1)),
      lastDate: DateTime.now().add(const Duration(days: 14)),
    );
    if (date == null || !mounted) return null;
    final time = await showTimePicker(context: context, initialTime: TimeOfDay.fromDateTime(initial));
    if (time == null) return null;
    return DateTime(date.year, date.month, date.day, time.hour, time.minute);
  }

  Future<void> _choosePhoto(ImageSource source) async {
    final file = await pickPhoto(context, source);
    if (file == null) return;
    final bytes = await file.readAsBytes();
    if (mounted) setState(() => _photoBytes = bytes);
  }

  String? get _windowProblem {
    if (!_until.isAfter(_from)) return 'The end time must be after the start time.';
    if (_until.difference(_from) > const Duration(hours: _maxHours)) return 'A temporary pickup can cover at most $_maxHours hours.';
    if (_until.isBefore(DateTime.now())) return 'The end time is in the past.';
    return null;
  }

  Future<void> _submit() async {
    final students = (context.read<AuthProvider>().profile?.students ?? []).where((s) => s.isAuthorizedForPickup).toList();
    if (!_form.currentState!.validate()) return;
    final problem = _windowProblem ?? (_photoBytes == null ? 'Add a clear photo of the person collecting.' : null);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    final studentId = _studentId ?? students.first.studentId;

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await context.read<AuthProvider>().api.createTemporaryPickup(
            studentId: studentId,
            fullName: _name.text.trim(),
            phone: _phone.text.trim(),
            relationship: _relationship.text.trim(),
            validFrom: _from,
            validUntil: _until,
            photoBase64: encodeImageBytes(_photoBytes!),
            identityNote: _idNote.text.trim().isEmpty ? null : _idNote.text.trim(),
            notes: _notes.text.trim().isEmpty ? null : _notes.text.trim(),
          );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Request sent. The school will review it.')));
      Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _error = e.message;
          _saving = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _error = 'Could not reach the server. Check your connection and try again.';
          _saving = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final students = (context.watch<AuthProvider>().profile?.students ?? const <StudentSummary>[]).where((s) => s.isAuthorizedForPickup).toList();
    _studentId ??= students.isEmpty ? null : students.first.studentId;
    final fmt = DateFormat('EEE d MMM, h:mm a');
    final hours = _until.difference(_from).inMinutes / 60;

    return Scaffold(
      appBar: AppBar(title: const Text('Temporary pickup')),
      body: Form(
        key: _form,
        child: CgPage(
          maxWidth: CgWidths.form,
          children: [
            const CgPageHead(title: 'New temporary pickup', subtitle: 'The school reviews every request before it can be used.'),
            if (_error != null) CgAlert(tone: CgTone.danger, title: 'Could not send the request', message: _error),
            CgCard(
              title: 'Child',
              child: DropdownButtonFormField<int>(
                initialValue: _studentId,
                decoration: const InputDecoration(labelText: 'Who will be collected?'),
                items: [for (final s in students) DropdownMenuItem(value: s.studentId, child: Text(s.fullName))],
                onChanged: (v) => setState(() => _studentId = v),
                validator: (v) => v == null ? 'Choose a child' : null,
              ),
            ),
            CgCard(
              title: 'Person collecting',
              subtitle: 'Their details are shown to school staff at the desk',
              child: Column(children: [
                TextFormField(
                  controller: _name,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Full name', prefixIcon: Icon(Icons.person_outline_rounded)),
                  validator: (v) => (v == null || v.trim().isEmpty) ? 'Enter their full name' : null,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _phone,
                  keyboardType: TextInputType.phone,
                  decoration: const InputDecoration(labelText: 'Contact number', prefixIcon: Icon(Icons.phone_outlined)),
                  validator: (v) => RegExp(r'^\+?[0-9 ()\-]{7,20}$').hasMatch((v ?? '').trim()) ? null : 'Enter a valid contact number',
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _relationship,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Relationship to your child', hintText: 'Uncle, neighbour, family friend…'),
                  validator: (v) => (v == null || v.trim().isEmpty) ? 'Say how they are related' : null,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _idNote,
                  keyboardType: TextInputType.number,
                  maxLength: 12,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  decoration: const InputDecoration(labelText: 'Aadhaar card number (optional)', hintText: 'Aadhaar card number'),
                  // An Aadhaar number is exactly 12 digits; the field stays optional.
                  validator: (v) {
                    final value = (v ?? '').trim();
                    return value.isEmpty || RegExp(r'^[0-9]{12}$').hasMatch(value) ? null : 'Aadhaar card number must be 12 digits';
                  },
                ),
              ]),
            ),
            CgCard(
              title: 'Photo (required)',
              subtitle: 'One person, face clearly visible, looking at the camera',
              child: Column(children: [
                if (_photoBytes != null)
                  ClipRRect(
                    borderRadius: BorderRadius.circular(16),
                    child: Image.memory(Uint8List.fromList(_photoBytes!), height: 200, width: double.infinity, fit: BoxFit.cover),
                  )
                else
                  Container(
                    height: 120,
                    width: double.infinity,
                    decoration: BoxDecoration(
                      color: CgColors.surface2,
                      borderRadius: BorderRadius.circular(16),
                      border: Border.all(color: CgColors.border),
                    ),
                    child: const Icon(Icons.face_rounded, size: 48, color: CgColors.textMuted2),
                  ),
                const SizedBox(height: 12),
                Row(children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: () => _choosePhoto(ImageSource.camera),
                      icon: const Icon(Icons.photo_camera_outlined),
                      label: const Text('Take photo'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: () => _choosePhoto(ImageSource.gallery),
                      icon: const Icon(Icons.photo_library_outlined),
                      label: const Text('Choose'),
                    ),
                  ),
                ]),
              ]),
            ),
            CgCard(
              title: 'When',
              subtitle: 'Up to $_maxHours hours in total',
              child: Column(children: [
                _timeRow('Starts', fmt.format(_from), () async {
                  final t = await _pick(_from);
                  if (t != null) {
                    setState(() {
                      _from = t;
                      if (!_until.isAfter(_from)) _until = _from.add(const Duration(hours: 3));
                    });
                  }
                }),
                const SizedBox(height: 10),
                _timeRow('Ends', fmt.format(_until), () async {
                  final t = await _pick(_until);
                  if (t != null) setState(() => _until = t);
                }),
                const SizedBox(height: 10),
                Align(
                  alignment: Alignment.centerLeft,
                  child: _windowProblem == null
                      ? CgBadge.info('${hours.toStringAsFixed(hours % 1 == 0 ? 0 : 1)} hours', icon: Icons.schedule_rounded)
                      : CgBadge.error(_windowProblem!, icon: Icons.error_outline_rounded),
                ),
              ]),
            ),
            CgCard(
              title: 'Note for the school (optional)',
              child: TextFormField(
                controller: _notes,
                maxLines: 3,
                maxLength: 500,
                decoration: const InputDecoration(hintText: 'Anything the desk should know'),
              ),
            ),
            const CgAlert(
              tone: CgTone.info,
              title: 'What happens next',
              message: 'The school reviews the request. Once approved you can share a QR code with the person. '
                  'You can cancel at any time before pickup.',
            ),
            ElevatedButton.icon(
              onPressed: _saving || students.isEmpty ? null : _submit,
              icon: _saving
                  ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Icon(Icons.send_rounded),
              label: Text(_saving ? 'Sending…' : 'Send request to school'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _timeRow(String label, String value, VoidCallback onTap) => InkWell(
        borderRadius: BorderRadius.circular(14),
        onTap: onTap,
        child: InputDecorator(
          decoration: InputDecoration(labelText: label, suffixIcon: const Icon(Icons.edit_calendar_rounded)),
          child: Text(value, style: const TextStyle(fontWeight: FontWeight.w700)),
        ),
      );
}
