// Widget test for 溢出面板单层扁平。
//
// Validates: Requirements 2.4
//
// Property 4 — 溢出面板单层扁平：
//   对任意非空 overflow 集合，[MobileBottomNav] 打开后的 overflow sheet
//   widget 子树都应满足：
//
//     * 不含任何 [ExpansionTile]
//     * 不含任何 [TabBar]
//     * 不含任何 [PageView]
//     * 不含嵌套 [showModalBottomSheet] 调用（即不存在第二层 [BottomSheet]）
//     * 可见 [ListTile] 行数恰好等于 `layout.overflow.length`
//
// 这条不变量对应 design §5.3 的「单层扁平」契约：sheet 必须是一个 [Column]
// + 一组 [ListTile]，禁止任何形式的嵌套子菜单 / 分页 / 二级模态。
//
// 测试策略：用默认的 [DashboardDestinations.mobileNavLayout]（4 个 primary +
// 3 个 overflow：certificates / settings / rustDeskRemoteControl）渲染
// [MobileBottomNav]，tap「更多」触发器打开 sheet，然后对 sheet 子树执行
// finder 断言。生成器层面的覆盖由 Property 1 的 PBT 完成；这里只需要确认
// widget 实现层面的扁平性不变量在默认 layout 下成立。

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  testWidgets(
    '[mobile-bottom-nav-redesign] Property 4: '
    'overflow sheet 是单层扁平 ListTile 列表，无嵌套子菜单',
    (tester) async {
      // 默认 mobile layout：4 primary + 3 overflow（certificates / settings /
      // rustDeskRemoteControl），对应 design §Data Models 中的 MobileNavLayout
      // 默认值。
      final layout = DashboardDestinations.mobileNavLayout();
      expect(
        layout.hasOverflow,
        isTrue,
        reason: '默认 mobile layout 必须存在 overflow，否则本测试失去意义',
      );
      expect(
        layout.overflow.length,
        equals(3),
        reason: '默认 mobile layout 应有 3 个 overflow 条目（certificates / '
            'settings / rustDeskRemoteControl）',
      );

      // 渲染 MobileBottomNav。activeKey 选 primary 中的 machines，避免触发器
      // 进入选中态干扰本测试关注的扁平性断言。
      DestinationKey? lastSelected;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            bottomNavigationBar: MobileBottomNav(
              layout: layout,
              activeKey: DestinationKey.machines,
              onDestinationSelected: (key) {
                lastSelected = key;
              },
            ),
          ),
        ),
      );
      await tester.pump();

      // tap「更多」触发器打开 sheet。
      final triggerFinder = find.text('更多');
      expect(
        triggerFinder,
        findsOneWidget,
        reason: '主槽序列末尾应渲染单一「更多」触发器',
      );
      await tester.tap(triggerFinder);
      await tester.pumpAndSettle();

      // 仅一个 BottomSheet：禁止嵌套二级模态。
      final bottomSheetFinder = find.byType(BottomSheet);
      expect(
        bottomSheetFinder,
        findsOneWidget,
        reason: '禁止 sheet 内再次 showModalBottomSheet（嵌套模态）',
      );

      // sheet 子树不允许出现任何分页 / Tab / 折叠形态的嵌套子菜单。
      expect(
        find.descendant(
          of: bottomSheetFinder,
          matching: find.byType(ExpansionTile),
        ),
        findsNothing,
        reason: 'overflow sheet 不应包含 ExpansionTile（禁止折叠子菜单）',
      );
      expect(
        find.descendant(
          of: bottomSheetFinder,
          matching: find.byType(TabBar),
        ),
        findsNothing,
        reason: 'overflow sheet 不应包含 TabBar（禁止 Tab 切换）',
      );
      expect(
        find.descendant(
          of: bottomSheetFinder,
          matching: find.byType(PageView),
        ),
        findsNothing,
        reason: 'overflow sheet 不应包含 PageView（禁止分页 / 横向滚动）',
      );

      // ListTile 行数恰好等于 overflow.length。
      final sheetListTiles = find.descendant(
        of: bottomSheetFinder,
        matching: find.byType(ListTile),
      );
      expect(
        sheetListTiles.evaluate().length,
        equals(layout.overflow.length),
        reason: 'sheet 内 ListTile 行数必须等于 layout.overflow.length='
            '${layout.overflow.length}',
      );

      // 打开 sheet 不应触发 onDestinationSelected：trigger 只负责开合面板，
      // 不应被误认为「选择了」更多入口。
      expect(
        lastSelected,
        isNull,
        reason: '打开 overflow sheet 不应触发 onDestinationSelected',
      );

      // 同时确认所有 overflow Destination 的 label 都出现在 sheet 中——
      // 这与 R2.4「在单一扁平 surface 上展示 overflow 集合」一致。
      for (final spec in layout.overflow) {
        expect(
          find.descendant(
            of: bottomSheetFinder,
            matching: find.text(spec.label),
          ),
          findsOneWidget,
          reason: 'sheet 应展示 overflow 项 "${spec.label}"',
        );
      }
    },
  );
}
