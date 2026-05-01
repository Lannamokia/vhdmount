import 'dart:async';
import 'dart:convert';

import 'package:vhd_mount_admin_flutter/app.dart';

class FakeAdminApi implements AdminApi {
  FakeAdminApi({
    required this.serverStatus,
    required this.authStatus,
    this.machines = const <MachineRecord>[],
    this.certificates = const <TrustedCertificateRecord>[],
    this.auditEntries = const <AuditEntry>[],
    this.logRetentionSettings = const LogRetentionSettings(
      defaultRetentionActiveDays: 7,
      dailyInspectionHour: 3,
      dailyInspectionMinute: 0,
      timezone: 'UTC',
      lastInspectionAt: null,
    ),
    this.machineLogSessions = const <MachineLogSession>[],
    this.machineLogEntries = const <MachineLogEntry>[],
    this.machineDeploymentHistory = const <String, List<DeploymentRecord>>{},
    this.getOtpStatusResponse,
    this.verifyOtpResponse,
    this.prepareOtpRotationResponse,
    this.completeOtpRotationResponse,
    this.getServerStatusError,
    this.getAuthStatusError,
    this.getMachinesError,
    this.getAuditEntriesError,
    this.getLogRetentionSettingsError,
    this.getMachineLogSessionsError,
    this.getMachineLogsError,
    this.exportMachineLogsError,
    this.getTrustedCertificatesError,
  });

  ServerStatus serverStatus;
  AuthStatus authStatus;
  List<MachineRecord> machines;
  List<TrustedCertificateRecord> certificates;
  List<AuditEntry> auditEntries;
  LogRetentionSettings logRetentionSettings;
  List<MachineLogSession> machineLogSessions;
  List<MachineLogEntry> machineLogEntries;
  Map<String, List<DeploymentRecord>> machineDeploymentHistory;
  OtpStatus? getOtpStatusResponse;
  OtpStatus? verifyOtpResponse;
  InitializationPreparation? prepareOtpRotationResponse;
  OtpStatus? completeOtpRotationResponse;

  Object? getServerStatusError;
  Object? getAuthStatusError;
  Object? getMachinesError;
  Object? getAuditEntriesError;
  Object? getLogRetentionSettingsError;
  Object? getMachineLogSessionsError;
  Object? getMachineLogsError;
  Object? exportMachineLogsError;
  Object? getTrustedCertificatesError;
  final Map<String, Duration> deploymentHistoryDelays = <String, Duration>{};

  int getServerStatusCalls = 0;
  int getAuthStatusCalls = 0;
  int getMachinesCalls = 0;
  int getAuditEntriesCalls = 0;
  int getLogRetentionSettingsCalls = 0;
  int getMachineLogSessionsCalls = 0;
  int getMachineLogsCalls = 0;
  int exportMachineLogsCalls = 0;
  int getTrustedCertificatesCalls = 0;
  int updateDefaultVhdCalls = 0;
  int updateLogRetentionSettingsCalls = 0;
  int changePasswordCalls = 0;
  int prepareOtpRotationCalls = 0;
  int completeOtpRotationCalls = 0;
  String? lastAuditMachineId;
  String? lastUpdatedDefaultVhd;
  String? lastCurrentPassword;
  String? lastNewPassword;
  String? lastOtpRotationCurrentCode;
  String? lastOtpRotationNewCode;
  String? lastMachineLogMachineId;
  String _baseUrl = 'http://localhost:8080';

  MachineRecord _copyMachine(
    MachineRecord machine, {
    bool? protectedState,
    String? vhdKeyword,
    bool? evhdPasswordConfigured,
    bool? approved,
    bool? revoked,
    String? keyId,
    String? keyType,
    String? registrationCertFingerprint,
    int? logRetentionActiveDaysOverride,
    bool keepRetentionOverride = true,
    String? lastSeen,
  }) {
    return MachineRecord(
      machineId: machine.machineId,
      protectedState: protectedState ?? machine.protectedState,
      vhdKeyword: vhdKeyword ?? machine.vhdKeyword,
      evhdPasswordConfigured:
          evhdPasswordConfigured ?? machine.evhdPasswordConfigured,
      approved: approved ?? machine.approved,
      revoked: revoked ?? machine.revoked,
      keyId: keyId ?? machine.keyId,
      keyType: keyType ?? machine.keyType,
      registrationCertFingerprint:
          registrationCertFingerprint ?? machine.registrationCertFingerprint,
      logRetentionActiveDaysOverride: keepRetentionOverride
          ? logRetentionActiveDaysOverride ?? machine.logRetentionActiveDaysOverride
          : logRetentionActiveDaysOverride,
      lastSeen: lastSeen ?? machine.lastSeen,
    );
  }

