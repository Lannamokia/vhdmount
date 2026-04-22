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

void main() {
  test('loadMachineLogSessions populates session list', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
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
        MachineLogSession(
          machineId: 'M-01',
          sessionId: 'sess-02',
          appVersion: '2.0.0',
          osVersion: 'Windows 11',
          startedAt: '2026-04-20T09:00:00Z',
          lastUploadAt: '2026-04-20T09:30:00Z',
          lastEventAt: '2026-04-20T09:30:00Z',
          totalCount: 200,
          warnCount: 0,
          errorCount: 0,
          lastLevel: 'info',
          lastComponent: 'Program',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogSessions(machineId: 'M-01');

    expect(controller.machineLogSessions, hasLength(2));
    expect(controller.machineLogSessions.first.sessionId, 'sess-01');
    expect(api.getMachineLogSessionsCalls, 1);
    expect(api.lastMachineLogMachineId, 'M-01');
  });

  test('loadMachineLogSessions filters by date range', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogSessions: const <MachineLogSession>[],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogSessions(
      machineId: 'M-01',
      from: '2026-04-20T00:00:00Z',
      to: '2026-04-21T00:00:00Z',
    );

    expect(api.getMachineLogSessionsCalls, 1);
  });

  test('loadMachineLogs populates log entries', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogEntries: const <MachineLogEntry>[
        MachineLogEntry(
          id: 1,
          machineId: 'M-01',
          sessionId: 'sess-01',
          seq: 1,
          occurredAt: '2026-04-20T08:00:00Z',
          level: 'info',
          component: 'VHDManager',
          eventKey: 'MOUNT_START',
          message: 'Starting mount',
          rawText: 'VHDManager: Starting mount',
          metadata: <String, dynamic>{},
        ),
        MachineLogEntry(
          id: 2,
          machineId: 'M-01',
          sessionId: 'sess-01',
          seq: 2,
          occurredAt: '2026-04-20T08:01:00Z',
          level: 'warn',
          component: 'VHDManager',
          eventKey: 'MOUNT_RETRY',
          message: 'Retrying mount',
          rawText: 'VHDManager: Retrying mount',
          metadata: <String, dynamic>{},
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogs(
      const MachineLogFilter(
        machineId: 'M-01',
        limit: 50,
      ),
    );

    expect(controller.machineLogEntries, hasLength(2));
    expect(controller.machineLogEntries.first.level, 'info');
    expect(controller.machineLogPage.hasMore, isFalse);
    expect(api.getMachineLogsCalls, 1);
  });

  test('loadMachineLogs applies filter parameters', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogEntries: const <MachineLogEntry>[],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogs(
      const MachineLogFilter(
        machineId: 'M-01',
        sessionId: 'sess-01',
        level: 'error',
        component: 'VHDManager',
        eventKey: 'MOUNT_FAIL',
        query: 'error',
        limit: 25,
      ),
    );

    expect(api.getMachineLogsCalls, 1);
  });

  test('loadMoreMachineLogs loads next page', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogEntries: List<MachineLogEntry>.generate(
        60,
        (index) => MachineLogEntry(
          id: index + 1,
          machineId: 'M-01',
          sessionId: 'sess-01',
          seq: index + 1,
          occurredAt: '2026-04-20T08:${(index % 60).toString().padLeft(2, '0')}:00Z',
          level: 'info',
          component: 'VHDManager',
          eventKey: 'STATUS',
          message: 'Message $index',
          rawText: 'VHDManager: Message $index',
          metadata: const <String, dynamic>{},
        ),
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogs(
      const MachineLogFilter(
        machineId: 'M-01',
        limit: 50,
      ),
    );

    expect(controller.machineLogEntries, hasLength(50));
    expect(controller.machineLogPage.hasMore, isTrue);
    expect(controller.machineLogPage.nextCursor, isNotNull);

    await controller.loadMoreMachineLogs();

    expect(controller.machineLogEntries.length, greaterThan(50));
  });

  test('exportMachineLogs returns formatted text', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogEntries: const <MachineLogEntry>[
        MachineLogEntry(
          id: 1,
          machineId: 'M-01',
          sessionId: 'sess-01',
          seq: 1,
          occurredAt: '2026-04-20T08:00:00Z',
          level: 'info',
          component: 'VHDManager',
          eventKey: 'MOUNT_START',
          message: 'Starting mount',
          rawText: 'VHDManager: Starting mount',
          metadata: <String, dynamic>{},
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    final result = await controller.exportMachineLogs(format: 'text');

    expect(result, contains('M-01'));
    expect(result, contains('VHDManager'));
    expect(api.exportMachineLogsCalls, 1);
  });

  test('loadMachineLogSessions clears entries on error', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogSessions: const <MachineLogSession>[],
      getMachineLogSessionsError: Exception('network error'),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogSessions(machineId: 'M-01');

    expect(controller.machineLogSessions, isEmpty);
  });

  test('loadMachineLogs clears entries on error', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineLogEntries: const <MachineLogEntry>[],
      getMachineLogsError: Exception('network error'),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadMachineLogs(
      const MachineLogFilter(machineId: 'M-01', limit: 50),
    );

    expect(controller.machineLogEntries, isEmpty);
    expect(controller.machineLogPage.nextCursor, isNull);
    expect(controller.machineLogPage.hasMore, isFalse);
  });
}
