import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

void main() {
  testWidgets('audit view filters visible entries by local search text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 960);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: const ServerStatus(
          initialized: true,
          pendingInitialization: false,
          databaseReady: true,
          defaultVhdKeyword: 'SAFEBOOT',
          trustedRegistrationCertificateCount: 1,
        ),
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
            metadata: <String, dynamic>{'machineId': 'MACHINE-01'},
          ),
          AuditEntry(
            timestamp: '2026-04-03T09:00:00Z',
            type: 'machine.delete',
            actor: 'admin',
            result: 'success',
            path: '/api/machines/MACHINE-01',
            ip: '127.0.0.1',
            metadata: <String, dynamic>{'machineId': 'MACHINE-01'},
          ),
        ],
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.text('审计').first);
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextField).last, '删除');
    await tester.pumpAndSettle();

    expect(find.text('已删除机台'), findsOneWidget);
    expect(find.text('管理员登录成功'), findsNothing);
  });
}