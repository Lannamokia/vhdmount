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

void main() {
  test('prepareInitialization returns OTP configuration', () async {
    final api = FakeAdminApi(
      serverStatus: _uninitializedServerStatus,
      authStatus: _unauthenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.prepareInitialization(
      issuer: 'VHDMountServer',
      accountName: 'admin',
    );

    expect(controller.initializationPreparation, isNotNull);
    expect(controller.initializationPreparation!.issuer, 'VHDMountServer');
    expect(controller.initializationPreparation!.accountName, 'admin');
    expect(controller.initializationPreparation!.totpSecret, isNotEmpty);
    expect(controller.initializationPreparation!.otpauthUrl, isNotEmpty);
  });

  test('completeInitialization finishes setup', () async {
    final api = FakeAdminApi(
      serverStatus: _uninitializedServerStatus,
      authStatus: _unauthenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.completeInitialization(
      adminPassword: 'ComplexPassword123!',
      sessionSecret: '0123456789abcdef0123456789abcdef',
      totpCode: '123456',
      defaultVhdKeyword: 'SAFEBOOT',
      dbHost: 'localhost',
      dbPort: 5432,
      dbName: 'vhd_select',
      dbUser: 'test',
      dbPassword: 'test',
      trustedCertificates: const <Map<String, String>>[],
    );

    expect(controller.serverStatus, isNotNull);
  });

  test('prepareInitialization uses default values when not provided', () async {
    final api = FakeAdminApi(
      serverStatus: _uninitializedServerStatus,
      authStatus: _unauthenticatedStatus,
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.prepareInitialization(
      issuer: '',
      accountName: '',
    );

    expect(controller.initializationPreparation, isNotNull);
    expect(controller.initializationPreparation!.issuer, 'VHDMountServer');
    expect(controller.initializationPreparation!.accountName, 'admin');
  });
}
