import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/theme.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import 'new_temporary_pickup_screen.dart';
import 'temporary_pickup_detail_screen.dart';

/// Chip for a temporary pickup's effective state, mirroring the Admin Web wording.
class TemporaryStateBadge extends StatelessWidget {
  final String state;
  const TemporaryStateBadge(this.state, {super.key});

  @override
  Widget build(BuildContext context) => switch (state) {
        'PendingApproval' => CgBadge.warning('Awaiting approval', icon: Icons.hourglass_top_rounded),
        'Scheduled' => CgBadge.info('Approved · starts later', icon: Icons.schedule_rounded),
        'Active' => CgBadge.success('Active now', icon: Icons.check_circle_rounded),
        'Completed' => CgBadge.success('Picked up', icon: Icons.task_alt_rounded),
        'Rejected' => CgBadge.error('Not approved', icon: Icons.block_rounded),
        'Cancelled' => CgBadge.neutral('Cancelled', icon: Icons.cancel_outlined),
        'Revoked' => CgBadge.error('Revoked', icon: Icons.gpp_bad_rounded),
        'Expired' => CgBadge.neutral('Expired', icon: Icons.timer_off_outlined),
        _ => CgBadge.neutral(state),
      };
}

/// Temporary pickup guardians: people the guardian authorises to collect a child for up to 24 hours,
/// with school approval, a QR code and a face check at the desk.
class TemporaryPickupsScreen extends StatefulWidget {
  const TemporaryPickupsScreen({super.key});

  @override
  State<TemporaryPickupsScreen> createState() => _TemporaryPickupsScreenState();
}

class _TemporaryPickupsScreenState extends State<TemporaryPickupsScreen> {
  Key _key = UniqueKey();

  Future<void> _open(Widget screen) async {
    await Navigator.of(context).push(MaterialPageRoute(builder: (_) => screen));
    if (mounted) setState(() => _key = UniqueKey());
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthProvider>();
    final canRequest = (auth.profile?.students ?? []).any((s) => s.isAuthorizedForPickup);

    return Scaffold(
      appBar: AppBar(title: const Text('Temporary pickup')),
      floatingActionButton: canRequest
          ? FloatingActionButton.extended(
              onPressed: () => _open(const NewTemporaryPickupScreen()),
              icon: const Icon(Icons.person_add_alt_1_rounded),
              label: const Text('New request'),
            )
          : null,
      body: CgAsync<List<TemporaryPickup>>(
        key: _key,
        load: () => auth.api.getTemporaryPickups(),
        builder: (context, items, reload) {
          final open = items.where((p) => p.isOpen).toList();
          final past = items.where((p) => !p.isOpen).toList();
          return RefreshIndicator(
            onRefresh: reload,
            child: ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 110),
              children: [
                const CgAlert(
                  tone: CgTone.info,
                  title: 'How it works',
                  message: 'Ask the school to let someone else collect your child for up to 24 hours. '
                      'The school must approve it. Share the QR code with that person; at the desk the QR is scanned and '
                      "their face is checked against the photo you supply. A temporary pickup can't be used twice.",
                ),
                if (!canRequest)
                  const CgAlert(
                    tone: CgTone.warning,
                    title: 'Not available on this account',
                    message: 'Only a guardian who is authorised to collect a child can arrange a temporary pickup.',
                  ),
                if (items.isEmpty)
                  const Padding(
                    padding: EdgeInsets.only(top: 24),
                    child: CgEmptyState(
                      icon: Icons.badge_rounded,
                      title: 'No temporary pickups',
                      message: 'When you arrange for someone to collect your child, it appears here with its status.',
                    ),
                  ),
                if (open.isNotEmpty) _section('Current and upcoming', open),
                if (past.isNotEmpty) _section('History', past),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _section(String title, List<TemporaryPickup> items) => CgCard(
        title: title,
        padding: const EdgeInsets.fromLTRB(16, 2, 12, 2),
        child: Column(children: [
          for (final (i, p) in items.indexed)
            CgRow(
              divider: i != items.length - 1,
              leading: const CgIconTile(icon: Icons.badge_rounded, color: CgColors.guardianText, background: CgColors.guardianSoft, size: 40),
              title: p.fullName,
              subtitle: '${p.studentName} · ${CgFormat.dayTime(p.validFrom)} – ${CgFormat.time(p.validUntil)}',
              trailing: TemporaryStateBadge(p.state),
              onTap: () => _open(TemporaryPickupDetailScreen(id: p.id)),
            ),
        ]),
      );
}
