import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_journey_timeline.dart';
import '../../widgets/cg_states.dart';
import '../../core/responsive.dart';
import '../../widgets/cg_ui.dart';

class JourneyScreen extends StatefulWidget {
  const JourneyScreen({super.key});
  @override
  State<JourneyScreen> createState() => _JourneyScreenState();
}

class _JourneyScreenState extends State<JourneyScreen> {
  int? _selectedStudentId;
  List<JourneyEvent>? _events;
  bool _loading = false;
  String? _error;

  Future<void> _load(int studentId) async {
    setState(() { _loading = true; _error = null; });
    try {
      final api = context.read<AuthProvider>().api;
      final events = await api.getJourney(studentId);
      if (mounted) setState(() { _events = events; _loading = false; });
    } catch (e) {
      if (mounted) setState(() { _error = 'Could not load this journey.'; _loading = false; });
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;
    final students = profile?.students ?? [];

    if (_selectedStudentId == null && students.isNotEmpty) {
      _selectedStudentId = students.first.studentId;
      WidgetsBinding.instance.addPostFrameCallback((_) => _load(_selectedStudentId!));
    }

    final selected = students.where((s) => s.studentId == _selectedStudentId).firstOrNull;

    return Scaffold(
      appBar: AppBar(title: const Text('Journeys')),
      body: students.isEmpty
          ? const CgEmptyState(
              icon: Icons.route_rounded,
              title: 'No children linked yet',
              message: 'Journeys will appear here once a child is linked to your account.',
            )
          : CgPage(
              maxWidth: CgWidths.reading,
              onRefresh: () => _load(_selectedStudentId!),
              children: [
                const CgPageHead(
                  title: 'Journeys',
                  subtitle: 'Checkpoints recorded for your child — bus boarding, school arrival, departure and drop-off.',
                ),
                if (students.length > 1)
                  Padding(
                    padding: const EdgeInsets.only(bottom: CgSpace.md),
                    child: Wrap(
                      runSpacing: CgSpace.xs,
                      children: [
                        for (final s in students)
                          CgChildChip(
                            student: s,
                            selected: s.studentId == _selectedStudentId,
                            photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                            photoHeaders: auth.photoHeaders,
                            onTap: () {
                              setState(() { _selectedStudentId = s.studentId; _events = null; });
                              _load(s.studentId);
                            },
                          ),
                      ],
                    ),
                  ),
                CgCard(
                  title: selected?.fullName ?? 'Journey',
                  subtitle: 'Most recent checkpoints',
                  trailing: selected == null ? null : CgBadge.safety(selected.safetyStatus),
                  child: _loading
                      ? const Padding(
                          padding: EdgeInsets.symmetric(vertical: CgSpace.xl),
                          child: Center(child: SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.4))),
                        )
                      : _error != null
                          ? CgErrorState(message: _error!, onRetry: () => _load(_selectedStudentId!))
                          : (_events == null || _events!.isEmpty)
                              ? const CgEmptyState(
                                  icon: Icons.route_rounded,
                                  title: 'No journey events yet',
                                  message: 'Checkpoint scans — bus boarding, school arrival, departure and drop-off — will appear here.',
                                )
                              : CgJourneyTimeline(events: _events!),
                ),
              ],
            ),
    );
  }
}

extension _FirstOrNull<T> on Iterable<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
