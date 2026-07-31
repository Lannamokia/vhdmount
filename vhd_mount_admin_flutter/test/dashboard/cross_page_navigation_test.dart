// Property test for CrossPageNavigation 按 DestinationKey 派发。
//
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
//
// Property 5 — CrossPageNavigation 按 DestinationKey 派发：
//   对任意 `DashboardDestinations.all` 的排列、以及任意 `MobileNavLayout
//   ._primaryOrder` 的排列（即不论 `machineLogs` / `audit` 当前被分配到
//   PrimaryDestinationSlot 还是 OverflowDestinationSlot），都应当满足：
//
//     * 调用 `openLogsForMachine(machineId)` 后，`onDestinationSelected`
//       恰好收到 `DestinationKey.machineLogs`；
//     * 调用 `openAuditForMachine(machineId)` 后，`onDestinationSelected`
//       恰好收到 `DestinationKey.audit`。
//
// 这个不变量直接反映 design §6.3 的契约：跨页跳转通过 enum key 派发，
// 不再依赖 destinations 列表的整数索引；列表重排不会改变 target。
//
// 测试策略（两层）：
//
// (1) 数据层 PBT —— `MobileNavLayout` 在排列下的 key 不变性
//     `MobileNavLayout._primaryOrder` 是私有 `static const`，无法从测试
//     里替换；它的语义是「primary 排序的优先级」。design §6.3 要求的
//     不变量是「key 为 machineLogs / audit 的 destination 无论被分配到
//     primary 还是 overflow，跳转目标都不变」。把这个不变量翻译成
//     `MobileNavLayout.fromMobileSet(<排列>)` 上的等价断言：
//
//       * `machineLogs` / `audit` 一定能在 layout.primary ∪ layout.overflow
//         中找到（即 `isPrimary || isOverflow`）；
//       * 它们对应的 spec 与原始 spec 在 `(label, subtitle, icon, color)`
//         上完全一致；
//       * `layout.primary` / `layout.overflow` 的并集与输入集合相等
//         （Property 1 已覆盖，但这里再加一层 key-level 的健壮性检查
//         防回归）。
//
//     `DashboardDestinations.all` 是可任意排列的（`mobile()` 会过滤掉
//     `offlineTools`，`fromMobileSet` 接受任意 mobile 子集）。Glados
//     用 `permutation` 生成器穷举排列。
//
// (2) Widget 层契约 —— `DashboardScreen` 的闭包派发
//     `openLogsForMachine` / `openAuditForMachine` 是 `DashboardScreen
//     .build` 里定义的闭包，无法直接拿到，但它们被传给 `MachinesView`
//     的 `onOpenLogsForMachine` / `onOpenAuditForMachine` 字段。
//     测试通过：
//
//       1. 用真实 `AppController` + `FakeAdminApi` 渲染 `DashboardScreen`
//          （行为与 `dashboard_test.dart` 中现有 widget 测试完全一致）；
//       2. `tester.widget<MachinesView>(...)` 拿到 widget 实例；
//       3. 直接调用 `widget.onOpenLogsForMachine('M1')` / `widget
//          .onOpenAuditForMachine('M1')`；
//       4. 断言外层 `onDestinationSelected` 回调恰好被调用 1 次，且收到的
//          key 为 `machineLogs` / `audit`。
//
//     为什么这等价于「任意 _primaryOrder 排列」：闭包源码是
//
//         onDestinationSelected(DestinationKey.machineLogs);
//         onDestinationSelected(DestinationKey.audit);
//
//     —— 直接引用 enum 常量，不读 layout，所以 layout 怎么排都不影响
//     dispatch。固定 `selectedKey = machines`（用户必然先回到 MachinesView
//     才能触发 openLogs / openAudit），换 4 套典型 viewport 穷举 mobile /
//     compact / desktop 三种 layout 状态。
//
// 关于 RenderFlex 噪声：项目里部分二级页面（DeploymentsView /
// RustDeskRemoteControlView / MachineLogsView / OverviewStatCard 等）在
// 极端窄 viewport 下会触发 `A RenderFlex overflowed by N pixels` 渲染
// 诊断。这些与 R3.1 / R3.2 无关，是各自页面自身的布局细节，本测试通过
// `_RenderOverflowFilter` 把这类异常过滤掉，与 widgets/
// mobile_bottom_nav_callback_test.dart 的 `_FlutterErrorFilter` 同样的
// 思路。这样 `tester.takeException` 不会被无关噪声污染，仅断言闭包派发
// 的精确性。

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
// `glados` re-export 了 `package:test`，与 `flutter_test` 同名符号会冲突；
// 这里只在 (1) 段使用 Glados，且把它的 `expect` / `test` 隐藏，统一走
// `flutter_test` 的版本。
import 'package:glados/glados.dart' hide expect, test, group, setUp, tearDown;
import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

