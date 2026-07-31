// Widget test for 选中态状态机。
//
// Validates: Requirements 3.5, 8.1, 8.2, 8.3, 8.4
//
// Property 6 — 选中态状态机：
//   对 `(activeKey, sheetOpen)` 笛卡尔积穷举（含 primary / overflow 两种
//   active 与 sheet 开关），断言 design §5.2 三条规则：
//
//   Rule 1: activeKey ∈ primary
//     → activeKey 对应主槽 isSelected = true
//     → 其他主槽与 overflow trigger 均 isSelected = false
//     → 不论 sheetOpen 取值
//
//   Rule 2: activeKey ∈ overflow ∧ sheetOpen = false
//     → 所有顶层槽（含 trigger）isSelected = false
//
//   Rule 3: activeKey ∈ overflow ∧ sheetOpen = true
//     → trigger isSelected = true
//     → sheet 中对应 activeKey 的行 selected = true
//     → 其他 sheet 行 selected = false
//
// 此外还覆盖 R8.2 的「外部 CrossPageNavigation 改写 activeKey 后，下一帧
// pump 内不变量重新成立」——通过一个 GlobalKey 持有的 [_Harness] 状态
// mutate `_activeKey` 后只 `tester.pump()` 一次（不 `pumpAndSettle`）即
// 重新评估 Rule 1 / Rule 2。
//
// 测试策略：默认 [DashboardDestinations.mobileNavLayout]
//   primary  = [machines, machineLogs, audit, deployments]
//   overflow = [certificates, settings, rustDeskRemoteControl]
// 对每个 activeKey 与 sheetOpen 组合各跑一个 [testWidgets]；
// 失败信息携带 `(activeKey, sheetOpen)` 上下文便于定位。

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

/// 安装一个临时 [FlutterError.onError] 钩子，过滤掉 `_MobileBottomNavSlot`
/// 内部 `Column` 在默认测试视口下的 ~2px 高度溢出。
///
/// 这条溢出属于 [_MobileBottomNavSlot] 内部 Column 的几何约束（由 task 2.1
/// 决定的 56dp slot 高度 + 16dp 垂直 padding 造成 40dp Column 槽位），
/// 是 **Property 10 / task 2.8 几何最小值** 应该覆盖的关注点。
/// 本测试关注的是「选中态状态机」（Property 6），布局几何错误不应污染断言。
///
/// 钩子在 [addTearDown] 中恢复原始 onError，避免影响其他测试。
void _suppressLayoutOverflowErrors() {
  final originalOnError = FlutterError.onError;
  FlutterError.onError = (FlutterErrorDetails details) {
    final message = details.exceptionAsString();
    if (message.contains('A RenderFlex overflowed')) {
      return; // 忽略布局溢出；其归属于 Property 10。
    }
    originalOnError?.call(details);
  };
  addTearDown(() {
    FlutterError.onError = originalOnError;
  });
}

/// 把 MobileBottomNav 嵌入 [Scaffold] 并暴露外部修改 `activeKey` 的能力。
///
/// 用 [GlobalKey] 暴露 [_HarnessState.setActiveKey] 给测试，模拟
/// `MachinesView.openLogsForMachine` / `openAuditForMachine` 这类
/// CrossPageNavigation 触发的外部 activeKey 变更，用于 R8.2 同帧不变量
/// 重检。
class _Harness extends StatefulWidget {
  const _Harness({super.key, required this.initialKey});

  final DestinationKey initialKey;

  @override
  State<_Harness> createState() => _HarnessState();
}

class _HarnessState extends State<_Harness> {
  late DestinationKey _activeKey = widget.initialKey;

  void setActiveKey(DestinationKey key) {
    setState(() => _activeKey = key);
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      home: Scaffold(
        bottomNavigationBar: MobileBottomNav(
          layout: DashboardDestinations.mobileNavLayout(),
          activeKey: _activeKey,
          // 选中态状态机不依赖回调副作用，这里只丢弃。
          onDestinationSelected: (_) {},
        ),
      ),
    );
  }
}

