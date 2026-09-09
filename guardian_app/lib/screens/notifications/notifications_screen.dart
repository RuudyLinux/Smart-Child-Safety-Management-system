import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_empty_state.dart';

class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});
  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  List<AppNotification>? _notifications;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final api = context.read<AuthProvider>().api;
    final list = await api.getNotifications();
    if (mounted) setState(() => _notifications = list);
  }

  IconData _iconFor(String type) {
    switch (type) {
      case 'Journey': return Icons.route_outlined;
      case 'Attendance': return Icons.event_available_outlined;
      case 'PickupVerification': return Icons.verified_user_outlined;
      default: return Icons.notifications_outlined;
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Notifications')),
      body: _notifications == null
          ? const Center(child: CircularProgressIndicator())
          : RefreshIndicator(
              onRefresh: _load,
              child: _notifications!.isEmpty
                  ? ListView(children: const [CgEmptyState('No notifications yet.')])
                  : ListView.builder(
                      padding: const EdgeInsets.all(16),
                      itemCount: _notifications!.length,
                      itemBuilder: (context, i) {
                        final n = _notifications![i];
                        return Card(
                          color: n.isRead ? CgColors.card : CgColors.primaryLight,
                          child: ListTile(
                            leading: Icon(_iconFor(n.type), color: CgColors.primary),
                            title: Text(n.title, style: const TextStyle(fontWeight: FontWeight.w600)),
                            subtitle: Text('${n.message}\n${DateFormat('d MMM, h:mm a').format(n.sentAt.toLocal())}'),
                            isThreeLine: true,
                            onTap: () async {
                              if (!n.isRead) {
                                await context.read<AuthProvider>().api.markNotificationRead(n.notificationId);
                                _load();
                              }
                            },
                          ),
                        );
                      },
                    ),
            ),
    );
  }
}
