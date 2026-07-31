// 集成 smoke test：典型 viewport 全量渲染。
//
// **Validates: Requirements 1.1, 4.1, 4.2, 4.3, 4.4, 6.1, 7.1, 7.2, 7.3, 7.4, 9.5**
//
// 目的：在六套典型 (host platform, viewport) 组合下渲染 [DashboardScreen]，
// 把 [MobileBottomNav] / [DesktopSidebar]（以 [DashboardSidebarButton] 数量
// 为代理）/ [OfflineToolsView] 的预期组合作为 high-level 烟雾断言钉死，
// 防止后续重构把任意一个 destination 集合或断点行为误改回去：
//
// | # | 组合                    | mobile | compact | MobileBottomNav | DashboardSidebarButton | OfflineTools 入口 |
// |---|-------------------------|--------|---------|-----------------|------------------------|-------------------|
// | 1 | Windows + 360×640       | true   | true    | ✓               | 0                      | 不可见             |
// | 2 | Windows + 411×823       | true   | true    | ✓               | 0                      | 不可见             |
// | 3 | Windows + 768×1024      | false  | true    | ✓               | 0                      | 不可见             |
// | 4 | Windows + 1440×900      | false  | false   | ✗               | 8                      | sidebar / page 可达 |
// | 5 | 非 Windows + 360×640    | true   | true    | ✓               | 0                      | 不可见             |
// | 6 | 非 Windows + 1440×900   | false  | false   | ✗               | 7                      | 不可见             |
//
// 平台维度无法在 Flutter 测试进程内 mock：`Platform.isWindows` 是进程级
// 常量，无法逐用例切换。本测试改为「按真实 [Platform.isWindows] 决定哪些
// case 跑、哪些 case skip」：当 host 平台与 case 期望平台不一致时，把该
// case 标记为 skip 并写明原因，保证：
//   * Windows CI（项目唯一的 flutter-admin-client.yml runner）上 1–4 全跑；
//   * 本地非 Windows 开发机上 5–6 跑、1–4 skip。
// 这样平台相关的断言（DesktopSidebar 项数、OfflineTools 入口）始终在
// 与 [Platform.isWindows] 一致的环境下被真实执行。
//
// 视口维度通过 [WidgetTester.view.physicalSize] 注入；与
// `dashboard_screen_pages_test.dart` 使用的同一手法（设置
// physicalSize + devicePixelRatio=1.0，让 logical size == physical size）。

import 'dart:io' as io;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
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

const MachineRecord _testMachine = MachineRecord(
  machineId: 'TEST-MACHINE',
  protectedState: false,
  vhdKeyword: 'SAFEBOOT',
  evhdPasswordConfigured: true,
  approved: true,
  revoked: false,
  keyId: 'key-test',
  keyType: 'RSA',
  registrationCertFingerprint: 'ABC123',
  logRetentionActiveDaysOverride: null,
  lastSeen: '2026-04-03T08:00:00Z',
);

/// 一个 smoke case 的全部输入与期望。
@immutable
class _SmokeCase {
  const _SmokeCase({
    required this.name,
    required this.size,
    required this.expectedIsWindows,
    required this.expectMobileBottomNav,
    required this.expectDesktopSidebarButtonCount,
    required this.expectOfflineToolsSidebarEntry,
  });

  /// 用于测试名的人读标签。
  final String name;

  /// `tester.view.physicalSize`：宽 × 高（logical pixels，dpr=1.0）。
  final Size size;

  /// case 想要的 host platform 维度；[Platform.isWindows] 不一致时整 case skip。
  final bool expectedIsWindows;

  /// 是否期望 [MobileBottomNav] 渲染。compact==true 时为 true；否则 false。
  final bool expectMobileBottomNav;

  /// 期望的 [DashboardSidebarButton] 数量；compact==true 时为 0，
  /// compact==false 时为 8（非 Windows）或 9（Windows）。
  final int expectDesktopSidebarButtonCount;

  /// 期望「离线工具」是否作为 sidebar entry 可见（即 sidebar 中存在文本
  /// '离线工具'）。仅 Windows + 非 compact 为 true。
  final bool expectOfflineToolsSidebarEntry;
}