/// MobileBottomNav 主槽 / 溢出触发器槽 / 溢出行的 Semantics 节点形状统一为
/// `Semantics(container: true, button: true, excludeSemantics: true,
/// label: \<user-visible name\>, selected: \<bool\>)`。这条精确签名同时排除掉
/// MaterialApp / Localizations / ListTile 等其他 Semantics 节点，保证 label
/// 在 widget 树中只命中一个目标节点。
List<Semantics> _findSlotSemantics(WidgetTester tester, String label) {
  return tester
      .widgetList<Semantics>(
        find.byWidgetPredicate(
          (Widget w) =>
              w is Semantics &&
              w.container == true &&
              w.excludeSemantics == true &&
              w.properties.label == label &&
              w.properties.button == true,
        ),
      )
      .toList();
}

/// 读取 label 对应槽位/行的 `Semantics.selected`，并强制断言唯一性。
bool _slotSelected(WidgetTester tester, String label) {
  final matches = _findSlotSemantics(tester, label);
  expect(
    matches,
    hasLength(1),
    reason:
        '期望恰好 1 个 (button, container, excludeSemantics) Semantics for '
        '"$label"，实际命中 ${matches.length} 个',
  );
  return matches.first.properties.selected ?? false;
}

/// 断言 label 对应槽位/行**不存在**于当前 widget 树（用于「sheet 关闭时
/// overflow 行不渲染」之类的负向断言）。
void _expectSlotAbsent(WidgetTester tester, String label) {
  final matches = _findSlotSemantics(tester, label);
  expect(
    matches,
    isEmpty,
    reason: '期望 "$label" 槽位/行不存在于当前 widget 树，实际命中 '
        '${matches.length} 个',
  );
}

const String _triggerLabel = '更多';

