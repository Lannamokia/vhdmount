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

const AuthStatus _unauthenticatedStatus = AuthStatus(
  initialized: true,
  isAuthenticated: false,
  otpVerified: false,
);

void main() {
  test(
    'otp status uses otpVerifiedUntil when verify response omits otpVerified',
    () {
      final status = OtpStatus.fromJson(<String, dynamic>{
        'otpVerifiedUntil': DateTime.now().millisecondsSinceEpoch + 60000,
      });

      expect(status.verified, isTrue);
    },
  );

  test('controller verifyOtp refreshes unlocked certificate data', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: const AuthStatus(
        initialized: true,
        isAuthenticated: true,
        otpVerified: false,
      ),
      certificates: const <TrustedCertificateRecord>[
        TrustedCertificateRecord(
          name: 'cert-01',
          fingerprint256: 'ABC123',
          subject: 'CN=Test',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem:
              '-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await controller.bootstrap();
    expect(controller.otpVerified, isFalse);
    expect(controller.certificates, isEmpty);

    await controller.verifyOtp('123456');

    expect(controller.otpVerified, isTrue);
    expect(controller.certificates, hasLength(1));
    expect(api.getTrustedCertificatesCalls, 1);
  });

  testWidgets('controller clears otp state when otp window expires', (
    tester,
  ) async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: const AuthStatus(
        initialized: true,
        isAuthenticated: true,
        otpVerified: false,
      ),
      certificates: const <TrustedCertificateRecord>[
        TrustedCertificateRecord(
          name: 'cert-01',
          fingerprint256: 'ABC123',
          subject: 'CN=Test',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem:
              '-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----',
        ),
      ],
      verifyOtpResponse: OtpStatus(
        verified: true,
        verifiedUntil: DateTime.now().millisecondsSinceEpoch + 50,
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await controller.bootstrap();
    await controller.verifyOtp('123456');

    expect(controller.otpVerified, isTrue);
    expect(controller.certificates, hasLength(1));

    await tester.pump(const Duration(milliseconds: 80));

    expect(controller.otpVerified, isFalse);
    expect(controller.otpVerifiedUntil, 0);
    expect(controller.certificates, isEmpty);
  });

  test('controller remembers successful server addresses in local config', () async {
    final configStore = FakeClientConfigStore();
    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: _unauthenticatedStatus,
      ),
      clientConfigStore: configStore,
    );

    controller.updateBaseUrl('http://server-a:8080/');
    await controller.login('ComplexPassword123!');

    expect(configStore.saveCalls, 1);
    expect(configStore.config.lastBaseUrl, 'http://server-a:8080');
    expect(configStore.config.serverHistory, <String>['http://server-a:8080']);
    expect(controller.rememberedBaseUrls, <String>['http://server-a:8080']);
  });

  test('bootstrap restores saved server address before probing server status', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _unauthenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(
        const ClientConfig(
          lastBaseUrl: 'http://saved-server:8080',
          serverHistory: <String>['http://saved-server:8080'],
        ),
      ),
    );

    await controller.bootstrap();

    expect(api.baseUrl, 'http://saved-server:8080');
    expect(controller.baseUrl, 'http://saved-server:8080');
    expect(controller.rememberedBaseUrls, <String>['http://saved-server:8080']);
  });

  test('bootstrap clears stale state when server probing fails', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'STALE-01',
          protectedState: false,
          vhdKeyword: 'SAFEBOOT',
          evhdPasswordConfigured: true,
          approved: true,
          revoked: false,
          keyId: 'key-01',
          keyType: 'RSA',
          registrationCertFingerprint: 'ABC123',
          logRetentionActiveDaysOverride: null,
          lastSeen: '2026-04-03T08:00:00Z',
        ),
      ],
      auditEntries: const <AuditEntry>[
        AuditEntry(
          timestamp: '2026-04-03T08:00:00Z',
          type: 'auth.login',
          actor: 'admin',
          result: 'success',
          path: '/api/auth/login',
          ip: '127.0.0.1',
          metadata: <String, dynamic>{'machineId': 'STALE-01'},
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await controller.bootstrap();
    expect(controller.serverStatus, isNotNull);
    expect(controller.machines, isNotEmpty);

    api.getServerStatusError = Exception('server offline');
    await controller.bootstrap();

    expect(controller.serverStatus, isNull);
    expect(controller.isAuthenticated, isFalse);
    expect(controller.machines, isEmpty);
    expect(controller.certificates, isEmpty);
    expect(controller.auditEntries, isEmpty);
    expect(controller.errorMessage, contains('操作失败'));
  });

  test('bootstrap keeps probing server when local config loading fails', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _unauthenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(
        const ClientConfig(),
        const ClientConfigStoreException('本地配置文件 JSON 已损坏，请修复或删除后重试。'),
      ),
    );

    await controller.bootstrap();

    expect(api.getServerStatusCalls, 1);
    expect(controller.serverStatus, isNotNull);
    expect(controller.errorMessage, contains('本地配置文件 JSON 已损坏'));
  });

  test('bootstrap clears stale deployment state after logout/server change', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SAFEBOOT',
          evhdPasswordConfigured: true,
          approved: true,
          revoked: false,
          keyId: 'key-01',
          keyType: 'RSA',
          registrationCertFingerprint: 'ABC123',
          logRetentionActiveDaysOverride: null,
          lastSeen: '2026-04-03T08:00:00Z',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await controller.bootstrap();
    controller.deploymentPackages = <DeploymentPackage>[
      const DeploymentPackage(
        packageId: 'pkg-01',
        name: 'pkg',
        version: '1.0.0',
        type: 'software-deploy',
        signer: 'admin',
        fileName: 'pkg-01.zip',
        fileSize: 1,
        createdAt: '2026-04-03T08:00:00Z',
      ),
    ];
    controller.deploymentTasks = <DeploymentTask>[
      const DeploymentTask(
        taskId: 'task-01',
        packageId: 'pkg-01',
        machineId: 'M-01',
        taskType: 'deploy',
        status: 'pending',
        errorMessage: null,
        createdAt: '2026-04-03T08:00:00Z',
        scheduledAt: null,
        completedAt: null,
        packageName: 'pkg',
        packageVersion: '1.0.0',
      ),
    ];
    controller.deploymentRecords = <DeploymentRecord>[
      const DeploymentRecord(
        recordId: 'rec-01',
        packageId: 'pkg-01',
        machineId: 'M-01',
        name: 'pkg',
        version: '1.0.0',
        type: 'software-deploy',
        targetPath: 'C:\\SOFT\\pkg-01',
        status: 'success',
        deployedAt: '2026-04-03T08:00:00Z',
        uninstalledAt: null,
        syncedAt: '2026-04-03T08:00:00Z',
      ),
    ];
    controller.deploymentSelectedMachineId = 'M-01';
    controller.deploymentTaskStatusFilter = 'pending';
    controller.deploymentSelectedTab = 'history';

    api.authStatus = _unauthenticatedStatus;
    await controller.bootstrap();

    expect(controller.deploymentPackages, isEmpty);
    expect(controller.deploymentTasks, isEmpty);
    expect(controller.deploymentRecords, isEmpty);
    expect(controller.deploymentSelectedMachineId, isNull);
    expect(controller.deploymentTaskStatusFilter, isNull);
    expect(controller.deploymentSelectedTab, 'packages');
  });

  test('loadMachineDeploymentHistory ignores stale slower responses', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machineDeploymentHistory: <String, List<DeploymentRecord>>{
        'M-01': const <DeploymentRecord>[
          DeploymentRecord(
            recordId: 'rec-01',
            packageId: 'pkg-01',
            machineId: 'M-01',
            name: 'pkg-old',
            version: '1.0.0',
            type: 'software-deploy',
            targetPath: 'C:\\SOFT\\pkg-old',
            status: 'success',
            deployedAt: '2026-04-03T08:00:00Z',
            uninstalledAt: null,
            syncedAt: '2026-04-03T08:00:00Z',
          ),
        ],
        'M-02': const <DeploymentRecord>[
          DeploymentRecord(
            recordId: 'rec-02',
            packageId: 'pkg-02',
            machineId: 'M-02',
            name: 'pkg-new',
            version: '2.0.0',
            type: 'software-deploy',
            targetPath: 'C:\\SOFT\\pkg-new',
            status: 'success',
            deployedAt: '2026-04-03T09:00:00Z',
            uninstalledAt: null,
            syncedAt: '2026-04-03T09:00:00Z',
          ),
        ],
      },
    );
    api.deploymentHistoryDelays['M-01'] = const Duration(milliseconds: 50);
    api.deploymentHistoryDelays['M-02'] = const Duration(milliseconds: 1);
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await controller.bootstrap();
    final first = controller.loadMachineDeploymentHistory('M-01');
    final second = controller.loadMachineDeploymentHistory('M-02');

    await Future.wait<void>(<Future<void>>[first, second]);

    expect(controller.deploymentSelectedMachineId, 'M-02');
    expect(controller.deploymentRecords, hasLength(1));
    expect(controller.deploymentRecords.single.machineId, 'M-02');
    expect(controller.deploymentRecords.single.name, 'pkg-new');
  });
}
