// Widget property test for MobileBottomNav 的「≤2 次点击可达」契约。
//
// **Validates: Requirements 2.1, 2.5**
//
// Property 2 — 任意 Destination ≤2 次点击可达（UI 路径）：
//   对任意非空 mobile 子集 (subset) 与任意 target ∈ subset，
//   渲染 `MobileBottomNav(layout: MobileNavLayout.fromMobileSet(subset), ...)`
//   后 SHALL 满足：
//
//     * 若 `layout.isPrimary(target)`：1 次 `tester.tap` 在主槽对应位置后，
//       `onDestinationSelected` 恰好收到 `target.key` 一次。
//     * 否则（target 在 overflow）：先 tap "更多" trigger 打开 sheet，再 tap
//       sheet 中 target 行；恰好 2 次点击后 `onDestinationSelected` 恰好收到
//       `target.key` 一次。
//
// 生成策略：本测试采用「内嵌循环 + deterministic Random」的属性测试形态。
// Glados 的 `.test(...)` 不与 `flutter_test` 的 `WidgetTester` 直接组合
// （Glados 走 `package:test` 的 `test`，而 widget pump 必须在
// `testWidgets` 内），因此用 `testWidgets` 外壳裹一个 ≥100 次随机迭代的
// 循环来获得等价的属性覆盖。失败时通过 `reason` 字段把当时的子集 / 目标
// dump 出来当作可重放反例（design §Testing Strategy 也允许此形态）。
//
// 视口固定为 720×1280 logical：足够横向容纳「最多 4 主槽 + 1 trigger = 5
// 槽 × 56dp = 280dp」，也足够纵向展开 modal bottom sheet。

import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

/// 把视口设为 720×1280 logical（DPR=1，physical 同 logical）；
/// 足够大以避免 5 槽 + sheet 的布局压缩或溢出 paint 错误。
Future<void> _setMediumMobileViewport(WidgetTester tester) async {
  await tester.binding.setSurfaceSize(const Size(720, 1280));
  tester.view.devicePixelRatio = 1.0;
}

Future<void> _resetViewport(WidgetTester tester) async {
  await tester.binding.setSurfaceSize(null);
  tester.view.resetDevicePixelRatio();
}

/// 用确定性掩码从 mobile 全集中抽出非空有序子集。
List<DashboardDestinationSpec> _subsetFromMask(
  List<DashboardDestinationSpec> mobileBase,
  List<bool> mask,
) {
  return <DashboardDestinationSpec>[
    for (var i = 0; i < mobileBase.length; i++)
      if (mask[i]) mobileBase[i],
  ];
}

/// 渲染只含 [MobileBottomNav] 的最小测试 widget；avoids body 上下文，
/// 让 `find.text` 不会撞上无关文本。
Widget _harness({
  required MobileNavLayout layout,
  required DestinationKey activeKey,
  required ValueChanged<DestinationKey> onDestinationSelected,
}) {
  return MaterialApp(
    home: Scaffold(
      // body 留空：测试只关心底栏行为，避免引入会渲染同名 Text 的页面内容。
      body: const SizedBox.expand(),
      bottomNavigationBar: MobileBottomNav(
        layout: layout,
        activeKey: activeKey,
        onDestinationSelected: onDestinationSelected,
      ),
    ),
  );
}

/// 验证给定 (subset, target) 在 MobileBottomNav 上确实 ≤2 次点击可达，且
/// `onDestinationSelected` 恰好收到 1 次目标 key。
Future<void> _verifyReachable(
  WidgetTester tester, {
  required List<DashboardDestinationSpec> subset,
  required DashboardDestinationSpec target,
}) async {
  final layout = MobileNavLayout.fromMobileSet(subset);
  // activeKey 不影响可达性测试本身——选 subset 第一个，避免空指针。
  final activeKey = subset.first.key;
  final captured = <DestinationKey>[];

  await tester.pumpWidget(
    _harness(
      layout: layout,
      activeKey: activeKey,
      onDestinationSelected: captured.add,
    ),
  );
  await tester.pump();

  final subsetKeys = subset.map((d) => d.key).toList();
  final caseLabel =
      'subset=$subsetKeys target=${target.key} layout.isPrimary='
      '${layout.isPrimary(target.key)}';

  if (layout.isPrimary(target.key)) {
    // ----- Primary path: 1 tap -----
    //
    // `find.text(target.label)` 在 MobileBottomNav 子树中应当唯一命中：
    //   * primary slot 的 Text；
    //   * overflow sheet 还未打开，sheet 内的 Text 不存在；
    //   * 测试 harness body 留空，没有同名干扰 Text。
    final primaryFinder = find.descendant(
      of: find.byType(MobileBottomNav),
      matching: find.text(target.label),
    );
    expect(
      primaryFinder,
      findsOneWidget,
      reason: 'primary slot 必须能通过 label 唯一定位 ($caseLabel)',
    );

    await tester.tap(primaryFinder);
    await tester.pump();

    expect(
      captured,
      equals(<DestinationKey>[target.key]),
      reason: '主槽 1 次点击后回调应恰好收到 target.key 一次 ($caseLabel)',
    );
  } else {
    // ----- Overflow path: 2 taps -----
    //
    // 第一步：tap "更多"。
    final triggerFinder = find.descendant(
      of: find.byType(MobileBottomNav),
      matching: find.text('更多'),
    );
    expect(
      triggerFinder,
      findsOneWidget,
      reason: 'overflow trigger "更多" 必须可定位 ($caseLabel)',
    );

    await tester.tap(triggerFinder);
    // sheet 打开是一段动画，必须 settle 后才能命中其 ListTile。
    await tester.pumpAndSettle();

    // tap "更多" 不应触发 onDestinationSelected（design §5.2）。
    expect(
      captured,
      isEmpty,
      reason: 'tap "更多" 不应触发 onDestinationSelected ($caseLabel)',
    );

    // 第二步：tap sheet 中 target 行。`find.text(target.label)` 此刻只能
    // 命中 sheet 内的 ListTile title——target 不在 primary，所以底栏中无
    // 同名 Text；harness body 留空也不会引入干扰。
    final rowFinder = find.text(target.label);
    expect(
      rowFinder,
      findsOneWidget,
      reason: 'overflow sheet 内 target 行必须唯一命中 ($caseLabel)',
    );

    await tester.tap(rowFinder);
    await tester.pumpAndSettle();

    expect(
      captured,
      equals(<DestinationKey>[target.key]),
      reason: '"更多" + sheet 行共 2 次点击后回调应恰好收到 target.key 一次 '
          '($caseLabel)',
    );
  }
}

