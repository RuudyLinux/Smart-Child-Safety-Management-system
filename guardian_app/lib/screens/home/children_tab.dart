import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/responsive.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import 'student_detail_screen.dart';

/// All linked children — one card each, in a grid that grows with the window.
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
            : CgPage(
                maxWidth: CgWidths.dashboard,
                top: context.isCompact ? 12 : 24,
                onRefresh: () => context.read<AuthProvider>().refreshProfile(),
                children: [
                  CgPageHead(
                    title: 'Children',
                    subtitle: profile.students.isEmpty
                        ? 'Students linked to your account.'
                        : '${profile.students.length} linked to your account · open a child for details, transport and reports.',
                  ),
                  if (profile.students.isEmpty)
                    const CgCard(
                      child: CgEmptyState(
                        icon: Icons.family_restroom_rounded,
                        title: 'No children linked yet',
                        message: 'Ask your school administrator to link your child to this account.',
                      ),
                    )
                  else
                    CgGrid(
                      minItemWidth: 300,
                      maxColumns: 3,
                      children: [
                        for (final s in profile.students)
                          CgChildCard(
                            student: s,
                            photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                            photoHeaders: auth.photoHeaders,
                            onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => StudentDetailScreen(student: s))),
                          ),
                      ],
                    ),
                ],
              ),
      ),
    );
  }
}
