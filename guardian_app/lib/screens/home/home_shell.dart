import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/responsive.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_navigation.dart';
import '../journey/journey_screen.dart';
import '../notifications/notifications_screen.dart';
import '../pickup/pickups_screen.dart';
import '../reports/reports_screen.dart';
import '../profile/profile_screen.dart';
import '../temporary/temporary_pickups_screen.dart';
import 'children_tab.dart';
import 'home_tab.dart';

/// The signed-in app shell: Home | Children | Pickups | Reports | Profile.
///
/// The navigation adapts to the available width — a bottom bar on phones, a rail on tablets and a
/// sidebar on desktop — while the tabs themselves stay the same widgets, so their state and data survive
/// a window resize.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key});
  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  /// Keeps the tab stack (and every tab's loaded data) when a resize moves it to a different layout.
  final _bodyKey = GlobalKey(debugLabel: 'cg-tabs');

  final _tabs = const [
    HomeTab(),
    ChildrenTab(),
    PickupsScreen(embedded: true),
    ReportsScreen(embedded: true),
    ProfileScreen(embedded: true),
  ];

  static const _items = [
    CgNavItem(Icons.home_outlined, Icons.home_rounded, 'Home'),
    CgNavItem(Icons.family_restroom_outlined, Icons.family_restroom_rounded, 'Children'),
    CgNavItem(Icons.how_to_reg_outlined, Icons.how_to_reg_rounded, 'Pickups'),
    CgNavItem(Icons.insights_outlined, Icons.insights_rounded, 'Reports'),
    CgNavItem(Icons.person_outline_rounded, Icons.person_rounded, 'Profile'),
  ];

  void _select(int i) => setState(() => _index = i);

  void _open(Widget screen) => Navigator.of(context).push(MaterialPageRoute(builder: (_) => screen));

  @override
  Widget build(BuildContext context) {
    final body = IndexedStack(key: _bodyKey, index: _index, children: _tabs);

    return LayoutBuilder(
      builder: (context, c) {
        switch (CgBreakpoints.of(c.maxWidth)) {
          case CgSize.compact:
            return Scaffold(
              body: body,
              bottomNavigationBar: CgBottomNav(index: _index, items: _items, onSelect: _select),
            );
          case CgSize.medium:
            return Scaffold(
              body: SafeArea(
                right: false,
                bottom: false,
                child: Row(children: [
                  CgNavRail(
                    index: _index,
                    items: _items,
                    onSelect: _select,
                    trailing: Padding(
                      padding: const EdgeInsets.only(top: CgSpace.md),
                      child: IconButton(
                        tooltip: 'Alerts',
                        icon: const Icon(Icons.notifications_outlined),
                        onPressed: () => _open(const NotificationsScreen()),
                      ),
                    ),
                  ),
                  Expanded(child: body),
                ]),
              ),
            );
          case CgSize.expanded:
            return Scaffold(
              body: Row(children: [
                CgSidebar(
                  index: _index,
                  items: _items,
                  onSelect: _select,
                  shortcuts: [
                    CgSidebarLink(icon: Icons.notifications_outlined, label: 'Alerts', onTap: () => _open(const NotificationsScreen())),
                    CgSidebarLink(icon: Icons.route_outlined, label: 'Journeys', onTap: () => _open(const JourneyScreen())),
                    CgSidebarLink(icon: Icons.badge_outlined, label: 'Temporary pickup', onTap: () => _open(const TemporaryPickupsScreen())),
                  ],
                  footer: _AccountTile(onTap: () => _select(4)),
                ),
                Expanded(child: body),
              ]),
            );
        }
      },
    );
  }
}

/// The signed-in guardian at the foot of the sidebar; opens Profile.
class _AccountTile extends StatelessWidget {
  final VoidCallback onTap;
  const _AccountTile({required this.onTap});

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final p = auth.profile;
    if (p == null) return const SizedBox.shrink();
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(CgRadius.md),
        hoverColor: CgColors.surface2,
        child: Padding(
          padding: const EdgeInsets.all(CgSpace.xs),
          child: Row(children: [
            CgAvatar(name: p.fullName, photoUrl: p.hasPhoto ? auth.api.myPhotoUrl : null, headers: auth.photoHeaders, size: 36),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(p.fullName, maxLines: 1, overflow: TextOverflow.ellipsis, style: CgText.bodyStrong.copyWith(fontSize: 13.5)),
                  Text(p.email, maxLines: 1, overflow: TextOverflow.ellipsis, style: CgText.caption.copyWith(fontWeight: FontWeight.w400)),
                ],
              ),
            ),
          ]),
        ),
      ),
    );
  }
}
