// Widget test for 断点切换保留 selectedKey。
//
// **Validates: Requirements 4.5**
//
// Property 9 — 断点切换保留选中：
//   对任意 `selectedKey ∈ MobileDestinationSet ∪ DesktopDestinationSet(isWindows)`,
//   当 `LayoutBuilder` 下发的 constraints 跨越 720dp 或 1100dp 边界从 compact
//   切到非 compact（或反之）时，`_AdminRootState._selectedKey` SHALL 保持原值，
//   AND 当前渲染的 page widget SHALL 仍对应该 key（除非新布局不再暴露该 key
//   的极端 fallback 路径，本测试不涉及该极端，因为 selectedKey 取自
//   MobileDestinationSet 中既属于 mobile 又属于 desktop 的项）。
//
// 测试策略：
//   1) `_selectedKey` 是 [_AdminRootState] 的私有字段，无法直接探针。本测试
//      通过「当前渲染的 page widget runtimeType」作为可观察代理：tap 一次
//      MobileBottomNav 中的「审计」主槽切到 [DestinationKey.audit]，然后跨
//      720dp / 1100dp 两条边界来回切 4 次 viewport（`mobile`↔
//      `compact && !mobile`↔`!compact`），每次都断言：
//        a) [AuditView] 仍恰好渲染一次；
//        b) [MachinesView]（初始默认 page）已不在树中；
//        c) MobileBottomNav / DesktopSidebar 与新布局相符（compact 显示
//           MobileBottomNav，非 compact 显示 DashboardSidebarButton）。
//   2) `tester.binding.setSurfaceSize` 配合 `pumpAndSettle` 触发 LayoutBuilder
//      下发新 constraints，模拟真机旋转 / 桌面窗口缩放。
//
// 之所以选择 [DestinationKey.audit]：它同时存在于 MobileDestinationSet 与
// DesktopDestinationSet（任何 isWindows）中，跨断点切换时不会触发
// dashboard.dart §6.2 的 `?? pages[machines]` 兜底路径。这正是 Property 9
// 注脚里「除非新布局不再暴露该 key 的极端 fallback 路径」想避开的情况。
//
// 视口选择：
//   * `Size(360, 640)`：`mobile = true`、`compact = true`，触发 mobile 滚动
//     布局 + MobileBottomNav。
//   * `Size(900, 800)`：`mobile = false`、`compact = true`（width < 1100），
//     触发 compact 列布局 + MobileBottomNav，无 DesktopSidebar。
//   * `Size(1440, 900)`：`mobile = false`、`compact = false`，触发
//     DesktopSidebar 行布局，不再渲染 MobileBottomNav。

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

