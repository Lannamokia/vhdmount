import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/main.dart';

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

  test(
    'controller remembers successful server addresses in local config',
    () async {
      final configStore = FakeClientConfigStore();
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
            isAuthenticated: false,
            otpVerified: false,
          ),
        ),
        clientConfigStore: configStore,
      );

      controller.updateBaseUrl('http://server-a:8080/');
      await controller.login('ComplexPassword123!');

      expect(configStore.saveCalls, 1);
      expect(configStore.config.lastBaseUrl, 'http://server-a:8080');
      expect(configStore.config.serverHistory, <String>[
        'http://server-a:8080',
      ]);
      expect(controller.rememberedBaseUrls, <String>['http://server-a:8080']);
    },
  );

  test(
    'bootstrap restores saved server address before probing server status',
    () async {
      final api = FakeAdminApi(
        serverStatus: const ServerStatus(
          initialized: true,
          pendingInitialization: false,
          databaseReady: true,
          defaultVhdKeyword: 'SAFEBOOT',
          trustedRegistrationCertificateCount: 1,
        ),
        authStatus: const AuthStatus(
          initialized: true,
          isAuthenticated: false,
          otpVerified: false,
        ),
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
      expect(controller.rememberedBaseUrls, <String>[
        'http://saved-server:8080',
      ]);
    },
  );

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

  testWidgets('login shell stays stable on narrow windows', (tester) async {
    tester.view.physicalSize = const Size(280, 900);
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

  testWidgets('login shell resizes to compact mode without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 920);
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
          isAuthenticated: false,
          otpVerified: false,
        ),
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    tester.view.physicalSize = const Size(1400, 780);
    await tester.pump();
    await tester.pumpAndSettle();

    expect(find.text('管理员登录'), findsOneWidget);
    expect(find.text('连接状态'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('login hero panel stays above the form on wide desktops', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 920);
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
            lastSeen: '2026-04-03T08:00:00Z',
          ),
        ],
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    expect(find.byType(DashboardSidebarButton), findsNWidgets(4));
    expect(find.text('审批、保护、EVHD'), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('overview cards keep equal size while resizing', (tester) async {
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
            lastSeen: '2026-04-03T08:00:00Z',
          ),
        ],
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

    tester.view.physicalSize = const Size(1180, 960);
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
    tester.view.physicalSize = const Size(1400, 540);
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
            lastSeen: '2026-04-03T08:00:00Z',
          ),
        ],
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
}

class FakeAdminApi implements AdminApi {
  FakeAdminApi({
    required this.serverStatus,
    required this.authStatus,
    this.machines = const <MachineRecord>[],
    this.certificates = const <TrustedCertificateRecord>[],
    this.auditEntries = const <AuditEntry>[],
    this.getOtpStatusResponse,
    this.verifyOtpResponse,
  });

  ServerStatus serverStatus;
  AuthStatus authStatus;
  List<MachineRecord> machines;
  List<TrustedCertificateRecord> certificates;
  List<AuditEntry> auditEntries;
  OtpStatus? getOtpStatusResponse;
  OtpStatus? verifyOtpResponse;
  int getTrustedCertificatesCalls = 0;
  String _baseUrl = 'http://localhost:8080';

  @override
  String get baseUrl => _baseUrl;

  @override
  void updateBaseUrl(String baseUrl) {
    _baseUrl = baseUrl;
  }

  @override
  Future<void> addMachine(MachineDraft draft) async {
    machines = <MachineRecord>[
      ...machines,
      MachineRecord(
        machineId: draft.machineId,
        protectedState: draft.protectedState,
        vhdKeyword: draft.vhdKeyword,
        evhdPasswordConfigured: draft.evhdPassword?.isNotEmpty == true,
        approved: false,
        revoked: false,
        keyId: null,
        keyType: null,
        registrationCertFingerprint: null,
        lastSeen: null,
      ),
    ];
  }

  @override
  Future<void> addTrustedCertificate(
    String name,
    String certificatePem,
  ) async {}

  @override
  Future<void> changePassword(
    String currentPassword,
    String newPassword,
  ) async {}

  @override
  Future<OtpStatus> completeOtpRotation(String code) async {
    return const OtpStatus(verified: true, verifiedUntil: 0);
  }

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
    List<Map<String, String>> trustedCertificates =
        const <Map<String, String>>[],
  }) async {}

  @override
  Future<AuthStatus> getAuthStatus() async => authStatus;

  @override
  Future<List<AuditEntry>> getAuditEntries({String? machineId}) async {
    if (machineId == null || machineId.isEmpty) {
      return auditEntries;
    }
    return auditEntries.where((entry) => entry.machineId == machineId).toList();
  }

  @override
  Future<List<MachineRecord>> getMachines() async => machines;

  @override
  Future<String> getPlainEvhdPassword(String machineId, String reason) async =>
      'secret';

  @override
  Future<OtpStatus> getOtpStatus() async =>
      getOtpStatusResponse ??
      OtpStatus(verified: authStatus.otpVerified, verifiedUntil: 0);

  @override
  Future<ServerStatus> getServerStatus() async => serverStatus;

  @override
  Future<List<TrustedCertificateRecord>> getTrustedCertificates() async {
    getTrustedCertificatesCalls += 1;
    return certificates;
  }

  @override
  Future<void> login(String password) async {
    authStatus = const AuthStatus(
      initialized: true,
      isAuthenticated: true,
      otpVerified: false,
    );
  }

  @override
  Future<void> logout() async {
    authStatus = const AuthStatus(
      initialized: true,
      isAuthenticated: false,
      otpVerified: false,
    );
  }

  @override
  Future<InitializationPreparation> prepareInitialization({
    required String issuer,
    required String accountName,
  }) async {
    return const InitializationPreparation(
      issuer: 'VHDMountServer',
      accountName: 'admin',
      totpSecret: 'secret',
      otpauthUrl: 'otpauth://example',
    );
  }

  @override
  Future<InitializationPreparation> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  }) async {
    return InitializationPreparation(
      issuer: issuer?.isNotEmpty == true ? issuer! : 'VHDMountServer',
      accountName: accountName?.isNotEmpty == true ? accountName! : 'admin',
      totpSecret: 'secret',
      otpauthUrl: 'otpauth://example',
    );
  }

  @override
  Future<void> removeTrustedCertificate(String fingerprint) async {}

  @override
  Future<void> resetMachineRegistration(String machineId) async {}

  @override
  Future<void> deleteMachine(String machineId) async {
    machines = machines
        .where((machine) => machine.machineId != machineId)
        .toList();
  }

  @override
  Future<void> setMachineApproval(String machineId, bool approved) async {}

  @override
  Future<void> setMachineProtection(
    String machineId,
    bool protectedState,
  ) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? MachineRecord(
                  machineId: machine.machineId,
                  protectedState: protectedState,
                  vhdKeyword: machine.vhdKeyword,
                  evhdPasswordConfigured: machine.evhdPasswordConfigured,
                  approved: machine.approved,
                  revoked: machine.revoked,
                  keyId: machine.keyId,
                  keyType: machine.keyType,
                  registrationCertFingerprint:
                      machine.registrationCertFingerprint,
                  lastSeen: machine.lastSeen,
                )
              : machine,
        )
        .toList();
  }

  @override
  Future<void> setMachineEvhdPassword(
    String machineId,
    String evhdPassword,
  ) async {}

  @override
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {}

  @override
  Future<void> updateDefaultVhd(String vhdKeyword) async {}

  @override
  Future<OtpStatus> verifyOtp(String code) async {
    authStatus = const AuthStatus(
      initialized: true,
      isAuthenticated: true,
      otpVerified: true,
    );
    return verifyOtpResponse ??
        const OtpStatus(verified: true, verifiedUntil: 0);
  }
}

class FakeClientConfigStore implements ClientConfigStore {
  FakeClientConfigStore([this.config = const ClientConfig()]);

  ClientConfig config;
  int saveCalls = 0;

  @override
  Future<ClientConfig> load() async => config;

  @override
  Future<void> save(ClientConfig nextConfig) async {
    saveCalls += 1;
    config = nextConfig;
  }
}
