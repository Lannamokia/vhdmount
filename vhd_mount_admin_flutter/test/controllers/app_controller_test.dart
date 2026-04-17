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
}