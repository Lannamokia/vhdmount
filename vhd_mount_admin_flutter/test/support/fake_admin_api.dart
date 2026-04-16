import 'package:vhd_mount_admin_flutter/app.dart';

class FakeAdminApi implements AdminApi {
  FakeAdminApi({
    required this.serverStatus,
    required this.authStatus,
    this.machines = const <MachineRecord>[],
    this.certificates = const <TrustedCertificateRecord>[],
    this.auditEntries = const <AuditEntry>[],
    this.getOtpStatusResponse,
    this.verifyOtpResponse,
    this.prepareOtpRotationResponse,
    this.completeOtpRotationResponse,
    this.getServerStatusError,
    this.getAuthStatusError,
    this.getMachinesError,
    this.getAuditEntriesError,
    this.getTrustedCertificatesError,
  });

  ServerStatus serverStatus;
  AuthStatus authStatus;
  List<MachineRecord> machines;
  List<TrustedCertificateRecord> certificates;
  List<AuditEntry> auditEntries;
  OtpStatus? getOtpStatusResponse;
  OtpStatus? verifyOtpResponse;
  InitializationPreparation? prepareOtpRotationResponse;
  OtpStatus? completeOtpRotationResponse;

  Object? getServerStatusError;
  Object? getAuthStatusError;
  Object? getMachinesError;
  Object? getAuditEntriesError;
  Object? getTrustedCertificatesError;

  int getServerStatusCalls = 0;
  int getAuthStatusCalls = 0;
  int getMachinesCalls = 0;
  int getAuditEntriesCalls = 0;
  int getTrustedCertificatesCalls = 0;
  int updateDefaultVhdCalls = 0;
  int changePasswordCalls = 0;
  int prepareOtpRotationCalls = 0;
  int completeOtpRotationCalls = 0;
  String? lastAuditMachineId;
  String? lastUpdatedDefaultVhd;
  String? lastCurrentPassword;
  String? lastNewPassword;
  String? lastOtpRotationCurrentCode;
  String? lastOtpRotationNewCode;
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
  ) async {
    certificates = <TrustedCertificateRecord>[
      ...certificates,
      TrustedCertificateRecord(
        name: name,
        fingerprint256: 'fingerprint-${certificates.length + 1}',
        subject: 'CN=$name',
        validFrom: '2026-01-01T00:00:00Z',
        validTo: '2027-01-01T00:00:00Z',
        certificatePem: certificatePem,
      ),
    ];
  }

  @override
  Future<void> changePassword(
    String currentPassword,
    String newPassword,
  ) async {
    changePasswordCalls += 1;
    lastCurrentPassword = currentPassword;
    lastNewPassword = newPassword;
  }

  @override
  Future<OtpStatus> completeOtpRotation(String code) async {
    completeOtpRotationCalls += 1;
    lastOtpRotationNewCode = code;
    return completeOtpRotationResponse ??
        const OtpStatus(verified: true, verifiedUntil: 0);
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
  Future<AuthStatus> getAuthStatus() async {
    getAuthStatusCalls += 1;
    if (getAuthStatusError != null) {
      throw getAuthStatusError!;
    }
    return authStatus;
  }

  @override
  Future<List<AuditEntry>> getAuditEntries({String? machineId}) async {
    getAuditEntriesCalls += 1;
    lastAuditMachineId = machineId;
    if (getAuditEntriesError != null) {
      throw getAuditEntriesError!;
    }
    if (machineId == null || machineId.isEmpty) {
      return auditEntries;
    }
    return auditEntries.where((entry) => entry.machineId == machineId).toList();
  }

  @override
  Future<List<MachineRecord>> getMachines() async {
    getMachinesCalls += 1;
    if (getMachinesError != null) {
      throw getMachinesError!;
    }
    return machines;
  }

  @override
  Future<String> getPlainEvhdPassword(String machineId, String reason) async =>
      'secret';

  @override
  Future<OtpStatus> getOtpStatus() async =>
      getOtpStatusResponse ??
      OtpStatus(verified: authStatus.otpVerified, verifiedUntil: 0);

  @override
  Future<ServerStatus> getServerStatus() async {
    getServerStatusCalls += 1;
    if (getServerStatusError != null) {
      throw getServerStatusError!;
    }
    return serverStatus;
  }

  @override
  Future<List<TrustedCertificateRecord>> getTrustedCertificates() async {
    getTrustedCertificatesCalls += 1;
    if (getTrustedCertificatesError != null) {
      throw getTrustedCertificatesError!;
    }
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
    prepareOtpRotationCalls += 1;
    lastOtpRotationCurrentCode = currentCode;
    return prepareOtpRotationResponse ??
        InitializationPreparation(
          issuer: issuer?.isNotEmpty == true ? issuer! : 'VHDMountServer',
          accountName: accountName?.isNotEmpty == true ? accountName! : 'admin',
          totpSecret: 'secret',
          otpauthUrl: 'otpauth://example',
        );
  }

  @override
  Future<void> removeTrustedCertificate(String fingerprint) async {
    certificates = certificates
        .where((certificate) => certificate.fingerprint256 != fingerprint)
        .toList();
  }

  @override
  Future<void> resetMachineRegistration(String machineId) async {}

  @override
  Future<void> deleteMachine(String machineId) async {
    machines = machines
        .where((machine) => machine.machineId != machineId)
        .toList();
  }

  @override
  Future<void> setMachineApproval(String machineId, bool approved) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? MachineRecord(
                  machineId: machine.machineId,
                  protectedState: machine.protectedState,
                  vhdKeyword: machine.vhdKeyword,
                  evhdPasswordConfigured: machine.evhdPasswordConfigured,
                  approved: approved,
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
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? MachineRecord(
                  machineId: machine.machineId,
                  protectedState: machine.protectedState,
                  vhdKeyword: vhdKeyword,
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
  Future<void> updateDefaultVhd(String vhdKeyword) async {
    updateDefaultVhdCalls += 1;
    lastUpdatedDefaultVhd = vhdKeyword;
    serverStatus = ServerStatus(
      initialized: serverStatus.initialized,
      pendingInitialization: serverStatus.pendingInitialization,
      databaseReady: serverStatus.databaseReady,
      defaultVhdKeyword: vhdKeyword,
      trustedRegistrationCertificateCount:
          serverStatus.trustedRegistrationCertificateCount,
    );
  }

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