import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_states.dart';

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
        'Journey' => (Icons.route_rounded, CgColors.info, CgColors.infoSoft),
        'Attendance' => (Icons.event_available_rounded, CgColors.success, CgColors.successSoft),
        'PickupVerification' => (Icons.verified_user_rounded, CgColors.primary, CgColors.primarySoft),
        _ => (Icons.notifications_rounded, CgColors.textMuted, CgColors.surface2),
      };

  @override
  Widget build(BuildContext context) {
    final unread = _notifications?.where((n) => !n.isRead).length ?? 0;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Alerts'),
        actions: [
          if (unread > 0)
            Padding(
              padding: const EdgeInsets.only(right: 16),
              child: Center(child: CgBadge.primary('$unread new')),
            ),
        ],
      ),
      body: _loading
          ? const CgLoadingState()
          : _error != null
              ? CgErrorState(message: _error!, onRetry: _load)
              : RefreshIndicator(
                  onRefresh: _load,
                  child: (_notifications == null || _notifications!.isEmpty)
                      ? ListView(
                          physics: const AlwaysScrollableScrollPhysics(),
                          children: const [
                            SizedBox(height: 80),
                            CgEmptyState(
                              icon: Icons.notifications_none_rounded,
                              title: 'No alerts yet',
                              message: 'Attendance, journey and pickup notifications from the school will appear here.',
                            ),
                          ],
                        )
                      : ListView.separated(
                          padding: const EdgeInsets.all(16),
                          physics: const AlwaysScrollableScrollPhysics(),
                          itemCount: _notifications!.length,
                          separatorBuilder: (_, index) => const SizedBox(height: 8),
                          itemBuilder: (context, i) {
                            final n = _notifications![i];
                            final (icon, fg, bg) = _style(n.type);
                            return Card(
                              color: n.isRead ? CgColors.card : CgColors.primarySoft,
                              child: InkWell(
                                onTap: () async {
                                  if (!n.isRead) {
                                    await context.read<AuthProvider>().api.markNotificationRead(n.notificationId);
                                    _load();
                                  }
                                },
                                child: Padding(
                                  padding: const EdgeInsets.all(14),
                                  child: Row(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Container(
                                        width: 38,
                                        height: 38,
                                        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(11)),
                                        child: Icon(icon, size: 19, color: fg),
                                      ),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Row(
                                              children: [
                                                Expanded(
                                                  child: Text(n.title,
                                                      style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
                                                ),
                                                if (!n.isRead)
                                                  Container(
                                                    width: 8,
                                                    height: 8,
                                                    margin: const EdgeInsets.only(left: 8, top: 4),
                                                    decoration: const BoxDecoration(color: CgColors.primary, shape: BoxShape.circle),
                                                  ),
                                              ],
                                            ),
                                            const SizedBox(height: 3),
                                            Text(n.message, style: const TextStyle(color: CgColors.text2, fontSize: 13, height: 1.4)),
                                            const SizedBox(height: 6),
                                            Text(DateFormat('d MMM, h:mm a').format(n.sentAt.toLocal()),
                                                style: const TextStyle(color: CgColors.textMuted2, fontSize: 11.5)),
                                          ],
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                            );
                          },
                        ),
                ),
    );
  }
}
