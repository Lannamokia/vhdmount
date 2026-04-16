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
  List<String> rememberedBaseUrls = <String>[];
  String? auditFilterMachineId;

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
      serverStatus = await api.getServerStatus();
      final authStatus = await api.getAuthStatus();
      isAuthenticated = authStatus.isAuthenticated;

      if (isAuthenticated) {
        _applyOtpStatus(await api.getOtpStatus(), notify: false);
        machines = await api.getMachines();
        auditEntries = await api.getAuditEntries(
          machineId: auditFilterMachineId,
        );
      } else {
        _clearOtpVerification(notify: false);
        otpRotationPreparation = null;
        machines = <MachineRecord>[];
        certificates = <TrustedCertificateRecord>[];
        auditEntries = <AuditEntry>[];
      }
    } catch (error) {
      serverStatus = null;
      isAuthenticated = false;
      initializationPreparation = null;
      otpRotationPreparation = null;
      machines = <MachineRecord>[];
      certificates = <TrustedCertificateRecord>[];
      auditEntries = <AuditEntry>[];
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

  @override
  void dispose() {
    _otpExpiryTimer?.cancel();
    super.dispose();
  }
}