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
    registrationCertFingerprint: 'ABC',
    logRetentionActiveDaysOverride: null,
    lastSeen: '2026-04-03T08:00:00Z',
  );
}

RustDeskReportSummary _report(
  String machineId, {
  String rustDeskId = '123456789',
  String passwordKind = 'temporary',
  String? passwordHashPrefix = 'abcdef12',
}) {
  return RustDeskReportSummary(
    machineId: machineId,
    rustDeskId: rustDeskId,
    passwordKind: passwordKind,
    reportedAt: '2026-04-03T08:00:00Z',
    passwordHashPrefix: passwordHashPrefix,
    lastWrapKeyId: 'wrap-key-1',
    secretVersion: 1,
    updatedAt: '2026-04-03T08:00:01Z',
  );
}

void _setDesktopViewport(WidgetTester tester,
    [Size size = const Size(1600, 960)]) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

Future<void> _navigateToReportsTab(WidgetTester tester) async {
  // Dashboard 侧栏第 7 个按钮（索引 6）= "RustDesk 远程控制"
  await tester.tap(find.byType(DashboardSidebarButton).at(6));
  await tester.pumpAndSettle();
  // 切到第三个 Tab "上报记录"
  await tester.tap(find.text('上报记录'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('RustDesk 上报记录 tab 加载列表', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01')],
    );
    api.rustDeskReports = <RustDeskReportSummary>[
      _report('MACHINE-01', rustDeskId: '111222333'),
      _report('MACHINE-02', rustDeskId: '444555666', passwordKind: 'absent',
          passwordHashPrefix: null),
    ];
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToReportsTab(tester);

    expect(api.getRustDeskReportsCalls, greaterThanOrEqualTo(1));
    expect(find.text('MACHINE-01'), findsWidgets);
    expect(find.text('MACHINE-02'), findsWidgets);
    expect(find.text('RustDesk ID 111222333'), findsOneWidget);
    expect(find.text('RustDesk ID 444555666'), findsOneWidget);
  });

  testWidgets('RustDesk 上报记录 reveal 流程：打开对话框、调用 readPlaintext',
      (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01')],
    );
    api.rustDeskReports = <RustDeskReportSummary>[
      _report('MACHINE-01', rustDeskId: '111222333'),
    ];
    api.rustDeskReportPlaintexts = <String, String>{
      'MACHINE-01': 'super-secret-pwd',
    };
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToReportsTab(tester);

    // 点击"读取明文"
    await tester.tap(find.text('读取明文'));
    await tester.pumpAndSettle();

    // 输入查询原因（dialog 已经预填了 'support investigation'，直接确认）
    final confirmFinder = find.text('确认');
    if (confirmFinder.evaluate().isNotEmpty) {
      await tester.tap(confirmFinder.first);
    } else {
      // 兼容 confirm 按钮文本不是"确认"的情况：取最后一个 FilledButton
      await tester.tap(find.byType(FilledButton).last);
    }
    await tester.pumpAndSettle();

    expect(api.readRustDeskReportPlaintextCalls, 1);
    expect(api.lastReadRustDeskReportMachineId, 'MACHINE-01');
    expect(
      api.lastReadRustDeskReportReason,
      isNotNull,
      reason: 'reveal 流程必须把 reason 透传到 API',
    );
    // plaintext 对话框已弹出，且密码可见
    expect(find.text('super-secret-pwd'), findsOneWidget);
  });

  testWidgets('passwordKind == absent 时"读取明文"按钮禁用', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01')],
    );
    api.rustDeskReports = <RustDeskReportSummary>[
      _report('MACHINE-01',
          passwordKind: 'absent', passwordHashPrefix: null),
    ];
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToReportsTab(tester);

    expect(find.text('密码未上报'), findsOneWidget);
    final btnFinder = find.widgetWithText(FilledButton, '密码未上报');
    expect(btnFinder, findsOneWidget);
    final btn = tester.widget<FilledButton>(btnFinder);
    expect(btn.onPressed, isNull,
        reason: 'absent 类型时按钮应禁用，避免对空密码触发明文读取');
  });
}
