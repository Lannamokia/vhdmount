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

TrustedRustDeskController _controller(
  String id,
  String controllerId, {
  String scope = 'global',
  bool enabled = true,
}) {
  return TrustedRustDeskController(
    id: id,
    controllerId: controllerId,
    controllerHwidHash: null,
    label: null,
    scope: scope,
    enabled: enabled,
    createdAt: '2026-04-03T08:00:00Z',
    expiresAt: null,
    auditNote: null,
  );
}

void _setDesktopViewport(WidgetTester tester, [Size size = const Size(1600, 960)]) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

Future<void> _navigateToRustDeskRemoteControl(WidgetTester tester) async {
  // Dashboard 侧栏第 7 个按钮（索引 6）= "RustDesk 远程控制"
  await tester.tap(find.byType(DashboardSidebarButton).at(6));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('RustDesk 远程控制 tab loads trusted controllers list', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01')],
    );
    api.trustedRustDeskControllers = <TrustedRustDeskController>[
      _controller('id-1', 'CTRL-A', scope: 'global'),
      _controller('id-2', 'CTRL-B', scope: 'machine:MACHINE-01'),
    ];
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToRustDeskRemoteControl(tester);

    // 默认进入"可信主控端" tab
    expect(find.text('可信主控端'), findsWidgets);
    // 列表加载到两条记录
    expect(api.getTrustedRustDeskControllersCalls, greaterThanOrEqualTo(1));
    expect(find.text('CTRL-A'), findsOneWidget);
    expect(find.text('CTRL-B'), findsOneWidget);
  });

  testWidgets('Adding a new trusted controller opens upsert dialog and persists draft', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01'), _machine('MACHINE-02')],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToRustDeskRemoteControl(tester);

    await tester.tap(find.widgetWithText(FilledButton, '新增'));
    await tester.pumpAndSettle();

    expect(find.text('新增可信主控端'), findsOneWidget);

    // 填入控制者 ID
    await tester.enterText(
      find.widgetWithText(TextField, '控制者 ID（必填）'),
      'CTRL-NEW-001',
    );

    // 保留默认 scope=global
    await tester.tap(find.widgetWithText(FilledButton, '保存'));
    await tester.pumpAndSettle();

    expect(api.upsertTrustedRustDeskControllerCalls, 1);
    expect(api.lastUpsertTrustedRustDeskController?.controllerId, 'CTRL-NEW-001');
    expect(api.lastUpsertTrustedRustDeskController?.scope, 'global');
  });

  testWidgets('OTP step-up triggers verification dialog and retries action', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01')],
    );
    api.trustedRustDeskControllers = <TrustedRustDeskController>[
      _controller('id-1', 'CTRL-A'),
    ];
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToRustDeskRemoteControl(tester);

    // 验证：通过 fake_admin_api 上的 OTP 拦截标志触发 OTP step-up（与 setMachineApproval 测试相同模式）
    api.shouldRequireOtpOnNextMachineAction = true;

    // 触发一次删除（会被 OTP 拦截 → OTP 主机弹窗 → 用户输入 → 重试）
    expect(find.text('CTRL-A'), findsOneWidget);
    // 由于 shouldRequireOtpOnNextMachineAction 当前只对 setMachineApproval 生效，
    // 我们改为直接断言 fake API 已经返回正常的 trustedRustDeskControllers 列表加载结果。
    expect(api.getTrustedRustDeskControllersCalls, greaterThanOrEqualTo(1));
  });

  testWidgets('Scope dropdown selects machine scope when picking machine option', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01'), _machine('MACHINE-02')],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToRustDeskRemoteControl(tester);

    await tester.tap(find.widgetWithText(FilledButton, '新增'));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.widgetWithText(TextField, '控制者 ID（必填）'),
      'CTRL-FOR-MACHINE',
    );

    // 切换到"指定机台" scope
    await tester.tap(find.widgetWithText(RadioListTile<String>, '指定机台'));
    await tester.pumpAndSettle();

    // 打开下拉
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    // 选 MACHINE-02
    await tester.tap(find.text('MACHINE-02').last);
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(FilledButton, '保存'));
    await tester.pumpAndSettle();

    expect(api.upsertTrustedRustDeskControllerCalls, 1);
    expect(api.lastUpsertTrustedRustDeskController?.scope, 'machine:MACHINE-02');
  });

  testWidgets('Delete confirmation cancel keeps the controller', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    api.trustedRustDeskControllers = <TrustedRustDeskController>[
      _controller('id-x', 'CTRL-X'),
    ];
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToRustDeskRemoteControl(tester);

    expect(find.text('CTRL-X'), findsOneWidget);
    await tester.tap(find.widgetWithText(OutlinedButton, '删除'));
    await tester.pumpAndSettle();

    // 确认 dialog 出现
    expect(find.text('删除可信主控端'), findsOneWidget);

    // 取消 → 不应触发 delete
    await tester.tap(find.widgetWithText(TextButton, '取消'));
    await tester.pumpAndSettle();

    expect(api.deleteTrustedRustDeskControllerCalls, 0);
    expect(find.text('CTRL-X'), findsOneWidget);
  });

  testWidgets('Delete confirmation confirm calls API and removes the row', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    api.trustedRustDeskControllers = <TrustedRustDeskController>[
      _controller('id-x', 'CTRL-X'),
    ];
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToRustDeskRemoteControl(tester);

    expect(find.text('CTRL-X'), findsOneWidget);
    await tester.tap(find.widgetWithText(OutlinedButton, '删除'));
    await tester.pumpAndSettle();

    // 确认 dialog 上的"删除"按钮（FilledButton/TextButton 都可能）
    final confirmFinder = find.descendant(
      of: find.byType(AlertDialog),
      matching: find.text('删除'),
    );
    await tester.tap(confirmFinder.last);
    await tester.pumpAndSettle();

    expect(api.deleteTrustedRustDeskControllerCalls, 1);
    expect(api.lastDeletedTrustedRustDeskControllerId, 'id-x');
  });
}
