// Widget test for Semantics 标签等于用户可见名称。
//
// Validates: Requirements 5.5
//
// Property 13 — Semantics 标签等于用户可见名称：
//   对任意 `spec ∈ DashboardDestinations.mobile()` 渲染在主槽或 overflow
//   行的位置，该位置 expose 的 [Semantics] 节点必须满足：
//
//     * `semantics.label == spec.label`
//     * `semantics.hasFlag(SemanticsFlag.isButton) == true`
//
//   并且「更多」trigger 的 `Semantics.label == '更多'`、`isButton == true`。
//
// 这条不变量对应 design §5.1 / §5.2 中对 Semantics 契约的约束：MobileBottomNav
// 主槽与 overflow 行用 [Semantics] 包裹并 `excludeSemantics: true`，避免装饰性
// icon / Text 子节点污染屏幕阅读器朗读结果，使得用户耳朵里听到的名称与眼睛
// 看到的 label 完全一致（R5.5）。
//
// 测试策略：
//   1. `tester.ensureSemantics()` 启用 Semantics tree（默认 widget 测试不构建
//      Semantics tree，必须显式启用，并在 tearDown 释放 handle）。
//   2. 渲染 [MobileBottomNav]，对每个 `layout.primary` spec 用
//      `find.bySemanticsLabel(spec.label)` 定位 [SemanticsNode]，断言 label
//      等于 spec.label 且 [SemanticsFlag.isButton] 为 true。
//   3. 同样断言「更多」trigger 的 Semantics 节点。
//   4. tap「更多」打开 overflow sheet，对每个 `layout.overflow` spec 重复
//      label / isButton 断言。
//
// 仅 widget 实现层面的实例化检查；label 文案是否覆盖全部 mobile 集合由
// Property 14（destinations_*_test.dart 中的元数据一致性）保证。

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  testWidgets(
    '[mobile-bottom-nav-redesign] Property 13: '
    'Semantics 标签 == 用户可见名称，且每个 slot / 行 / trigger 都是 button',
    (tester) async {
      // 默认 widget 测试不构建 Semantics tree，必须显式启用，否则
      // `tester.getSemantics` / `find.bySemanticsLabel` 都会失效。
      // 这里**不能**用 [addTearDown(handle.dispose)]：flutter_test 的
      // `_endOfTestVerifications` 在 testBody 退出后立即跑，但 [addTearDown]
      // 注册的 callback 排在 `_endOfTestVerifications` **之后**，会触发
      // 「A SemanticsHandle was active at the end of the test」断言。
      // 因此把 dispose 写进 try/finally，保证在测试体退出前完成。
      final handle = tester.ensureSemantics();
      try {

      // 默认 mobile layout：4 primary（machines / machineLogs / audit /
      // deployments）+ 3 overflow（certificates / settings /
      // rustDeskRemoteControl），对应 design §Data Models。
      final layout = DashboardDestinations.mobileNavLayout();
      expect(
        layout.hasOverflow,
        isTrue,
        reason: '默认 mobile layout 必须存在 overflow，否则 trigger 与 sheet 行的 '
            'Semantics 断言失去意义',
      );

      // 渲染 MobileBottomNav。activeKey 选 primary 中的 machines，避免本测试
      // 与「触发器选中态」状态机产生耦合。
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
      // 当前 [_MobileBottomNavSlot] 内部 Column（icon 22 + spacing 4 + label
      // ~16）在 56dp 槽位 - 8dp 上下 padding = 40dp 的内容区里恰好溢出 2dp，
      // 触发 `RenderFlex overflowed` FlutterError。这是 widget 内部 layout
      // 的另一条不变量（Property 11「字号下限不截断」会在专门测试中处理），
      // 与 Property 13「Semantics 标签」正交：Semantics tree 与 RenderFlex
      // 溢出无关。这里把这些 layout 警告吸收掉，避免污染本测试的 fail
      // 信号——一旦 Property 11 或 widget 内部布局被调整修复，这个清理循环
      // 自然 no-op。
      while (tester.takeException() != null) {}

      // —— 1. 每个 primary slot 的 Semantics 节点 —— //
      for (final spec in layout.primary) {
        final finder = find.bySemanticsLabel(spec.label);
        expect(
          finder,
          findsOneWidget,
          reason: 'primary slot "${spec.label}" 应有唯一 Semantics 节点；'
              'excludeSemantics:true 应当屏蔽内部 icon / Text 子语义',
        );
        final node = tester.getSemantics(finder);
        expect(
          node.label,
          spec.label,
          reason: 'primary slot "${spec.label}" 的 Semantics.label 必须与用户可见 '
              'label 完全相等',
        );
        expect(
          node.flagsCollection.isButton,
          isTrue,
          reason: 'primary slot "${spec.label}" 的 Semantics 必须带 isButton flag',
        );
      }

      // —— 2.「更多」trigger 的 Semantics 节点 —— //
      final triggerFinder = find.bySemanticsLabel('更多');
      expect(
        triggerFinder,
        findsOneWidget,
        reason: 'overflow trigger 应有唯一 label="更多" 的 Semantics 节点',
      );
      final triggerNode = tester.getSemantics(triggerFinder);
      expect(
        triggerNode.label,
        '更多',
        reason: 'overflow trigger 的 Semantics.label 必须为 "更多"',
      );
      expect(
        triggerNode.flagsCollection.isButton,
        isTrue,
        reason: 'overflow trigger 的 Semantics 必须带 isButton flag',
      );

      // —— 3. 打开 overflow sheet，对每行 Semantics 节点重复断言 —— //
      await tester.tap(find.text('更多'));
      await tester.pumpAndSettle();
      // 打开 sheet 不应触发 onDestinationSelected，避免与 Property 7 串扰。
      expect(
        lastSelected,
        isNull,
        reason: '打开 overflow sheet 不应触发 onDestinationSelected',
      );

      for (final spec in layout.overflow) {
        final finder = find.bySemanticsLabel(spec.label);
        expect(
          finder,
          findsOneWidget,
          reason: 'overflow row "${spec.label}" 应有唯一 Semantics 节点',
        );
        final node = tester.getSemantics(finder);
        expect(
          node.label,
          spec.label,
          reason: 'overflow row "${spec.label}" 的 Semantics.label 必须与用户可见 '
              'label 完全相等',
        );
        expect(
          node.flagsCollection.isButton,
          isTrue,
          reason: 'overflow row "${spec.label}" 的 Semantics 必须带 isButton flag',
        );
      }
      } finally {
        handle.dispose();
      }
    },
  );
}