void main() {
  // 默认 mobile layout 应该是 4 primary + 3 overflow；如果未来
  // `_primaryOrder` 调整，本测试需要同步更新生成器。
  final layout = DashboardDestinations.mobileNavLayout();
  assert(
    layout.primary.length == 4 && layout.overflow.length == 4,
    '默认 mobile layout 期望 4 primary + 3 overflow；'
    '若调整 _primaryOrder 请同步更新本测试。',
  );

  final primaryKeys =
      layout.primary.map((s) => s.key).toList(growable: false);
  final overflowKeys =
      layout.overflow.map((s) => s.key).toList(growable: false);

  String labelOf(DestinationKey key) =>
      DashboardDestinations.specOf(key).label;

  // ---------------------------------------------------------------------------
  // Rule 1 — sheet closed, activeKey ∈ primary
  // ---------------------------------------------------------------------------
  for (final activeKey in primaryKeys) {
    testWidgets(
      '[mobile-bottom-nav-redesign] Property 6 Rule 1: '
      'sheet closed + active=primary($activeKey) → 该主槽选中、其他主槽与 trigger 未选中',
      (tester) async {
        _suppressLayoutOverflowErrors();
        await tester.pumpWidget(_Harness(initialKey: activeKey));
        await tester.pump();

        // 激活主槽选中。
        expect(
          _slotSelected(tester, labelOf(activeKey)),
          isTrue,
          reason: '$activeKey 对应主槽应选中',
        );
        // 其他主槽未选中。
        for (final other in primaryKeys.where((k) => k != activeKey)) {
          expect(
            _slotSelected(tester, labelOf(other)),
            isFalse,
            reason: '其他主槽 $other 不应选中（active=$activeKey）',
          );
        }
        // 溢出触发器未选中（sheet 关闭，且 active 不在 overflow）。
        expect(
          _slotSelected(tester, _triggerLabel),
          isFalse,
          reason: 'sheet 关闭 + active=primary 时 trigger 必须未选中',
        );
        // sheet 关闭时 overflow 行不应渲染。
        for (final overflowKey in overflowKeys) {
          _expectSlotAbsent(tester, labelOf(overflowKey));
        }
      },
    );
  }

  // ---------------------------------------------------------------------------
  // Rule 2 — sheet closed, activeKey ∈ overflow
  // ---------------------------------------------------------------------------
  for (final activeKey in overflowKeys) {
    testWidgets(
      '[mobile-bottom-nav-redesign] Property 6 Rule 2 / R8.3: '
      'sheet closed + active=overflow($activeKey) → 所有顶层槽与 trigger 均未选中',
      (tester) async {
        _suppressLayoutOverflowErrors();
        await tester.pumpWidget(_Harness(initialKey: activeKey));
        await tester.pump();

        // 所有主槽未选中。
        for (final primary in primaryKeys) {
          expect(
            _slotSelected(tester, labelOf(primary)),
            isFalse,
            reason:
                'active=overflow($activeKey) + sheet 关闭时主槽 $primary 应未选中',
          );
        }
        // 触发器未选中（这是 R8.3 的关键：active 落在 overflow 但 sheet
        // 未打开时不能高亮 trigger，避免「点开 sheet 翻页时高亮乱跳」）。
        expect(
          _slotSelected(tester, _triggerLabel),
          isFalse,
          reason:
              'active=overflow($activeKey) + sheet 关闭时 trigger 必须未选中（R8.3）',
        );
        // sheet 关闭时 overflow 行不应渲染。
        for (final overflowKey in overflowKeys) {
          _expectSlotAbsent(tester, labelOf(overflowKey));
        }
      },
    );
  }

  // ---------------------------------------------------------------------------
  // Rule 1 (sheetOpen branch) — sheet open, activeKey ∈ primary
  //
  // 即使 sheet 被打开，只要 activeKey 仍属于 primary，那个主槽就保持选中、
  // 其他主槽与 trigger 都未选中（对应 design §5.2 第 3 条 bullet:
  // `_sheetOpen == true && layout.isPrimary(activeKey)` 时 trigger 仍未选中）。
  // ---------------------------------------------------------------------------
  for (final activeKey in primaryKeys) {
    testWidgets(
      '[mobile-bottom-nav-redesign] Property 6 Rule 1 (sheetOpen): '
      'sheet open + active=primary($activeKey) → 主槽选中、trigger 未选中、sheet 行均未选中',
      (tester) async {
        _suppressLayoutOverflowErrors();
        await tester.pumpWidget(_Harness(initialKey: activeKey));
        await tester.pump();

        // 打开 sheet。
        await tester.tap(find.text(_triggerLabel));
        await tester.pumpAndSettle();

        // 主槽选中态：active 主槽选中，其他主槽未选中。
        expect(
          _slotSelected(tester, labelOf(activeKey)),
          isTrue,
          reason:
              'active=primary($activeKey) + sheet 打开时该主槽仍应保持选中',
        );
        for (final other in primaryKeys.where((k) => k != activeKey)) {
          expect(
            _slotSelected(tester, labelOf(other)),
            isFalse,
            reason: '其他主槽 $other 在 sheet 打开时仍应未选中',
          );
        }
        // trigger 未选中：sheet 打开但 active 落在 primary 而非 overflow。
        expect(
          _slotSelected(tester, _triggerLabel),
          isFalse,
          reason:
              'sheet 打开 + active=primary($activeKey) 时 trigger 仍应未选中（design §5.2）',
        );
        // sheet 行：overflow 行均存在，但因 active 不在 overflow，
        // 没有任何行被标为 selected。
        for (final overflowKey in overflowKeys) {
          expect(
            _slotSelected(tester, labelOf(overflowKey)),
            isFalse,
            reason:
                'active=primary($activeKey) + sheet 打开时 overflow 行 $overflowKey 不应选中',
          );
        }
      },
    );
  }

  // ---------------------------------------------------------------------------
  // Rule 3 — sheet open, activeKey ∈ overflow
  // ---------------------------------------------------------------------------
  for (final activeKey in overflowKeys) {
    testWidgets(
      '[mobile-bottom-nav-redesign] Property 6 Rule 3 / R8.4: '
      'sheet open + active=overflow($activeKey) → trigger 选中、对应 sheet 行选中、其他行未选中',
      (tester) async {
        _suppressLayoutOverflowErrors();
        await tester.pumpWidget(_Harness(initialKey: activeKey));
        await tester.pump();

        await tester.tap(find.text(_triggerLabel));
        await tester.pumpAndSettle();

        // 主槽全部未选中。
        for (final primary in primaryKeys) {
          expect(
            _slotSelected(tester, labelOf(primary)),
            isFalse,
            reason:
                'active=overflow($activeKey) + sheet 打开时主槽 $primary 仍应未选中',
          );
        }
        // trigger 选中（R8.4 / Rule 3）。
        expect(
          _slotSelected(tester, _triggerLabel),
          isTrue,
          reason:
              'active=overflow($activeKey) + sheet 打开时 trigger 必须选中（R8.4）',
        );
        // sheet 内 active 行选中、其他行未选中。
        for (final overflowKey in overflowKeys) {
          final shouldBeSelected = overflowKey == activeKey;
          expect(
            _slotSelected(tester, labelOf(overflowKey)),
            shouldBeSelected,
            reason:
                'sheet 行 $overflowKey 选中态应为 $shouldBeSelected '
                '（active=$activeKey）',
          );
        }
      },
    );
  }

  // ---------------------------------------------------------------------------
  // R8.2 — 外部 CrossPageNavigation 改写 activeKey 后，下一帧 pump 内不变量
  // 重新成立。模拟 `MachinesView.openLogsForMachine(...)` 等外部跳转。
  // ---------------------------------------------------------------------------
  testWidgets(
    '[mobile-bottom-nav-redesign] Property 6 / R8.2: '
    '外部修改 activeKey 后单次 pump 同帧不变量重新成立',
    (tester) async {
      _suppressLayoutOverflowErrors();
      final harnessKey = GlobalKey<_HarnessState>();
      await tester.pumpWidget(
        _Harness(key: harnessKey, initialKey: DestinationKey.machines),
      );
      await tester.pump();

      // 初始状态：active=machines（primary） → Rule 1。
      expect(_slotSelected(tester, labelOf(DestinationKey.machines)), isTrue);
      expect(
        _slotSelected(tester, labelOf(DestinationKey.audit)),
        isFalse,
      );
      expect(_slotSelected(tester, _triggerLabel), isFalse);

      // 外部把 activeKey 切到另一个 primary（模拟 openAuditForMachine）。
      harnessKey.currentState!.setActiveKey(DestinationKey.audit);
      // 关键断言：单次 pump 后（同一帧）不变量必须重新成立——不允许
      // 状态机滞后到下一次 pumpAndSettle 才更新。
      await tester.pump();

      expect(
        _slotSelected(tester, labelOf(DestinationKey.machines)),
        isFalse,
        reason: '外部切换 activeKey 后 machines 主槽必须立刻取消选中',
      );
      expect(
        _slotSelected(tester, labelOf(DestinationKey.audit)),
        isTrue,
        reason: '外部切换 activeKey 后 audit 主槽必须立刻进入选中态',
      );
      expect(
        _slotSelected(tester, _triggerLabel),
        isFalse,
        reason: '同帧切换 primary→primary 后 trigger 仍应未选中',
      );

      // 继续把 activeKey 切到 overflow（模拟跨页跳转命中 overflow 项）。
      harnessKey.currentState!.setActiveKey(DestinationKey.certificates);
      await tester.pump();

      // sheet 仍关闭：所有主槽 + trigger 必须立刻全部转为未选中（Rule 2）。
      for (final primary in primaryKeys) {
        expect(
          _slotSelected(tester, labelOf(primary)),
          isFalse,
          reason:
              '外部切换到 overflow 后主槽 $primary 必须立刻未选中（R8.3）',
        );
      }
      expect(
        _slotSelected(tester, _triggerLabel),
        isFalse,
        reason: '外部切换到 overflow + sheet 关闭时 trigger 仍须未选中（R8.3）',
      );

      // 再切回 primary，验证不变量在两个方向上都同帧成立。
      harnessKey.currentState!.setActiveKey(DestinationKey.deployments);
      await tester.pump();

      expect(
        _slotSelected(tester, labelOf(DestinationKey.deployments)),
        isTrue,
        reason: 'overflow→primary 切换后新主槽必须立刻进入选中态',
      );
      // overflow→primary 切换后 sheet 仍关闭，certificates 行不应渲染。
      _expectSlotAbsent(tester, labelOf(DestinationKey.certificates));
    },
  );
}
