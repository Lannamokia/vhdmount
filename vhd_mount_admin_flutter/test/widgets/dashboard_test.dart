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
  testWidgets('opening audit from machine card preserves machine filter', (
    tester,
  ) async {
    _setDesktopViewport(tester, const Size(1600, 960));
    addTearDown(() => _resetViewport(tester));

    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: _authenticatedStatus,
        machines: <MachineRecord>[_machine('MACHINE-01'), _machine('MACHINE-02')],
        auditEntries: const <AuditEntry>[
          AuditEntry(
            timestamp: '2026-04-03T08:00:00Z',
            type: 'auth.login',
            actor: 'admin',
            result: 'success',
            path: '/api/auth/login',
            ip: '127.0.0.1',
            metadata: <String, dynamic>{'machineId': 'MACHINE-01'},
          ),
          AuditEntry(
            timestamp: '2026-04-03T09:00:00Z',
            type: 'machine.create',
            actor: 'admin',
            result: 'success',
            path: '/api/machines',
            ip: '127.0.0.1',
            metadata: <String, dynamic>{'machineId': 'MACHINE-02'},
          ),
        ],
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.text('查阅审计日志').first);
    await tester.pumpAndSettle();

    expect(find.text('审计日志'), findsOneWidget);
    expect(find.text('当前仅显示机台 MACHINE-01 的审计记录。'), findsOneWidget);
  });

  testWidgets('login shell stays stable on narrow windows', (tester) async {
    _setDesktopViewport(tester, const Size(280, 900));
    addTearDown(() => _resetViewport(tester));

    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: const AuthStatus(
          initialized: true,
          isAuthenticated: false,
          otpVerified: false,
        ),
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.text('管理员登录'), findsOneWidget);
    expect(find.text('连接状态'), findsOneWidget);
    expect(find.text('数据库已连接'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('login hero panel stays above the form on wide desktops', (
    tester,
  ) async {
    _setDesktopViewport(tester, const Size(1400, 920));
    addTearDown(() => _resetViewport(tester));

    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: const AuthStatus(
          initialized: true,
          isAuthenticated: false,
          otpVerified: false,
        ),
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    final heroTop = tester.getTopLeft(find.text('安全登录')).dy;
    final formTop = tester.getTopLeft(find.text('管理员登录')).dy;
    final heroPanel = find.ancestor(
      of: find.text('安全登录'),
      matching: find.byType(AppPanel),
    );
    final formPanel = find.ancestor(
      of: find.text('管理员登录'),
      matching: find.byType(AppPanel),
    );
    final heroWidth = tester.getSize(heroPanel.first).width;
    final formWidth = tester.getSize(formPanel.first).width;

    expect(formTop - heroTop, greaterThan(120));
    expect(heroWidth, closeTo(formWidth, 0.01));
    expect(tester.takeException(), isNull);
  });

  testWidgets('uses styled sidebar buttons on wide desktop windows', (
    tester,
  ) async {
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

    expect(find.byType(DashboardSidebarButton), findsNWidgets(4));
    expect(find.text('审批、保护、EVHD'), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('overview cards keep equal size while resizing', (tester) async {
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

    Finder cards = find.byType(OverviewStatCard);
    expect(cards, findsNWidgets(4));

    Size firstSize = tester.getSize(cards.at(0));
    for (int index = 1; index < 4; index++) {
      final size = tester.getSize(cards.at(index));
      expect(size.width, closeTo(firstSize.width, 0.01));
      expect(size.height, closeTo(firstSize.height, 0.01));
    }

    _setDesktopViewport(tester, const Size(1180, 960));
    await tester.pump();
    await tester.pumpAndSettle();

    cards = find.byType(OverviewStatCard);
    firstSize = tester.getSize(cards.at(0));
    for (int index = 1; index < 4; index++) {
      final size = tester.getSize(cards.at(index));
      expect(size.width, closeTo(firstSize.width, 0.01));
      expect(size.height, closeTo(firstSize.height, 0.01));
    }

    expect(tester.takeException(), isNull);
  });

  testWidgets('uses compact dashboard layout on short desktop windows', (
    tester,
  ) async {
    _setDesktopViewport(tester, const Size(1400, 540));
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

    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
    expect(find.text('MACHINE-01'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}