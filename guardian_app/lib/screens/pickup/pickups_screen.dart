import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/formatters.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import '../temporary/temporary_pickups_screen.dart';

/// Pickup status — the guardian's view of the Admin Web verification queue: every pickup attempt
/// for their children, by a permanent guardian or an approved temporary pickup person, with its state.
class PickupsScreen extends StatefulWidget {
  /// True when shown as a bottom-nav tab (no back button).
  final bool embedded;
  const PickupsScreen({this.embedded = false, super.key});

  @override
  State<PickupsScreen> createState() => _PickupsScreenState();
}

class _PickupsScreenState extends State<PickupsScreen> {
  int? _studentId;
  int _days = 30;
  Key _reloadKey = UniqueKey();

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final students = auth.profile?.students ?? const <StudentSummary>[];

    return Scaffold(
      appBar: widget.embedded ? null : AppBar(title: const Text('Pickup status')),
      body: SafeArea(
        bottom: false,
        child: CgAsync<List<PickupRecord>>(
          key: _reloadKey,
          load: () => auth.api.getPickups(
            studentId: _studentId,
            from: DateTime.now().subtract(Duration(days: _days - 1)),
            to: DateTime.now(),
          ),
          builder: (context, pickups, reload) {
            final pending = pickups.where((p) => p.state == 'Pending').length;
            final done = pickups.where((p) => p.state == 'Approved').length;
            return CgPage(
              maxWidth: CgWidths.list,
              top: widget.embedded ? (context.isCompact ? 12 : 24) : CgSpace.lg,
              onRefresh: reload,
              children: [
                if (widget.embedded) const CgPageHead(title: 'Pickups', subtitle: 'Every pickup attempt for your children and its outcome.'),
                CgCard(
                  padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.sm, 0),
                  child: CgRow(
                    divider: false,
                    leading: const CgIconTile(icon: Icons.badge_rounded, color: CgColors.guardianText, background: CgColors.guardianSoft, size: 36),
                    title: 'Temporary pickup guardians',
                    subtitle: 'Authorise someone else to collect for up to 24 hours',
                    onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const TemporaryPickupsScreen())),
                  ),
                ),
                CgKpiGrid(children: [
                  CgKpi(
                      label: 'Awaiting approval',
                      value: '$pending',
                      icon: Icons.hourglass_top_rounded,
                      tone: pending > 0 ? CgTone.warning : CgTone.neutral),
                  CgKpi(label: 'Completed pickups', value: '$done', icon: Icons.check_circle_rounded, tone: CgTone.success, note: 'Last $_days days'),
                ]),
                const SizedBox(height: CgSpace.md),
                _filters(students),
                const SizedBox(height: CgSpace.md),
                if (pickups.isEmpty)
                  const CgCard(
                    child: CgEmptyState(
                      icon: Icons.how_to_reg_rounded,
                      title: 'No pickups in this period',
                      message: 'When someone is verified at the school desk to collect your child, it appears here.',
                    ),
                  )
                else
                  CgCard(
                    title: 'History',
                    subtitle: '${pickups.length} attempt${pickups.length == 1 ? '' : 's'}',
                    padding: const EdgeInsets.fromLTRB(CgSpace.md, 0, CgSpace.md, 0),
                    child: Column(children: [
                      for (final (i, p) in pickups.indexed) _row(p, last: i == pickups.length - 1),
                    ]),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }

  void _set(VoidCallback change) => setState(() {
        change();
        _reloadKey = UniqueKey();
      });

  Widget _filters(List<StudentSummary> students) => Wrap(
        spacing: 10,
        runSpacing: 10,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          CgSegment<int>(
            options: const [(7, '7 days'), (30, '30 days'), (90, '90 days')],
            value: _days,
            onChanged: (v) => _set(() => _days = v),
          ),
          if (students.length > 1)
            Container(
              height: 38,
              padding: const EdgeInsets.symmetric(horizontal: CgSpace.sm),
              decoration: BoxDecoration(
                color: CgColors.card,
                borderRadius: BorderRadius.circular(CgRadius.md),
                border: Border.all(color: CgColors.border),
              ),
              child: DropdownButtonHideUnderline(
                child: DropdownButton<int?>(
                  value: _studentId,
                  style: CgText.bodyStrong.copyWith(fontSize: 13.5),
                  icon: const Icon(Icons.expand_more_rounded, size: 18),
                  borderRadius: BorderRadius.circular(CgRadius.md),
                  items: [
                    const DropdownMenuItem(value: null, child: Text('All children')),
                    for (final s in students) DropdownMenuItem(value: s.studentId, child: Text(s.fullName)),
                  ],
                  onChanged: (v) => _set(() => _studentId = v),
                ),
              ),
            ),
        ],
      );

  Widget _row(PickupRecord p, {required bool last}) => CgRow(
        divider: !last,
        leading: CgIconTile(
          icon: p.isTemporary ? Icons.badge_rounded : Icons.face_rounded,
          color: p.isTemporary ? CgColors.guardianText : CgColors.primaryDeep,
          background: p.isTemporary ? CgColors.guardianSoft : CgColors.primarySoft,
          size: 36,
        ),
        title: p.studentName,
        subtitle: [
          p.isYou ? 'You' : p.pickedUpBy,
          if (p.isTemporary) 'temporary pickup',
          CgFormat.dayTime(p.attemptedAt),
          if (p.state == 'Pending' && p.expiresAt != null) 'expires ${CgFormat.time(p.expiresAt!)}',
        ].join(' · '),
        trailing: CgPickupStateBadge(p.state),
      );
}