  Iterable<MachineLogEntry> _filteredMachineLogEntries(MachineLogFilter filter) {
    Iterable<MachineLogEntry> rows = machineLogEntries;

    if (filter.machineId != null && filter.machineId!.isNotEmpty) {
      rows = rows.where((entry) => entry.machineId == filter.machineId);
    }
    if (filter.sessionId != null && filter.sessionId!.isNotEmpty) {
      rows = rows.where((entry) => entry.sessionId == filter.sessionId);
    }
    if (filter.level != null && filter.level!.isNotEmpty) {
      rows = rows.where((entry) => entry.level == filter.level);
    }
    if (filter.component != null && filter.component!.isNotEmpty) {
      rows = rows.where((entry) => entry.component == filter.component);
    }
    if (filter.eventKey != null && filter.eventKey!.isNotEmpty) {
      rows = rows.where((entry) => entry.eventKey == filter.eventKey);
    }
    if (filter.query != null && filter.query!.isNotEmpty) {
      final query = filter.query!.toLowerCase();
      rows = rows.where((entry) {
        final searchable = jsonEncode(<String, dynamic>{
          'message': entry.message,
          'rawText': entry.rawText,
          'metadata': entry.metadata,
          'component': entry.component,
          'eventKey': entry.eventKey,
        }).toLowerCase();
        return searchable.contains(query);
      });
    }
    if (filter.from != null && filter.from!.isNotEmpty) {
      final from = DateTime.parse(filter.from!);
      rows = rows.where((entry) {
        final occurredAt = DateTime.parse(entry.occurredAt);
        return occurredAt.isAfter(from) || occurredAt.isAtSameMomentAs(from);
      });
    }
    if (filter.to != null && filter.to!.isNotEmpty) {
      final to = DateTime.parse(filter.to!);
      rows = rows.where((entry) {
        final occurredAt = DateTime.parse(entry.occurredAt);
        return occurredAt.isBefore(to) || occurredAt.isAtSameMomentAs(to);
      });
    }

    final sorted = rows.toList()
      ..sort((left, right) {
        final timeCompare = DateTime.parse(right.occurredAt).compareTo(
          DateTime.parse(left.occurredAt),
        );
        return timeCompare != 0 ? timeCompare : right.id.compareTo(left.id);
      });

    if (filter.cursor == null || filter.cursor!.isEmpty) {
      return sorted;
    }

    final parsed = jsonDecode(
      utf8.decode(base64Url.decode(base64Url.normalize(filter.cursor!))),
    ) as Map<String, dynamic>;
    final cursorOccurredAt = DateTime.parse(parsed['occurredAt'] as String);
    final cursorId = (parsed['id'] as num).toInt();
    return sorted.where((entry) {
      final occurredAt = DateTime.parse(entry.occurredAt);
      return occurredAt.isBefore(cursorOccurredAt) ||
          (occurredAt.isAtSameMomentAs(cursorOccurredAt) && entry.id < cursorId);
    });
  }

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
        logRetentionActiveDaysOverride: null,
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
  Future<void> deleteMachine(String machineId) async {
    machines = machines
        .where((machine) => machine.machineId != machineId)
        .toList();
  }

  @override
  Future<String> exportMachineLogs(
    MachineLogFilter filter, {
    String format = 'text',
  }) async {
    exportMachineLogsCalls += 1;
    if (exportMachineLogsError != null) {
      throw exportMachineLogsError!;
    }

    final rows = _filteredMachineLogEntries(
      MachineLogFilter(
        machineId: filter.machineId,
        sessionId: filter.sessionId,
        level: filter.level,
        component: filter.component,
        eventKey: filter.eventKey,
        query: filter.query,
        from: filter.from,
        to: filter.to,
        limit: 5000,
      ),
    );
    return rows
        .map(
          (entry) =>
              '[${entry.occurredAt}] [${entry.level.toUpperCase()}] [${entry.component}/${entry.eventKey}] ${entry.rawText}',
        )
        .join('\n');
  }

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
  Future<LogRetentionSettings> getLogRetentionSettings() async {
    getLogRetentionSettingsCalls += 1;
    if (getLogRetentionSettingsError != null) {
      throw getLogRetentionSettingsError!;
    }
    return logRetentionSettings;
  }

  @override
  Future<MachineLogPage> getMachineLogs(MachineLogFilter filter) async {
    getMachineLogsCalls += 1;
    if (getMachineLogsError != null) {
      throw getMachineLogsError!;
    }

    final rows = _filteredMachineLogEntries(filter).toList();
    final pageEntries = rows.take(filter.limit).toList();
    final hasMore = rows.length > filter.limit;
    final nextCursor = hasMore && pageEntries.isNotEmpty
        ? base64Url.encode(
            utf8.encode(
              jsonEncode(<String, dynamic>{
                'occurredAt': pageEntries.last.occurredAt,
                'id': pageEntries.last.id,
              }),
            ),
          )
        : null;
    return MachineLogPage(
      entries: pageEntries,
      nextCursor: nextCursor,
      hasMore: hasMore,
    );
  }