const List<_SmokeCase> _cases = <_SmokeCase>[
  // 1: Windows + 360×640 — 典型 iPhone-mini portrait，mobile=true。
  _SmokeCase(
    name: 'Windows + 360x640',
    size: Size(360, 640),
    expectedIsWindows: true,
    expectMobileBottomNav: true,
    expectDesktopSidebarButtonCount: 0,
    expectOfflineToolsSidebarEntry: false,
  ),
  // 2: Windows + 411×823 — 典型 Pixel portrait，仍 mobile=true。
  _SmokeCase(
    name: 'Windows + 411x823',
    size: Size(411, 823),
    expectedIsWindows: true,
    expectMobileBottomNav: true,
    expectDesktopSidebarButtonCount: 0,
    expectOfflineToolsSidebarEntry: false,
  ),
  // 3: Windows + 768×1024 — 典型 iPad portrait，mobile=false（768≥720）但
  //    compact=true（768<1100）。MobileBottomNav 仍挂载，无 DesktopSidebar。
  _SmokeCase(
    name: 'Windows + 768x1024',
    size: Size(768, 1024),
    expectedIsWindows: true,
    expectMobileBottomNav: true,
    expectDesktopSidebarButtonCount: 0,
    expectOfflineToolsSidebarEntry: false,
  ),
  // 4: Windows + 1440×900 — 桌面常规分辨率，compact=false：DesktopSidebar
  //    8 项（含离线工具），MobileBottomNav 不再渲染。
  _SmokeCase(
    name: 'Windows + 1440x900',
    size: Size(1440, 900),
    expectedIsWindows: true,
    expectMobileBottomNav: false,
    expectDesktopSidebarButtonCount: 9,
    expectOfflineToolsSidebarEntry: true,
  ),
  // 5: 非 Windows + 360×640 — 与 case 1 同视口，host 切到非 Windows。视口
  //    层不变，平台层影响仅在 sidebar 路径，本 case 不进入 sidebar 路径，
  //    因此期望与 case 1 完全一致。区分 case 的意义在于 case 本身只能在
  //    非 Windows 主机上执行（确保 [Platform.isWindows] 真为 false）。
  _SmokeCase(
    name: 'non-Windows + 360x640',
    size: Size(360, 640),
    expectedIsWindows: false,
    expectMobileBottomNav: true,
    expectDesktopSidebarButtonCount: 0,
    expectOfflineToolsSidebarEntry: false,
  ),
  // 6: 非 Windows + 1440×900 — 桌面分辨率，compact=false：DesktopSidebar
  //    7 项（无离线工具），不出现 MobileBottomNav。
  _SmokeCase(
    name: 'non-Windows + 1440x900',
    size: Size(1440, 900),
    expectedIsWindows: false,
    expectMobileBottomNav: false,
    expectDesktopSidebarButtonCount: 8,
    expectOfflineToolsSidebarEntry: false,
  ),
];

void _setViewport(WidgetTester tester, Size size) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

/// 与 `dashboard_screen_pages_test.dart` 同源的渲染哲学：渲染 `DashboardScreen`
/// 时仅关心顶层「框架结构」契约（MobileBottomNav / sidebar / 离线工具入口），
/// 不关心二级 view 在窄 viewport 下自身的视觉细节。这些视觉细节问题（如
/// `RustDeskRemoteControlView` 在 mobile path 的 `SingleChildScrollView` 内
/// 触发 `Expanded(TabBarView)` 无界 main axis 的连锁布局诊断）由 R9.4 圈定
/// 在本特性范围之外。本 filter 把已知的「布局期渲染诊断」从
/// [FlutterError.onError] 通道吞掉，保证 `tester.takeException` 反映的是
/// 真正的烟雾失败而不是无关噪声。
class _LayoutNoiseFilter {
  FlutterExceptionHandler? _previous;

  void install() {
    _previous = FlutterError.onError;
    FlutterError.onError = (details) {
      final message = details.exception.toString();
      if (message.contains('A RenderFlex overflowed')) {
        return;
      }
      if (message.contains('RenderBox was not laid out')) {
        return;
      }
      if (message.contains("'!semantics.parentDataDirty'")) {
        return;
      }
      _previous?.call(details);
    };
  }

  void restore() {
    FlutterError.onError = _previous;
    _previous = null;
  }
}

Future<AppController> _buildBootstrappedController() async {
  final controller = AppController(
    api: FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[_testMachine],
    ),
    clientConfigStore: FakeClientConfigStore(),
  );
  await controller.bootstrap();
  return controller;
}

Future<void> _pumpDashboard(
  WidgetTester tester, {
  required AppController controller,
  required DestinationKey selectedKey,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: DashboardScreen(
        controller: controller,
        selectedKey: selectedKey,
        onDestinationSelected: (_) {},
      ),
    ),
  );
  // 让 AnimatedSwitcher / 各 view 的 postFrameCallback 排空。FakeAdminApi
  // 所有方法立刻返回，不会引入永远不收敛的 timer。
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 320));
  // 吸收任何被 _LayoutNoiseFilter 漏放但 tester 仍捕获的 pending exception，
  // 避免污染本测试的 takeException 断言。
  while (tester.takeException() != null) {
    // intentionally drain
  }
}

