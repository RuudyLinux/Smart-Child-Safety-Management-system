import 'package:flutter/material.dart';
import '../models/models.dart';

/// Student selector dropdown — identical markup was duplicated in Attendance
/// and Journey screens; both pick from the guardian's linked students.
class CgStudentPicker extends StatelessWidget {
  final List<StudentSummary> students;
  final int? value;
  final ValueChanged<int?> onChanged;
  const CgStudentPicker({required this.students, required this.value, required this.onChanged, super.key});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.all(16),
        child: DropdownButtonFormField<int>(
          initialValue: value,
          decoration: const InputDecoration(labelText: 'Student'),
          items: students.map((s) => DropdownMenuItem(value: s.studentId, child: Text(s.fullName))).toList(),
          onChanged: onChanged,
        ),
      );
}
