import 'package:flutter/material.dart';
import '../core/theme.dart';
import 'cg_glass.dart';

/// A secondary link in the sidebar (opens a screen rather than switching tabs).
class CgSidebarLink {
  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final int badge;
  const CgSidebarLink({required this.icon, required this.label, required this.onTap, this.badge = 0});
}

/// Phone navigation: a plain bar with a top hairline.
class CgBottomNav extends StatelessWidget {
  final int index;
  final List<CgNavItem> items;
  final ValueChanged<int> onSelect;
  const CgBottomNav({required this.index, required this.items, required this.onSelect, super.key});

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(border: Border(top: BorderSide(color: CgColors.border))),
        child: NavigationBar(
          selectedIndex: index,
          onDestinationSelected: onSelect,
          labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
          destinations: [
            for (final i in items)
              NavigationDestination(icon: Icon(i.icon), selectedIcon: Icon(i.activeIcon), label: i.label, tooltip: ''),
          ],
        ),
      );
}

/// Tablet navigation: a compact rail with labels.
class CgNavRail extends StatelessWidget {
  final int index;
  final List<CgNavItem> items;
  final ValueChanged<int> onSelect;
  final Widget? trailing;
  const CgNavRail({required this.index, required this.items, required this.onSelect, this.trailing, super.key});

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(border: Border(right: BorderSide(color: CgColors.border))),
        child: NavigationRail(
          selectedIndex: index,
          onDestinationSelected: onSelect,
          labelType: NavigationRailLabelType.all,
          minWidth: 84,
          groupAlignment: -0.85,
          leading: const Padding(padding: EdgeInsets.only(top: CgSpace.xs, bottom: CgSpace.md), child: CgBrandMark(size: 36)),
          trailing: trailing,
          destinations: [
            for (final i in items)
              NavigationRailDestination(
                icon: Icon(i.icon),
                selectedIcon: Icon(i.activeIcon),
                label: Text(i.label),
                padding: const EdgeInsets.symmetric(vertical: 2),
              ),
          ],
        ),
      );
}

/// Desktop navigation: a fixed-width sidebar with the brand, the main destinations, shortcuts and the
/// signed-in guardian at the bottom.
class CgSidebar extends StatelessWidget {
  static const double width = 248;

  final int index;
  final List<CgNavItem> items;
  final ValueChanged<int> onSelect;
  final List<CgSidebarLink> shortcuts;
  final Widget? footer;

  const CgSidebar({
    required this.index,
    required this.items,
    required this.onSelect,
    this.shortcuts = const [],
    this.footer,
    super.key,
  });

  @override
  Widget build(BuildContext context) => Container(
        width: width,
        decoration: const BoxDecoration(color: CgColors.card, border: Border(right: BorderSide(color: CgColors.border))),
        child: SafeArea(
          right: false,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Padding(
                padding: EdgeInsets.fromLTRB(CgSpace.lg, CgSpace.lg, CgSpace.lg, CgSpace.lg),
                child: CgBrandLockup(caption: 'Guardian portal'),
              ),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.symmetric(horizontal: CgSpace.sm),
                  children: [
                    for (var i = 0; i < items.length; i++)
                      _SidebarTile(
                        icon: i == index ? items[i].activeIcon : items[i].icon,
                        label: items[i].label,
                        selected: i == index,
                        onTap: () => onSelect(i),
                      ),
                    if (shortcuts.isNotEmpty) ...[
                      const Padding(
                        padding: EdgeInsets.fromLTRB(CgSpace.sm, CgSpace.lg, CgSpace.sm, CgSpace.xs),
                        child: Text('SHORTCUTS', style: CgText.overline),
                      ),
                      for (final s in shortcuts) _SidebarTile(icon: s.icon, label: s.label, onTap: s.onTap, badge: s.badge),
                    ],
                  ],
                ),
              ),
              if (footer != null)
                Container(
                  padding: const EdgeInsets.all(CgSpace.sm),
                  decoration: const BoxDecoration(border: Border(top: BorderSide(color: CgColors.border))),
                  child: footer,
                ),
            ],
          ),
        ),
      );
}

class _SidebarTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final bool selected;
  final VoidCallback onTap;
  final int badge;
  const _SidebarTile({required this.icon, required this.label, required this.onTap, this.selected = false, this.badge = 0});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 2),
        child: Semantics(
          button: true,
          selected: selected,
          child: Material(
            color: selected ? CgColors.primarySoft : Colors.transparent,
            borderRadius: BorderRadius.circular(CgRadius.md),
            child: InkWell(
              borderRadius: BorderRadius.circular(CgRadius.md),
              onTap: onTap,
              hoverColor: CgColors.surface2,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: CgSpace.sm, vertical: 10),
                child: Row(
                  children: [
                    Icon(icon, size: 21, color: selected ? CgColors.primaryDark : CgColors.textMuted),
                    const SizedBox(width: CgSpace.sm),
                    Expanded(
                      child: Text(label,
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                            color: selected ? CgColors.primaryDeep : CgColors.text2,
                          )),
                    ),
                    if (badge > 0)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 1),
                        decoration: BoxDecoration(color: CgColors.error, borderRadius: BorderRadius.circular(CgRadius.pill)),
                        child: Text(badge > 9 ? '9+' : '$badge',
                            style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w700)),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      );
}
