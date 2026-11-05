import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_states.dart';
import 'student_detail_screen.dart';

/// All linked children, one card each — the full roster this guardian can see.
class ChildrenTab extends StatelessWidget {
  const ChildrenTab({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;

    return Scaffold(
      appBar: AppBar(title: const Text('Children')),
      body: profile == null
          ? const CgLoadingState()
          : RefreshIndicator(
              onRefresh: () => context.read<AuthProvider>().refreshProfile(),
              child: profile.students.isEmpty
                  ? ListView(
                      physics: const AlwaysScrollableScrollPhysics(),
                      children: const [
                        SizedBox(height: 80),
                        CgEmptyState(
                          icon: Icons.family_restroom_rounded,
                          title: 'No children linked yet',
                          message: 'Ask your school administrator to link your child to this account.',
                        ),
                      ],
                    )
                  : ListView(
                      padding: const EdgeInsets.all(16),
                      physics: const AlwaysScrollableScrollPhysics(),
                      children: [
                        Text('${profile.students.length} linked', style: const TextStyle(color: CgColors.textMuted, fontSize: 12.5)),
                        const SizedBox(height: 10),
                        ...profile.students.map((s) => Padding(
                              padding: const EdgeInsets.only(bottom: 10),
                              child: CgChildCard(
                                student: s,
                                photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                                photoHeaders: auth.photoHeaders,
                                onTap: () => Navigator.of(context)
                                    .push(MaterialPageRoute(builder: (_) => StudentDetailScreen(student: s))),
                              ),
                            )),
                      ],
                    ),
            ),
    );
  }
}
