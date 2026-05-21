import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

Finder _settingsScrollable() {
  return find.descendant(
    of: find.byType(SettingsView),
    matching: find.byWidgetPredicate(
      (widget) =>
          widget is Scrollable && widget.axisDirection == AxisDirection.down,
      description: 'vertical SettingsView Scrollable',
    ),
  ).first;
}

void main() {
  group('TotpKeysManagementSection', () {
    testWidgets('renders key list with name and type', (tester) async {
      tester.view.physicalSize = const Size(1600, 1200);
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
      api.totpKeys = <TotpKeyRecord>[
        const TotpKeyRecord(
          id: 'key_001',
          name: '初始认证器',
          type: 'authenticator',
          platform: null,
          createdAt: '2024-06-15T12:00:00.000Z',
          lastUsedAt: '2024-06-16T08:30:00.000Z',
        ),
        const TotpKeyRecord(
          id: 'key_002',
          name: 'Windows Hello (DESKTOP-ABC)',
          type: 'biometric',
          platform: 'windows-hello',
          createdAt: '2024-06-16T10:00:00.000Z',
          lastUsedAt: null,
        ),
      ];

      final controller = AppController(
        api: api,
        clientConfigStore: FakeClientConfigStore(),
      );
      // Pre-load keys into controller to avoid notifyListeners during build
      controller.totpKeys = api.totpKeys;

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      // Navigate to settings page (index 4)
      await tester.tap(find.byType(DashboardSidebarButton).at(4));
      await tester.pumpAndSettle();

      // Scroll to TOTP section
      await tester.scrollUntilVisible(
        find.text('TOTP 密钥管理'),
        300,
        scrollable: _settingsScrollable(),
      );
      await tester.pumpAndSettle();

      // Verify section title is visible
      expect(find.text('TOTP 密钥管理'), findsOneWidget);

      // Verify key names are rendered
      expect(find.text('初始认证器'), findsOneWidget);
      expect(find.text('Windows Hello (DESKTOP-ABC)'), findsOneWidget);

      // Verify type chips are rendered
      expect(find.text('认证器'), findsOneWidget);
      expect(find.text('生物识别'), findsOneWidget);

      // Verify platform chip for biometric key
      expect(find.text('Windows Hello'), findsOneWidget);
    });

    testWidgets('shows revoke confirmation dialog when 注销 is tapped',
        (tester) async {
      tester.view.physicalSize = const Size(1600, 1200);
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
      api.totpKeys = <TotpKeyRecord>[
        const TotpKeyRecord(
          id: 'key_001',
          name: '测试认证器',
          type: 'authenticator',
          platform: null,
          createdAt: '2024-06-15T12:00:00.000Z',
          lastUsedAt: null,
        ),
      ];

      final controller = AppController(
        api: api,
        clientConfigStore: FakeClientConfigStore(),
      );
      controller.totpKeys = api.totpKeys;

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      await tester.tap(find.byType(DashboardSidebarButton).at(4));
      await tester.pumpAndSettle();

      // Scroll to the 注销 button
      await tester.scrollUntilVisible(
        find.text('注销'),
        300,
        scrollable: _settingsScrollable(),
      );
      await tester.pumpAndSettle();

      // Tap the 注销 button
      await tester.tap(find.text('注销'));
      await tester.pumpAndSettle();

      // Verify confirmation dialog appears
      expect(find.text('注销密钥'), findsOneWidget);
      expect(
        find.textContaining('确定要注销密钥"测试认证器"吗'),
        findsOneWidget,
      );
      expect(find.text('取消'), findsOneWidget);
    });

    testWidgets('shows name input dialog when 添加认证器 is tapped',
        (tester) async {
      tester.view.physicalSize = const Size(1600, 1200);
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

      // Scroll to 添加认证器 button
      await tester.scrollUntilVisible(
        find.text('添加认证器'),
        300,
        scrollable: _settingsScrollable(),
      );
      await tester.pumpAndSettle();

      // Tap 添加认证器 button
      await tester.tap(find.text('添加认证器'));
      await tester.pumpAndSettle();

      // Verify name input dialog appears
      expect(find.text('密钥名称'), findsOneWidget);
      expect(find.text('创建'), findsOneWidget);
      expect(find.text('取消'), findsOneWidget);
    });
  });
}
