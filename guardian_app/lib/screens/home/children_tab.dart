import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import 'student_detail_screen.dart';

/// All linked children, one card each — the full roster this guardian can see.
class ChildrenTab extends StatelessWidget {
  const ChildrenTab({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;

    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: profile == null
          ? const CgLoadingState()
          : RefreshIndicator(
              onRefresh: () => context.read<AuthProvider>().refreshProfile(),
              child: profile.students.isEmpty
                  ? ListView(
                      physics: const AlwaysScrollableScrollPhysics(),
                      padding: const EdgeInsets.fromLTRB(16, 8, 16, 110),
                      children: const [
                        CgPageHead(title: 'Children', subtitle: 'Students linked to your account.'),
                        SizedBox(height: 60),
                        CgEmptyState(
                          icon: Icons.family_restroom_rounded,
                          title: 'No children linked yet',
                          message: 'Ask your school administrator to link your child to this account.',
                        ),
                      ],
                    )
                  : ListView(
                      padding: const EdgeInsets.fromLTRB(16, 8, 16, 110),
                      physics: const AlwaysScrollableScrollPhysics(),
                      children: [
                        CgPageHead(
                          title: 'Children',
                          subtitle: '${profile.students.length} linked to your account · tap a child for details, transport and reports.',
                        ),
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
      ),
    );
  }
}