  @override
  Future<List<MachineLogSession>> getMachineLogSessions({
    String? machineId,
    String? from,
    String? to,
    int limit = 50,
  }) async {
    getMachineLogSessionsCalls += 1;
    lastMachineLogMachineId = machineId;
    if (getMachineLogSessionsError != null) {
      throw getMachineLogSessionsError!;
    }

    var rows = machineLogSessions;
    if (machineId != null && machineId.isNotEmpty) {
      rows = rows.where((session) => session.machineId == machineId).toList();
    }
    if (from != null && from.isNotEmpty) {
      final start = DateTime.parse(from);
      rows = rows.where((session) {
        final anchor = DateTime.tryParse(session.lastEventAt ?? '') ??
            DateTime.tryParse(session.startedAt ?? '') ??
            DateTime.fromMillisecondsSinceEpoch(0);
        return anchor.isAfter(start) || anchor.isAtSameMomentAs(start);
      }).toList();
    }
    if (to != null && to.isNotEmpty) {
      final end = DateTime.parse(to);
      rows = rows.where((session) {
        final anchor = DateTime.tryParse(session.lastEventAt ?? '') ??
            DateTime.tryParse(session.startedAt ?? '') ??
            DateTime.fromMillisecondsSinceEpoch(0);
        return anchor.isBefore(end) || anchor.isAtSameMomentAs(end);
      }).toList();
    }
    return rows.take(limit).toList();
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
  Future<void> setMachineApproval(String machineId, bool approved) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? _copyMachine(machine, approved: approved)
              : machine,
        )
        .toList();
  }

  @override
  Future<void> setMachineEvhdPassword(
    String machineId,
    String evhdPassword,
  ) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? _copyMachine(
                  machine,
                  evhdPasswordConfigured: evhdPassword.trim().isNotEmpty,
                )
              : machine,
        )
        .toList();
  }

  @override
  Future<void> setMachineLogRetentionOverride(
    String machineId,
    int? retentionActiveDaysOverride,
  ) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? _copyMachine(
                  machine,
                  logRetentionActiveDaysOverride: retentionActiveDaysOverride,
                  keepRetentionOverride: false,
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
              ? _copyMachine(machine, protectedState: protectedState)
              : machine,
        )
        .toList();
  }

  @override
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    machines = machines
        .map(
          (machine) => machine.machineId == machineId
              ? _copyMachine(machine, vhdKeyword: vhdKeyword)
              : machine,
        )
        .toList();
  }

  @override
  Future<LogRetentionSettings> updateLogRetentionSettings({
    required int defaultRetentionActiveDays,
    required int dailyInspectionHour,
    required int dailyInspectionMinute,
    required String timezone,
  }) async {
    updateLogRetentionSettingsCalls += 1;
    logRetentionSettings = LogRetentionSettings(
      defaultRetentionActiveDays: defaultRetentionActiveDays,
      dailyInspectionHour: dailyInspectionHour,
      dailyInspectionMinute: dailyInspectionMinute,
      timezone: timezone,
      lastInspectionAt: logRetentionSettings.lastInspectionAt,
    );
    return logRetentionSettings;
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

  @override
  Future<List<DeploymentPackage>> getDeploymentPackages() async =>
      <DeploymentPackage>[];

  @override
  Future<void> uploadDeploymentPackage({
    required String name,
    required String version,
    required String type,
    required String signer,
    required String packagePath,
    required String packageFileName,
    required String signaturePath,
    required String signatureFileName,
  }) async {}

  @override
  Future<void> deleteDeploymentPackage(String packageId) async {}

  @override
  Future<void> deleteDeploymentTask(String taskId) async {}

  @override
  Future<List<DeploymentTask>> getDeploymentTasks({
    String? machineId,
    String? status,
  }) async =>
      <DeploymentTask>[];

  @override
  Future<void> createDeploymentTask(
    String packageId,
    List<String> targetMachineIds, {
    String? scheduledAt,
  }) async {}

  @override
  Future<List<DeploymentRecord>> getMachineDeploymentHistory(
    String machineId,
  ) async {
    final delay = deploymentHistoryDelays[machineId];
    if (delay != null) {
      await Future<void>.delayed(delay);
    }
    return machineDeploymentHistory[machineId] ?? <DeploymentRecord>[];
  }

  @override
  Future<void> triggerUninstall(String machineId, String recordId) async {}

  @override
  Future<void> restoreSession() async {}
}
