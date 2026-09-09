import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_empty_state.dart';
import '../../widgets/cg_student_picker.dart';

class AttendanceScreen extends StatefulWidget {
  const AttendanceScreen({super.key});
  @override
  State<AttendanceScreen> createState() => _AttendanceScreenState();
}

class _AttendanceScreenState extends State<AttendanceScreen> {
  int? _selectedStudentId;
  List<AttendanceRecord>? _records;

  Future<void> _load(int studentId) async {
    final api = context.read<AuthProvider>().api;
    final records = await api.getAttendance(studentId);
    if (mounted) setState(() => _records = records);
  }

  @override
  Widget build(BuildContext context) {
    final profile = context.watch<AuthProvider>().profile;
    final students = profile?.students ?? [];

    if (_selectedStudentId == null && students.isNotEmpty) {
      _selectedStudentId = students.first.studentId;
      WidgetsBinding.instance.addPostFrameCallback((_) => _load(_selectedStudentId!));
    }

    return Scaffold(
      appBar: AppBar(title: const Text('Attendance')),
      body: students.isEmpty
          ? const CgEmptyState('No students linked.')
          : Column(
              children: [
                CgStudentPicker(
                  students: students,
                  value: _selectedStudentId,
                  onChanged: (v) {
                    setState(() { _selectedStudentId = v; _records = null; });
                    if (v != null) _load(v);
                  },
                ),
                Expanded(
                  child: _records == null
                      ? const Center(child: CircularProgressIndicator())
                      : _records!.isEmpty
                          ? const CgEmptyState('No attendance records yet.')
                          : ListView.builder(
                              padding: const EdgeInsets.symmetric(horizontal: 16),
                              itemCount: _records!.length,
                              itemBuilder: (context, i) {
                                final r = _records![i];
                                final isArrival = r.eventType == 'SchoolArrival';
                                return Card(
                                  child: ListTile(
                                    leading: Icon(isArrival ? Icons.login : Icons.logout, color: CgColors.primary),
                                    title: Text(isArrival ? 'School Arrival' : 'School Departure'),
                                    subtitle: Text(DateFormat('d MMM yyyy').format(r.date)),
                                    trailing: Text(r.time, style: const TextStyle(color: CgColors.textMuted)),
                                  ),
                                );
                              },
                            ),
                ),
              ],
            ),
    );
  }
}
