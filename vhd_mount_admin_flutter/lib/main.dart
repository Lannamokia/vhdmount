import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter/material.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(
    AdminApp(
      controller: AppController(api: HttpAdminApi()),
    ),
  );
}

String describeError(Object error) {
  if (error is AdminApiException) {
    return error.message;
  }
  return error.toString();
}

String generateSessionSecret([int length = 48]) {
  const alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#\$%^&*()-_=+';
  final random = Random.secure();
  return List.generate(length, (_) => alphabet[random.nextInt(alphabet.length)]).join();
}

class AdminApiException implements Exception {
  AdminApiException(
    this.message, {
    this.statusCode,
    this.requireAuth = false,
    this.requireOtp = false,
    this.initializeRequired = false,
  });

  final String message;
  final int? statusCode;
  final bool requireAuth;
  final bool requireOtp;
  final bool initializeRequired;

  @override
  String toString() => message;
}

class ServerStatus {
  const ServerStatus({
    required this.initialized,
    required this.pendingInitialization,
    required this.databaseReady,
    required this.defaultVhdKeyword,
    required this.trustedRegistrationCertificateCount,
  });

  final bool initialized;
  final bool pendingInitialization;
  final bool databaseReady;
  final String defaultVhdKeyword;
  final int trustedRegistrationCertificateCount;

  factory ServerStatus.fromJson(Map<String, dynamic> json) {
    return ServerStatus(
      initialized: json['initialized'] == true,
      pendingInitialization: json['pendingInitialization'] == true,
      databaseReady: json['databaseReady'] == true,
      defaultVhdKeyword: (json['defaultVhdKeyword'] as String?) ?? 'SDEZ',
      trustedRegistrationCertificateCount:
          (json['trustedRegistrationCertificateCount'] as num?)?.toInt() ?? 0,
    );
  }
}

class AuthStatus {
  const AuthStatus({
    required this.initialized,
    required this.isAuthenticated,
    required this.otpVerified,
  });

  final bool initialized;
  final bool isAuthenticated;
  final bool otpVerified;

  factory AuthStatus.fromJson(Map<String, dynamic> json) {
    return AuthStatus(
      initialized: json['initialized'] == true,
      isAuthenticated: json['isAuthenticated'] == true,
      otpVerified: json['otpVerified'] == true,
    );
  }
}

class InitializationPreparation {
  const InitializationPreparation({
    required this.issuer,
    required this.accountName,
    required this.totpSecret,
    required this.otpauthUrl,
  });

  final String issuer;
  final String accountName;
  final String totpSecret;
  final String otpauthUrl;

  factory InitializationPreparation.fromJson(Map<String, dynamic> json) {
    return InitializationPreparation(
      issuer: (json['issuer'] as String?) ?? 'VHDMountServer',
      accountName: (json['accountName'] as String?) ?? 'admin',
      totpSecret: (json['totpSecret'] as String?) ?? '',
      otpauthUrl: (json['otpauthUrl'] as String?) ?? '',
    );
  }
}

class OtpStatus {
  const OtpStatus({
    required this.verified,
    required this.verifiedUntil,
  });

  final bool verified;
  final int verifiedUntil;

  factory OtpStatus.fromJson(Map<String, dynamic> json) {
    final verifiedUntil = (json['otpVerifiedUntil'] as num?)?.toInt() ?? 0;
    return OtpStatus(
      verified: json['otpVerified'] == true || verifiedUntil > DateTime.now().millisecondsSinceEpoch,
      verifiedUntil: verifiedUntil,
    );
  }
}

class MachineRecord {
  const MachineRecord({
    required this.machineId,
    required this.protectedState,
    required this.vhdKeyword,
    required this.evhdPasswordConfigured,
    required this.approved,
    required this.revoked,
    required this.keyId,
    required this.keyType,
    required this.registrationCertFingerprint,
    required this.lastSeen,
  });

  final String machineId;
  final bool protectedState;
  final String vhdKeyword;
  final bool evhdPasswordConfigured;
  final bool approved;
  final bool revoked;
  final String? keyId;
  final String? keyType;
  final String? registrationCertFingerprint;
  final String? lastSeen;

  factory MachineRecord.fromJson(Map<String, dynamic> json) {
    return MachineRecord(
      machineId: (json['machine_id'] as String?) ?? '',
      protectedState: json['protected'] == true,
      vhdKeyword: (json['vhd_keyword'] as String?) ?? 'SDEZ',
      evhdPasswordConfigured: json['evhd_password_configured'] == true,
      approved: json['approved'] == true,
      revoked: json['revoked'] == true,
      keyId: json['key_id'] as String?,
      keyType: json['key_type'] as String?,
      registrationCertFingerprint: json['registration_cert_fingerprint'] as String?,
      lastSeen: json['last_seen'] as String?,
    );
  }
}

class MachineDraft {
  const MachineDraft({
    required this.machineId,
    required this.vhdKeyword,
    required this.protectedState,
    this.evhdPassword,
  });

  final String machineId;
  final String vhdKeyword;
  final bool protectedState;
  final String? evhdPassword;
}

class TrustedCertificateRecord {
  const TrustedCertificateRecord({
    required this.name,
    required this.fingerprint256,
    required this.subject,
    required this.validFrom,
    required this.validTo,
    required this.certificatePem,
  });

  final String name;
  final String fingerprint256;
  final String subject;
  final String validFrom;
  final String validTo;
  final String certificatePem;

  factory TrustedCertificateRecord.fromJson(Map<String, dynamic> json) {
    return TrustedCertificateRecord(
      name: (json['name'] as String?) ?? 'unnamed-certificate',
      fingerprint256: (json['fingerprint256'] as String?) ?? '',
      subject: (json['subject'] as String?) ?? '',
      validFrom: (json['validFrom'] as String?) ?? '',
      validTo: (json['validTo'] as String?) ?? '',
      certificatePem: (json['certificatePem'] as String?) ?? '',
    );
  }
}

class AuditEntry {
  const AuditEntry({
    required this.timestamp,
    required this.type,
    required this.actor,
    required this.result,
    required this.path,
    required this.ip,
  });

  final String timestamp;
  final String type;
  final String actor;
  final String result;
  final String path;
  final String ip;

  factory AuditEntry.fromJson(Map<String, dynamic> json) {
    return AuditEntry(
      timestamp: (json['timestamp'] as String?) ?? '',
      type: (json['type'] as String?) ?? '',
      actor: (json['actor'] as String?) ?? '',
      result: (json['result'] as String?) ?? '',
      path: (json['path'] as String?) ?? '',
      ip: (json['ip'] as String?) ?? '',
    );
  }
}

