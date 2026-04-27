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

void _setDesktopViewport(WidgetTester tester, Size size) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
}

void _setMobileViewport(WidgetTester tester) {
  // iPhone 14 Pro-ish: 393x852 physical at 3.0 DPR = 131x284 logical.
  tester.view.physicalSize = const Size(393, 852);
  tester.view.devicePixelRatio = 3.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

void main() {
  testWidgets(
    'filter panel renders on mobile viewport',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _authenticatedStatus,
          machines: <MachineRecord>[_machine('M-01')],
          machineLogSessions: const <MachineLogSession>[
            MachineLogSession(
              machineId: 'M-01',
              sessionId: 'sess-01',
              appVersion: '2.0.0',
              osVersion: 'Windows 11',
              startedAt: '2026-04-20T08:00:00Z',
              lastUploadAt: '2026-04-20T08:30:00Z',
              lastEventAt: '2026-04-20T08:30:00Z',
              totalCount: 150,
              warnCount: 5,
              errorCount: 1,
              lastLevel: 'info',
              lastComponent: 'VHDManager',
            ),
          ],
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      // Navigate to Machine Logs.
      await tester.tap(find.text('机台日志'));
      await tester.pumpAndSettle();

      // Switch to mobile viewport.
      _setMobileViewport(tester);
      await tester.pump();
      await tester.pumpAndSettle();

      // Filter panel DropdownMenus should still be present.
      expect(find.byType(DropdownMenu<String>), findsWidgets);

      // Filter buttons should be present (icon-only on narrow width).
      expect(find.byType(IconButton), findsWidgets);
    },
  );

  testWidgets(
    'filter panel dropdown menus fit within narrow viewport',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _authenticatedStatus,
          machines: <MachineRecord>[_machine('M-01')],
          machineLogSessions: const <MachineLogSession>[
            MachineLogSession(
              machineId: 'M-01',
              sessionId: 'sess-01',
              appVersion: '2.0.0',
              osVersion: 'Windows 11',
              startedAt: '2026-04-20T08:00:00Z',
              lastUploadAt: '2026-04-20T08:30:00Z',
              lastEventAt: '2026-04-20T08:30:00Z',
              totalCount: 150,
              warnCount: 5,
              errorCount: 1,
              lastLevel: 'info',
              lastComponent: 'VHDManager',
            ),
          ],
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      await tester.pumpAndSettle();

      await tester.tap(find.text('机台日志'));
      await tester.pumpAndSettle();

      // Use a very narrow viewport (360px is a common minimum phone width).
      tester.view.physicalSize = const Size(360, 800);
      tester.view.devicePixelRatio = 2.0;
      await tester.pump();
      await tester.pumpAndSettle();

      // All DropdownMenu instances should be within viewport bounds.
      final dropdownFinders = find.byType(DropdownMenu<String>);
      expect(dropdownFinders, findsWidgets);

      final viewportWidth = tester.view.physicalSize.width;
      for (var i = 0; i < tester.widgetList(dropdownFinders).length; i++) {
        final finder = dropdownFinders.at(i);
        final size = tester.getSize(finder);
        expect(
          size.width,
          lessThanOrEqualTo(viewportWidth),
          reason: 'DropdownMenu $i exceeds viewport width',
        );
      }

      // Buttons should switch to icon-only on narrow width.
      expect(find.byType(IconButton), findsWidgets);
    },
  );
}
