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
  test('addMachine appends machine to list', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.addMachine(
      const MachineDraft(
        machineId: 'NEW-01',
        protectedState: false,
        vhdKeyword: 'SDEZ',
        evhdPassword: '',
      ),
    );

    expect(controller.machines, hasLength(1));
    expect(controller.machines.first.machineId, 'NEW-01');
    expect(controller.machines.first.vhdKeyword, 'SDEZ');
    expect(controller.machines.first.approved, isFalse);
  });

  test('setMachineApproval updates approval state', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.setMachineApproval('M-01', true);

    expect(controller.machines.first.approved, isTrue);
  });

  test('setMachineProtection updates protected state', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: true,
          revoked: false,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.setMachineProtection('M-01', true);

    expect(controller.machines.first.protectedState, isTrue);
  });

  test('resetMachineRegistration clears key state', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: true,
          approved: true,
          revoked: false,
          keyId: 'key-01',
          keyType: 'RSA',
          registrationCertFingerprint: 'ABC',
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.resetMachineRegistration('M-01');

    expect(controller.machines, hasLength(1));
  });

  test('deleteMachine removes machine from list', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
        ),
        MachineRecord(
          machineId: 'M-02',
          protectedState: false,
          vhdKeyword: 'SAFEBOOT',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.deleteMachine('M-01');

    expect(controller.machines, hasLength(1));
    expect(controller.machines.first.machineId, 'M-02');
  });

  test('setMachineVhd updates keyword', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.setMachineVhd('M-01', 'SAFEBOOT');

    expect(controller.machines.first.vhdKeyword, 'SAFEBOOT');
  });

  test('setMachineEvhdPassword updates configured flag', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.setMachineEvhdPassword('M-01', 'SecretPassword123');

    expect(controller.machines.first.evhdPasswordConfigured, isTrue);
  });

  test('setMachineLogRetentionOverride updates override', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
          logRetentionActiveDaysOverride: null,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.setMachineLogRetentionOverride('M-01', 30);

    expect(controller.machines.first.logRetentionActiveDaysOverride, 30);
  });

  test('setMachineLogRetentionOverride clears override with null', () async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: const <MachineRecord>[
        MachineRecord(
          machineId: 'M-01',
          protectedState: false,
          vhdKeyword: 'SDEZ',
          evhdPasswordConfigured: false,
          approved: false,
          revoked: false,
          logRetentionActiveDaysOverride: 30,
        ),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );
    await controller.bootstrap();

    await controller.setMachineLogRetentionOverride('M-01', null);

    expect(controller.machines.first.logRetentionActiveDaysOverride, isNull);
  });
}