abstract class AdminApi {
  String get baseUrl;

  void updateBaseUrl(String baseUrl);

  Future<ServerStatus> getServerStatus();

  Future<AuthStatus> getAuthStatus();

  Future<InitializationPreparation> prepareInitialization({
    required String issuer,
    required String accountName,
  });

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
  });

  Future<void> login(String password);

  Future<void> logout();

  Future<OtpStatus> verifyOtp(String code);

  Future<OtpStatus> getOtpStatus();

  Future<List<MachineRecord>> getMachines();

  Future<void> addMachine(MachineDraft draft);

  Future<void> setMachineApproval(String machineId, bool approved);

  Future<void> setMachineProtection(String machineId, bool protectedState);

  Future<void> resetMachineRegistration(String machineId);

  Future<void> setMachineVhd(String machineId, String vhdKeyword);

  Future<void> setMachineEvhdPassword(String machineId, String evhdPassword);

  Future<String> getPlainEvhdPassword(String machineId, String reason);

  Future<void> deleteMachine(String machineId);

  Future<List<TrustedCertificateRecord>> getTrustedCertificates();

  Future<void> addTrustedCertificate(String name, String certificatePem);

  Future<void> removeTrustedCertificate(String fingerprint);

  Future<List<AuditEntry>> getAuditEntries();

  Future<void> updateDefaultVhd(String vhdKeyword);

  Future<void> changePassword(String currentPassword, String newPassword);
}

class HttpAdminApi implements AdminApi {
  HttpAdminApi({String baseUrl = 'http://localhost:8080'}) : _baseUrl = baseUrl;

  final HttpClient _client = HttpClient();
  final Map<String, Cookie> _cookies = <String, Cookie>{};
  String _baseUrl;

  @override
  String get baseUrl => _baseUrl;

  @override
  void updateBaseUrl(String baseUrl) {
    _baseUrl = baseUrl.trim().replaceAll(RegExp(r'/+$'), '');
  }

  Uri _resolve(String path) {
    final normalizedBase = _baseUrl.isEmpty ? 'http://localhost:8080' : _baseUrl;
    return Uri.parse('$normalizedBase$path');
  }

  void _applyCookies(HttpClientRequest request) {
    if (_cookies.isEmpty) {
      return;
    }
    request.headers.set(
      HttpHeaders.cookieHeader,
      _cookies.values.map((cookie) => '${cookie.name}=${cookie.value}').join('; '),
    );
  }

  void _storeCookies(HttpClientResponse response) {
    for (final cookie in response.cookies) {
      _cookies[cookie.name] = cookie;
    }
  }

  Future<Map<String, dynamic>> _requestJson(
    String method,
    String path, {
    Object? body,
  }) async {
    final request = await _client.openUrl(method, _resolve(path));
    request.headers.set(HttpHeaders.acceptHeader, 'application/json');
    _applyCookies(request);

    if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(jsonEncode(body));
    }