MachineRecord _machine(String machineId) {
  return MachineRecord(
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
}

Future<void> _setSurface(WidgetTester tester, Size size) async {
  await tester.binding.setSurfaceSize(size);
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

Future<void> _resetSurface(WidgetTester tester) async {
  await tester.binding.setSurfaceSize(null);
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

void main() {
  testWidgets(
    '[mobile-bottom-nav-redesign] Property 9: '
    '断点切换（720dp / 1100dp 边界）保留 selectedKey，'
    '渲染的 page widget runtimeType 不变',
    (tester) async {
      // 1) 起始视口：compact mobile 360×640。
      await _setSurface(tester, const Size(360, 640));
      addTearDown(() => _resetSurface(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _authenticatedStatus,
          machines: <MachineRecord>[_machine('MACHINE-01')],
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      // 默认进入后 _selectedKey == DestinationKey.machines，渲染
      // MachinesView；MobileBottomNav 在 compact 分支下挂载。
      expect(
        find.byType(MachinesView),
        findsOneWidget,
        reason: '初始 compact mobile 布局应渲染 MachinesView',
      );
      expect(
        find.byType(MobileBottomNav),
        findsOneWidget,
        reason: '初始 compact mobile 布局应挂载 MobileBottomNav',
      );

      // 2) 切到 [DestinationKey.audit]：tap MobileBottomNav 主槽中的「审计」。
      //
      // 主槽序列固定为 [machines, machineLogs, audit, deployments]
      //（design §Data Models / MobileNavLayout._primaryOrder），「审计」
      // 在 primary 中，单次 tap 即可切页。
      final auditSlot = find.descendant(
        of: find.byType(MobileBottomNav),
        matching: find.text('审计'),
      );
      expect(
        auditSlot,
        findsOneWidget,
        reason: '「审计」主槽应在 MobileBottomNav 中唯一可定位',
      );
      await tester.tap(auditSlot);
      await tester.pumpAndSettle();

      expect(
        find.byType(AuditView),
        findsOneWidget,
        reason: 'tap「审计」后应切到 AuditView',
      );
      expect(
        find.byType(MachinesView),
        findsNothing,
        reason: 'tap「审计」后 MachinesView 应被替换',
      );

      // 一个统一的「页面 / 布局」断言闭包，便于跨视口复用。
      void expectAuditAtLayout({
        required bool expectMobileBottomNav,
        required bool expectDesktopSidebar,
        required String stage,
      }) {
        expect(
          find.byType(AuditView),
          findsOneWidget,
          reason: '$stage：selectedKey 应保持 audit，AuditView 仍渲染',
        );
        expect(
          find.byType(MachinesView),
          findsNothing,
          reason: '$stage：selectedKey 不再是 machines，'
              'MachinesView 不应出现',
        );
        expect(
          find.byType(MobileBottomNav),
          expectMobileBottomNav ? findsOneWidget : findsNothing,
          reason: '$stage：MobileBottomNav 渲染状态应与 compact 分支匹配',
        );
        expect(
          find.byType(DashboardSidebarButton),
          expectDesktopSidebar ? findsAtLeastNWidgets(7) : findsNothing,
          reason: '$stage：DesktopSidebar 渲染状态应与非 compact 分支匹配',
        );
      }

      // 3) 跨 720dp 边界（mobile → compact && !mobile）：900×800。
      //    `mobile = false`、`compact = true`：MobileBottomNav 仍挂载，
      //    没有 DesktopSidebar。
      await _setSurface(tester, const Size(900, 800));
      await tester.pumpAndSettle();
      expectAuditAtLayout(
        expectMobileBottomNav: true,
        expectDesktopSidebar: false,
        stage: '900×800（compact && !mobile）',
      );

      // 4) 跨 1100dp 边界（compact → !compact）：1440×900。
      //    `mobile = false`、`compact = false`：MobileBottomNav 不再渲染，
      //    DesktopSidebar 出现（8 项 on Windows，7 项 on 其他平台；这里
      //    只断言 ≥7 以保持平台无关）。
      await _setSurface(tester, const Size(1440, 900));
      await tester.pumpAndSettle();
      expectAuditAtLayout(
        expectMobileBottomNav: false,
        expectDesktopSidebar: true,
        stage: '1440×900（!compact）',
      );

      // 5) 反向跨回 1100dp 边界回 compact && !mobile：900×800。
      //    selectedKey 应不受 desktop ↔ compact 切换影响。
      await _setSurface(tester, const Size(900, 800));
      await tester.pumpAndSettle();
      expectAuditAtLayout(
        expectMobileBottomNav: true,
        expectDesktopSidebar: false,
        stage: '回到 900×800（compact && !mobile）',
      );

      // 6) 反向跨回 720dp 边界回 mobile：360×640。
      //    selectedKey 仍为 audit；mobile 滚动 + MobileBottomNav 重新挂载。
      await _setSurface(tester, const Size(360, 640));
      await tester.pumpAndSettle();
      expectAuditAtLayout(
        expectMobileBottomNav: true,
        expectDesktopSidebar: false,
        stage: '回到 360×640（compact && mobile）',
      );

      expect(
        tester.takeException(),
        isNull,
        reason: '断点切换链路不应抛出未捕获异常',
      );
    },
  );
}
