part of '../app.dart';

class AppController extends ChangeNotifier {
  AppController({required this.api, ClientConfigStore? clientConfigStore})
    : clientConfigStore = clientConfigStore ?? FileClientConfigStore();

  final AdminApi api;
  final ClientConfigStore clientConfigStore;

  Timer? _otpExpiryTimer;
  bool _clientConfigLoaded = false;

  bool isLoading = true;
  bool isWorking = false;
  String? errorMessage;
  ServerStatus? serverStatus;
  bool isAuthenticated = false;
  bool otpVerified = false;
  int otpVerifiedUntil = 0;
  InitializationPreparation? initializationPreparation;
  InitializationPreparation? otpRotationPreparation;
  List<MachineRecord> machines = <MachineRecord>[];
  List<TrustedCertificateRecord> certificates = <TrustedCertificateRecord>[];
  List<AuditEntry> auditEntries = <AuditEntry>[];
  LogRetentionSettings? logRetentionSettings;
  List<MachineLogSession> machineLogSessions = <MachineLogSession>[];
  List<MachineLogEntry> machineLogEntries = <MachineLogEntry>[];
  List<String> rememberedBaseUrls = <String>[];
  String? auditFilterMachineId;
  String? machineLogSelectedMachineId;
  String? machineLogSelectedSessionId;
  String? machineLogLevel;
  String? machineLogComponent;
  String? machineLogEventKey;
  String? machineLogQuery;
  String? machineLogFrom;
  String? machineLogTo;
  String? machineLogCursor;
  bool machineLogHasMore = false;
  List<DeploymentPackage> deploymentPackages = <DeploymentPackage>[];
  List<DeploymentTask> deploymentTasks = <DeploymentTask>[];
  List<DeploymentRecord> deploymentRecords = <DeploymentRecord>[];
  String? deploymentSelectedMachineId;
  String? deploymentTaskStatusFilter;

  String get baseUrl => api.baseUrl;

  void updateBaseUrl(String value) {
    api.updateBaseUrl(value);
    notifyListeners();
  }

  void setAuditMachineFilter(String? machineId, {bool notify = true}) {
    final normalized = machineId?.trim();
    auditFilterMachineId = normalized == null || normalized.isEmpty
        ? null
        : normalized;
    if (notify) {
      notifyListeners();
    }
  }

  void _clearMachineLogState({bool notify = true}) {
    machineLogSessions = <MachineLogSession>[];
    machineLogEntries = <MachineLogEntry>[];
    machineLogSelectedMachineId = null;
    machineLogSelectedSessionId = null;
    machineLogLevel = null;
    machineLogComponent = null;
    machineLogEventKey = null;
    machineLogQuery = null;
    machineLogFrom = null;
    machineLogTo = null;
    machineLogCursor = null;
    machineLogHasMore = false;
    if (notify) {
      notifyListeners();
    }
  }

  Future<void> _ensureClientConfigLoaded() async {
    if (_clientConfigLoaded) {
      return;
    }

    try {
      final config = await clientConfigStore.load();
      rememberedBaseUrls = config.serverHistory;
      if (config.lastBaseUrl.isNotEmpty) {
        api.updateBaseUrl(config.lastBaseUrl);
      }
    } on ClientConfigStoreException catch (error) {
      rememberedBaseUrls = <String>[];
      errorMessage = describeError(error);
    }

    _clientConfigLoaded = true;
  }

  Future<void> _rememberCurrentBaseUrl() async {
    final normalizedBaseUrl = normalizeBaseUrl(baseUrl);
    if (normalizedBaseUrl.isEmpty) {
      return;
    }

    rememberedBaseUrls = mergeRememberedBaseUrls(
      rememberedBaseUrls,
      preferredFirst: normalizedBaseUrl,
    );

    await clientConfigStore.save(
      ClientConfig(
        lastBaseUrl: normalizedBaseUrl,
        serverHistory: rememberedBaseUrls,
      ),
    );
  }

