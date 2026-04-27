import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

const ServerStatus _uninitializedServerStatus = ServerStatus(
  initialized: false,
  pendingInitialization: false,
  databaseReady: false,
  defaultVhdKeyword: 'SDEZ',
  trustedRegistrationCertificateCount: 0,
);

const AuthStatus _unauthenticatedStatus = AuthStatus(
  initialized: true,
  isAuthenticated: false,
  otpVerified: false,
);

void _setDesktopViewport(WidgetTester tester, Size size) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _setNarrowViewport(WidgetTester tester) {
  tester.view.physicalSize = const Size(400, 800);
  tester.view.devicePixelRatio = 2.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

void main() {
  testWidgets(
    'OTP import panel QR container fits on narrow viewport',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _uninitializedServerStatus,
          authStatus: _unauthenticatedStatus,
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      // Trigger prepareInitialization to show OTP import panel.
      await controller.prepareInitialization(
        issuer: 'VHDMountServer',
        accountName: 'admin',
      );
      await tester.pump();
      await tester.pumpAndSettle();

      // OTP import panel should be visible.
      expect(find.text('OTP 导入信息'), findsOneWidget);

      // Switch to narrow viewport.
      _setNarrowViewport(tester);
      await tester.pump();
      await tester.pumpAndSettle();

      // Should not overflow.
      expect(tester.takeException(), isNull);

      // The QR container should not exceed viewport width.
      final qrFinder = find.byType(Container).hitTestable();
      final viewportWidth = tester.view.physicalSize.width;
      bool foundWideContainer = false;
      for (var i = 0; i < tester.widgetList(qrFinder).length; i++) {
        final size = tester.getSize(qrFinder.at(i));
        if (size.width > viewportWidth) {
          foundWideContainer = true;
          break;
        }
      }
      expect(foundWideContainer, isFalse);
    },
  );

  testWidgets(
    'OTP import panel on Windows shows scan QR text, no quick bind button',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _uninitializedServerStatus,
          authStatus: _unauthenticatedStatus,
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      // Trigger prepareInitialization to show OTP import panel.
      await controller.prepareInitialization(
        issuer: 'VHDMountServer',
        accountName: 'admin',
      );
      await tester.pump();
      await tester.pumpAndSettle();

      // OTP import panel should be visible.
      expect(find.text('OTP 导入信息'), findsOneWidget);

      // On Windows (non-mobile), the scan QR instruction text should be shown.
      expect(
        find.text('使用手机验证器扫描此二维码即可添加 TOTP。'),
        findsOneWidget,
      );

      // Mobile quick bind buttons should NOT appear on Windows.
      expect(find.text('绑定到 iCloud 密码'), findsNothing);
      expect(find.text('绑定到验证器'), findsNothing);

      // Manual binding hint should be shown for desktop.
      expect(
        find.text(
          '优先扫码导入；如果验证器不支持扫码，再使用下方密钥或 URI 手动添加。',
        ),
        findsOneWidget,
      );
    },
  );
}