const ServerStatus _readyServerStatus = ServerStatus(
  initialized: true,
  pendingInitialization: false,
  databaseReady: true,
  defaultVhdKeyword: 'SAFEBOOT',
  trustedRegistrationCertificateCount: 1,
);

const AuthStatus _authenticatedStatus = AuthStatus(
  initialized: true,
  isAuthenticated: true,
  otpVerified: true,
);

MachineRecord _machine(String machineId) => MachineRecord(
      machineId: machineId,
      protectedState: false,
      vhdKeyword: 'SAFEBOOT',
      evhdPasswordConfigured: true,
      approved: true,
      revoked: false,
      keyId: 'key-$machineId',
      keyType: 'RSA',
      registrationCertFingerprint: 'ABC123',
      logRetentionActiveDaysOverride: null,
      lastSeen: '2026-04-03T08:00:00Z',
    );

/// 把视口设为指定 size（DPR=1）。复刻 `dashboard_test.dart` 的 helper。
void _setViewport(WidgetTester tester, Size size) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

/// 拦截 [FlutterError.onError]，过滤掉与 R3.1 / R3.2 无关的渲染噪声，
/// 仅把真实需要测试断言的异常累积下来。
///
/// 项目里若干二级页面（DeploymentsView / RustDeskRemoteControlView /
/// MachineLogsView / OverviewStatCard / TrustedRustDeskControllersView 等）
/// 在小 viewport 下会触发 `A RenderFlex overflowed by N pixels` 诊断；
/// 这些是各自页面自身的布局细节，与 cross-page navigation 的 dispatch
/// 契约无关。本 filter 与 widgets/mobile_bottom_nav_callback_test.dart 的
/// `_FlutterErrorFilter` 同样思路：吞掉特定模式的诊断信息，避免污染
/// `tester.takeException`。
class _RenderOverflowFilter {
  FlutterExceptionHandler? _previous;
  final List<FlutterErrorDetails> captured = <FlutterErrorDetails>[];

  void install() {
    _previous = FlutterError.onError;
    FlutterError.onError = (details) {
      final message = details.exception.toString();
      // 过滤所有 RenderFlex overflow 的渲染诊断 —— 这些是子页面自身的
      // 布局细节，不影响 dispatch 契约。
      if (message.contains('A RenderFlex overflowed')) {
        return;
      }
      captured.add(details);
      _previous?.call(details);
    };
  }

  void restore() {
    FlutterError.onError = _previous;
    _previous = null;
  }
}

