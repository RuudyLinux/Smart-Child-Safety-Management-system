import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_empty_state.dart';
import '../../widgets/cg_student_picker.dart';

class JourneyScreen extends StatefulWidget {
  const JourneyScreen({super.key});
  @override
  State<JourneyScreen> createState() => _JourneyScreenState();
}

class _JourneyScreenState extends State<JourneyScreen> {
  int? _selectedStudentId;
  List<JourneyEvent>? _events;

  Future<void> _load(int studentId) async {
    final api = context.read<AuthProvider>().api;
    final events = await api.getJourney(studentId);
    if (mounted) setState(() => _events = events);
  }

  IconData _iconFor(String type) {
    switch (type) {
      case 'BusBoarding': return Icons.directions_bus_outlined;
      case 'SchoolArrival': return Icons.login;
      case 'SchoolDeparture': return Icons.logout;
      case 'BusDropoff': return Icons.home_outlined;
      default: return Icons.circle;
    }
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
      appBar: AppBar(title: const Text('Journey')),
      body: students.isEmpty
          ? const CgEmptyState('No students linked.')
          : Column(
              children: [
                CgStudentPicker(
                  students: students,
                  value: _selectedStudentId,
                  onChanged: (v) {
                    setState(() { _selectedStudentId = v; _events = null; });
                    if (v != null) _load(v);
                  },
                ),
                Expanded(
                  child: _events == null
                      ? const Center(child: CircularProgressIndicator())
                      : _events!.isEmpty
                          ? const CgEmptyState('No journey events yet.')
                          : ListView.separated(
                              padding: const EdgeInsets.all(16),
                              itemCount: _events!.length,
                              separatorBuilder: (_, index) => const SizedBox(height: 4),
                              itemBuilder: (context, i) {
                                final e = _events![i];
                                return Row(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Column(
                                      children: [
                                        Icon(_iconFor(e.eventType), color: CgColors.primary, size: 22),
                                        if (i != _events!.length - 1) Container(width: 2, height: 36, color: CgColors.border),
                                      ],
                                    ),
                                    const SizedBox(width: 12),
                                    Expanded(
                                      child: Padding(
                                        padding: const EdgeInsets.only(bottom: 16),
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Text(e.eventType, style: const TextStyle(fontWeight: FontWeight.w600)),
                                            Text(DateFormat('d MMM yyyy, h:mm a').format(e.timestamp.toLocal()), style: const TextStyle(color: CgColors.textMuted, fontSize: 12.5)),
                                          ],
                                        ),
                                      ),
                                    ),
                                  ],
                                );
                              },
                            ),
                ),
              ],
            ),
    );
  }
}
