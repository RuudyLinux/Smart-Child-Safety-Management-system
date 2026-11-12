import 'package:flutter/material.dart';
import '../../widgets/cg_glass.dart';
import '../pickup/pickups_screen.dart';
import '../reports/reports_screen.dart';
import '../profile/profile_screen.dart';
import 'children_tab.dart';
import 'home_tab.dart';

/// Bottom navigation shell: Home | Children | Pickups | Reports | Profile. Alerts open from the
/// bell on the dashboard, like the Admin Web top bar.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key});
  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      // IndexedStack keeps each tab's scroll position and loaded data.
      body: IndexedStack(index: _index, children: _tabs),
      bottomNavigationBar: CgGlassNavBar(
        index: _index,
        items: _items,
        onTap: (i) => setState(() => _index = i),
      ),
    );
  }
}
