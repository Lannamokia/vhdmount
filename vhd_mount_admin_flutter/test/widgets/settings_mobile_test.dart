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
    'log retention fields stack vertically on narrow viewport',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _authenticatedStatus,
          logRetentionSettings: const LogRetentionSettings(
            defaultRetentionActiveDays: 7,
            dailyInspectionHour: 3,
            dailyInspectionMinute: 0,
            timezone: 'UTC',
            lastInspectionAt: null,
          ),
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      // Navigate to Settings.
      await tester.tap(find.text('设置'));
      await tester.pumpAndSettle();

      // Switch to narrow viewport (< 460px).
      _setNarrowViewport(tester);
      await tester.pump();
      await tester.pumpAndSettle();

      // On narrow viewport, the three field labels should be vertically
      // stacked (different y coordinates).
      final retentionRect = tester.getRect(find.text('默认保留活动日数'));
      final hourRect = tester.getRect(find.text('每日巡检小时'));
      final minuteRect = tester.getRect(find.text('每日巡检分钟'));

      expect(retentionRect.top, lessThan(hourRect.top));
      expect(hourRect.top, lessThan(minuteRect.top));
    },
  );

  testWidgets(
    'log retention fields use horizontal layout on wide viewport',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _authenticatedStatus,
          logRetentionSettings: const LogRetentionSettings(
            defaultRetentionActiveDays: 7,
            dailyInspectionHour: 3,
            dailyInspectionMinute: 0,
            timezone: 'UTC',
            lastInspectionAt: null,
          ),
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      await tester.tap(find.text('设置'));
      await tester.pumpAndSettle();

      // On wide viewport, the three field labels should be roughly on the
      // same horizontal line (similar y coordinates, different x).
      final retentionRect = tester.getRect(find.text('默认保留活动日数'));
      final hourRect = tester.getRect(find.text('每日巡检小时'));
      final minuteRect = tester.getRect(find.text('每日巡检分钟'));

      // Labels should be at roughly the same vertical position (within Row).
      expect(
        (retentionRect.top - hourRect.top).abs(),
        lessThan(20),
        reason: 'retention and hour labels should be on the same row',
      );
      expect(
        (hourRect.top - minuteRect.top).abs(),
        lessThan(20),
        reason: 'hour and minute labels should be on the same row',
      );

      // And horizontally separated.
      expect(retentionRect.left, lessThan(hourRect.left));
      expect(hourRect.left, lessThan(minuteRect.left));
    },
  );

  testWidgets(
    'OTP rotation fields stack vertically on narrow viewport',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _authenticatedStatus,
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      await tester.tap(find.text('设置'));
      await tester.pumpAndSettle();

      // Switch to narrow viewport.
      _setNarrowViewport(tester);
      await tester.pump();
      await tester.pumpAndSettle();

      // Scroll down to OTP rotation section.
      await tester.dragUntilVisible(
        find.text('新的 OTP Issuer（可选）'),
        find.byType(SingleChildScrollView).first,
        const Offset(0, -200),
      );
      await tester.pumpAndSettle();

      // On narrow viewport, OTP issuer and account fields should be
      // vertically stacked.
      final issuerRect = tester.getRect(find.text('新的 OTP Issuer（可选）'));
      final accountRect = tester.getRect(find.text('新的 OTP Account（可选）'));

      expect(issuerRect.top, lessThan(accountRect.top));
    },
  );
}