void main() {
  // mobile() 的快照：当前应为 7 项（不含 OfflineTools）。
  final mobileBase = DashboardDestinations.mobile();
  assert(
    mobileBase.length == 8,
    'mobile() 当前应为 8 项；如需调整请同步更新枚举案例。',
  );

  group(
    '[mobile-bottom-nav-redesign] MobileBottomNav reachability '
    '(Property 2)',
    () {
      // ------------------------------------------------------------------
      // (1) 显式覆盖：full mobile set 下逐一 target 每个 destination。
      //
      // 这套用例把 7 个 destinations 在「主槽 vs 溢出槽」两条路径上各扫一遍，
      // 锁住 design §5.3 表里那 7 行的契约——这是 R2.1 的字面化见证。
      // ------------------------------------------------------------------
      for (final target in mobileBase) {
        testWidgets(
          '[mobile-bottom-nav-redesign] full mobile set: '
          'target=${target.key} reachable in ≤2 taps',
          (tester) async {
            await _setMediumMobileViewport(tester);
            addTearDown(() => _resetViewport(tester));

            await _verifyReachable(
              tester,
              subset: mobileBase,
              target: target,
            );
          },
        );
      }

      // ------------------------------------------------------------------
      // (2) 边界子集：单元素子集——退化成「只有 trigger」或「只有 1 主槽」
      // 两种最简形态，用来钉死 layout 在边界上的可达性。
      // ------------------------------------------------------------------
      testWidgets(
        '[mobile-bottom-nav-redesign] singleton primary-only subset: '
        'machines reachable via 1 tap',
        (tester) async {
          await _setMediumMobileViewport(tester);
          addTearDown(() => _resetViewport(tester));

          final machines = mobileBase
              .firstWhere((d) => d.key == DestinationKey.machines);
          await _verifyReachable(
            tester,
            subset: <DashboardDestinationSpec>[machines],
            target: machines,
          );
        },
      );

      testWidgets(
        '[mobile-bottom-nav-redesign] singleton overflow-only subset: '
        'certificates reachable via 2 taps',
        (tester) async {
          await _setMediumMobileViewport(tester);
          addTearDown(() => _resetViewport(tester));

          final certificates = mobileBase
              .firstWhere((d) => d.key == DestinationKey.certificates);
          await _verifyReachable(
            tester,
            subset: <DashboardDestinationSpec>[certificates],
            target: certificates,
          );
        },
      );

      // ------------------------------------------------------------------
      // (3) PBT 主体：用 deterministic Random 跑 ≥100 次随机非空子集 +
      // 任意 target，覆盖所有 (primary / overflow) 组合。每次失败时通过
      // reason 把子集和 target dump 出来作为可重放反例。
      // ------------------------------------------------------------------
      testWidgets(
        '[mobile-bottom-nav-redesign] random subsets: every target '
        'reachable in ≤2 taps (≥100 iterations)',
        (tester) async {
          await _setMediumMobileViewport(tester);
          addTearDown(() => _resetViewport(tester));

          // 固定种子，便于 CI 复现失败 case；shrinking 退化为 example
          // dump（reason 字段已记录子集与 target）。
          final rng = Random(0x4D424E76);
          const iterations = 120;

          for (var i = 0; i < iterations; i++) {
            // 生成非空掩码：若全 false 则强制把第一个位翻成 true，确保
            // subset 非空，避免无效迭代。
            final mask = <bool>[
              for (var j = 0; j < mobileBase.length; j++) rng.nextBool(),
            ];
            if (!mask.any((b) => b)) {
              mask[0] = true;
            }
            final subset = _subsetFromMask(mobileBase, mask);
            // 随机选一个 target ∈ subset。
            final target = subset[rng.nextInt(subset.length)];

            await _verifyReachable(
              tester,
              subset: subset,
              target: target,
            );

            // 每轮之间清空 widget 树，避免上一轮 Scaffold / Navigator
            // 状态影响下一轮（尤其是上一轮如果走到 overflow path，
            // pumpAndSettle 已经关闭 sheet，但保险起见再 detach 一次）。
            await tester.pumpWidget(const SizedBox.shrink());
            await tester.pump();
          }
        },
      );
    },
  );
}