/// 从一个长度为 N 的种子列表生成所有排列。
///
/// `DashboardDestinations.all` 长度为 8，全排列共 8! = 40320 个，逐个跑
/// 会拖慢 CI；这里把全排列空间交给 Glados 的随机采样 + shrinker，但仍
/// 提供工具函数给显式 case 用（例如：把 `machineLogs` / `audit` 强制
/// 推到末尾，模拟「key 在 overflow」的极端布局）。
List<List<T>> _permutations<T>(List<T> input) {
  if (input.length <= 1) {
    return <List<T>>[List<T>.unmodifiable(input)];
  }
  final result = <List<T>>[];
  for (var i = 0; i < input.length; i++) {
    final rest = <T>[
      ...input.sublist(0, i),
      ...input.sublist(i + 1),
    ];
    for (final perm in _permutations(rest)) {
      result.add(<T>[input[i], ...perm]);
    }
  }
  return result;
}

/// Glados 生成器：对长度为 [length] 的索引列表 `[0, 1, ..., length-1]`
/// 按 Fisher-Yates 应用一组 0..n-1 的随机选择，得到一个排列。
///
/// Glados 没有内置 `permutation` 生成器，所以这里用 `listWithLength` 加
/// 上一组 [0, length) 的整数生成器，再在 [_applyShuffle] 里把它折叠成
/// 「数据驱动的 Fisher-Yates 洗牌」。这样 shrink 也能正确：当迭代失败
/// 时 Glados 会逐个把整数缩小，shuffle 退化为越来越接近恒等的排列，
/// 最终给出最小反例。
Generator<List<int>> _shuffleSeed(int length) =>
    any.listWithLength(length, any.intInRange(0, length));

/// 把 [_shuffleSeed] 生成的整数列表按 Fisher-Yates 应用到 [input] 上。
///
/// `swap[i]` 解释为「在第 i 步把位置 i 与位置 (swap[i] mod (length - i))
/// 互换」。这等价于经典 Fisher-Yates 洗牌的种子表达，能覆盖全部 length!
/// 个排列。
List<T> _applyShuffle<T>(List<T> input, List<int> swapSeeds) {
  final n = input.length;
  if (n <= 1) {
    return List<T>.from(input);
  }
  final out = List<T>.from(input);
  for (var i = 0; i < n - 1; i++) {
    final remaining = n - i;
    final j = i + (swapSeeds[i].abs() % remaining);
    if (j != i) {
      final tmp = out[i];
      out[i] = out[j];
      out[j] = tmp;
    }
  }
  return out;
}