  Future<void> _rememberCurrentBaseUrlSilently() async {
    try {
      await _rememberCurrentBaseUrl();
    } catch (_) {
      return;
    }
  }

  void _clearOtpVerification({bool notify = true}) {
    _otpExpiryTimer?.cancel();
    _otpExpiryTimer = null;
    otpVerified = false;
    otpVerifiedUntil = 0;
    certificates = <TrustedCertificateRecord>[];
    if (notify) {
      notifyListeners();
    }
  }

  void _scheduleOtpExpiryTimer() {
    _otpExpiryTimer?.cancel();
    _otpExpiryTimer = null;

    if (!otpVerified || otpVerifiedUntil <= 0) {
      return;
    }

    final remainingMilliseconds =
        otpVerifiedUntil - DateTime.now().millisecondsSinceEpoch;
    if (remainingMilliseconds <= 0) {
      otpVerified = false;
      otpVerifiedUntil = 0;
      certificates = <TrustedCertificateRecord>[];
      return;
    }

    _otpExpiryTimer = Timer(
      Duration(milliseconds: remainingMilliseconds + 1),
      _clearOtpVerification,
    );
  }

  void _applyOtpStatus(OtpStatus otpStatus, {bool notify = true}) {
    otpVerified = otpStatus.verified;
    otpVerifiedUntil = otpStatus.verified ? otpStatus.verifiedUntil : 0;
    if (!otpVerified) {
      certificates = <TrustedCertificateRecord>[];
    }
    _scheduleOtpExpiryTimer();
    if (notify) {
      notifyListeners();
    }
  }

  Future<T> _runAction<T>(Future<T> Function() action) async {
    isWorking = true;
    errorMessage = null;
    notifyListeners();
    try {
      return await action();
    } catch (error) {
      errorMessage = describeError(error);
      notifyListeners();
      rethrow;
    } finally {
      isWorking = false;
      notifyListeners();
    }
  }

  Future<void> bootstrap() async {
    isLoading = true;
    errorMessage = null;
    notifyListeners();
    try {
      await _ensureClientConfigLoaded();
      await api.restoreSession();
      serverStatus = await api.getServerStatus();
      final authStatus = await api.getAuthStatus();
      isAuthenticated = authStatus.isAuthenticated;

      if (isAuthenticated) {
        _applyOtpStatus(await api.getOtpStatus(), notify: false);
        machines = await api.getMachines();
        auditEntries = await api.getAuditEntries(
          machineId: auditFilterMachineId,
        );
        logRetentionSettings = await api.getLogRetentionSettings();
      } else {
        _clearOtpVerification(notify: false);
        otpRotationPreparation = null;
        machines = <MachineRecord>[];
        certificates = <TrustedCertificateRecord>[];
        auditEntries = <AuditEntry>[];
        logRetentionSettings = null;
        _clearMachineLogState(notify: false);
      }
    } catch (error) {
      serverStatus = null;
      isAuthenticated = false;
      initializationPreparation = null;
      otpRotationPreparation = null;
      machines = <MachineRecord>[];
      certificates = <TrustedCertificateRecord>[];
      auditEntries = <AuditEntry>[];
      logRetentionSettings = null;
      _clearMachineLogState(notify: false);
      _clearOtpVerification(notify: false);
      errorMessage = describeError(error);
    } finally {
      isLoading = false;
      notifyListeners();
    }
  }

