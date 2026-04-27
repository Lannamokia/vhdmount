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

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

void main() {
  testWidgets(
    'switching deployment tab then resizing viewport preserves selected tab',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

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

      // Navigate to Deployments page.
      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      // Switch to "机台历史" tab.
      await tester.tap(find.text('机台历史'));
      await tester.pumpAndSettle();

      // Verify we are on the history tab.
      expect(find.widgetWithText(InfoPanel, '请选择机台'), findsOneWidget);
      expect(controller.deploymentSelectedTab, 'history');

      // Simulate window resize (both width and height changes).
      _setDesktopViewport(tester, const Size(1200, 800));
      await tester.pump();
      await tester.pumpAndSettle();

      // Tab should still be "history", not reset to "packages".
      expect(find.widgetWithText(InfoPanel, '请选择机台'), findsOneWidget);
      expect(controller.deploymentSelectedTab, 'history');

      // Resize again to a different aspect ratio (keep enough height).
      _setDesktopViewport(tester, const Size(1400, 900));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.widgetWithText(InfoPanel, '请选择机台'), findsOneWidget);
      expect(controller.deploymentSelectedTab, 'history');
    },
  );

  testWidgets(
    'switching deployment tab then crossing mobile breakpoint preserves selected tab',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

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

      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      // Switch to "部署任务" tab.
      await tester.tap(find.text('部署任务'));
      await tester.pumpAndSettle();

      expect(controller.deploymentSelectedTab, 'tasks');

      // Cross the mobile breakpoint (720px width).
      _setDesktopViewport(tester, const Size(600, 960));
      await tester.pump();
      await tester.pumpAndSettle();

      // Should still be on tasks tab, not reset.
      expect(controller.deploymentSelectedTab, 'tasks');

      // Cross back to desktop.
      _setDesktopViewport(tester, const Size(1600, 960));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(controller.deploymentSelectedTab, 'tasks');
    },
  );

  testWidgets(
    'history tab empty state info panel height is bounded by content',
    (tester) async {
      _setDesktopViewport(tester, const Size(1600, 960));
      addTearDown(() => _resetViewport(tester));

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

      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('机台历史'));
      await tester.pumpAndSettle();

      // Find the InfoPanel inside the history tab.
      final infoPanelFinder = find.widgetWithText(InfoPanel, '请选择机台');
      expect(infoPanelFinder, findsOneWidget);

      final infoPanelSize = tester.getSize(infoPanelFinder);
      // The InfoPanel should not be stretched to fill the entire window height.
      // A reasonable content height for this panel is well under 300px.
      expect(infoPanelSize.height, lessThan(300));

      // Resize to a much taller window - the panel height should stay bounded.
      _setDesktopViewport(tester, const Size(1600, 1400));
      await tester.pump();
      await tester.pumpAndSettle();

      final infoPanelSizeTall = tester.getSize(infoPanelFinder);
      expect(infoPanelSizeTall.height, lessThan(300));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'packages tab empty state info panel height is bounded by content',
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

      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      // On packages tab, no packages uploaded yet.
      final infoPanelFinder = find.widgetWithText(InfoPanel, '当前没有部署包');
      expect(infoPanelFinder, findsOneWidget);

      final infoPanelSize = tester.getSize(infoPanelFinder);
      expect(infoPanelSize.height, lessThan(300));

      // Resize to taller window.
      _setDesktopViewport(tester, const Size(1600, 1400));
      await tester.pump();
      await tester.pumpAndSettle();

      final infoPanelSizeTall = tester.getSize(infoPanelFinder);
      expect(infoPanelSizeTall.height, lessThan(300));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'tasks tab empty state info panel height is bounded by content',
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

      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('部署任务'));
      await tester.pumpAndSettle();

      final infoPanelFinder = find.widgetWithText(InfoPanel, '当前没有部署任务');
      expect(infoPanelFinder, findsOneWidget);

      final infoPanelSize = tester.getSize(infoPanelFinder);
      expect(infoPanelSize.height, lessThan(300));

      // Resize to taller window.
      _setDesktopViewport(tester, const Size(1600, 1400));
      await tester.pump();
      await tester.pumpAndSettle();

      final infoPanelSizeTall = tester.getSize(infoPanelFinder);
      expect(infoPanelSizeTall.height, lessThan(300));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'segmented button shows labels on wide viewport',
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

      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      expect(find.text('部署包'), findsOneWidget);
      expect(find.text('部署任务'), findsOneWidget);
      expect(find.text('机台历史'), findsOneWidget);
    },
  );

  testWidgets(
    'segmented button switches to icon-only on narrow viewport',
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

      await tester.tap(find.text('部署管理'));
      await tester.pumpAndSettle();

      // On desktop, labels are visible.
      expect(find.text('部署包'), findsOneWidget);

      // Switch to narrow viewport.
      tester.view.physicalSize = const Size(400, 800);
      tester.view.devicePixelRatio = 2.0;
      await tester.pump();
      await tester.pumpAndSettle();

      // On narrow viewport, labels should be hidden (icon-only mode).
      expect(find.text('部署包'), findsNothing);
      expect(find.text('部署任务'), findsNothing);
      expect(find.text('机台历史'), findsNothing);

      expect(tester.takeException(), isNull);
    },
  );
}
