import 'dart:convert';

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

void _setDesktopViewport(WidgetTester tester, [Size size = const Size(1600, 960)]) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

Future<void> _navigateToBridgeSecretTab(WidgetTester tester) async {
  await tester.tap(find.byType(DashboardSidebarButton).at(6));
  await tester.pumpAndSettle();
  // 切到第二个 Tab（命名管道交互密钥）
  await tester.tap(find.text('命名管道交互密钥'));
  await tester.pumpAndSettle();
}

Future<void> _openInputDialog(WidgetTester tester) async {
  await tester.tap(find.widgetWithText(FilledButton, '录入新版本'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('Bridge secret tab loads versions and shows empty state initially', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await _navigateToBridgeSecretTab(tester);

    expect(api.getBridgeSecretVersionsCalls, greaterThanOrEqualTo(1));
    expect(find.textContaining('尚未录入'), findsOneWidget);
  });

  testWidgets('Hex tab: 64 hex chars enables submit; invalid disables', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);
    await _openInputDialog(tester);

    final hexField = find.widgetWithText(TextField, '64 位十六进制（32 字节）');
    expect(hexField, findsOneWidget);

    // 边界：63 字符不够
    await tester.enterText(hexField, 'a' * 63);
    await tester.pump();
    var submitBtn = tester.widget<FilledButton>(find.widgetWithText(FilledButton, '提交并激活'));
    expect(submitBtn.onPressed, isNull, reason: '63 字符的 hex 应当无法提交');

    // 64 字符 ✓
    await tester.enterText(hexField, 'b' * 64);
    await tester.pump();
    submitBtn = tester.widget<FilledButton>(find.widgetWithText(FilledButton, '提交并激活'));
    expect(submitBtn.onPressed, isNotNull, reason: '64 字符的合法 hex 应当能提交');

    // 边界：含非 hex 字符
    await tester.enterText(hexField, '${'b' * 62}ZZ');
    await tester.pump();
    // FilteringTextInputFormatter 会过滤掉 ZZ → 只剩 62 字符
    submitBtn = tester.widget<FilledButton>(find.widgetWithText(FilledButton, '提交并激活'));
    expect(submitBtn.onPressed, isNull);
  });

  testWidgets('Hex submit: 32-byte hex calls uploadBridgeSecret with hex format', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);
    await _openInputDialog(tester);

    final hexValue = 'ababababababababababababababababababababababababababababababab' 'ab';
    expect(hexValue.length, 64);

    await tester.enterText(
      find.widgetWithText(TextField, '64 位十六进制（32 字节）'),
      hexValue,
    );
    await tester.pump();

    await tester.tap(find.widgetWithText(FilledButton, '提交并激活'));
    await tester.pumpAndSettle();

    expect(api.uploadBridgeSecretCalls, 1);
    expect(api.lastUploadBridgeSecretFormat, BridgeSecretInputFormat.hex);
    expect(api.lastUploadBridgeSecretBytes?.length, 32);
    // 每字节都应当是 0xab
    expect(api.lastUploadBridgeSecretBytes!.every((b) => b == 0xab), isTrue);
  });

  testWidgets('Base64 tab: 32-byte base64 enables submit; short bytes disable', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);
    await _openInputDialog(tester);

    // 切到 Base64 tab
    await tester.tap(find.widgetWithText(Tab, 'Base64'));
    await tester.pumpAndSettle();

    final b64Field = find.widgetWithText(TextField, 'Base64（解码后 32 字节，约 43-44 字符）');
    expect(b64Field, findsOneWidget);

    // 31 字节 → 不够
    final tooShort = base64Encode(List<int>.filled(31, 0xab));
    await tester.enterText(b64Field, tooShort);
    await tester.pump();
    var submitBtn = tester.widget<FilledButton>(find.widgetWithText(FilledButton, '提交并激活'));
    expect(submitBtn.onPressed, isNull);

    // 33 字节 → 不行
    final tooLong = base64Encode(List<int>.filled(33, 0xab));
    await tester.enterText(b64Field, tooLong);
    await tester.pump();
    submitBtn = tester.widget<FilledButton>(find.widgetWithText(FilledButton, '提交并激活'));
    expect(submitBtn.onPressed, isNull);

    // 32 字节 → OK
    final justRight = base64Encode(List<int>.filled(32, 0xab));
    await tester.enterText(b64Field, justRight);
    await tester.pump();
    submitBtn = tester.widget<FilledButton>(find.widgetWithText(FilledButton, '提交并激活'));
    expect(submitBtn.onPressed, isNotNull);
  });

  testWidgets('Base64 submit: 32-byte base64 calls uploadBridgeSecret with base64 format', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);
    await _openInputDialog(tester);

    await tester.tap(find.widgetWithText(Tab, 'Base64'));
    await tester.pumpAndSettle();

    final b64 = base64Encode(List<int>.filled(32, 0xcd));
    await tester.enterText(
      find.widgetWithText(TextField, 'Base64（解码后 32 字节，约 43-44 字符）'),
      b64,
    );
    await tester.pump();

    await tester.tap(find.widgetWithText(FilledButton, '提交并激活'));
    await tester.pumpAndSettle();

    expect(api.uploadBridgeSecretCalls, 1);
    expect(api.lastUploadBridgeSecretFormat, BridgeSecretInputFormat.base64);
    expect(api.lastUploadBridgeSecretBytes?.length, 32);
    expect(api.lastUploadBridgeSecretBytes!.every((b) => b == 0xcd), isTrue);
  });

  testWidgets('Audit note is forwarded when provided', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);
    await _openInputDialog(tester);

    await tester.enterText(
      find.widgetWithText(TextField, '64 位十六进制（32 字节）'),
      '0' * 64,
    );
    await tester.pump();
    await tester.enterText(
      find.widgetWithText(TextField, '审计备注（可选）'),
      'Q4 rotate per ops policy',
    );
    await tester.pump();

    await tester.tap(find.widgetWithText(FilledButton, '提交并激活'));
    await tester.pumpAndSettle();

    expect(api.uploadBridgeSecretCalls, 1);
    expect(api.lastUploadBridgeSecretAuditNote, 'Q4 rotate per ops policy');
  });

  testWidgets('Cancel button on input dialog does not call uploadBridgeSecret', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);
    await _openInputDialog(tester);

    await tester.enterText(
      find.widgetWithText(TextField, '64 位十六进制（32 字节）'),
      'a' * 64,
    );
    await tester.pump();

    await tester.tap(find.widgetWithText(TextButton, '取消'));
    await tester.pumpAndSettle();

    expect(api.uploadBridgeSecretCalls, 0);
  });

  testWidgets('After successful upload the version list refreshes', (tester) async {
    _setDesktopViewport(tester);
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();
    await _navigateToBridgeSecretTab(tester);

    final initialLoads = api.getBridgeSecretVersionsCalls;

    await _openInputDialog(tester);
    await tester.enterText(
      find.widgetWithText(TextField, '64 位十六进制（32 字节）'),
      'a' * 64,
    );
    await tester.pump();
    await tester.tap(find.widgetWithText(FilledButton, '提交并激活'));
    await tester.pumpAndSettle();

    // 上传成功后 controller.uploadBridgeSecret 内部会再 loadBridgeSecretVersions
    expect(api.getBridgeSecretVersionsCalls, greaterThan(initialLoads));
    expect(api.bridgeSecretVersions, isNotEmpty);
    // 列表中出现"版本 v0"卡片
    expect(find.textContaining('版本 v'), findsWidgets);
  });
}