  Future<void> prepareInitialization({
    required String issuer,
    required String accountName,
  }) async {
    initializationPreparation = await _runAction(
      () => api.prepareInitialization(issuer: issuer, accountName: accountName),
    );
  }

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
  }) async {
    await _runAction(
      () => api.completeInitialization(
        adminPassword: adminPassword,
        sessionSecret: sessionSecret,
        totpCode: totpCode,
        defaultVhdKeyword: defaultVhdKeyword,
        dbHost: dbHost,
        dbPort: dbPort,
        dbName: dbName,
        dbUser: dbUser,
        dbPassword: dbPassword,
        trustedCertificates: trustedCertificates,
      ),
    );
    initializationPreparation = null;
    await _rememberCurrentBaseUrlSilently();
    await bootstrap();
  }

  Future<void> login(String password) async {
    await _runAction(() => api.login(password));
    await _rememberCurrentBaseUrlSilently();
    await bootstrap();
  }

  Future<void> logout() async {
    await _runAction(api.logout);
    _clearOtpVerification(notify: false);
    otpRotationPreparation = null;
    logRetentionSettings = null;
    _clearMachineLogState(notify: false);
    await bootstrap();
  }

  Future<void> verifyOtp(String code) async {
    final otpStatus = await _runAction(() => api.verifyOtp(code));
    _applyOtpStatus(otpStatus);

    if (!otpVerified) {
      return;
    }

    try {
      await loadCertificates();
    } catch (_) {
      // OTP 已成功，只把证书刷新失败作为界面错误保留，不中断成功状态。
    }
  }

  Future<void> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  }) async {
    otpRotationPreparation = await _runAction(
      () => api.prepareOtpRotation(
        currentCode: currentCode,
        issuer: issuer,
        accountName: accountName,
      ),
    );
    notifyListeners();
  }

  Future<void> completeOtpRotation(String code) async {
    final otpStatus = await _runAction(() => api.completeOtpRotation(code));
    otpRotationPreparation = null;
    _applyOtpStatus(otpStatus);
  }

  void clearOtpRotationPreparation() {
    otpRotationPreparation = null;
    notifyListeners();
  }

  Future<void> refreshOtpStatus() async {
    try {
      _applyOtpStatus(await api.getOtpStatus());
    } catch (_) {
      _clearOtpVerification();
    }
  }

  Future<void> loadMachines() async {
    machines = await _runAction(api.getMachines);
    notifyListeners();
  }

  Future<void> loadCertificates() async {
    certificates = await _runAction(api.getTrustedCertificates);
    notifyListeners();
  }

  Future<void> loadAudit({String? machineId}) async {
    setAuditMachineFilter(machineId, notify: false);
    auditEntries = await _runAction(
      () => api.getAuditEntries(machineId: auditFilterMachineId),
    );
    notifyListeners();
  }

  Future<void> loadLogRetentionSettings() async {
    logRetentionSettings = await _runAction(api.getLogRetentionSettings);
    notifyListeners();
  }

  Future<void> updateLogRetentionSettings({
    required int defaultRetentionActiveDays,
    required int dailyInspectionHour,
    required int dailyInspectionMinute,
    required String timezone,
  }) async {
    logRetentionSettings = await _runAction(
      () => api.updateLogRetentionSettings(
        defaultRetentionActiveDays: defaultRetentionActiveDays,
        dailyInspectionHour: dailyInspectionHour,
        dailyInspectionMinute: dailyInspectionMinute,
        timezone: timezone,
      ),
    );
    notifyListeners();
  }

  Future<void> setMachineLogRetentionOverride(
    String machineId,
    int? retentionActiveDaysOverride,
  ) async {
    await _runAction(
      () => api.setMachineLogRetentionOverride(
        machineId,
        retentionActiveDaysOverride,
      ),
    );
    await loadMachines();
  }

  Future<void> loadMachineLogSessions({
    String? machineId,
    String? from,
    String? to,
    bool loadEntries = true,
  }) async {
    machineLogSelectedMachineId = machineId?.trim().isEmpty == true
        ? null
        : machineId?.trim();
    machineLogFrom = from?.trim().isEmpty == true ? null : from?.trim();
    machineLogTo = to?.trim().isEmpty == true ? null : to?.trim();
    machineLogSessions = await _runAction(
      () => api.getMachineLogSessions(
        machineId: machineLogSelectedMachineId,
        from: machineLogFrom,
        to: machineLogTo,
      ),
    );

    final hasSelectedSession = machineLogSessions.any(
      (session) => session.sessionId == machineLogSelectedSessionId,
    );
    if (!hasSelectedSession) {
      machineLogSelectedSessionId = machineLogSessions.isNotEmpty
          ? machineLogSessions.first.sessionId
          : null;
    }

    notifyListeners();

    if (loadEntries) {
      await loadMachineLogs(
        machineId: machineLogSelectedMachineId,
        sessionId: machineLogSelectedSessionId,
        level: machineLogLevel,
        component: machineLogComponent,
        eventKey: machineLogEventKey,
        query: machineLogQuery,
        from: machineLogFrom,
        to: machineLogTo,
      );
    }
  }

  Future<void> loadMachineLogs({
    String? machineId,
    String? sessionId,
    String? level,
    String? component,
    String? eventKey,
    String? query,
    String? from,
    String? to,
    bool append = false,
  }) async {
    machineLogSelectedMachineId = machineId?.trim().isEmpty == true
        ? null
        : machineId?.trim();
    machineLogSelectedSessionId = sessionId?.trim().isEmpty == true
        ? null
        : sessionId?.trim();
    machineLogLevel = level?.trim().isEmpty == true ? null : level?.trim();
    machineLogComponent = component?.trim().isEmpty == true
        ? null
        : component?.trim();
    machineLogEventKey = eventKey?.trim().isEmpty == true
        ? null
        : eventKey?.trim().toUpperCase();
    machineLogQuery = query?.trim().isEmpty == true ? null : query?.trim();
    machineLogFrom = from?.trim().isEmpty == true ? null : from?.trim();
    machineLogTo = to?.trim().isEmpty == true ? null : to?.trim();

    final page = await _runAction(
      () => api.getMachineLogs(
        MachineLogFilter(
          machineId: machineLogSelectedMachineId,
          sessionId: machineLogSelectedSessionId,
          level: machineLogLevel,
          component: machineLogComponent,
          eventKey: machineLogEventKey,
          query: machineLogQuery,
          from: machineLogFrom,
          to: machineLogTo,
          cursor: append ? machineLogCursor : null,
        ),
      ),
    );

    machineLogEntries = append
        ? <MachineLogEntry>[...machineLogEntries, ...page.entries]
        : page.entries;
    machineLogCursor = page.nextCursor;
    machineLogHasMore = page.hasMore;
    notifyListeners();
  }

  Future<void> loadMoreMachineLogs() async {
    if (!machineLogHasMore || machineLogCursor == null) {
      return;
    }

    await loadMachineLogs(
      machineId: machineLogSelectedMachineId,
      sessionId: machineLogSelectedSessionId,
      level: machineLogLevel,
      component: machineLogComponent,
      eventKey: machineLogEventKey,
      query: machineLogQuery,
      from: machineLogFrom,
      to: machineLogTo,
      append: true,
    );
  }

  void clearMachineLogFilters({bool notify = true}) {
    _clearMachineLogState(notify: false);
    if (notify) {
      notifyListeners();
    }
  }

  Future<String> exportMachineLogs({String format = 'text'}) {
    return _runAction(
      () => api.exportMachineLogs(
        MachineLogFilter(
          machineId: machineLogSelectedMachineId,
          sessionId: machineLogSelectedSessionId,
          level: machineLogLevel,
          component: machineLogComponent,
          eventKey: machineLogEventKey,
          query: machineLogQuery,
          from: machineLogFrom,
          to: machineLogTo,
          limit: 5000,
        ),
        format: format,
      ),
    );
  }

  int? effectiveMachineLogRetentionDays(MachineRecord machine) {
    return machine.logRetentionActiveDaysOverride ??
        logRetentionSettings?.defaultRetentionActiveDays;
  }

  String describeMachineLogRetention(MachineRecord machine) {
    final override = machine.logRetentionActiveDaysOverride;
    if (override != null) {
      return '日志保留：$override 个活动日覆盖';
    }

    final inherited = logRetentionSettings?.defaultRetentionActiveDays;
    if (inherited == null) {
      return '日志保留：继承默认值';
    }
    return '日志保留：继承 $inherited 个活动日';
  }

  Future<void> addMachine(MachineDraft draft) async {
    await _runAction(() => api.addMachine(draft));
    await loadMachines();
  }

  Future<void> setMachineApproval(String machineId, bool approved) async {
    await _runAction(() => api.setMachineApproval(machineId, approved));
    await loadMachines();
  }

  Future<void> setMachineProtection(
    String machineId,
    bool protectedState,
  ) async {
    await _runAction(() => api.setMachineProtection(machineId, protectedState));
    await loadMachines();
  }

  Future<void> resetMachineRegistration(String machineId) async {
    await _runAction(() => api.resetMachineRegistration(machineId));
    await loadMachines();
  }

  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    await _runAction(() => api.setMachineVhd(machineId, vhdKeyword));
    await loadMachines();
  }

  Future<void> setMachineEvhdPassword(
    String machineId,
    String evhdPassword,
  ) async {
    await _runAction(() => api.setMachineEvhdPassword(machineId, evhdPassword));
    await loadMachines();
  }

  Future<String> readPlainEvhdPassword(String machineId, String reason) async {
    return _runAction(() => api.getPlainEvhdPassword(machineId, reason));
  }

  Future<void> deleteMachine(String machineId) async {
    await _runAction(() => api.deleteMachine(machineId));
    await loadMachines();
  }

  Future<void> addTrustedCertificate(String name, String certificatePem) async {
    await _runAction(() => api.addTrustedCertificate(name, certificatePem));
    await loadCertificates();
  }

  Future<void> removeTrustedCertificate(String fingerprint) async {
    await _runAction(() => api.removeTrustedCertificate(fingerprint));
    await loadCertificates();
  }

  Future<void> updateDefaultVhd(String vhdKeyword) async {
    await _runAction(() => api.updateDefaultVhd(vhdKeyword));
    serverStatus = await api.getServerStatus();
    notifyListeners();
  }

  Future<void> changePassword(
    String currentPassword,
    String newPassword,
  ) async {
    await _runAction(() => api.changePassword(currentPassword, newPassword));
    otpRotationPreparation = null;
    notifyListeners();
  }

  Future<void> loadDeploymentPackages() async {
    deploymentPackages = await _runAction(api.getDeploymentPackages);
    notifyListeners();
  }

  Future<void> uploadDeploymentPackage({
    required String name,
    required String version,
    required String type,
    required String signer,
    required List<int> packageBytes,
    required String packageFileName,
    required List<int> signatureBytes,
    required String signatureFileName,
  }) async {
    await _runAction(() => api.uploadDeploymentPackage(
      name: name,
      version: version,
      type: type,
      signer: signer,
      packageBytes: packageBytes,
      packageFileName: packageFileName,
      signatureBytes: signatureBytes,
      signatureFileName: signatureFileName,
    ));
    await loadDeploymentPackages();
  }

  Future<void> deleteDeploymentPackage(String packageId) async {
    await _runAction(() => api.deleteDeploymentPackage(packageId));
    await loadDeploymentPackages();
  }

  Future<void> loadDeploymentTasks({
    String? machineId,
    String? status,
  }) async {
    deploymentTasks = await _runAction(
      () => api.getDeploymentTasks(machineId: machineId, status: status),
    );
    notifyListeners();
  }

  Future<void> createDeploymentTask(
    String packageId,
    List<String> targetMachineIds, {
    String? scheduledAt,
  }) async {
    await _runAction(
      () => api.createDeploymentTask(packageId, targetMachineIds, scheduledAt: scheduledAt),
    );
    await loadDeploymentTasks();
  }

  Future<void> loadMachineDeploymentHistory(String machineId) async {
    deploymentSelectedMachineId = machineId.trim().isEmpty ? null : machineId.trim();
    deploymentRecords = await _runAction(
      () => api.getMachineDeploymentHistory(machineId),
    );
    notifyListeners();
  }

  Future<void> triggerUninstall(String machineId, String recordId) async {
    await _runAction(() => api.triggerUninstall(machineId, recordId));
    await loadMachineDeploymentHistory(machineId);
  }

  @override
  void dispose() {
    _otpExpiryTimer?.cancel();
    super.dispose();
  }
}