import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/main.dart';

void main() {
  testWidgets('shows initialization screen when server is not initialized', (tester) async {
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
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.text('服务未初始化'), findsOneWidget);
    expect(find.text('初始化向导'), findsOneWidget);
  });

  testWidgets('shows dashboard when admin is authenticated', (tester) async {
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
          ),
        ],
      ),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.text('VHD Mount Admin'), findsOneWidget);
    expect(find.text('机器管理'), findsAtLeastNWidgets(1));
    expect(find.text('MACHINE-01'), findsOneWidget);
  });
}

class FakeAdminApi implements AdminApi {
  FakeAdminApi({
    required this.serverStatus,
    required this.authStatus,
    this.machines = const <MachineRecord>[],
    this.certificates = const <TrustedCertificateRecord>[],
    this.auditEntries = const <AuditEntry>[],
  });

  ServerStatus serverStatus;
  AuthStatus authStatus;
  List<MachineRecord> machines;
  List<TrustedCertificateRecord> certificates;
  List<AuditEntry> auditEntries;
  String _baseUrl = 'http://localhost:8080';

  @override
  String get baseUrl => _baseUrl;

  @override
  void updateBaseUrl(String baseUrl) {
    _baseUrl = baseUrl;
  }

  @override
  Future<void> addTrustedCertificate(String name, String certificatePem) async {}

  @override
  Future<void> changePassword(String currentPassword, String newPassword) async {}

  @override
  Future<void> completeInitialization({
    required String adminPassword,
    required String sessionSecret,
    required String totpCode,
    required String defaultVhdKeyword,
    required String dbHost,
    required int dbPort,
    required String dbName,
    required String dbUser,
    required String dbPassword,
    List<Map<String, String>> trustedCertificates = const <Map<String, String>>[],
  }) async {}

  @override
  Future<AuthStatus> getAuthStatus() async => authStatus;

  @override
  Future<List<AuditEntry>> getAuditEntries() async => auditEntries;

  @override
  Future<List<MachineRecord>> getMachines() async => machines;

  @override
  Future<String> getPlainEvhdPassword(String machineId, String reason) async => 'secret';

  @override
  Future<OtpStatus> getOtpStatus() async => OtpStatus(verified: authStatus.otpVerified, verifiedUntil: 0);

  @override
  Future<ServerStatus> getServerStatus() async => serverStatus;

  @override
  Future<List<TrustedCertificateRecord>> getTrustedCertificates() async => certificates;

  @override
  Future<void> login(String password) async {
    authStatus = const AuthStatus(initialized: true, isAuthenticated: true, otpVerified: false);
  }

  @override
  Future<void> logout() async {
    authStatus = const AuthStatus(initialized: true, isAuthenticated: false, otpVerified: false);
  }

  @override
  Future<InitializationPreparation> prepareInitialization({required String issuer, required String accountName}) async {
    return const InitializationPreparation(
      issuer: 'VHDMountServer',
      accountName: 'admin',
      totpSecret: 'secret',
      otpauthUrl: 'otpauth://example',
    );
  }

  @override
  Future<void> removeTrustedCertificate(String fingerprint) async {}

  @override
  Future<void> resetMachineRegistration(String machineId) async {}

  @override
  Future<void> setMachineApproval(String machineId, bool approved) async {}

  @override
  Future<void> setMachineEvhdPassword(String machineId, String evhdPassword) async {}

  @override
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {}

  @override
  Future<void> updateDefaultVhd(String vhdKeyword) async {}

  @override
  Future<OtpStatus> verifyOtp(String code) async {
    authStatus = const AuthStatus(initialized: true, isAuthenticated: true, otpVerified: true);
    return const OtpStatus(verified: true, verifiedUntil: 0);
  }
}