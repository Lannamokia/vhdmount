import 'dart:io';

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

void main() {
  testWidgets('shows initialization screen when server is not initialized', (
    tester,
  ) async {
    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: const ServerStatus(
          initialized: false,
          pendingInitialization: false,
          databaseReady: false,
          defaultVhdKeyword: 'SDEZ',
          trustedRegistrationCertificateCount: 0,
        ),
        authStatus: const AuthStatus(
          initialized: false,
          isAuthenticated: false,
          otpVerified: false,
        ),
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.text('服务未初始化'), findsOneWidget);
    expect(find.text('初始化向导'), findsOneWidget);
  });

  testWidgets('shows connection screen when bootstrap cannot load server status', (
    tester,
  ) async {
    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: const AuthStatus(
        initialized: true,
        isAuthenticated: false,
        otpVerified: false,
      ),
      getServerStatusError: SocketException(
        'Connection refused',
        address: InternetAddress.loopbackIPv4,
        port: 8080,
      ),
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.text('连接你的 VHD 服务'), findsOneWidget);
    expect(find.textContaining('无法连接到本机服务端'), findsOneWidget);
    expect(find.textContaining('Connection refused'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('shows dashboard when admin is authenticated', (tester) async {
    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: const AuthStatus(
          initialized: true,
          isAuthenticated: true,
          otpVerified: true,
        ),
        machines: const <MachineRecord>[
          MachineRecord(
            machineId: 'MACHINE-01',
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
            metadata: <String, dynamic>{},
          ),
        ],
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.text('VHD Mount Admin'), findsOneWidget);
    expect(find.text('机器管理'), findsAtLeastNWidgets(1));
    expect(find.text('MACHINE-01'), findsOneWidget);
  });
}