    final response = await request.close();
    _storeCookies(response);
    final text = await response.transform(utf8.decoder).join();

    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (text.trim().isEmpty) {
        return <String, dynamic>{};
      }
      final decoded = jsonDecode(text);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
      return <String, dynamic>{'data': decoded};
    }

    Map<String, dynamic> errorJson = <String, dynamic>{};
    if (text.trim().isNotEmpty) {
      try {
        final decoded = jsonDecode(text);
        if (decoded is Map<String, dynamic>) {
          errorJson = decoded;
        }
      } catch (_) {
        errorJson = <String, dynamic>{'error': text};
      }
    }

    throw AdminApiException(
      (errorJson['error'] as String?) ??
          (errorJson['message'] as String?) ??
          '请求失败: ${response.statusCode}',
      statusCode: response.statusCode,
      requireAuth: errorJson['requireAuth'] == true,
      requireOtp: errorJson['requireOtp'] == true,
      initializeRequired: errorJson['initializeRequired'] == true,
    );
  }

  @override
  Future<ServerStatus> getServerStatus() async {
    final json = await _requestJson('GET', '/api/init/status');
    return ServerStatus.fromJson(json);
  }

  @override
  Future<AuthStatus> getAuthStatus() async {
    final json = await _requestJson('GET', '/api/auth/check');
    return AuthStatus.fromJson(json);
  }

  @override
  Future<InitializationPreparation> prepareInitialization({
    required String issuer,
    required String accountName,
  }) async {
    final json = await _requestJson(
      'POST',
      '/api/init/prepare',
      body: <String, dynamic>{
        'issuer': issuer,
        'accountName': accountName,
      },
    );
    return InitializationPreparation.fromJson(json);
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
    List<Map<String, String>> trustedCertificates = const <Map<String, String>>[],
  }) async {
    await _requestJson(
      'POST',
      '/api/init/complete',
      body: <String, dynamic>{
        'adminPassword': adminPassword,
        'sessionSecret': sessionSecret,
        'totpCode': totpCode,
        'defaultVhdKeyword': defaultVhdKeyword,
        'dbConfig': <String, dynamic>{
          'host': dbHost,
          'port': dbPort,
          'database': dbName,
          'user': dbUser,
          'password': dbPassword,
        },
        'trustedRegistrationCertificates': trustedCertificates,
      },
    );
  }

  @override
  Future<void> login(String password) async {
    await _requestJson(
      'POST',
      '/api/auth/login',
      body: <String, dynamic>{'password': password},
    );
  }

  @override
  Future<void> logout() async {
    await _requestJson('POST', '/api/auth/logout');
  }

  @override
  Future<OtpStatus> verifyOtp(String code) async {
    final json = await _requestJson(
      'POST',
      '/api/auth/otp/verify',
      body: <String, dynamic>{'code': code},
    );
    return OtpStatus.fromJson(json);
  }

  @override
  Future<OtpStatus> getOtpStatus() async {
    final json = await _requestJson('GET', '/api/auth/otp/status');
    return OtpStatus.fromJson(json);
  }

  @override
  Future<List<MachineRecord>> getMachines() async {
    final json = await _requestJson('GET', '/api/machines');
    final rows = (json['machines'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(MachineRecord.fromJson)
        .toList();
    return rows;
  }

  @override
  Future<void> addMachine(MachineDraft draft) async {
    await _requestJson(
      'POST',
      '/api/machines',
      body: <String, dynamic>{
        'machineId': draft.machineId,
        'vhdKeyword': draft.vhdKeyword,
        'protected': draft.protectedState,
        if (draft.evhdPassword != null && draft.evhdPassword!.isNotEmpty)
          'evhdPassword': draft.evhdPassword,
      },
    );
  }

  @override
  Future<void> setMachineApproval(String machineId, bool approved) async {
    await _requestJson(
      'POST',
      '/api/machines/$machineId/approve',
      body: <String, dynamic>{'approved': approved},
    );
  }

  @override
  Future<void> setMachineProtection(String machineId, bool protectedState) async {
    await _requestJson(
      'POST',
      '/api/protect',
      body: <String, dynamic>{
        'machineId': machineId,
        'protected': protectedState,
      },
    );
  }

  @override
  Future<void> resetMachineRegistration(String machineId) async {
    await _requestJson('POST', '/api/machines/$machineId/revoke');
  }

  @override
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    await _requestJson(
      'POST',
      '/api/machines/$machineId/vhd',
      body: <String, dynamic>{'vhdKeyword': vhdKeyword},
    );
  }

  @override
  Future<void> setMachineEvhdPassword(String machineId, String evhdPassword) async {
    await _requestJson(
      'POST',
      '/api/machines/$machineId/evhd-password',
      body: <String, dynamic>{'evhdPassword': evhdPassword},
    );
  }

  @override
  Future<String> getPlainEvhdPassword(String machineId, String reason) async {
    final encodedMachineId = Uri.encodeQueryComponent(machineId);
    final encodedReason = Uri.encodeQueryComponent(reason);
    final json = await _requestJson(
      'GET',
      '/api/evhd-password/plain?machineId=$encodedMachineId&reason=$encodedReason',
    );
    return (json['evhdPassword'] as String?) ?? '';
  }

  @override
  Future<void> deleteMachine(String machineId) async {
    await _requestJson('DELETE', '/api/machines/$machineId');
  }

  @override
  Future<List<TrustedCertificateRecord>> getTrustedCertificates() async {
    final json = await _requestJson('GET', '/api/security/trusted-certificates');
    return (json['certificates'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(TrustedCertificateRecord.fromJson)
        .toList();
  }

  @override
  Future<void> addTrustedCertificate(String name, String certificatePem) async {
    await _requestJson(
      'POST',
      '/api/security/trusted-certificates',
      body: <String, dynamic>{
        'name': name,
        'certificatePem': certificatePem,
      },
    );
  }

  @override
  Future<void> removeTrustedCertificate(String fingerprint) async {
    await _requestJson('DELETE', '/api/security/trusted-certificates/$fingerprint');
  }

  @override
  Future<List<AuditEntry>> getAuditEntries() async {
    final json = await _requestJson('GET', '/api/audit?limit=100');
    return (json['entries'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(AuditEntry.fromJson)
        .toList();
  }

  @override
  Future<void> updateDefaultVhd(String vhdKeyword) async {
    await _requestJson(
      'POST',
      '/api/settings/default-vhd',
      body: <String, dynamic>{'vhdKeyword': vhdKeyword},
    );
  }

  @override
  Future<void> changePassword(String currentPassword, String newPassword) async {
    await _requestJson(
      'POST',
      '/api/auth/change-password',
      body: <String, dynamic>{
        'currentPassword': currentPassword,
        'newPassword': newPassword,
        'confirmPassword': newPassword,
      },
    );
  }
}

class AppController extends ChangeNotifier {
  AppController({required this.api});

  final AdminApi api;

  bool isLoading = true;
  bool isWorking = false;
  String? errorMessage;
  ServerStatus? serverStatus;
  bool isAuthenticated = false;
  bool otpVerified = false;
  InitializationPreparation? initializationPreparation;
  List<MachineRecord> machines = <MachineRecord>[];
  List<TrustedCertificateRecord> certificates = <TrustedCertificateRecord>[];
  List<AuditEntry> auditEntries = <AuditEntry>[];

  String get baseUrl => api.baseUrl;

  void updateBaseUrl(String value) {
    api.updateBaseUrl(value);
    notifyListeners();
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
      serverStatus = await api.getServerStatus();
      final authStatus = await api.getAuthStatus();
      isAuthenticated = authStatus.isAuthenticated;
      otpVerified = authStatus.otpVerified;

      if (isAuthenticated) {
        machines = await api.getMachines();
        auditEntries = await api.getAuditEntries();
      } else {
        machines = <MachineRecord>[];
        certificates = <TrustedCertificateRecord>[];
        auditEntries = <AuditEntry>[];
      }
    } catch (error) {
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
    List<Map<String, String>> trustedCertificates = const <Map<String, String>>[],
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
    await bootstrap();
  }

  Future<void> login(String password) async {
    await _runAction(() => api.login(password));
    await bootstrap();
  }

  Future<void> logout() async {
    await _runAction(api.logout);
    await bootstrap();
  }

  Future<void> verifyOtp(String code) async {
    final otpStatus = await _runAction(() => api.verifyOtp(code));
    otpVerified = otpStatus.verified;
    notifyListeners();

    if (!otpVerified) {
      return;
    }

    try {
      await loadCertificates();
    } catch (_) {
      // OTP 已成功，只把证书刷新失败作为界面错误保留，不中断成功状态。
    }
  }

  Future<void> refreshOtpStatus() async {
    try {
      final otpStatus = await api.getOtpStatus();
      otpVerified = otpStatus.verified;
      if (!otpVerified) {
        certificates = <TrustedCertificateRecord>[];
      }
      notifyListeners();
    } catch (_) {
      otpVerified = false;
      certificates = <TrustedCertificateRecord>[];
      notifyListeners();
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

  Future<void> loadAudit() async {
    auditEntries = await _runAction(api.getAuditEntries);
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

  Future<void> setMachineProtection(String machineId, bool protectedState) async {
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

  Future<void> setMachineEvhdPassword(String machineId, String evhdPassword) async {
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

  Future<void> changePassword(String currentPassword, String newPassword) async {
    await _runAction(() => api.changePassword(currentPassword, newPassword));
  }
}

class AdminApp extends StatelessWidget {
  const AdminApp({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'VHD Mount Admin',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF165A4A),
          brightness: Brightness.light,
        ),
        scaffoldBackgroundColor: const Color(0xFFF5F1E8),
        fontFamily: 'Verdana',
        appBarTheme: const AppBarTheme(
          backgroundColor: Color(0xFF16322B),
          foregroundColor: Colors.white,
        ),
        cardTheme: CardThemeData(
          color: Colors.white,
          elevation: 0,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(18),
          ),
          surfaceTintColor: Colors.white,
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: Colors.white,
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(14),
            borderSide: BorderSide.none,
          ),
        ),
      ),
      home: AdminRoot(controller: controller),
    );
  }
}

class AdminRoot extends StatefulWidget {
  const AdminRoot({super.key, required this.controller});

  final AppController controller;

  @override
  State<AdminRoot> createState() => _AdminRootState();
}

class _AdminRootState extends State<AdminRoot> {
  int _selectedIndex = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      widget.controller.bootstrap();
    });
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.controller,
      builder: (context, _) {
        final controller = widget.controller;

        Widget content;
        if (controller.isLoading) {
          content = const SplashScreen();
        } else if (controller.serverStatus == null) {
          content = ConnectionScreen(controller: controller);
        } else if (!controller.serverStatus!.initialized) {
          content = InitializationScreen(controller: controller);
        } else if (!controller.isAuthenticated) {
          content = LoginScreen(controller: controller);
        } else {
          content = DashboardScreen(
            controller: controller,
            selectedIndex: _selectedIndex,
            onDestinationSelected: (index) async {
              setState(() {
                _selectedIndex = index;
              });
              if (index == 0) {
                await controller.loadMachines();
              } else if (index == 1 && controller.otpVerified) {
                await controller.loadCertificates();
              } else if (index == 2) {
                await controller.loadAudit();
              }
            },
          );
        }

        return Stack(
          children: <Widget>[
            content,
            if (controller.isWorking)
              Positioned.fill(
                child: ColoredBox(
                  color: Colors.black.withValues(alpha: 0.12),
                  child: const Center(child: CircularProgressIndicator()),
                ),
              ),
          ],
        );
      },
    );
  }
}