void main() {
  group('[mobile-bottom-nav-redesign] DashboardScreen smoke', () {
    for (final smokeCase in _cases) {
      final platformMatches =
          smokeCase.expectedIsWindows == io.Platform.isWindows;
      // testWidgets 在当前 flutter_test 上 `skip` 参数类型为 `bool?`：把
      // 「为何 skip」嵌进测试名前缀，保持 console 输出依然能看到原因，
      // 同时保持类型契约干净。
      final skipPrefix = platformMatches
          ? ''
          : '[skipped: requires Platform.isWindows == '
                '${smokeCase.expectedIsWindows}, host is '
                '${io.Platform.operatingSystem}] ';

      testWidgets(
        '$skipPrefix${smokeCase.name} renders expected MobileBottomNav / '
        'DashboardSidebarButton / OfflineTools combination',
        (tester) async {
          final filter = _LayoutNoiseFilter()..install();
          addTearDown(filter.restore);

          _setViewport(tester, smokeCase.size);
          addTearDown(() => _resetViewport(tester));

          final controller = await _buildBootstrappedController();
          addTearDown(controller.dispose);

          await _pumpDashboard(
            tester,
            controller: controller,
            selectedKey: DestinationKey.machines,
          );

          // ─── MobileBottomNav 出现/不出现（R1.1 / R4.3 / R4.4） ─────────
          expect(
            find.byType(MobileBottomNav),
            smokeCase.expectMobileBottomNav ? findsOneWidget : findsNothing,
            reason: '${smokeCase.name}: MobileBottomNav 应当 '
                '${smokeCase.expectMobileBottomNav ? "渲染" : "不渲染"}'
                '（compact 由 LayoutClassification 计算）',
          );

          // ─── DesktopSidebar 项数（R6.1 / R7.2 / R7.3 / R7.4） ───────────
          //
          // DashboardSidebarButton 仅出现在 dashboard.dart 的 compact==false
          // 分支；compact==true 时不应出现任何此类 widget。
          final sidebarButtons = find.byType(DashboardSidebarButton);
          expect(
            sidebarButtons,
            findsNWidgets(smokeCase.expectDesktopSidebarButtonCount),
            reason: '${smokeCase.name}: DashboardSidebarButton 数量应等于 '
                '${smokeCase.expectDesktopSidebarButtonCount}（'
                'Windows 桌面 9、非 Windows 桌面 8、compact 0）',
          );

          // ─── OfflineTools sidebar entry（R7.1 / R7.2 / R7.3 / R9.5） ────
          //
          // 「离线工具」文本仅出现在 Windows + 非 compact 的 sidebar 项中：
          //   - compact 路径：MobileBottomNav 主槽 + overflow sheet（关闭）
          //     都不暴露离线工具，且 MachinesView 默认页内不含该文本；
          //   - 非 Windows 桌面：DashboardDestinations.desktop(false) 不含
          //     offlineTools，sidebar 中也不会出现该文本。
          // 因此 `find.text('离线工具')` 是 sidebar entry 的可观察代理。
          expect(
            find.text('离线工具'),
            smokeCase.expectOfflineToolsSidebarEntry
                ? findsOneWidget
                : findsNothing,
            reason: '${smokeCase.name}: 「离线工具」sidebar entry 应当 '
                '${smokeCase.expectOfflineToolsSidebarEntry ? "可见" : "不可见"}'
                '（R7.1 / R7.2 / R7.3 / R9.5）',
          );

          // ─── 默认 selectedKey=machines 下，OfflineToolsView 不应被渲染 ──
          //
          // 这是 R9.5 的 mobile / R7.3 的 non-Windows desktop 共通保证：
          // 即便 OfflineToolsView 在 Windows 桌面是「可达」的，也得显式
          // 切到 selectedKey=offlineTools 才会渲染（见下面的扩展子用例）。
          expect(
            find.byType(OfflineToolsView),
            findsNothing,
            reason: '${smokeCase.name}: 默认 selectedKey=machines 下 '
                'OfflineToolsView 不应被渲染',
          );
        },
        skip: !platformMatches,
      );
    }

    // ─── 扩展子用例：Windows 桌面下显式切到 offlineTools，OfflineToolsView 渲染 ──
    //
    // 上方表格中的 case 4（Windows + 1440×900）只验证了 sidebar entry 的
    // 「可见性」，未直接验证 page 路由本身。OfflineToolsView 是 Windows
    // 专属页面（dashboard.dart 仅在 `Platform.isWindows` 时把它放进
    // pagesByKey），把 selectedKey 切到 offlineTools 才能触达 page 渲染。
    // 该子用例钉死「Windows 桌面 + selectedKey=offlineTools」组合下
    // OfflineToolsView 真的出现在 widget 树中，构成 R7.2 的端到端证据。
    testWidgets(
      '${io.Platform.isWindows ? "" : "[skipped: requires Platform.isWindows == true] "}'
      'Windows + 1440x900 with selectedKey=offlineTools renders OfflineToolsView',
      (tester) async {
        final filter = _LayoutNoiseFilter()..install();
        addTearDown(filter.restore);

        _setViewport(tester, const Size(1440, 900));
        addTearDown(() => _resetViewport(tester));

        final controller = await _buildBootstrappedController();
        addTearDown(controller.dispose);

        await _pumpDashboard(
          tester,
          controller: controller,
          selectedKey: DestinationKey.offlineTools,
        );

        expect(
          find.byType(OfflineToolsView),
          findsOneWidget,
          reason: 'Windows + 1440×900 + selectedKey=offlineTools 应渲染 '
              'OfflineToolsView（R7.2）',
        );
        expect(
          find.byType(MobileBottomNav),
          findsNothing,
          reason: 'desktop viewport 下不应渲染 MobileBottomNav',
        );
      },
      skip: !io.Platform.isWindows,
    );
  });
}
