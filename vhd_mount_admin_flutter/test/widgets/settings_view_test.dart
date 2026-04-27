import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

Finder _settingsScrollable() {
  return find.descendant(
    of: find.byType(SettingsView),
    matching: find.byWidgetPredicate(
      (widget) => widget is Scrollable && widget.axisDirection == AxisDirection.down,
      description: 'vertical SettingsView Scrollable',
    ),
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

    await tester.tap(find.byType(DashboardSidebarButton).at(4));
    await tester.pumpAndSettle();

    await tester.enterText(_textFieldWithLabel('默认启动关键词'), 'recovery');
    final saveDefaultVhdButton = find.ancestor(
      of: find.text('保存默认启动关键词'),
      matching: find.byType(FilledButton),
    );
    tester.widget<FilledButton>(saveDefaultVhdButton).onPressed!.call();
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

    await tester.tap(find.byType(DashboardSidebarButton).at(4));
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

  testWidgets(
    'OTP rotation panel on Windows shows manual bind hint, no quick bind button',
    (tester) async {
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

      await tester.tap(find.byType(DashboardSidebarButton).at(4));
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

      // OTP rotation panel should be visible.
      expect(find.text('新的 OTP 绑定信息'), findsOneWidget);

      // On Windows (non-mobile), manual bind hint should be shown.
      expect(
        find.text('如果验证器不支持扫码，可以使用下面的参数手动绑定。'),
        findsOneWidget,
      );

      // Mobile quick bind buttons should NOT appear on Windows.
      expect(find.text('绑定到 iCloud 密码'), findsNothing);
      expect(find.text('绑定到验证器'), findsNothing);
    },
  );

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

    await tester.tap(find.byType(DashboardSidebarButton).at(4));
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

  testWidgets('settings view updates machine log retention policy', (
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
      logRetentionSettings: const LogRetentionSettings(
        defaultRetentionActiveDays: 7,
        dailyInspectionHour: 3,
        dailyInspectionMinute: 0,
        timezone: 'UTC',
        lastInspectionAt: '2026-04-03T00:00:00Z',
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(DashboardSidebarButton).at(4));
    await tester.pumpAndSettle();

    await tester.enterText(_textFieldWithLabel('默认保留活动日数'), '21');
    await tester.enterText(_textFieldWithLabel('每日巡检小时'), '5');
    await tester.enterText(_textFieldWithLabel('每日巡检分钟'), '30');
    await tester.enterText(_textFieldWithLabel('服务端时区'), 'Asia/Shanghai');
    await tester.scrollUntilVisible(
      find.text('保存日志保留策略'),
      300,
      scrollable: _settingsScrollable(),
    );
    await tester.tap(find.text('保存日志保留策略'));
    await tester.pumpAndSettle();

    expect(api.updateLogRetentionSettingsCalls, 1);
    expect(api.logRetentionSettings.defaultRetentionActiveDays, 21);
    expect(api.logRetentionSettings.dailyInspectionHour, 5);
    expect(api.logRetentionSettings.dailyInspectionMinute, 30);
    expect(api.logRetentionSettings.timezone, 'Asia/Shanghai');
  });

  testWidgets('settings view rejects non-IANA timezone locally', (
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
      logRetentionSettings: const LogRetentionSettings(
        defaultRetentionActiveDays: 7,
        dailyInspectionHour: 3,
        dailyInspectionMinute: 0,
        timezone: 'UTC',
        lastInspectionAt: '2026-04-03T00:00:00Z',
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(DashboardSidebarButton).at(4));
    await tester.pumpAndSettle();

    await tester.enterText(_textFieldWithLabel('默认保留活动日数'), '21');
    await tester.enterText(_textFieldWithLabel('每日巡检小时'), '5');
    await tester.enterText(_textFieldWithLabel('每日巡检分钟'), '30');
    await tester.enterText(_textFieldWithLabel('服务端时区'), 'China Standard Time');
    await tester.scrollUntilVisible(
      find.text('保存日志保留策略'),
      300,
      scrollable: _settingsScrollable(),
    );
    await tester.tap(find.text('保存日志保留策略'));
    await tester.pumpAndSettle();

    expect(api.updateLogRetentionSettingsCalls, 0);
    expect(
      find.text('服务端时区必须是 IANA 时区，例如 UTC 或 Asia/Shanghai。'),
      findsOneWidget,
    );
  });
}