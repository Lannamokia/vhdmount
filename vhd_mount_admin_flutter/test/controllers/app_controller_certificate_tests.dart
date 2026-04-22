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
  test('loadCertificates populates certificate list', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      certificates: const <TrustedCertificateRecord>[
        TrustedCertificateRecord(
          name: 'cert-01',
          fingerprint256: 'ABC123',
          subject: 'CN=Test',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem: '-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----',
        ),
        TrustedCertificateRecord(
          name: 'cert-02',
          fingerprint256: 'DEF456',
          subject: 'CN=Test2',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem: '-----BEGIN CERTIFICATE-----\nTEST2\n-----END CERTIFICATE-----',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadCertificates();

    expect(controller.certificates, hasLength(2));
    expect(controller.certificates.first.name, 'cert-01');
    expect(api.getTrustedCertificatesCalls, 1);
  });

  test('loadCertificates clears list on error', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      certificates: const <TrustedCertificateRecord>[
        TrustedCertificateRecord(
          name: 'cert-01',
          fingerprint256: 'ABC123',
          subject: 'CN=Test',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem: '-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----',
        ),
      ],
      getTrustedCertificatesError: Exception('network error'),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.loadCertificates();

    expect(controller.certificates, isEmpty);
  });

  test('addTrustedCertificate appends to list', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      certificates: const <TrustedCertificateRecord>[],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.addTrustedCertificate(
      'new-cert',
      '-----BEGIN CERTIFICATE-----\nNEW\n-----END CERTIFICATE-----',
    );

    expect(controller.certificates, hasLength(1));
    expect(controller.certificates.first.name, 'new-cert');
    expect(controller.certificates.first.fingerprint256, 'fingerprint-1');
  });

  test('removeTrustedCertificate removes by fingerprint', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      certificates: const <TrustedCertificateRecord>[
        TrustedCertificateRecord(
          name: 'cert-01',
          fingerprint256: 'ABC123',
          subject: 'CN=Test',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem: '-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----',
        ),
        TrustedCertificateRecord(
          name: 'cert-02',
          fingerprint256: 'DEF456',
          subject: 'CN=Test2',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem: '-----BEGIN CERTIFICATE-----\nTEST2\n-----END CERTIFICATE-----',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.removeTrustedCertificate('ABC123');

    expect(controller.certificates, hasLength(1));
    expect(controller.certificates.first.fingerprint256, 'DEF456');
  });

  test('removeTrustedCertificate clears list on error', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      certificates: const <TrustedCertificateRecord>[
        TrustedCertificateRecord(
          name: 'cert-01',
          fingerprint256: 'ABC123',
          subject: 'CN=Test',
          validFrom: '2026-04-01T00:00:00Z',
          validTo: '2027-04-01T00:00:00Z',
          certificatePem: '-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----',
        ),
      ],
      getTrustedCertificatesError: Exception('network error'),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.removeTrustedCertificate('ABC123');

    expect(controller.certificates, isEmpty);
  });
}
