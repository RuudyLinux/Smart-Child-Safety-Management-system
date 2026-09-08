import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';

class StudentDetailScreen extends StatefulWidget {
  final StudentSummary student;
  const StudentDetailScreen({required this.student, super.key});

  @override
  State<StudentDetailScreen> createState() => _StudentDetailScreenState();
}

class _StudentDetailScreenState extends State<StudentDetailScreen> {
  List<JourneyEvent>? _journey;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final api = context.read<AuthProvider>().api;
    final journey = await api.getJourney(widget.student.studentId);
    if (mounted) setState(() { _journey = journey; _loading = false; });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.student.fullName)),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Grade: ${widget.student.grade ?? "—"}', style: const TextStyle(fontSize: 15)),
                        const SizedBox(height: 4),
                        Text('Relationship: ${widget.student.relationship ?? "—"}', style: const TextStyle(color: CgColors.textMuted)),
                        const SizedBox(height: 8),
                        widget.student.isAuthorizedForPickup ? CgBadge.success('Authorized for pickup') : CgBadge.neutral('Not a pickup guardian'),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                const Text('Journey Timeline', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: CgColors.text)),
                const SizedBox(height: 8),
                if (_journey == null || _journey!.isEmpty)
                  const Padding(padding: EdgeInsets.all(12), child: Text('No journey events yet.', style: TextStyle(color: CgColors.textMuted)))
                else
                  ..._journey!.map((e) => Card(
                        child: ListTile(
                          leading: const Icon(Icons.circle, size: 10, color: CgColors.primary),
                          title: Text(e.eventType),
                          subtitle: Text(DateFormat('d MMM yyyy, h:mm a').format(e.timestamp.toLocal())),
                          trailing: CgBadge.neutral(e.source),
                        ),
                      )),
              ],
            ),
    );
  }
}
