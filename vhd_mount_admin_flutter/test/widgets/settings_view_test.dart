import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

Finder _settingsScrollable() {
  return find.descendant(
    of: find.byType(SettingsView),
    matching: find.byType(Scrollable),
  ).first;
}

Finder _textFieldWithLabel(String labelText) {
  return find.byWidgetPredicate(
    (widget) => widget is TextField && widget.decoration?.labelText == labelText,
    description: 'TextField(labelText: $labelText)',
  );
}

void main() {
  testWidgets('settings view updates default VHD keyword', (tester) async {
    tester.view.physicalSize = const Size(1600, 960);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    final api = FakeAdminApi(
      serverStatus: const ServerStatus(
        initialized: true,
        pendingInitialization: false,
        databaseReady: true,
        defaultVhdKeyword: 'SAFEBOOT',
        trustedRegistrationCertificateCount: 1,
      ),
      authStatus: const AuthStatus(
        initialized: true,
        isAuthenticated: true,
        otpVerified: true,
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(DashboardSidebarButton).at(3));
    await tester.pumpAndSettle();

    await tester.enterText(_textFieldWithLabel('默认启动关键词'), 'recovery');
    await tester.tap(find.text('保存默认启动关键词'));
    await tester.pumpAndSettle();

    expect(api.updateDefaultVhdCalls, 1);
    expect(api.lastUpdatedDefaultVhd, 'RECOVERY');
  });

  testWidgets('settings view starts OTP rotation with current code', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 960);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    final api = FakeAdminApi(
      serverStatus: const ServerStatus(
        initialized: true,
        pendingInitialization: false,
        databaseReady: true,
        defaultVhdKeyword: 'SAFEBOOT',
        trustedRegistrationCertificateCount: 1,
      ),
      authStatus: const AuthStatus(
        initialized: true,
        isAuthenticated: true,
        otpVerified: true,
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(DashboardSidebarButton).at(3));
    await tester.pumpAndSettle();

    await tester.scrollUntilVisible(
      _textFieldWithLabel('旧 OTP 验证码'),
      300,
      scrollable: _settingsScrollable(),
    );
    await tester.enterText(_textFieldWithLabel('旧 OTP 验证码'), '123456');
    await tester.scrollUntilVisible(
      find.text('生成新的绑定密钥'),
      300,
      scrollable: _settingsScrollable(),
    );
    await tester.tap(find.text('生成新的绑定密钥'));
    await tester.pumpAndSettle();

    expect(api.prepareOtpRotationCalls, 1);
    expect(api.lastOtpRotationCurrentCode, '123456');
    expect(find.text('新的 OTP 绑定信息'), findsOneWidget);
  });

  testWidgets('settings view submits password change', (tester) async {
    tester.view.physicalSize = const Size(1600, 960);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    final api = FakeAdminApi(
      serverStatus: const ServerStatus(
        initialized: true,
        pendingInitialization: false,
        databaseReady: true,
        defaultVhdKeyword: 'SAFEBOOT',
        trustedRegistrationCertificateCount: 1,
      ),
      authStatus: const AuthStatus(
        initialized: true,
        isAuthenticated: true,
        otpVerified: true,
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(DashboardSidebarButton).at(3));
    await tester.pumpAndSettle();

    await tester.scrollUntilVisible(
      _textFieldWithLabel('当前密码'),
      300,
      scrollable: _settingsScrollable(),
    );
    await tester.enterText(_textFieldWithLabel('当前密码'), 'old-password');
    await tester.enterText(_textFieldWithLabel('新密码'), 'new-password-123');
    await tester.enterText(
      _textFieldWithLabel('确认新密码'),
      'new-password-123',
    );
    await tester.scrollUntilVisible(
      find.text('更新密码'),
      300,
      scrollable: _settingsScrollable(),
    );
    await tester.tap(find.text('更新密码'));
    await tester.pumpAndSettle();

    expect(api.changePasswordCalls, 1);
    expect(api.lastCurrentPassword, 'old-password');
    expect(api.lastNewPassword, 'new-password-123');
  });
}