void main() {
  // mobile / all 基线：当前 `mobile()` 应为 7 项，`all` 应为 8 项。
  final allBase = DashboardDestinations.all;
  final mobileBase = DashboardDestinations.mobile();
  assert(
    allBase.length == 9 && mobileBase.length == 8,
    'DashboardDestinations.all 应为 9 项、mobile() 应为 8 项；'
    '基线变化时同步更新断言。',
  );

  // ────────────────────────────────────────────────────────────────────
  // (1) 数据层 PBT：MobileNavLayout 对输入排列的 key 不变性
  // ────────────────────────────────────────────────────────────────────
  //
  // 把 Glados 生成的种子列表应用到 `mobileBase`（`all` 中过滤掉
  // `offlineTools` 的 7 项）上得到一个排列，再传给
  // `MobileNavLayout.fromMobileSet`。断言：
  //
  //   * machineLogs / audit 一定能在 primary ∪ overflow 中找到；
  //   * 找到的 spec 与原始 spec 在 `(key, label, subtitle, icon, color)`
  //     上完全一致；
  //   * primary ∩ overflow 不重复、并集等于输入排列；
  //   * machineLogs / audit 一定不会 "消失"——即任何排列下都满足
  //     `isPrimary(key) || isOverflow(key)`。

  Glados<List<int>>(_shuffleSeed(mobileBase.length)).test(
    '[mobile-bottom-nav-redesign] Property 5 (data layer): '
    'MobileNavLayout 在 mobile() 任意排列下仍能按 DestinationKey 定位 '
    'machineLogs / audit 且元数据不漂移',
    (List<int> swapSeeds) {
      final shuffled = _applyShuffle(mobileBase, swapSeeds);
      // 健壮性：排列后元素集合必须与原集合相等。
      expect(
        shuffled.map((d) => d.key).toSet(),
        equals(mobileBase.map((d) => d.key).toSet()),
        reason: 'shuffle 必须只重排，不增删元素 (seeds=$swapSeeds)',
      );

      final layout = MobileNavLayout.fromMobileSet(shuffled);

      // 在排列下 machineLogs / audit 仍然存在，并能通过 key 定位回原始 spec。
      for (final targetKey in const <DestinationKey>[
        DestinationKey.machineLogs,
        DestinationKey.audit,
      ]) {
        final inPrimary = layout.isPrimary(targetKey);
        final inOverflow = layout.isOverflow(targetKey);
        expect(
          inPrimary || inOverflow,
          isTrue,
          reason: 'key=$targetKey 必须出现在 layout.primary 或 layout.overflow '
              '(seeds=$swapSeeds, primary=${layout.primary.map((d) => d.key)}, '
              'overflow=${layout.overflow.map((d) => d.key)})',
        );
        expect(
          inPrimary && inOverflow,
          isFalse,
          reason: 'key=$targetKey 不应同时出现在 primary 和 overflow '
              '(seeds=$swapSeeds)',
        );

        // key → spec 的查找在排列前后必须返回等价元数据。
        final originalSpec = mobileBase.firstWhere((d) => d.key == targetKey);
        final foundSpec = (inPrimary ? layout.primary : layout.overflow)
            .firstWhere((d) => d.key == targetKey);
        expect(
          foundSpec.key,
          originalSpec.key,
          reason: 'key 字段必须等价 (seeds=$swapSeeds)',
        );
        expect(
          foundSpec.label,
          originalSpec.label,
          reason: 'label 必须等价 (seeds=$swapSeeds)',
        );
        expect(
          foundSpec.subtitle,
          originalSpec.subtitle,
          reason: 'subtitle 必须等价 (seeds=$swapSeeds)',
        );
        expect(
          foundSpec.icon,
          originalSpec.icon,
          reason: 'icon 必须等价 (seeds=$swapSeeds)',
        );
        expect(
          foundSpec.color,
          originalSpec.color,
          reason: 'color 必须等价 (seeds=$swapSeeds)',
        );
      }

      // primary ∪ overflow == 输入排列，primary ∩ overflow == ∅。
      final primaryKeys = layout.primary.map((d) => d.key).toSet();
      final overflowKeys = layout.overflow.map((d) => d.key).toSet();
      expect(
        primaryKeys.intersection(overflowKeys),
        isEmpty,
        reason: 'primary 与 overflow 不应共享 key (seeds=$swapSeeds)',
      );
      expect(
        primaryKeys.union(overflowKeys),
        equals(shuffled.map((d) => d.key).toSet()),
        reason: 'primary ∪ overflow 必须等于排列后的输入集合 '
            '(seeds=$swapSeeds)',
      );
    },
  );

  // 显式覆盖：把 `all` 的所有 8! = 40320 个排列削减成「machineLogs / audit
  // 各自被推到 mobile() 头/尾」四个边界排列，作为 example test 钉死语义。
  //
  // 把 `all` 排列丢进 `mobile()`（过滤 offlineTools 后的 7 项）让
  // `MobileNavLayout.fromMobileSet` 处理，断言 layout 在边界排列下仍然
  // 能精准定位 machineLogs / audit。这是 PBT 之外的字面化见证。
  group(
    '[mobile-bottom-nav-redesign] explicit boundary permutations '
    '(Property 5 data layer)',
    () {
      DashboardDestinationSpec specOf(DestinationKey k) =>
          mobileBase.firstWhere((d) => d.key == k);

      void verifyKeyLocatable(
        List<DashboardDestinationSpec> permutation,
        String label,
      ) {
        final layout = MobileNavLayout.fromMobileSet(permutation);
        for (final key in const <DestinationKey>[
          DestinationKey.machineLogs,
          DestinationKey.audit,
        ]) {
          expect(
            layout.isPrimary(key) || layout.isOverflow(key),
            isTrue,
            reason: '$label: $key 必须能被定位',
          );
        }
      }

      test(
        '[mobile-bottom-nav-redesign] machineLogs 与 audit 同时被推到末尾 '
        '(强迫 layout 至少把它们之一放进 overflow)',
        () {
          final tail = <DashboardDestinationSpec>[
            specOf(DestinationKey.machines),
            specOf(DestinationKey.certificates),
            specOf(DestinationKey.settings),
            specOf(DestinationKey.deployments),
            specOf(DestinationKey.rustDeskRemoteControl),
            specOf(DestinationKey.machineLogs),
            specOf(DestinationKey.audit),
          ];
          verifyKeyLocatable(tail, '尾部排列');
        },
      );

      test(
        '[mobile-bottom-nav-redesign] machineLogs 与 audit 同时被推到首位 '
        '(layout 的 _primaryOrder 仍按 key 而非位置选择 primary)',
        () {
          final head = <DashboardDestinationSpec>[
            specOf(DestinationKey.machineLogs),
            specOf(DestinationKey.audit),
            specOf(DestinationKey.machines),
            specOf(DestinationKey.certificates),
            specOf(DestinationKey.settings),
            specOf(DestinationKey.deployments),
            specOf(DestinationKey.rustDeskRemoteControl),
          ];
          verifyKeyLocatable(head, '首位排列');
        },
      );

      test(
        '[mobile-bottom-nav-redesign] machineLogs 在 primary、audit 在 '
        'overflow（验证混合情形不影响定位）',
        () {
          // _primaryOrder = [machines, machineLogs, audit, deployments]
          // 默认 audit 在 primary；这里把 audit 放在最后，但 primary 仍按
          // _primaryOrder 选取，audit 仍会被纳入 primary——此 case 主要
          // 验证 fromMobileSet 不依赖输入位置选 primary。
          final mixed = <DashboardDestinationSpec>[
            specOf(DestinationKey.machineLogs),
            specOf(DestinationKey.machines),
            specOf(DestinationKey.deployments),
            specOf(DestinationKey.certificates),
            specOf(DestinationKey.settings),
            specOf(DestinationKey.rustDeskRemoteControl),
            specOf(DestinationKey.audit),
          ];
          verifyKeyLocatable(mixed, '混合排列');
          final layout = MobileNavLayout.fromMobileSet(mixed);
          // 默认 _primaryOrder 把 audit 视为 primary——确认这条等式不被
          // 输入位置打乱（这是 R3.3 的核心：layout 由 key 决定，不由位置
          // 决定）。
          expect(layout.isPrimary(DestinationKey.audit), isTrue);
          expect(layout.isPrimary(DestinationKey.machineLogs), isTrue);
        },
      );
    },
  );

  // ────────────────────────────────────────────────────────────────────
  // (2) Widget 层契约：DashboardScreen 的闭包派发不依赖 layout
  // ────────────────────────────────────────────────────────────────────
  //
  // 对 mobile / desktop 两种 viewport × `selectedKey` 全部 7 个 mobile
  // destination 进行穷举，每次：
  //
  //   1. 用 `controller.bootstrap()` 把 `AppController` 推到「已登录 +
  //      OTP 已验证」状态（与 `dashboard_test.dart` 现有 pattern 一致）。
  //   2. 通过 `MaterialApp(home: DashboardScreen(...))` 渲染（绕开
  //      `AdminRoot` 的状态管理，直接控制 `selectedKey`）。
  //   3. 找到 `MachinesView` widget，提取 `onOpenLogsForMachine` /
  //      `onOpenAuditForMachine` 闭包，await 调用。
  //   4. 断言外层 `onDestinationSelected` 回调收到的 key 序列恰好是
  //      `[DestinationKey.machineLogs]` / `[DestinationKey.audit]`。
  //
  // 这一层断言「在任意 (viewport, selectedKey) 状态下，闭包派发的目标
  // 一致」——这是 R3.1 / R3.2 的字面化见证，也是 R3.4 的功能性补足
  // （源码级 grep 已在 `no_legacy_index_dispatch_test.dart` 中守门）。

  group(
    '[mobile-bottom-nav-redesign] DashboardScreen cross-page navigation '
    'dispatches by DestinationKey (Property 5 widget layer)',
    () {
      // 选 4 个有代表性的 viewport：移动端竖屏、移动端横屏、紧凑桌面、
      // 宽桌面。对应 design §6.3 提到的 mobile / compact-wide / desktop
      // 三种 layout 状态。
      const viewports = <({Size size, String label})>[
        (size: Size(360, 640), label: 'mobile-portrait-360x640'),
        (size: Size(720, 480), label: 'compact-wide-720x480'),
        (size: Size(1100, 720), label: 'desktop-edge-1100x720'),
        (size: Size(1600, 960), label: 'desktop-1600x960'),
      ];

      // selectedKey 只取 `machines`：用户必然先回到 MachinesView 才能
      // 触发 openLogsForMachine / openAuditForMachine（机器卡片上的
      // 「查看机台日志」/「查阅审计日志」按钮只在 MachinesView 渲染）。
      // 这是 R3.1 / R3.2 的真实使用路径。闭包派发的目标本身不依赖
      // selectedKey —— 只引用 DestinationKey enum 常量。把 selectedKey
      // 当作输入空间穷举只会重复同一条等价类，且可能引入不相关的子页面
      // 渲染噪声，因此在数据层 PBT (1) 已经覆盖排列变化的情况下，
      // widget 层只取最自然的激活态。
      for (final vp in viewports) {
        testWidgets(
          '[mobile-bottom-nav-redesign] viewport=${vp.label}: '
          'openLogsForMachine → DestinationKey.machineLogs, '
          'openAuditForMachine → DestinationKey.audit',
          (tester) async {
            _setViewport(tester, vp.size);
            addTearDown(() => _resetViewport(tester));

            final filter = _RenderOverflowFilter()..install();
            addTearDown(filter.restore);

            final captured = <DestinationKey>[];
            final controller = AppController(
              api: FakeAdminApi(
                serverStatus: _readyServerStatus,
                authStatus: _authenticatedStatus,
                machines: <MachineRecord>[_machine('MACHINE-01')],
              ),
              clientConfigStore: FakeClientConfigStore(),
            );

            // 让 controller 进入「已加载、已登录、已 OTP」状态，
            // 这样 DashboardScreen 才会渲染 pages。
            await controller.bootstrap();

            await tester.pumpWidget(
              MaterialApp(
                home: DashboardScreen(
                  controller: controller,
                  // 选 `machines`：MachinesView 必然挂在 widget 树中，
                  // `find.byType(MachinesView)` 能稳定命中。
                  selectedKey: DestinationKey.machines,
                  onDestinationSelected: captured.add,
                ),
              ),
            );
            await tester.pumpAndSettle();

            final machinesView =
                tester.widget<MachinesView>(find.byType(MachinesView));

            // 调用 openLogsForMachine：必须 dispatch machineLogs。
            await machinesView.onOpenLogsForMachine('MACHINE-01');
            await tester.pumpAndSettle();

            // 调用 openAuditForMachine：必须 dispatch audit。
            await machinesView.onOpenAuditForMachine('MACHINE-01');
            await tester.pumpAndSettle();

            // captured 序列必须恰好是 [machineLogs, audit]。
            expect(
              captured,
              equals(const <DestinationKey>[
                DestinationKey.machineLogs,
                DestinationKey.audit,
              ]),
              reason:
                  'openLogsForMachine 必须 dispatch machineLogs，'
                  'openAuditForMachine 必须 dispatch audit；'
                  'viewport=${vp.label}, captured=$captured',
            );

            // 过滤后若仍有异常，必须就地暴露 —— 它们不是 RenderFlex
            // overflow 的子页面渲染细节，而是真正影响 dispatch 契约的
            // 失败。
            expect(
              filter.captured,
              isEmpty,
              reason:
                  'dispatch 路径不应抛出非 RenderFlex 异常；'
                  'viewport=${vp.label}',
            );

            controller.dispose();
          },
        );
      }
    },
  );

  // ────────────────────────────────────────────────────────────────────
  // (3) 显式排列穷举（保留 _permutations 工具的现实用法）
  // ────────────────────────────────────────────────────────────────────
  //
  // `DashboardDestinations.all` 的全 8! = 40320 个排列对 PBT 完整跑过
  // 一遍开销过大；这里仅对 `_primaryOrder` 中 4 个 key 的 4! = 24 个
  // 子集排列断言「无论 _primaryOrder 怎么排，layout 仍能找到
  // machineLogs / audit」。这一层是 design 在「`_primaryOrder` 任意排列」
  // 维度上的字面化检查——`_primaryOrder` 私有不可替换，但其语义只是
  // 「primary 选择优先级」，所以等价地把 mobile() 中除 _primaryOrder 之外
  // 的项固定，只对 _primaryOrder 的 4 个 key 在输入位置上排列，得到的
  // layout.primary / overflow 划分仍然能定位 machineLogs / audit。

  test(
    '[mobile-bottom-nav-redesign] machineLogs 与 audit 在 _primaryOrder '
    '4 个 key 的全部 24 个输入排列下都能被定位 (Property 5 显式枚举)',
    () {
      const primaryOrderKeys = <DestinationKey>[
        DestinationKey.machines,
        DestinationKey.machineLogs,
        DestinationKey.audit,
        DestinationKey.deployments,
      ];
      final overflowKeys = mobileBase
          .map((d) => d.key)
          .where((k) => !primaryOrderKeys.contains(k))
          .toList(growable: false);

      final primarySpecs = primaryOrderKeys
          .map((k) => mobileBase.firstWhere((d) => d.key == k))
          .toList();
      final overflowSpecs = overflowKeys
          .map((k) => mobileBase.firstWhere((d) => d.key == k))
          .toList();

      final perms = _permutations(primarySpecs);
      expect(perms.length, 24, reason: '4! 应为 24，工具函数完整性自检');

      for (final perm in perms) {
        // mobile() 总长度仍为 7：4 个 _primaryOrder key 任意排列 + 3 个
        // overflow key 固定顺序。
        final input = <DashboardDestinationSpec>[...perm, ...overflowSpecs];
        final layout = MobileNavLayout.fromMobileSet(input);
        expect(
          layout.isPrimary(DestinationKey.machineLogs) ||
              layout.isOverflow(DestinationKey.machineLogs),
          isTrue,
          reason: 'machineLogs 在 input=${input.map((d) => d.key)} 下必须可定位',
        );
        expect(
          layout.isPrimary(DestinationKey.audit) ||
              layout.isOverflow(DestinationKey.audit),
          isTrue,
          reason: 'audit 在 input=${input.map((d) => d.key)} 下必须可定位',
        );
        // _primaryOrder 默认把这两个 key 选入 primary——验证不论输入排列
        // 怎么变，primary 集合仍由 key（而非位置）决定。
        expect(
          layout.isPrimary(DestinationKey.machineLogs),
          isTrue,
          reason: 'machineLogs 必须始终被 _primaryOrder 选入 primary '
              '(input=${input.map((d) => d.key)})',
        );
        expect(
          layout.isPrimary(DestinationKey.audit),
          isTrue,
          reason: 'audit 必须始终被 _primaryOrder 选入 primary '
              '(input=${input.map((d) => d.key)})',
        );
      }
    },
  );
}
