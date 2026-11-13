import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';

/// "Alerts" tab: every notification the school has sent — attendance,
/// journey, pickup verification and general — newest first.
class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});
  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  List<AppNotification>? _notifications;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final api = context.read<AuthProvider>().api;
      final list = await api.getNotifications();
      if (mounted) setState(() { _notifications = list; _loading = false; });
    } catch (e) {
      if (mounted) setState(() { _error = 'Could not load your alerts.'; _loading = false; });
    }
  }

  (IconData, Color, Color) _style(String type) => switch (type) {
        'Journey' => (Icons.route_rounded, CgColors.infoText, CgColors.infoSoft),
        'Attendance' => (Icons.event_available_rounded, CgColors.successText, CgColors.successSoft),
        'PickupVerification' => (Icons.verified_user_rounded, CgColors.primaryDeep, CgColors.primarySoft),
        _ => (Icons.notifications_rounded, CgColors.textMuted, CgColors.surface2),
      };

  Future<void> _markRead(AppNotification n) async {
    if (n.isRead) return;
    try {
      await context.read<AuthProvider>().api.markNotificationRead(n.notificationId);
      _load();
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Could not mark this alert as read. Try again.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final unread = _notifications?.where((n) => !n.isRead).length ?? 0;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Alerts'),
        actions: [
          if (unread > 0)
            Padding(
              padding: const EdgeInsets.only(right: CgSpace.md),
              child: Center(child: CgBadge.primary('$unread new')),
            ),
        ],
      ),
      body: _loading
          ? const CgLoadingState()
          : _error != null
              ? CgErrorState(message: _error!, onRetry: _load)
              : CgPage(
                  maxWidth: CgWidths.reading,
                  onRefresh: _load,
                  children: [
                    if (_notifications == null || _notifications!.isEmpty)
                      const CgCard(
                        child: CgEmptyState(
                          icon: Icons.notifications_none_rounded,
                          title: 'No alerts yet',
                          message: 'Attendance, journey and pickup notifications from the school will appear here.',
                        ),
                      )
                    else ...[
                      Padding(
                        padding: const EdgeInsets.only(bottom: CgSpace.sm),
                        child: Text(
                          unread > 0 ? 'Select an unread alert to mark it as read.' : 'You are all caught up.',
                          style: CgText.small,
                        ),
                      ),
                      CgGlass(
                        padding: EdgeInsets.zero,
                        child: Column(children: [
                          for (final (i, n) in _notifications!.indexed) _item(n, last: i == _notifications!.length - 1),
                        ]),
                      ),
                    ],
                  ],
                ),
    );
  }

  Widget _item(AppNotification n, {required bool last}) {
    final (icon, fg, bg) = _style(n.type);
    return Material(
      color: n.isRead ? Colors.transparent : CgColors.primarySoft.withValues(alpha: 0.55),
      borderRadius: last ? const BorderRadius.vertical(bottom: Radius.circular(CgRadius.lg)) : null,
      child: InkWell(
        onTap: n.isRead ? null : () => _markRead(n),
        hoverColor: CgColors.surface2,
        child: Container(
          padding: const EdgeInsets.all(CgSpace.md),
          decoration: BoxDecoration(border: last ? null : const Border(bottom: BorderSide(color: CgColors.border))),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              CgIconTile(icon: icon, color: fg, background: bg, size: 36),
              const SizedBox(width: CgSpace.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(child: Text(n.title, style: CgText.bodyStrong.copyWith(fontWeight: n.isRead ? FontWeight.w600 : FontWeight.w700))),
                        const SizedBox(width: CgSpace.xs),
                        Text(DateFormat('d MMM, h:mm a').format(n.sentAt.toLocal()), style: CgText.caption.copyWith(fontWeight: FontWeight.w400)),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(n.message, style: const TextStyle(color: CgColors.text2, fontSize: 13.5, height: 1.45)),
                  ],
                ),
              ),
              if (!n.isRead)
                Container(
                  width: 8,
                  height: 8,
                  margin: const EdgeInsets.only(left: CgSpace.xs, top: 6),
                  decoration: const BoxDecoration(color: CgColors.primary, shape: BoxShape.circle),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
