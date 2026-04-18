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

MachineRecord _machine(
  String machineId, {
  int? logRetentionActiveDaysOverride,
}) {
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
    logRetentionActiveDaysOverride: logRetentionActiveDaysOverride,
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

  testWidgets('opening machine logs from machine card preserves machine filter', (
    tester,
  ) async {
    _setDesktopViewport(tester, const Size(1600, 960));
    addTearDown(() => _resetViewport(tester));

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01'), _machine('MACHINE-02')],
      machineLogSessions: const <MachineLogSession>[
        MachineLogSession(
          machineId: 'MACHINE-01',
          sessionId: 'SESSION-01',
          appVersion: '1.0.0',
          osVersion: 'Windows 11',
          startedAt: '2026-04-03T08:00:00Z',
          lastUploadAt: '2026-04-03T08:05:00Z',
          lastEventAt: '2026-04-03T08:04:00Z',
          totalCount: 2,
          warnCount: 0,
          errorCount: 0,
          lastLevel: 'info',
          lastComponent: 'VHDManager',
        ),
      ],
      machineLogEntries: const <MachineLogEntry>[
        MachineLogEntry(
          id: 1,
          machineId: 'MACHINE-01',
          sessionId: 'SESSION-01',
          seq: 1,
          occurredAt: '2026-04-03T08:04:00Z',
          logDay: '2026-04-03',
          receivedAt: '2026-04-03T08:05:00Z',
          level: 'info',
          component: 'VHDManager',
          eventKey: 'MOUNT_SUCCESS',
          message: '卷挂载完成',
          rawText: 'volume mounted',
          metadata: <String, dynamic>{'driveLetter': 'Z'},
          uploadRequestId: 'upload-01',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.text('查看机台日志').first);
    await tester.pumpAndSettle();

    expect(find.text('机台日志'), findsAtLeastNWidgets(1));
    expect(find.text('SESSION-01'), findsAtLeastNWidgets(1));
    expect(find.text('卷挂载完成'), findsOneWidget);
    expect(api.lastMachineLogMachineId, 'MACHINE-01');
    expect(controller.machineLogSelectedMachineId, 'MACHINE-01');
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

    expect(find.byType(DashboardSidebarButton), findsNWidgets(5));
    expect(find.text('审批、保护、EVHD'), findsOneWidget);
    expect(find.text('会话、分页、详情'), findsOneWidget);
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

  testWidgets('mobile login hero stays compact', (tester) async {
    _setDesktopViewport(tester, const Size(390, 844));
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

    final heroPanel = find.ancestor(
      of: find.text('安全登录'),
      matching: find.byType(AppPanel),
    );
    final heroHeight = tester.getSize(heroPanel.first).height;

    expect(heroHeight, lessThan(340));
    expect(tester.takeException(), isNull);
  });

  testWidgets('mobile dashboard overview cards use two-by-two grid without captions', (
    tester,
  ) async {
    _setDesktopViewport(tester, const Size(390, 844));
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

    final cards = find.byType(OverviewStatCard);
    expect(cards, findsNWidgets(4));

    final firstCardSize = tester.getSize(cards.first);
    expect(firstCardSize.height, lessThan(104));

    final card0 = tester.getTopLeft(cards.at(0));
    final card1 = tester.getTopLeft(cards.at(1));
    final card2 = tester.getTopLeft(cards.at(2));
    final card3 = tester.getTopLeft(cards.at(3));

    final scaffold = tester.widget<Scaffold>(find.byType(Scaffold).first);

    expect((card0.dy - card1.dy).abs(), lessThan(1));
    expect((card2.dy - card3.dy).abs(), lessThan(1));
    expect(card2.dy, greaterThan(card0.dy));
    expect(scaffold.extendBody, isTrue);
    expect(find.text('服务端数据库链路健康状态。'), findsNothing);
    expect(find.text('当前连接的管理服务地址。'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('mobile dashboard scrolls as a single page', (tester) async {
    _setDesktopViewport(tester, const Size(390, 844));
    addTearDown(() => _resetViewport(tester));

    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: _authenticatedStatus,
        machines: List<MachineRecord>.generate(
          12,
          (index) => _machine('MACHINE-${index.toString().padLeft(2, '0')}'),
        ),
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    final headerFinder = find.text('VHD Mount Admin');
    final initialHeaderDy = tester.getTopLeft(headerFinder).dy;

    await tester.drag(find.byType(SingleChildScrollView).first, const Offset(0, -320));
    await tester.pumpAndSettle();

    final movedHeaderDy = tester.getTopLeft(headerFinder).dy;
    expect(movedHeaderDy, lessThan(initialHeaderDy));
    expect(tester.takeException(), isNull);
  });

  testWidgets('cancelling add machine dialog after focusing password does not crash', (
    tester,
  ) async {
    _setDesktopViewport(tester, const Size(390, 844));
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

    await tester.ensureVisible(find.text('添加机台'));
    await tester.pumpAndSettle();

    await tester.tap(find.text('添加机台'));
    await tester.pumpAndSettle();

    final dialogFields = find.descendant(
      of: find.byType(AlertDialog),
      matching: find.byType(TextField),
    );

    await tester.tap(dialogFields.last);
    await tester.pumpAndSettle();

    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();

    expect(find.byType(AlertDialog), findsNothing);
    expect(tester.takeException(), isNull);
  });
}