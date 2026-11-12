import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:qr_flutter/qr_flutter.dart';
import '../../core/api_client.dart';
import '../../core/formatters.dart';
import '../../core/theme.dart';
import '../../models/portal_models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_ui.dart';
import 'temporary_pickups_screen.dart';

/// One temporary pickup: status, the person and window, the QR code to share once approved,
/// the audit trail, and cancel.
class TemporaryPickupDetailScreen extends StatefulWidget {
  final int id;
  const TemporaryPickupDetailScreen({required this.id, super.key});

  @override
  State<TemporaryPickupDetailScreen> createState() => _TemporaryPickupDetailScreenState();
}

class _TemporaryPickupDetailScreenState extends State<TemporaryPickupDetailScreen> {
  Key _key = UniqueKey();
  bool _busy = false;

  Future<void> _cancel(TemporaryPickup p) async {
    final reason = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Cancel this pickup?'),
        content: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('${p.fullName} will no longer be allowed to collect ${p.studentName}. Any QR code you shared stops working.'),
          const SizedBox(height: 12),
          TextField(controller: reason, decoration: const InputDecoration(labelText: 'Reason (optional)')),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Keep it')),
          TextButton(
            onPressed: () => Navigator.pop(c, true),
            style: TextButton.styleFrom(foregroundColor: CgColors.errorText),
            child: const Text('Cancel pickup'),
          ),
        ],
      ),
    );
    if (ok != true || !mounted) return;
    setState(() => _busy = true);
    try {
      await context.read<AuthProvider>().api.cancelTemporaryPickup(p.id, reason: reason.text.trim().isEmpty ? null : reason.text.trim());
      if (mounted) setState(() { _busy = false; _key = UniqueKey(); });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthProvider>();
    return Scaffold(
      appBar: AppBar(title: const Text('Temporary pickup')),
      body: CgAsync<TemporaryPickupDetail>(
        key: _key,
        load: () => auth.api.getTemporaryPickup(widget.id),
        builder: (context, d, reload) {
          final p = d.pickup;
          return RefreshIndicator(
            onRefresh: reload,
            child: ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 40),
              children: [
                CgPageHead(
                  title: p.fullName,
                  subtitle: '${p.relationship} · collecting ${p.studentName}',
                  leading: CgAvatar(
                    name: p.fullName,
                    photoUrl: p.hasPhoto ? auth.api.temporaryPickupPhotoUrl(p.id) : null,
                    headers: auth.photoHeaders,
                    size: 56,
                    guardian: true,
                  ),
                ),
                Padding(padding: const EdgeInsets.only(bottom: 14), child: Align(alignment: Alignment.centerLeft, child: TemporaryStateBadge(p.state))),
                if (p.state == 'Rejected' || p.state == 'Revoked')
                  CgAlert(tone: CgTone.danger, title: p.state == 'Rejected' ? 'The school did not approve this' : 'The school revoked this pickup', message: p.decisionNote),
                if (p.state == 'PendingApproval')
                  const CgAlert(tone: CgTone.warning, title: 'Waiting for the school', message: 'You will get a notification as soon as it is approved or declined.'),
                if (p.qrAvailable) _QrCard(id: p.id, api: auth.api),
                CgCard(
                  title: 'Details',
                  child: CgDefList([
                    CgDefList.text('Child', p.studentName),
                    CgDefList.text('Person', p.fullName),
                    CgDefList.text('Relationship', p.relationship),
                    CgDefList.text('Contact', p.phone),
                    CgDefList.text('ID hint', p.identityNote),
                    CgDefList.text('From', CgFormat.dayTime(p.validFrom)),
                    CgDefList.text('Until', CgFormat.dayTime(p.validUntil)),
                    CgDefList.text('Requested by', p.requestedByYou ? 'You' : p.requestedByName),
                    if (p.notes != null) CgDefList.text('Note', p.notes),
                    if (p.completedAt != null) CgDefList.text('Picked up', CgFormat.dayTime(p.completedAt!)),
                  ]),
                ),
                CgCard(
                  title: 'Activity',
                  subtitle: 'Everything recorded for this pickup',
                  child: d.audit.isEmpty
                      ? const Text('Nothing recorded yet.', style: TextStyle(color: CgColors.textMuted))
                      : CgTimeline([
                          for (final (i, a) in d.audit.indexed)
                            CgTimelineItem(
                              title: _actionLabel(a.action),
                              time: CgFormat.dayTime(a.at),
                              body: _actionBody(a),
                              icon: _actionIcon(a.action),
                              tone: _actionTone(a.action),
                              current: i == d.audit.length - 1,
                            ),
                        ]),
                ),
                if (p.canCancel)
                  OutlinedButton.icon(
                    onPressed: _busy ? null : () => _cancel(p),
                    style: OutlinedButton.styleFrom(foregroundColor: CgColors.errorText, side: const BorderSide(color: CgColors.errorBorder)),
                    icon: const Icon(Icons.cancel_outlined),
                    label: Text(_busy ? 'Cancelling…' : 'Cancel this pickup'),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }

  static String _actionLabel(String a) => switch (a) {
        'Requested' => 'Request sent',
        'Approved' => 'Approved by school',
        'Rejected' => 'Not approved',
        'Cancelled' => 'Cancelled',
        'Revoked' => 'Revoked by school',
        'QrScanned' => 'QR code scanned at the desk',
        'QrRejected' => 'An invalid QR code was presented',
        'FaceMismatch' => 'Face did not match — child not released',
        'HandoverRefused' => 'Handover refused',
        'HandedOver' => 'Child collected',
        _ => a,
      };

  static String? _actionBody(TemporaryPickupAuditEntry a) => switch (a.action) {
        'Rejected' || 'Revoked' || 'Cancelled' || 'HandoverRefused' => a.detail,
        _ => a.actorRole == 'Admin' ? 'School staff' : null,
      };

  static IconData _actionIcon(String a) => switch (a) {
        'Requested' => Icons.send_rounded,
        'Approved' => Icons.check_rounded,
        'QrScanned' => Icons.qr_code_scanner_rounded,
        'HandedOver' => Icons.task_alt_rounded,
        'FaceMismatch' || 'QrRejected' || 'HandoverRefused' => Icons.warning_amber_rounded,
        _ => Icons.close_rounded,
      };

  static CgTone _actionTone(String a) => switch (a) {
        'Approved' || 'HandedOver' => CgTone.success,
        'Requested' || 'QrScanned' => CgTone.info,
        'Cancelled' => CgTone.neutral,
        _ => CgTone.danger,
      };
}

/// The QR code is fetched on demand (never cached in the list) and refreshed when the card is rebuilt.
class _QrCard extends StatelessWidget {
  final int id;
  final dynamic api;
  const _QrCard({required this.id, required this.api});

  @override
  Widget build(BuildContext context) => CgCard(
        title: 'Pickup QR code',
        subtitle: 'Show this to school staff — valid once, only during the window',
        child: FutureBuilder<TemporaryPickupQr>(
          future: api.getTemporaryPickupQr(id) as Future<TemporaryPickupQr>,
          builder: (context, snap) {
            if (snap.connectionState != ConnectionState.done) {
              return const SizedBox(height: 220, child: Center(child: CircularProgressIndicator()));
            }
            if (snap.hasError) {
              return Text('${snap.error}', style: const TextStyle(color: CgColors.errorText));
            }
            final qr = snap.data!;
            return Column(children: [
              Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: CgColors.border)),
                child: QrImageView(data: qr.token, size: 220, backgroundColor: Colors.white, semanticsLabel: 'Temporary pickup QR code'),
              ),
              const SizedBox(height: 10),
              Text('Valid ${CgFormat.dayTime(qr.validFrom)} – ${CgFormat.dayTime(qr.validUntil)}',
                  textAlign: TextAlign.center, style: const TextStyle(color: CgColors.textMuted, fontSize: 12.5)),
              const SizedBox(height: 6),
              const Text(
                'The person must also pass a face check against the photo you supplied. '
                'Do not share this code publicly; cancelling the pickup stops it working.',
                textAlign: TextAlign.center,
                style: TextStyle(color: CgColors.text2, fontSize: 12.5, height: 1.4),
              ),
            ]);
          },
        ),
      );
}