class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: <Widget>[
            Icon(Icons.security_rounded, size: 72, color: Color(0xFF165A4A)),
            SizedBox(height: 16),
            Text('VHD Mount Admin'),
            SizedBox(height: 12),
            CircularProgressIndicator(),
          ],
        ),
      ),
    );
  }
}

class ConnectionScreen extends StatefulWidget {
  const ConnectionScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<ConnectionScreen> createState() => _ConnectionScreenState();
}

class _ConnectionScreenState extends State<ConnectionScreen> {
  late final TextEditingController _baseUrlController;

  @override
  void initState() {
    super.initState();
    _baseUrlController = TextEditingController(text: widget.controller.baseUrl);
  }

  @override
  void dispose() {
    _baseUrlController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 540),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: SectionPanel(
              title: '连接服务器',
              subtitle: '旧 Web 管理页已经下线，这里是新的 Flutter 管理入口。',
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  if (widget.controller.errorMessage != null)
                    ErrorBanner(message: widget.controller.errorMessage!),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _baseUrlController,
                    decoration: const InputDecoration(labelText: '服务器地址'),
                  ),
                  const SizedBox(height: 16),
                  FilledButton.icon(
                    onPressed: () async {
                      widget.controller.updateBaseUrl(_baseUrlController.text);
                      await widget.controller.bootstrap();
                    },
                    icon: const Icon(Icons.sync_rounded),
                    label: const Text('重新检查服务状态'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class InitializationScreen extends StatefulWidget {
  const InitializationScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<InitializationScreen> createState() => _InitializationScreenState();
}

class _InitializationScreenState extends State<InitializationScreen> {
  late final TextEditingController _baseUrlController;
  late final TextEditingController _issuerController;
  late final TextEditingController _accountNameController;
  late final TextEditingController _adminPasswordController;
  late final TextEditingController _confirmPasswordController;
  late final TextEditingController _sessionSecretController;
  late final TextEditingController _dbHostController;
  late final TextEditingController _dbPortController;
  late final TextEditingController _dbNameController;
  late final TextEditingController _dbUserController;
  late final TextEditingController _dbPasswordController;
  late final TextEditingController _defaultVhdController;
  late final TextEditingController _trustedCertificateNameController;
  late final TextEditingController _trustedCertificatePemController;
  late final TextEditingController _totpCodeController;

  @override
  void initState() {
    super.initState();
    _baseUrlController = TextEditingController(text: widget.controller.baseUrl);
    _issuerController = TextEditingController(text: 'VHDMountServer');
    _accountNameController = TextEditingController(text: 'admin');
    _adminPasswordController = TextEditingController();
    _confirmPasswordController = TextEditingController();
    _sessionSecretController = TextEditingController(text: generateSessionSecret());
    _dbHostController = TextEditingController(text: 'localhost');
    _dbPortController = TextEditingController(text: '5432');
    _dbNameController = TextEditingController(text: 'vhd_select');
    _dbUserController = TextEditingController(text: 'postgres');
    _dbPasswordController = TextEditingController();
    _defaultVhdController = TextEditingController(text: 'SDEZ');
    _trustedCertificateNameController = TextEditingController(text: 'machine-registration');
    _trustedCertificatePemController = TextEditingController();
    _totpCodeController = TextEditingController();
  }

  @override
  void dispose() {
    _baseUrlController.dispose();
    _issuerController.dispose();
    _accountNameController.dispose();
    _adminPasswordController.dispose();
    _confirmPasswordController.dispose();
    _sessionSecretController.dispose();
    _dbHostController.dispose();
    _dbPortController.dispose();
    _dbNameController.dispose();
    _dbUserController.dispose();
    _dbPasswordController.dispose();
    _defaultVhdController.dispose();
    _trustedCertificateNameController.dispose();
    _trustedCertificatePemController.dispose();
    _totpCodeController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final preparation = widget.controller.initializationPreparation;
    return Scaffold(
      appBar: AppBar(title: const Text('服务未初始化')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 860),
            child: SectionPanel(
              title: '初始化向导',
              subtitle: '配置管理员口令、Session Secret、数据库连接和可信注册证书。',
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  if (widget.controller.errorMessage != null)
                    ErrorBanner(message: widget.controller.errorMessage!),
                  _buildFieldRow(
                    child: TextField(
                      controller: _baseUrlController,
                      decoration: const InputDecoration(labelText: '服务器地址'),
                    ),
                  ),
                  _buildFieldRow(
                    left: TextField(
                      controller: _issuerController,
                      decoration: const InputDecoration(labelText: 'OTP issuer'),
                    ),
                    right: TextField(
                      controller: _accountNameController,
                      decoration: const InputDecoration(labelText: 'OTP account'),
                    ),
                  ),
                  _buildFieldRow(
                    left: TextField(
                      controller: _adminPasswordController,
                      obscureText: true,
                      decoration: const InputDecoration(labelText: '管理员密码'),
                    ),
                    right: TextField(
                      controller: _confirmPasswordController,
                      obscureText: true,
                      decoration: const InputDecoration(labelText: '确认管理员密码'),
                    ),
                  ),
                  _buildFieldRow(
                    child: TextField(
                      controller: _sessionSecretController,
                      decoration: InputDecoration(
                        labelText: 'Session Secret',
                        suffixIcon: IconButton(
                          onPressed: () {
                            _sessionSecretController.text = generateSessionSecret();
                          },
                          icon: const Icon(Icons.casino_rounded),
                        ),
                      ),
                    ),
                  ),
                  _buildFieldRow(
                    left: TextField(
                      controller: _dbHostController,
                      decoration: const InputDecoration(labelText: 'DB Host'),
                    ),
                    right: TextField(
                      controller: _dbPortController,
                      decoration: const InputDecoration(labelText: 'DB Port'),
                    ),
                  ),
                  _buildFieldRow(
                    left: TextField(
                      controller: _dbNameController,
                      decoration: const InputDecoration(labelText: 'DB Name'),
                    ),
                    right: TextField(
                      controller: _dbUserController,
                      decoration: const InputDecoration(labelText: 'DB User'),
                    ),
                  ),
                  _buildFieldRow(
                    left: TextField(
                      controller: _dbPasswordController,
                      obscureText: true,
                      decoration: const InputDecoration(labelText: 'DB Password'),
                    ),
                    right: TextField(
                      controller: _defaultVhdController,
                      decoration: const InputDecoration(labelText: '默认 VHD 关键词'),
                    ),
                  ),
                  _buildFieldRow(
                    left: TextField(
                      controller: _trustedCertificateNameController,
                      decoration: const InputDecoration(labelText: '可信注册证书名称'),
                    ),
                    right: TextField(
                      controller: _totpCodeController,
                      decoration: const InputDecoration(labelText: 'TOTP 验证码'),
                    ),
                  ),
                  TextField(
                    controller: _trustedCertificatePemController,
                    minLines: 8,
                    maxLines: 12,
                    decoration: const InputDecoration(
                      labelText: '初始可信注册证书 PEM（可选）',
                      alignLabelWithHint: true,
                    ),
                  ),
                  const SizedBox(height: 16),
                  Wrap(
                    spacing: 12,
                    runSpacing: 12,
                    children: <Widget>[
                      FilledButton.icon(
                        onPressed: () async {
                          widget.controller.updateBaseUrl(_baseUrlController.text);
                          try {
                            await widget.controller.prepareInitialization(
                              issuer: _issuerController.text.trim(),
                              accountName: _accountNameController.text.trim(),
                            );
                            if (!context.mounted) {
                              return;
                            }
                            ScaffoldMessenger.of(context).showSnackBar(
                              const SnackBar(content: Text('OTP 准备完成，请在验证器中导入密钥后填写验证码。')),
                            );
                          } catch (error) {
                            if (!context.mounted) {
                              return;
                            }
                            ScaffoldMessenger.of(context).showSnackBar(
                              SnackBar(content: Text(describeError(error))),
                            );
                          }
                        },
                        icon: const Icon(Icons.qr_code_2_rounded),
                        label: const Text('准备 OTP'),
                      ),
                      OutlinedButton.icon(
                        onPressed: () async {
                          widget.controller.updateBaseUrl(_baseUrlController.text);
                          await widget.controller.bootstrap();
                        },
                        icon: const Icon(Icons.sync_rounded),
                        label: const Text('刷新状态'),
                      ),
                    ],
                  ),
                  if (preparation != null) ...<Widget>[
                    const SizedBox(height: 18),
                    InfoPanel(
                      title: 'OTP 导入信息',
                      body: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          SelectableText('Issuer: ${preparation.issuer}'),
                          const SizedBox(height: 6),
                          SelectableText('Account: ${preparation.accountName}'),
                          const SizedBox(height: 6),
                          SelectableText('Secret: ${preparation.totpSecret}'),
                          const SizedBox(height: 6),
                          SelectableText('otpauth:// ${preparation.otpauthUrl}'),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: 18),
                  FilledButton.tonalIcon(
                    onPressed: () async {
                      if (_adminPasswordController.text != _confirmPasswordController.text) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('管理员密码与确认密码不一致。')),
                        );
                        return;
                      }

                      final trustedCertificates = <Map<String, String>>[];
                      if (_trustedCertificatePemController.text.trim().isNotEmpty) {
                        trustedCertificates.add(<String, String>{
                          'name': _trustedCertificateNameController.text.trim().isEmpty
                              ? 'machine-registration'
                              : _trustedCertificateNameController.text.trim(),
                          'certificatePem': _trustedCertificatePemController.text.trim(),
                        });
                      }

                      widget.controller.updateBaseUrl(_baseUrlController.text);
                      try {
                        await widget.controller.completeInitialization(
                          adminPassword: _adminPasswordController.text,
                          sessionSecret: _sessionSecretController.text,
                          totpCode: _totpCodeController.text.trim(),
                          defaultVhdKeyword: _defaultVhdController.text.trim(),
                          dbHost: _dbHostController.text.trim(),
                          dbPort: int.tryParse(_dbPortController.text.trim()) ?? 5432,
                          dbName: _dbNameController.text.trim(),
                          dbUser: _dbUserController.text.trim(),
                          dbPassword: _dbPasswordController.text,
                          trustedCertificates: trustedCertificates,
                        );
                        if (!context.mounted) {
                          return;
                        }
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('初始化完成，请使用管理员密码登录。')),
                        );
                      } catch (error) {
                        if (!context.mounted) {
                          return;
                        }
                        ScaffoldMessenger.of(context).showSnackBar(
                          SnackBar(content: Text(describeError(error))),
                        );
                      }
                    },
                    icon: const Icon(Icons.lock_open_rounded),
                    label: const Text('完成初始化'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildFieldRow({Widget? left, Widget? right, Widget? child}) {
    if (child != null) {
      return Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: child,
      );
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Row(
        children: <Widget>[
          Expanded(child: left ?? const SizedBox.shrink()),
          const SizedBox(width: 12),
          Expanded(child: right ?? const SizedBox.shrink()),
        ],
      ),
    );
  }
}

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  late final TextEditingController _baseUrlController;
  late final TextEditingController _passwordController;

  @override
  void initState() {
    super.initState();
    _baseUrlController = TextEditingController(text: widget.controller.baseUrl);
    _passwordController = TextEditingController();
  }

  @override
  void dispose() {
    _baseUrlController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final serverStatus = widget.controller.serverStatus;
    return Scaffold(
      appBar: AppBar(title: const Text('管理员登录')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: SectionPanel(
              title: '登录',
              subtitle: '当前服务已初始化，管理操作通过 Session + OTP 二次验证保护。',
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  if (widget.controller.errorMessage != null)
                    ErrorBanner(message: widget.controller.errorMessage!),
                  if (serverStatus != null)
                    InfoPanel(
                      title: '服务状态',
                      body: Text(
                        '数据库: ${serverStatus.databaseReady ? '已连接' : '未连接'} | 默认 VHD: ${serverStatus.defaultVhdKeyword} | 可信证书: ${serverStatus.trustedRegistrationCertificateCount}',
                      ),
                    ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _baseUrlController,
                    decoration: const InputDecoration(labelText: '服务器地址'),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _passwordController,
                    obscureText: true,
                    decoration: const InputDecoration(labelText: '管理员密码'),
                  ),
                  const SizedBox(height: 16),
                  Wrap(
                    spacing: 12,
                    runSpacing: 12,
                    children: <Widget>[
                      FilledButton.icon(
                        onPressed: () async {
                          widget.controller.updateBaseUrl(_baseUrlController.text);
                          try {
                            await widget.controller.login(_passwordController.text);
                          } catch (error) {
                            if (!context.mounted) {
                              return;
                            }
                            ScaffoldMessenger.of(context).showSnackBar(
                              SnackBar(content: Text(describeError(error))),
                            );
                          }
                        },
                        icon: const Icon(Icons.login_rounded),
                        label: const Text('登录'),
                      ),
                      OutlinedButton.icon(
                        onPressed: () async {
                          widget.controller.updateBaseUrl(_baseUrlController.text);
                          await widget.controller.bootstrap();
                        },
                        icon: const Icon(Icons.sync_rounded),
                        label: const Text('刷新状态'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class DashboardScreen extends StatelessWidget {
  const DashboardScreen({
    super.key,
    required this.controller,
    required this.selectedIndex,
    required this.onDestinationSelected,
  });

  final AppController controller;
  final int selectedIndex;
  final ValueChanged<int> onDestinationSelected;

  @override
  Widget build(BuildContext context) {
    final pages = <Widget>[
      MachinesView(controller: controller),
      CertificatesView(controller: controller),
      AuditView(controller: controller),
      SettingsView(controller: controller),
    ];

    return Scaffold(
      appBar: AppBar(
        title: const Text('VHD Mount Admin'),
        actions: <Widget>[
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: Center(
              child: StatusChip(
                label: controller.otpVerified ? 'OTP 已验证' : 'OTP 未验证',
                color: controller.otpVerified ? const Color(0xFF24785F) : const Color(0xFF8A6F2A),
              ),
            ),
          ),
          IconButton(
            tooltip: '验证 OTP',
            onPressed: () async {
              final code = await showSingleInputDialog(
                context,
                title: 'OTP 二次验证',
                label: '验证码',
                obscureText: false,
              );
              if (code == null || code.trim().isEmpty) {
                return;
              }
              try {
                await controller.verifyOtp(code.trim());
                if (!context.mounted) {
                  return;
                }
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(content: Text('OTP 验证成功。')),
                );
              } catch (error) {
                if (!context.mounted) {
                  return;
                }
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(content: Text(describeError(error))),
                );
              }
            },
            icon: const Icon(Icons.key_rounded),
          ),
          IconButton(
            tooltip: '刷新',
            onPressed: () async {
              await controller.bootstrap();
            },
            icon: const Icon(Icons.sync_rounded),
          ),
          IconButton(
            tooltip: '登出',
            onPressed: () async {
              await controller.logout();
            },
            icon: const Icon(Icons.logout_rounded),
          ),
        ],
      ),
      body: Row(
        children: <Widget>[
          NavigationRail(
            selectedIndex: selectedIndex,
            onDestinationSelected: onDestinationSelected,
            labelType: NavigationRailLabelType.all,
            destinations: const <NavigationRailDestination>[
              NavigationRailDestination(
                icon: Icon(Icons.dns_rounded),
                label: Text('机器管理'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.verified_user_rounded),
                label: Text('证书'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.history_rounded),
                label: Text('审计'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.tune_rounded),
                label: Text('设置'),
              ),
            ],
          ),
          const VerticalDivider(width: 1),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Wrap(
                    spacing: 10,
                    runSpacing: 10,
                    children: <Widget>[
                      StatusChip(
                        label: controller.serverStatus?.databaseReady == true ? '数据库已连接' : '数据库异常',
                        color: controller.serverStatus?.databaseReady == true
                            ? const Color(0xFF24785F)
                            : const Color(0xFF9D3C2D),
                      ),
                      StatusChip(
                        label: '默认 VHD: ${controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ'}',
                        color: const Color(0xFF385B72),
                      ),
                    ],
                  ),
                  if (controller.errorMessage != null) ...<Widget>[
                    const SizedBox(height: 12),
                    ErrorBanner(message: controller.errorMessage!),
                  ],
                  const SizedBox(height: 16),
                  Expanded(child: pages[selectedIndex]),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class MachinesView extends StatelessWidget {
  const MachinesView({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(
              '机器管理',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const Spacer(),
            FilledButton.icon(
              onPressed: () async {
                final draft = await showAddMachineDialog(
                  context,
                  defaultVhdKeyword: controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
                );
                if (draft == null) {
                  return;
                }
                try {
                  await controller.addMachine(draft);
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text('机台 ${draft.machineId} 已添加。')),
                  );
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(describeError(error))),
                  );
                }
              },
              icon: const Icon(Icons.add_rounded),
              label: const Text('添加机台'),
            ),
            const SizedBox(width: 8),
            OutlinedButton.icon(
              onPressed: controller.loadMachines,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新列表'),
            ),
          ],
        ),
        const SizedBox(height: 12),
        if (controller.machines.isEmpty)
          const Expanded(
            child: Center(
              child: Text('当前还没有机器记录。'),
            ),
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: controller.machines.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final machine = controller.machines[index];
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          children: <Widget>[
                            Expanded(
                              child: Text(
                                machine.machineId,
                                style: Theme.of(context).textTheme.titleLarge,
                              ),
                            ),
                            StatusChip(
                              label: machine.approved ? '已审批' : '待审批',
                              color: machine.approved
                                  ? const Color(0xFF24785F)
                                  : const Color(0xFF8A6F2A),
                            ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: <Widget>[
                            StatusChip(
                              label: 'VHD ${machine.vhdKeyword}',
                              color: const Color(0xFF385B72),
                            ),
                            StatusChip(
                              label: machine.protectedState ? '保护开启' : '保护关闭',
                              color: machine.protectedState
                                  ? const Color(0xFF7F2F2F)
                                  : const Color(0xFF4A6D4E),
                            ),
                            StatusChip(
                              label: machine.evhdPasswordConfigured ? 'EVHD 已配置' : 'EVHD 未配置',
                              color: machine.evhdPasswordConfigured
                                  ? const Color(0xFF24785F)
                                  : const Color(0xFF8A6F2A),
                            ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Text('Key ID: ${machine.keyId ?? '未注册'}'),
                        Text('最后在线: ${machine.lastSeen ?? '未知'}'),
                        if (machine.registrationCertFingerprint != null)
                          Text('注册证书: ${machine.registrationCertFingerprint}'),
                        const SizedBox(height: 16),
                        Wrap(
                          spacing: 10,
                          runSpacing: 10,
                          children: <Widget>[
                            FilledButton.tonal(
                              onPressed: () async {
                                try {
                                  await controller.setMachineApproval(machine.machineId, !machine.approved);
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(machine.approved ? '已取消审批。' : '已审批通过。')),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              child: Text(machine.approved ? '取消审批' : '审批通过'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                try {
                                  await controller.setMachineProtection(
                                    machine.machineId,
                                    !machine.protectedState,
                                  );
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(
                                        machine.protectedState ? '已关闭保护。' : '已开启保护。',
                                      ),
                                    ),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              child: Text(machine.protectedState ? '关闭保护' : '开启保护'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                try {
                                  await controller.resetMachineRegistration(machine.machineId);
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              child: const Text('重置注册'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                final value = await showSingleInputDialog(
                                  context,
                                  title: '设置 VHD 关键词',
                                  label: 'VHD 关键词',
                                  initialValue: machine.vhdKeyword,
                                );
                                if (value == null || value.trim().isEmpty) {
                                  return;
                                }
                                try {
                                  await controller.setMachineVhd(machine.machineId, value.trim().toUpperCase());
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              child: const Text('设置 VHD'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                final value = await showSingleInputDialog(
                                  context,
                                  title: '设置 EVHD 密码',
                                  label: 'EVHD 密码',
                                  obscureText: true,
                                );
                                if (value == null || value.isEmpty) {
                                  return;
                                }
                                try {
                                  await controller.setMachineEvhdPassword(machine.machineId, value);
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              child: const Text('设置 EVHD'),
                            ),
                            FilledButton(
                              onPressed: () async {
                                final reason = await showSingleInputDialog(
                                  context,
                                  title: '读取 EVHD 明文',
                                  label: '查询原因',
                                  initialValue: 'support investigation',
                                );
                                if (reason == null || reason.trim().isEmpty) {
                                  return;
                                }
                                try {
                                  final password = await controller.readPlainEvhdPassword(machine.machineId, reason.trim());
                                  if (!context.mounted) {
                                    return;
                                  }
                                  await showDialog<void>(
                                    context: context,
                                    builder: (context) => AlertDialog(
                                      title: const Text('EVHD 明文'),
                                      content: SelectableText(password),
                                      actions: <Widget>[
                                        TextButton(
                                          onPressed: () => Navigator.of(context).pop(),
                                          child: const Text('关闭'),
                                        ),
                                      ],
                                    ),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              child: const Text('读取明文'),
                            ),
                            OutlinedButton.icon(
                              onPressed: () async {
                                final confirmed = await showConfirmDialog(
                                  context,
                                  title: '删除机台',
                                  message: '确认删除 ${machine.machineId} 吗？这会移除该机台的管理记录与已保存的 EVHD 密码。',
                                  confirmLabel: '删除',
                                );
                                if (confirmed != true) {
                                  return;
                                }
                                try {
                                  await controller.deleteMachine(machine.machineId);
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text('机台 ${machine.machineId} 已删除。')),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              icon: const Icon(Icons.delete_outline_rounded),
                              label: const Text('删除机台'),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class CertificatesView extends StatelessWidget {
  const CertificatesView({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(
              '可信注册证书',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const Spacer(),
            OutlinedButton.icon(
              onPressed: controller.otpVerified
                  ? controller.loadCertificates
                  : null,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新证书'),
            ),
            const SizedBox(width: 8),
            FilledButton.icon(
              onPressed: () async {
                if (!controller.otpVerified) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('添加可信证书前请先完成 OTP 验证。')),
                  );
                  return;
                }

                final values = await showTwoFieldDialog(
                  context,
                  title: '添加可信证书',
                  firstLabel: '名称',
                  secondLabel: 'PEM 证书',
                  secondMinLines: 10,
                );
                if (values == null) {
                  return;
                }
                try {
                  await controller.addTrustedCertificate(values.first.trim(), values.last.trim());
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(describeError(error))),
                  );
                }
              },
              icon: const Icon(Icons.add_rounded),
              label: const Text('导入证书'),
            ),
          ],
        ),
        const SizedBox(height: 12),
        if (!controller.otpVerified)
          const InfoPanel(
            title: '需要 OTP',
            body: Text('证书管理属于高敏感操作。请先在右上角完成 OTP 验证，再刷新证书列表。'),
          )
        else if (controller.certificates.isEmpty)
          const Expanded(
            child: Center(child: Text('当前没有可信注册证书。')),
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: controller.certificates.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final certificate = controller.certificates[index];
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          children: <Widget>[
                            Expanded(
                              child: Text(
                                certificate.name,
                                style: Theme.of(context).textTheme.titleLarge,
                              ),
                            ),
                            IconButton(
                              onPressed: () async {
                                try {
                                  await controller.removeTrustedCertificate(certificate.fingerprint256);
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text(describeError(error))),
                                  );
                                }
                              },
                              icon: const Icon(Icons.delete_outline_rounded),
                            ),
                          ],
                        ),
                        Text('Subject: ${certificate.subject}'),
                        Text('Fingerprint: ${certificate.fingerprint256}'),
                        Text('Valid: ${certificate.validFrom} -> ${certificate.validTo}'),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class AuditView extends StatelessWidget {
  const AuditView({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(
              '审计日志',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const Spacer(),
            OutlinedButton.icon(
              onPressed: controller.loadAudit,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新审计'),
            ),
          ],
        ),
        const SizedBox(height: 12),
        if (controller.auditEntries.isEmpty)
          const Expanded(
            child: Center(child: Text('暂时没有审计记录。')),
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: controller.auditEntries.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final entry = controller.auditEntries[index];
                return Card(
                  child: ListTile(
                    title: Text(entry.type),
                    subtitle: Text('${entry.timestamp}\n${entry.actor} · ${entry.result} · ${entry.ip}\n${entry.path}'),
                    isThreeLine: true,
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class SettingsView extends StatefulWidget {
  const SettingsView({super.key, required this.controller});

  final AppController controller;

  @override
  State<SettingsView> createState() => _SettingsViewState();
}

class _SettingsViewState extends State<SettingsView> {
  late final TextEditingController _defaultVhdController;
  final TextEditingController _currentPasswordController = TextEditingController();
  final TextEditingController _newPasswordController = TextEditingController();
  final TextEditingController _confirmPasswordController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _defaultVhdController = TextEditingController(
      text: widget.controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
    );
  }

  @override
  void dispose() {
    _defaultVhdController.dispose();
    _currentPasswordController.dispose();
    _newPasswordController.dispose();
    _confirmPasswordController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final status = widget.controller.serverStatus;
    return ListView(
      children: <Widget>[
        SectionPanel(
          title: '服务设置',
          subtitle: '更新默认 VHD 关键词。',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              TextField(
                controller: _defaultVhdController,
                decoration: const InputDecoration(labelText: '默认 VHD 关键词'),
              ),
              const SizedBox(height: 12),
              FilledButton.icon(
                onPressed: () async {
                  try {
                    await widget.controller.updateDefaultVhd(_defaultVhdController.text.trim().toUpperCase());
                  } catch (error) {
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(content: Text(describeError(error))),
                    );
                  }
                },
                icon: const Icon(Icons.save_rounded),
                label: const Text('保存默认 VHD'),
              ),
              if (status != null) ...<Widget>[
                const SizedBox(height: 12),
                Text('数据库状态: ${status.databaseReady ? '正常' : '异常'}'),
                Text('可信注册证书数量: ${status.trustedRegistrationCertificateCount}'),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),
        SectionPanel(
          title: '修改管理员密码',
          subtitle: '服务端要求新密码长度至少 12 位。',
          child: Column(
            children: <Widget>[
              TextField(
                controller: _currentPasswordController,
                obscureText: true,
                decoration: const InputDecoration(labelText: '当前密码'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _newPasswordController,
                obscureText: true,
                decoration: const InputDecoration(labelText: '新密码'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _confirmPasswordController,
                obscureText: true,
                decoration: const InputDecoration(labelText: '确认新密码'),
              ),
              const SizedBox(height: 12),
              FilledButton.tonalIcon(
                onPressed: () async {
                  if (_newPasswordController.text != _confirmPasswordController.text) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('两次输入的新密码不一致。')),
                    );
                    return;
                  }
                  try {
                    await widget.controller.changePassword(
                      _currentPasswordController.text,
                      _newPasswordController.text,
                    );
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('管理员密码已更新。')),
                    );
                  } catch (error) {
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(content: Text(describeError(error))),
                    );
                  }
                },
                icon: const Icon(Icons.password_rounded),
                label: const Text('更新密码'),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class SectionPanel extends StatelessWidget {
  const SectionPanel({
    super.key,
    required this.title,
    required this.subtitle,
    required this.child,
  });

  final String title;
  final String subtitle;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Text(title, style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 6),
            Text(subtitle, style: Theme.of(context).textTheme.bodyMedium),
            const SizedBox(height: 16),
            child,
          ],
        ),
      ),
    );
  }
}

class InfoPanel extends StatelessWidget {
  const InfoPanel({super.key, required this.title, required this.body});

  final String title;
  final Widget body;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: const Color(0xFFE9F1EF),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 6),
          body,
        ],
      ),
    );
  }
}

class ErrorBanner extends StatelessWidget {
  const ErrorBanner({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: const Color(0xFFFBE5E1),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(
        children: <Widget>[
          const Icon(Icons.error_outline_rounded, color: Color(0xFF9D3C2D)),
          const SizedBox(width: 10),
          Expanded(child: Text(message)),
        ],
      ),
    );
  }
}

class StatusChip extends StatelessWidget {
  const StatusChip({super.key, required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: color,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600),
      ),
    );
  }
}

Future<String?> showSingleInputDialog(
  BuildContext context, {
  required String title,
  required String label,
  String initialValue = '',
  bool obscureText = false,
}) async {
  final controller = TextEditingController(text: initialValue);
  final result = await showDialog<String>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: TextField(
        controller: controller,
        obscureText: obscureText,
        decoration: InputDecoration(labelText: label),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(controller.text),
          child: const Text('确定'),
        ),
      ],
    ),
  );
  controller.dispose();
  return result;
}

Future<List<String>?> showTwoFieldDialog(
  BuildContext context, {
  required String title,
  required String firstLabel,
  required String secondLabel,
  int secondMinLines = 1,
}) async {
  final firstController = TextEditingController();
  final secondController = TextEditingController();
  final result = await showDialog<List<String>>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: SizedBox(
        width: 560,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            TextField(
              controller: firstController,
              decoration: InputDecoration(labelText: firstLabel),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: secondController,
              minLines: secondMinLines,
              maxLines: secondMinLines + 4,
              decoration: InputDecoration(
                labelText: secondLabel,
                alignLabelWithHint: true,
              ),
            ),
          ],
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(<String>[firstController.text, secondController.text]),
          child: const Text('确定'),
        ),
      ],
    ),
  );
  firstController.dispose();
  secondController.dispose();
  return result;
}

Future<MachineDraft?> showAddMachineDialog(
  BuildContext context, {
  required String defaultVhdKeyword,
}) async {
  final machineIdController = TextEditingController();
  final vhdController = TextEditingController(text: defaultVhdKeyword);
  final evhdPasswordController = TextEditingController();
  bool protectedState = false;

  final result = await showDialog<MachineDraft>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, setState) => AlertDialog(
        title: const Text('添加机台'),
        content: SizedBox(
          width: 560,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              TextField(
                controller: machineIdController,
                decoration: const InputDecoration(labelText: '机台 ID'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: vhdController,
                decoration: const InputDecoration(labelText: '初始 VHD 关键词'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: evhdPasswordController,
                obscureText: true,
                decoration: const InputDecoration(labelText: '初始 EVHD 密码（可选）'),
              ),
              const SizedBox(height: 12),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('创建后立即开启保护'),
                value: protectedState,
                onChanged: (value) {
                  setState(() {
                    protectedState = value;
                  });
                },
              ),
            ],
          ),
        ),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.of(context).pop(
                MachineDraft(
                  machineId: machineIdController.text.trim(),
                  vhdKeyword: vhdController.text.trim().toUpperCase(),
                  protectedState: protectedState,
                  evhdPassword: evhdPasswordController.text.isEmpty
                      ? null
                      : evhdPasswordController.text,
                ),
              );
            },
            child: const Text('创建'),
          ),
        ],
      ),
    ),
  );

  machineIdController.dispose();
  vhdController.dispose();
  evhdPasswordController.dispose();
  return result;
}

Future<bool?> showConfirmDialog(
  BuildContext context, {
  required String title,
  required String message,
  String confirmLabel = '确定',
}) {
  return showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: Text(message),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
}