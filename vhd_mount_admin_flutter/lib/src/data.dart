part of '../app.dart';

const Set<String> _auditReservedKeys = <String>{
  'timestamp',
  'type',
  'actor',
  'result',
  'path',
  'ip',
};

class AuditEventPresentation {
  const AuditEventPresentation({
    required this.title,
    required this.description,
  });

  final String title;
  final String description;
}

String formatAuditTimestamp(String value) {
  final parsed = DateTime.tryParse(value);
  if (parsed == null) {
    return value.isEmpty ? '未知时间' : value;
  }

  final local = parsed.toLocal();
  final offset = local.timeZoneOffset;
  final sign = offset.isNegative ? '-' : '+';
  final offsetHours = offset.inHours.abs().toString().padLeft(2, '0');
  final offsetMinutes = (offset.inMinutes.abs() % 60).toString().padLeft(
    2,
    '0',
  );

  return '${local.year.toString().padLeft(4, '0')}-'
      '${local.month.toString().padLeft(2, '0')}-'
      '${local.day.toString().padLeft(2, '0')} '
      '${local.hour.toString().padLeft(2, '0')}:'
      '${local.minute.toString().padLeft(2, '0')}:'
      '${local.second.toString().padLeft(2, '0')} '
      'UTC$sign$offsetHours:$offsetMinutes';
}

String describeAuditActor(String actor) {
  switch (actor) {
    case 'admin':
      return '管理员';
    case 'machine':
      return '机台';
    case 'bootstrap':
      return '初始化流程';
    default:
      return actor.isEmpty ? '未知主体' : actor;
  }
}

String describeAuditResult(String result) {
  switch (result) {
    case 'success':
      return '成功';
    case 'failure':
      return '失败';
    default:
      return result.isEmpty ? '未知结果' : result;
  }
}

String normalizeAuditIp(String ip) {
  final trimmed = ip.trim();
  if (trimmed.isEmpty) {
    return '未知地址';
  }
  if (trimmed.startsWith('::ffff:')) {
    return trimmed.substring('::ffff:'.length);
  }
  return trimmed;
}

String _auditMetadataText(Map<String, dynamic> metadata, String key) {
  final value = metadata[key];
  if (value == null) {
    return '';
  }
  return value.toString().trim();
}

bool? _auditMetadataBool(Map<String, dynamic> metadata, String key) {
  final value = metadata[key];
  if (value is bool) {
    return value;
  }
  if (value is String) {
    if (value == 'true') {
      return true;
    }
    if (value == 'false') {
      return false;
    }
  }
  return null;
}

String _shortAuditValue(String value, {int edge = 8}) {
  if (value.length <= edge * 2 + 3) {
    return value;
  }
  return '${value.substring(0, edge)}...${value.substring(value.length - edge)}';
}

AuditEventPresentation describeAuditEvent(AuditEntry entry) {
  final machineId = _auditMetadataText(entry.metadata, 'machineId');
  final keyword = _auditMetadataText(entry.metadata, 'vhdKeyword').isNotEmpty
      ? _auditMetadataText(entry.metadata, 'vhdKeyword')
      : _auditMetadataText(entry.metadata, 'defaultVhdKeyword');
  final reason = _auditMetadataText(entry.metadata, 'reason');
  final fingerprint =
      _auditMetadataText(entry.metadata, 'fingerprint256').isNotEmpty
      ? _auditMetadataText(entry.metadata, 'fingerprint256')
      : _auditMetadataText(entry.metadata, 'registrationCertFingerprint');
  final protectedState = _auditMetadataBool(entry.metadata, 'protected');
  final approved = _auditMetadataBool(entry.metadata, 'approved');

  switch (entry.type) {
    case 'init.prepare':
      return const AuditEventPresentation(
        title: '准备初始化服务',
        description: '已生成首次初始化所需的 OTP 信息与安全配置草稿。',
      );
    case 'init.complete':
      final certCount = _auditMetadataText(
        entry.metadata,
        'trustedRegistrationCertificateCount',
      );
      return AuditEventPresentation(
        title: '完成服务初始化',
        description: certCount.isEmpty
            ? '服务已完成初始化并写入安全配置。'
            : '服务已完成初始化，并载入 $certCount 个可信注册证书。',
      );
    case 'auth.login':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '管理员登录成功' : '管理员登录失败',
        description: entry.result == 'success'
            ? '管理员凭证校验通过，已建立登录会话。'
            : '管理员凭证校验失败，登录被拒绝。',
      );
    case 'auth.change-password':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '管理员密码已修改' : '管理员密码修改失败',
        description: entry.result == 'success'
            ? '管理员密码已更新，旧会话的高敏感验证状态已清除。'
            : '密码变更时当前密码校验失败。',
      );
    case 'auth.otp.verify':
      return AuditEventPresentation(
        title: entry.result == 'success' ? 'OTP 二次验证通过' : 'OTP 二次验证失败',
        description: entry.result == 'success'
            ? '高敏感操作窗口已开启。'
            : '提供的 OTP 验证码未通过校验。',
      );
    case 'auth.otp.rotate.prepare':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '开始更换 OTP 绑定密钥' : '旧 OTP 校验失败',
        description: entry.result == 'success'
            ? '已通过旧 OTP 校验，并生成新的 OTP 绑定密钥等待确认。'
            : '提供的旧 OTP 验证码未通过校验，无法开始更换流程。',
      );
    case 'auth.otp.rotate.complete':
      return AuditEventPresentation(
        title: entry.result == 'success' ? 'OTP 绑定密钥已更换' : '新 OTP 校验失败',
        description: entry.result == 'success'
            ? '新的 OTP 绑定密钥已验证通过并替换原有绑定。'
            : '使用新绑定密钥生成的 OTP 验证码未通过校验，原绑定保持不变。',
      );
    case 'settings.default-vhd.update':
      return AuditEventPresentation(
        title: '默认启动关键词已更新',
        description: keyword.isEmpty
            ? '服务默认使用的启动关键词已被修改。'
            : '服务默认使用的启动关键词已更新为 $keyword。',
      );
    case 'machine.protection.update':
      return AuditEventPresentation(
        title: '机台保护状态已更新',
        description: machineId.isEmpty
            ? '机台保护状态已被修改。'
            : '机台 $machineId 的保护状态已切换为${protectedState == true ? '开启' : '关闭'}。',
      );
    case 'machine.create':
      return AuditEventPresentation(
        title: '已新增机台',
        description: machineId.isEmpty
            ? '已创建新的机台记录。'
            : keyword.isEmpty
            ? '已创建机台 $machineId。'
            : '已创建机台 $machineId，默认启动关键词为 $keyword。',
      );
    case 'machine.registration.submit':
      if (entry.result != 'success') {
        return AuditEventPresentation(
          title: '机台公钥注册失败',
          description: machineId.isEmpty
              ? (reason.isEmpty ? '机台提交公钥注册失败。' : '机台提交公钥注册失败，原因：$reason。')
              : (reason.isEmpty
                    ? '机台 $machineId 提交公钥注册失败。'
                    : '机台 $machineId 提交公钥注册失败，原因：$reason。'),
        );
      }
      return AuditEventPresentation(
        title: '机台提交公钥注册',
        description: machineId.isEmpty
            ? '收到新的机台公钥注册请求，等待管理员审批。'
            : fingerprint.isEmpty
            ? '机台 $machineId 已提交公钥注册，等待管理员审批。'
            : '机台 $machineId 已提交公钥注册，请求由证书 ${_shortAuditValue(fingerprint)} 签名。',
      );
    case 'machine.approval.update':
      return AuditEventPresentation(
        title: approved == false ? '已取消机台审批' : '已审批机台公钥',
        description: machineId.isEmpty
            ? '机台公钥审批状态已更新。'
            : approved == false
            ? '机台 $machineId 的公钥审批已取消。'
            : '机台 $machineId 的公钥审批已通过。',
      );
    case 'machine.registration.reset':
      return AuditEventPresentation(
        title: '已重置机台注册状态',
        description: machineId.isEmpty
            ? '机台的注册密钥与审批状态已被清空。'
            : '机台 $machineId 的注册密钥与审批状态已被清空。',
      );
    case 'machine.vhd.update':
      return AuditEventPresentation(
        title: '机台启动关键词已更新',
        description: machineId.isEmpty
            ? '机台启动关键词已被修改。'
            : keyword.isEmpty
            ? '机台 $machineId 的启动关键词已更新。'
            : '机台 $machineId 的启动关键词已更新为 $keyword。',
      );
    case 'machine.evhd-password.update':
      return AuditEventPresentation(
        title: '机台 EVHD 密码已更新',
        description: machineId.isEmpty
            ? '机台 EVHD 密码已被修改。'
            : '机台 $machineId 的 EVHD 密码已更新。',
      );
    case 'machine.evhd-password.read':
      return AuditEventPresentation(
        title: '读取机台 EVHD 明文密码',
        description: machineId.isEmpty
            ? (reason.isEmpty
                  ? '管理员读取了 EVHD 明文密码。'
                  : '管理员读取了 EVHD 明文密码，原因：$reason。')
            : (reason.isEmpty
                  ? '管理员读取了机台 $machineId 的 EVHD 明文密码。'
                  : '管理员读取了机台 $machineId 的 EVHD 明文密码，原因：$reason。'),
      );
    case 'machine.evhd-envelope.read':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '机台获取 EVHD 密文成功' : '机台获取 EVHD 密文失败',
        description: machineId.isEmpty
            ? (entry.result == 'success'
                  ? '机台已通过鉴权并获取 EVHD 密文信封。'
                  : (reason.isEmpty
                        ? '机台获取 EVHD 密文失败。'
                        : '机台获取 EVHD 密文失败，原因：$reason。'))
            : (entry.result == 'success'
                  ? '机台 $machineId 已通过鉴权并获取 EVHD 密文信封。'
                  : (reason.isEmpty
                        ? '机台 $machineId 获取 EVHD 密文失败。'
                        : '机台 $machineId 获取 EVHD 密文失败，原因：$reason。')),
      );
    case 'machine.delete':
      return AuditEventPresentation(
        title: '已删除机台',
        description: machineId.isEmpty ? '机台记录已被删除。' : '机台 $machineId 的记录已被删除。',
      );
    case 'security.trusted-certificate.add':
      return AuditEventPresentation(
        title: '已添加可信注册证书',
        description: fingerprint.isEmpty
            ? '可信注册证书列表已新增一项。'
            : '已添加可信注册证书 ${_shortAuditValue(fingerprint)}。',
      );
    case 'security.trusted-certificate.remove':
      return AuditEventPresentation(
        title: '已移除可信注册证书',
        description: fingerprint.isEmpty
            ? '可信注册证书列表已移除一项。'
            : '已移除可信注册证书 ${_shortAuditValue(fingerprint)}。',
      );
    default:
      return AuditEventPresentation(
        title: entry.type.isEmpty ? '未命名审计事件' : entry.type,
        description: '暂未配置该事件的中文说明。',
      );
  }
}

List<String> mergeRememberedBaseUrls(
  Iterable<String> existing, {
  String? preferredFirst,
  int maxItems = 8,
}) {
  final merged = <String>[];

  void addUrl(String value) {
    final normalized = normalizeBaseUrl(value);
    if (normalized.isEmpty || merged.contains(normalized)) {
      return;
    }
    merged.add(normalized);
  }

  if (preferredFirst != null) {
    addUrl(preferredFirst);
  }

  for (final value in existing) {
    addUrl(value);
    if (merged.length >= maxItems) {
      break;
    }
  }

  return merged;
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
  const OtpStatus({required this.verified, required this.verifiedUntil});

  final bool verified;
  final int verifiedUntil;

  factory OtpStatus.fromJson(Map<String, dynamic> json) {
    final verifiedUntil = (json['otpVerifiedUntil'] as num?)?.toInt() ?? 0;
    return OtpStatus(
      verified:
          json['otpVerified'] == true ||
          verifiedUntil > DateTime.now().millisecondsSinceEpoch,
      verifiedUntil: verifiedUntil,
    );
  }
}

class ClientConfig {
  const ClientConfig({
    this.lastBaseUrl = '',
    this.serverHistory = const <String>[],
  });

  final String lastBaseUrl;
  final List<String> serverHistory;

  factory ClientConfig.fromJson(Map<String, dynamic> json) {
    final rawHistory =
        (json['serverHistory'] as List<dynamic>? ?? const <dynamic>[])
            .map((value) => value.toString())
            .toList();
    final lastBaseUrl = normalizeBaseUrl(
      (json['lastBaseUrl'] as String?) ??
          (rawHistory.isNotEmpty ? rawHistory.first : ''),
    );
    return ClientConfig(
      lastBaseUrl: lastBaseUrl,
      serverHistory: mergeRememberedBaseUrls(
        rawHistory,
        preferredFirst: lastBaseUrl,
      ),
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'lastBaseUrl': lastBaseUrl,
      'serverHistory': serverHistory,
    };
  }
}

abstract class ClientConfigStore {
  Future<ClientConfig> load();

  Future<void> save(ClientConfig config);
}

class ClientConfigStoreException implements Exception {
  const ClientConfigStoreException(this.message);

  final String message;

  @override
  String toString() => message;
}

class FileClientConfigStore implements ClientConfigStore {
  FileClientConfigStore({
    Future<Directory> Function()? directoryProvider,
    this.fileName = 'vhd_mount_admin_client.json',
  }) : _directoryProvider = directoryProvider ?? getApplicationSupportDirectory;

  final Future<Directory> Function() _directoryProvider;
  final String fileName;

  Future<File> _getConfigFile() async {
    final directory = await _directoryProvider();
    return File('${directory.path}${Platform.pathSeparator}$fileName');
  }

  @override
  Future<ClientConfig> load() async {
    try {
      final file = await _getConfigFile();
      if (!await file.exists()) {
        return const ClientConfig();
      }

      final content = await file.readAsString();
      if (content.trim().isEmpty) {
        return const ClientConfig();
      }

      final decoded = jsonDecode(content);
      if (decoded is Map<String, dynamic>) {
        return ClientConfig.fromJson(decoded);
      }

      if (decoded is Map) {
        return ClientConfig.fromJson(Map<String, dynamic>.from(decoded));
      }

      throw const ClientConfigStoreException('本地配置文件格式无效，请修复或删除后重试。');
    } on ClientConfigStoreException {
      rethrow;
    } on FileSystemException catch (error) {
      throw ClientConfigStoreException('读取本地配置失败: ${error.message}');
    } on FormatException {
      throw const ClientConfigStoreException('本地配置文件 JSON 已损坏，请修复或删除后重试。');
    } catch (error) {
      throw ClientConfigStoreException('读取本地配置失败: $error');
    }
  }

  @override
  Future<void> save(ClientConfig config) async {
    final file = await _getConfigFile();
    await file.parent.create(recursive: true);
    await file.writeAsString(
      '${const JsonEncoder.withIndent('  ').convert(config.toJson())}\n',
    );
  }
}

class CookieStoreException implements Exception {
  const CookieStoreException(this.message);

  final String message;

  @override
  String toString() => message;
}

abstract class CookieStore {
  Future<Map<String, Cookie>> load();

  Future<void> save(Map<String, Cookie> cookies);

  Future<void> clear();
}

class SecureCookieStore implements CookieStore {
  SecureCookieStore({
    FlutterSecureStorage? secureStorage,
  }) : _secureStorage = secureStorage ?? const FlutterSecureStorage();

  static const String _storageKey = 'vhd_mount_admin_cookies';
  final FlutterSecureStorage _secureStorage;

  @override
  Future<Map<String, Cookie>> load() async {
    try {
      final content = await _secureStorage.read(key: _storageKey);
      if (content == null || content.trim().isEmpty) {
        return <String, Cookie>{};
      }

      final decoded = jsonDecode(content);
      if (decoded is! Map<String, dynamic>) {
        return <String, Cookie>{};
      }

      final cookiesJson = decoded['cookies'];
      if (cookiesJson is! Map<String, dynamic>) {
        return <String, Cookie>{};
      }

      final result = <String, Cookie>{};
      for (final entry in cookiesJson.entries) {
        final cookieData = entry.value;
        if (cookieData is! Map<String, dynamic>) {
          continue;
        }
        final name = cookieData['name'] as String?;
        final value = cookieData['value'] as String?;
        if (name == null || value == null) {
          continue;
        }
        final cookie = Cookie(name, value);
        final expiresStr = cookieData['expires'] as String?;
        if (expiresStr != null && expiresStr.isNotEmpty) {
          final expires = DateTime.tryParse(expiresStr);
          if (expires != null) {
            cookie.expires = expires;
          }
        }
        final domain = cookieData['domain'] as String?;
        if (domain != null && domain.isNotEmpty) {
          cookie.domain = domain;
        }
        final path = cookieData['path'] as String?;
        if (path != null && path.isNotEmpty) {
          cookie.path = path;
        }
        result[name] = cookie;
      }

      return result;
    } on FormatException {
      return <String, Cookie>{};
    } catch (error) {
      throw CookieStoreException('读取持久化 Cookie 失败: $error');
    }
  }

  @override
  Future<void> save(Map<String, Cookie> cookies) async {
    try {
      final cookiesJson = <String, dynamic>{};
      for (final entry in cookies.entries) {
        final cookie = entry.value;
        cookiesJson[entry.key] = <String, dynamic>{
          'name': cookie.name,
          'value': cookie.value,
          if (cookie.expires != null)
            'expires': cookie.expires!.toUtc().toIso8601String(),
          if (cookie.domain != null) 'domain': cookie.domain,
          if (cookie.path != null) 'path': cookie.path,
        };
      }

      final data = <String, dynamic>{'cookies': cookiesJson};
      await _secureStorage.write(
        key: _storageKey,
        value: jsonEncode(data),
      );
    } catch (error) {
      throw CookieStoreException('保存 Cookie 失败: $error');
    }
  }

  @override
  Future<void> clear() async {
    try {
      await _secureStorage.delete(key: _storageKey);
    } catch (error) {
      throw CookieStoreException('清除 Cookie 失败: $error');
    }
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
    required this.logRetentionActiveDaysOverride,
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
  final int? logRetentionActiveDaysOverride;
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
      registrationCertFingerprint:
          json['registration_cert_fingerprint'] as String?,
      logRetentionActiveDaysOverride:
          (json['log_retention_active_days_override'] as num?)?.toInt(),
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
    required this.metadata,
  });

  final String timestamp;
  final String type;
  final String actor;
  final String result;
  final String path;
  final String ip;
  final Map<String, dynamic> metadata;

  factory AuditEntry.fromJson(Map<String, dynamic> json) {
    return AuditEntry(
      timestamp: (json['timestamp'] as String?) ?? '',
      type: (json['type'] as String?) ?? '',
      actor: (json['actor'] as String?) ?? '',
      result: (json['result'] as String?) ?? '',
      path: (json['path'] as String?) ?? '',
      ip: (json['ip'] as String?) ?? '',
      metadata: Map<String, dynamic>.fromEntries(
        json.entries.where((entry) => !_auditReservedKeys.contains(entry.key)),
      ),
    );
  }

  String get localizedTimestamp => formatAuditTimestamp(timestamp);

  String get localizedActor => describeAuditActor(actor);

  String get localizedResult => describeAuditResult(result);

  String? get machineId {
    final value = _auditMetadataText(metadata, 'machineId');
    return value.isEmpty ? null : value;
  }

  String get normalizedIp => normalizeAuditIp(ip);

  String get displayPath => path.isEmpty ? '未知接口' : path;

  AuditEventPresentation get presentation => describeAuditEvent(this);

  String get searchableText {
    final details = presentation;
    return <String>[
      timestamp,
      type,
      actor,
      result,
      path,
      ip,
      machineId ?? '',
      details.title,
      details.description,
      ...metadata.entries.map((entry) => '${entry.key} ${entry.value}'),
    ].join(' ').toLowerCase();
  }
}

class LogRetentionSettings {
  const LogRetentionSettings({
    required this.defaultRetentionActiveDays,
    required this.dailyInspectionHour,
    required this.dailyInspectionMinute,
    required this.timezone,
    required this.lastInspectionAt,
  });

  final int defaultRetentionActiveDays;
  final int dailyInspectionHour;
  final int dailyInspectionMinute;
  final String timezone;
  final String? lastInspectionAt;

  factory LogRetentionSettings.fromJson(Map<String, dynamic> json) {
    return LogRetentionSettings(
      defaultRetentionActiveDays:
          (json['defaultRetentionActiveDays'] as num?)?.toInt() ?? 7,
      dailyInspectionHour: (json['dailyInspectionHour'] as num?)?.toInt() ?? 3,
      dailyInspectionMinute:
          (json['dailyInspectionMinute'] as num?)?.toInt() ?? 0,
      timezone: (json['timezone'] as String?) ?? 'UTC',
      lastInspectionAt: json['lastInspectionAt'] as String?,
    );
  }

  String get inspectionScheduleLabel =>
      '${dailyInspectionHour.toString().padLeft(2, '0')}:${dailyInspectionMinute.toString().padLeft(2, '0')}';

  String get localizedLastInspectionAt {
    final value = lastInspectionAt;
    if (value == null || value.isEmpty) {
      return '尚未执行';
    }
    return formatAuditTimestamp(value);
  }
}

class MachineLogSession {
  const MachineLogSession({
    required this.machineId,
    required this.sessionId,
    required this.appVersion,
    required this.osVersion,
    required this.startedAt,
    required this.lastUploadAt,
    required this.lastEventAt,
    required this.totalCount,
    required this.warnCount,
    required this.errorCount,
    required this.lastLevel,
    required this.lastComponent,
  });

  final String machineId;
  final String sessionId;
  final String? appVersion;
  final String? osVersion;
  final String? startedAt;
  final String? lastUploadAt;
  final String? lastEventAt;
  final int totalCount;
  final int warnCount;
  final int errorCount;
  final String? lastLevel;
  final String? lastComponent;

  factory MachineLogSession.fromJson(Map<String, dynamic> json) {
    return MachineLogSession(
      machineId: (json['machine_id'] as String?) ?? '',
      sessionId: (json['session_id'] as String?) ?? '',
      appVersion: json['app_version'] as String?,
      osVersion: json['os_version'] as String?,
      startedAt: json['started_at'] as String?,
      lastUploadAt: json['last_upload_at'] as String?,
      lastEventAt: json['last_event_at'] as String?,
      totalCount: (json['total_count'] as num?)?.toInt() ?? 0,
      warnCount: (json['warn_count'] as num?)?.toInt() ?? 0,
      errorCount: (json['error_count'] as num?)?.toInt() ?? 0,
      lastLevel: json['last_level'] as String?,
      lastComponent: json['last_component'] as String?,
    );
  }

  String get localizedStartedAt => startedAt == null || startedAt!.isEmpty
      ? '未知时间'
      : formatAuditTimestamp(startedAt!);

  String get localizedLastEventAt => lastEventAt == null || lastEventAt!.isEmpty
      ? '未知时间'
      : formatAuditTimestamp(lastEventAt!);
}

class MachineLogEntry {
  const MachineLogEntry({
    required this.id,
    required this.machineId,
    required this.sessionId,
    required this.seq,
    required this.occurredAt,
    required this.logDay,
    required this.receivedAt,
    required this.level,
    required this.component,
    required this.eventKey,
    required this.message,
    required this.rawText,
    required this.metadata,
    required this.uploadRequestId,
  });

  final int id;
  final String machineId;
  final String sessionId;
  final int seq;
  final String occurredAt;
  final String? logDay;
  final String? receivedAt;
  final String level;
  final String component;
  final String eventKey;
  final String message;
  final String rawText;
  final Map<String, dynamic> metadata;
  final String? uploadRequestId;

  factory MachineLogEntry.fromJson(Map<String, dynamic> json) {
    final rawMetadata = json['metadata_json'] ?? json['metadata'];
    return MachineLogEntry(
      id: (json['id'] as num?)?.toInt() ?? 0,
      machineId: (json['machine_id'] as String?) ?? '',
      sessionId: (json['session_id'] as String?) ?? '',
      seq: (json['seq'] as num?)?.toInt() ?? 0,
      occurredAt: (json['occurred_at'] as String?) ?? '',
      logDay: json['log_day'] as String?,
      receivedAt: json['received_at'] as String?,
      level: (json['level'] as String?) ?? 'info',
      component: (json['component'] as String?) ?? 'Program',
      eventKey:
          (json['event_key'] as String?) ??
          (json['eventKey'] as String?) ??
          'TRACE_LINE',
      message: (json['message'] as String?) ?? '',
      rawText:
          (json['raw_text'] as String?) ?? (json['rawText'] as String?) ?? '',
      metadata: rawMetadata is Map<String, dynamic>
          ? rawMetadata
          : rawMetadata is Map
          ? Map<String, dynamic>.from(rawMetadata)
          : <String, dynamic>{},
      uploadRequestId: json['upload_request_id'] as String?,
    );
  }

  String get localizedOccurredAt =>
      occurredAt.isEmpty ? '未知时间' : formatAuditTimestamp(occurredAt);
}

class MachineLogPage {
  const MachineLogPage({
    required this.entries,
    required this.nextCursor,
    required this.hasMore,
  });

  final List<MachineLogEntry> entries;
  final String? nextCursor;
  final bool hasMore;

  factory MachineLogPage.fromJson(Map<String, dynamic> json) {
    return MachineLogPage(
      entries: (json['entries'] as List<dynamic>? ?? <dynamic>[])
          .whereType<Map<String, dynamic>>()
          .map(MachineLogEntry.fromJson)
          .toList(),
      nextCursor: json['nextCursor'] as String?,
      hasMore: json['hasMore'] == true,
    );
  }
}

class MachineLogFilter {
  const MachineLogFilter({
    this.machineId,
    this.sessionId,
    this.level,
    this.component,
    this.eventKey,
    this.query,
    this.from,
    this.to,
    this.cursor,
    this.limit = 100,
  });

  final String? machineId;
  final String? sessionId;
  final String? level;
  final String? component;
  final String? eventKey;
  final String? query;
  final String? from;
  final String? to;
  final String? cursor;
  final int limit;

  Map<String, String> toQueryParameters() {
    return <String, String>{
      if (machineId != null && machineId!.trim().isNotEmpty)
        'machineId': machineId!.trim(),
      if (sessionId != null && sessionId!.trim().isNotEmpty)
        'sessionId': sessionId!.trim(),
      if (level != null && level!.trim().isNotEmpty) 'level': level!.trim(),
      if (component != null && component!.trim().isNotEmpty)
        'component': component!.trim(),
      if (eventKey != null && eventKey!.trim().isNotEmpty)
        'eventKey': eventKey!.trim().toUpperCase(),
      if (query != null && query!.trim().isNotEmpty) 'q': query!.trim(),
      if (from != null && from!.trim().isNotEmpty) 'from': from!.trim(),
      if (to != null && to!.trim().isNotEmpty) 'to': to!.trim(),
      if (cursor != null && cursor!.trim().isNotEmpty) 'cursor': cursor!.trim(),
      'limit': limit.toString(),
    };
  }
}

class DeploymentPackage {
  const DeploymentPackage({
    required this.packageId,
    required this.name,
    required this.version,
    required this.type,
    required this.signer,
    required this.fileName,
    required this.fileSize,
    required this.createdAt,
  });

  final String packageId;
  final String name;
  final String version;
  final String type;
  final String signer;
  final String fileName;
  final int fileSize;
  final String createdAt;

  factory DeploymentPackage.fromJson(Map<String, dynamic> json) {
    return DeploymentPackage(
      packageId:
          (json['package_id'] as String?) ??
          (json['packageId'] as String?) ??
          '',
      name: (json['name'] as String?) ?? '',
      version: (json['version'] as String?) ?? '',
      type: (json['type'] as String?) ?? 'software-deploy',
      signer: (json['signer'] as String?) ?? '',
      fileName:
          (json['file_name'] as String?) ?? (json['filePath'] as String?) ?? '',
      fileSize:
          (json['file_size'] as num?)?.toInt() ??
          (json['fileSize'] as num?)?.toInt() ??
          0,
      createdAt:
          (json['created_at'] as String?) ??
          (json['createdAt'] as String?) ??
          '',
    );
  }

  String get displayType {
    switch (type) {
      case 'software-deploy':
        return '软件部署';
      case 'file-deploy':
        return '文件部署';
      default:
        return type;
    }
  }

  String get displaySize {
    if (fileSize < 1024) {
      return '$fileSize B';
    } else if (fileSize < 1024 * 1024) {
      return '${(fileSize / 1024).toStringAsFixed(1)} KB';
    } else if (fileSize < 1024 * 1024 * 1024) {
      return '${(fileSize / (1024 * 1024)).toStringAsFixed(1)} MB';
    } else {
      return '${(fileSize / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
    }
  }
}

class DeploymentTask {
  const DeploymentTask({
    required this.taskId,
    required this.packageId,
    required this.machineId,
    required this.taskType,
    required this.status,
    required this.errorMessage,
    required this.createdAt,
    required this.scheduledAt,
    required this.completedAt,
    required this.packageName,
    required this.packageVersion,
  });

  final String taskId;
  final String packageId;
  final String machineId;
  final String taskType;
  final String status;
  final String? errorMessage;
  final String createdAt;
  final String? scheduledAt;
  final String? completedAt;
  final String? packageName;
  final String? packageVersion;

  factory DeploymentTask.fromJson(Map<String, dynamic> json) {
    return DeploymentTask(
      taskId: (json['task_id'] as String?) ?? (json['taskId'] as String?) ?? '',
      packageId:
          (json['package_id'] as String?) ??
          (json['packageId'] as String?) ??
          '',
      machineId:
          (json['machine_id'] as String?) ??
          (json['machineId'] as String?) ??
          '',
      taskType:
          (json['task_type'] as String?) ??
          (json['taskType'] as String?) ??
          'deploy',
      status: (json['status'] as String?) ?? 'pending',
      errorMessage:
          json['error_message'] as String? ?? json['errorMessage'] as String?,
      createdAt:
          (json['created_at'] as String?) ??
          (json['createdAt'] as String?) ??
          '',
      scheduledAt:
          json['scheduled_at'] as String? ?? json['scheduledAt'] as String?,
      completedAt:
          json['completed_at'] as String? ?? json['completedAt'] as String?,
      packageName:
          json['package_name'] as String? ?? json['packageName'] as String?,
      packageVersion:
          json['package_version'] as String? ??
          json['packageVersion'] as String?,
    );
  }

  String get displayStatus {
    switch (status) {
      case 'pending':
        return '等待中';
      case 'downloading':
        return '下载中';
      case 'running':
        return '执行中';
      case 'success':
        return '成功';
      case 'failed':
        return '失败';
      default:
        return status;
    }
  }

  String get displayType {
    switch (taskType) {
      case 'deploy':
        return '部署';
      case 'uninstall':
        return '卸载';
      default:
        return taskType;
    }
  }
}

class DeploymentRecord {
  const DeploymentRecord({
    required this.recordId,
    required this.machineId,
    required this.packageId,
    required this.name,
    required this.version,
    required this.type,
    required this.targetPath,
    required this.status,
    required this.deployedAt,
    required this.uninstalledAt,
    required this.syncedAt,
  });

  final String recordId;
  final String machineId;
  final String packageId;
  final String name;
  final String version;
  final String type;
  final String? targetPath;
  final String status;
  final String deployedAt;
  final String? uninstalledAt;
  final String? syncedAt;

  factory DeploymentRecord.fromJson(Map<String, dynamic> json) {
    return DeploymentRecord(
      recordId:
          (json['record_id'] as String?) ?? (json['recordId'] as String?) ?? '',
      machineId:
          (json['machine_id'] as String?) ??
          (json['machineId'] as String?) ??
          '',
      packageId:
          (json['package_id'] as String?) ??
          (json['packageId'] as String?) ??
          '',
      name: (json['name'] as String?) ?? '',
      version: (json['version'] as String?) ?? '',
      type: (json['type'] as String?) ?? 'software-deploy',
      targetPath:
          json['target_path'] as String? ?? json['targetPath'] as String?,
      status: (json['status'] as String?) ?? 'success',
      deployedAt:
          (json['deployed_at'] as String?) ??
          (json['deployedAt'] as String?) ??
          '',
      uninstalledAt:
          json['uninstalled_at'] as String? ?? json['uninstalledAt'] as String?,
      syncedAt: json['synced_at'] as String? ?? json['syncedAt'] as String?,
    );
  }

  String get displayStatus {
    switch (status) {
      case 'success':
        return '已部署';
      case 'uninstalled':
        return '已卸载';
      case 'failed':
        return '失败';
      default:
        return status;
    }
  }

  String get displayType {
    switch (type) {
      case 'software-deploy':
        return '软件部署';
      case 'file-deploy':
        return '文件部署';
      default:
        return type;
    }
  }
}

List<String> buildAuditMachineOptions(
  Iterable<MachineRecord> machines,
  Iterable<AuditEntry> entries,
) {
  final values = <String>{
    ...machines
        .map((machine) => machine.machineId)
        .where((value) => value.trim().isNotEmpty),
    ...entries
        .map((entry) => entry.machineId ?? '')
        .where((value) => value.trim().isNotEmpty),
  }.toList();

  values.sort(
    (left, right) => left.toLowerCase().compareTo(right.toLowerCase()),
  );
  return values;
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
    List<Map<String, String>> trustedCertificates =
        const <Map<String, String>>[],
  });

  Future<void> login(String password);

  Future<void> logout();

  Future<OtpStatus> verifyOtp(String code);

  Future<OtpStatus> getOtpStatus();

  Future<InitializationPreparation> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  });

  Future<OtpStatus> completeOtpRotation(String code);

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

  Future<List<AuditEntry>> getAuditEntries({String? machineId});

  Future<LogRetentionSettings> getLogRetentionSettings();

  Future<LogRetentionSettings> updateLogRetentionSettings({
    required int defaultRetentionActiveDays,
    required int dailyInspectionHour,
    required int dailyInspectionMinute,
    required String timezone,
  });

  Future<void> setMachineLogRetentionOverride(
    String machineId,
    int? retentionActiveDaysOverride,
  );

  Future<List<MachineLogSession>> getMachineLogSessions({
    String? machineId,
    String? from,
    String? to,
    int limit = 50,
  });

  Future<MachineLogPage> getMachineLogs(MachineLogFilter filter);

  Future<String> exportMachineLogs(
    MachineLogFilter filter, {
    String format = 'text',
  });

  Future<void> updateDefaultVhd(String vhdKeyword);

  Future<void> changePassword(String currentPassword, String newPassword);

  Future<void> restoreSession();

  Future<List<DeploymentPackage>> getDeploymentPackages();

  Future<void> uploadDeploymentPackage({
    required String name,
    required String version,
    required String type,
    required String signer,
    required List<int> packageBytes,
    required String packageFileName,
    required List<int> signatureBytes,
    required String signatureFileName,
  });

  Future<void> deleteDeploymentPackage(String packageId);

  Future<void> deleteDeploymentTask(String taskId);

  Future<List<DeploymentTask>> getDeploymentTasks({
    String? machineId,
    String? status,
  });

  Future<void> createDeploymentTask(
    String packageId,
    List<String> targetMachineIds, {
    String? scheduledAt,
  });

  Future<List<DeploymentRecord>> getMachineDeploymentHistory(String machineId);

  Future<void> triggerUninstall(String machineId, String recordId);
}

class _MultipartFile {
  const _MultipartFile({
    required this.fileName,
    required this.bytes,
    required this.contentType,
  });

  final String fileName;
  final List<int> bytes;
  final String contentType;
}

class HttpAdminApi implements AdminApi {
  HttpAdminApi({
    String baseUrl = 'http://localhost:8080',
    Duration requestTimeout = const Duration(seconds: 15),
    this.cookieStore,
  }) : _baseUrl = baseUrl,
       _requestTimeout = requestTimeout,
       _client = HttpClient()..connectionTimeout = requestTimeout;

  final HttpClient _client;
  final Map<String, Cookie> _cookies = <String, Cookie>{};
  final Duration _requestTimeout;
  String _baseUrl;
  final Random _random = Random.secure();
  final CookieStore? cookieStore;

  Future<void> _persistCookies() async {
    if (cookieStore == null || _cookies.isEmpty) {
      return;
    }
    try {
      await cookieStore!.save(Map<String, Cookie>.unmodifiable(_cookies));
    } catch (_) {
      // 静默失败，Cookie 持久化失败时不阻塞请求。
    }
  }

  Future<void> clearCookies() async {
    _cookies.clear();
    if (cookieStore != null) {
      try {
        await cookieStore!.clear();
      } catch (_) {
        // 静默失败。
      }
    }
  }

  @override
  String get baseUrl => _baseUrl;

  @override
  void updateBaseUrl(String baseUrl) {
    _baseUrl = normalizeBaseUrl(baseUrl);
  }

  Uri _resolve(String path) {
    final normalizedBase = _baseUrl.isEmpty
        ? 'http://localhost:8080'
        : _baseUrl;
    return Uri.parse('$normalizedBase$path');
  }

  void _applyCookies(HttpClientRequest request) {
    if (_cookies.isEmpty) {
      return;
    }
    request.headers.set(
      HttpHeaders.cookieHeader,
      _cookies.values
          .map((cookie) => '${cookie.name}=${cookie.value}')
          .join('; '),
    );
  }

  void _storeCookies(HttpClientResponse response) {
    var changed = false;
    for (final cookie in response.cookies) {
      _cookies[cookie.name] = cookie;
      changed = true;
    }
    if (changed) {
      unawaited(_persistCookies());
    }
  }

  Future<T> _withTimeout<T>(Future<T> future) {
    return future.timeout(
      _requestTimeout,
      onTimeout: () => throw AdminApiException('请求超时，请稍后重试。'),
    );
  }

  Future<Map<String, dynamic>> _requestJson(
    String method,
    String path, {
    Object? body,
  }) async {
    return _withTimeout(_requestJsonInternal(method, path, body: body));
  }

  Future<String> _requestText(
    String method,
    String path, {
    Object? body,
  }) async {
    return _withTimeout(_requestTextInternal(method, path, body: body));
  }

  Future<Map<String, dynamic>> _requestJsonInternal(
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

  Future<String> _requestTextInternal(
    String method,
    String path, {
    Object? body,
  }) async {
    final request = await _client.openUrl(method, _resolve(path));
    request.headers.set(HttpHeaders.acceptHeader, '*/*');
    _applyCookies(request);

    if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(jsonEncode(body));
    }

    final response = await request.close();
    _storeCookies(response);
    final text = await response.transform(utf8.decoder).join();

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return text;
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
      body: <String, dynamic>{'issuer': issuer, 'accountName': accountName},
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
    List<Map<String, String>> trustedCertificates =
        const <Map<String, String>>[],
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
  Future<void> restoreSession() async {
    if (cookieStore == null) {
      return;
    }
    try {
      final loaded = await cookieStore!.load();
      if (loaded.isNotEmpty) {
        _cookies.addAll(loaded);
      }
    } catch (_) {
      // 静默失败，Cookie 恢复失败时不阻塞启动流程。
    }
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
    await clearCookies();
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
  Future<InitializationPreparation> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  }) async {
    final body = <String, dynamic>{'currentCode': currentCode};
    if (issuer != null && issuer.trim().isNotEmpty) {
      body['issuer'] = issuer.trim();
    }
    if (accountName != null && accountName.trim().isNotEmpty) {
      body['accountName'] = accountName.trim();
    }

    final json = await _requestJson(
      'POST',
      '/api/auth/otp/rotate/prepare',
      body: body,
    );
    return InitializationPreparation.fromJson(json);
  }

  @override
  Future<OtpStatus> completeOtpRotation(String code) async {
    final json = await _requestJson(
      'POST',
      '/api/auth/otp/rotate/complete',
      body: <String, dynamic>{'code': code},
    );
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
      '/api/machines/${encodePathSegment(machineId)}/approve',
      body: <String, dynamic>{'approved': approved},
    );
  }

  @override
  Future<void> setMachineProtection(
    String machineId,
    bool protectedState,
  ) async {
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
    await _requestJson(
      'POST',
      '/api/machines/${encodePathSegment(machineId)}/revoke',
    );
  }

  @override
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    await _requestJson(
      'POST',
      '/api/machines/${encodePathSegment(machineId)}/vhd',
      body: <String, dynamic>{'vhdKeyword': vhdKeyword},
    );
  }

  @override
  Future<void> setMachineEvhdPassword(
    String machineId,
    String evhdPassword,
  ) async {
    await _requestJson(
      'POST',
      '/api/machines/${encodePathSegment(machineId)}/evhd-password',
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
    await _requestJson(
      'DELETE',
      '/api/machines/${encodePathSegment(machineId)}',
    );
  }

  @override
  Future<List<TrustedCertificateRecord>> getTrustedCertificates() async {
    final json = await _requestJson(
      'GET',
      '/api/security/trusted-certificates',
    );
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
      body: <String, dynamic>{'name': name, 'certificatePem': certificatePem},
    );
  }

  @override
  Future<void> removeTrustedCertificate(String fingerprint) async {
    await _requestJson(
      'DELETE',
      '/api/security/trusted-certificates/${encodePathSegment(fingerprint)}',
    );
  }

  @override
  Future<List<AuditEntry>> getAuditEntries({String? machineId}) async {
    final queryParameters = <String, String>{'limit': '100'};
    if (machineId != null && machineId.trim().isNotEmpty) {
      queryParameters['machineId'] = machineId.trim();
    }

    final query = Uri(queryParameters: queryParameters).query;
    final json = await _requestJson('GET', '/api/audit?$query');
    return (json['entries'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(AuditEntry.fromJson)
        .toList();
  }

  @override
  Future<LogRetentionSettings> getLogRetentionSettings() async {
    final json = await _requestJson('GET', '/api/settings/log-retention');
    return LogRetentionSettings.fromJson(json);
  }

  @override
  Future<LogRetentionSettings> updateLogRetentionSettings({
    required int defaultRetentionActiveDays,
    required int dailyInspectionHour,
    required int dailyInspectionMinute,
    required String timezone,
  }) async {
    final json = await _requestJson(
      'POST',
      '/api/settings/log-retention',
      body: <String, dynamic>{
        'defaultRetentionActiveDays': defaultRetentionActiveDays,
        'dailyInspectionHour': dailyInspectionHour,
        'dailyInspectionMinute': dailyInspectionMinute,
        'timezone': timezone,
      },
    );
    return LogRetentionSettings.fromJson(json);
  }

  @override
  Future<void> setMachineLogRetentionOverride(
    String machineId,
    int? retentionActiveDaysOverride,
  ) async {
    await _requestJson(
      'POST',
      '/api/machines/${encodePathSegment(machineId)}/log-retention',
      body: <String, dynamic>{
        'retentionActiveDaysOverride': retentionActiveDaysOverride,
      },
    );
  }

  @override
  Future<List<MachineLogSession>> getMachineLogSessions({
    String? machineId,
    String? from,
    String? to,
    int limit = 50,
  }) async {
    final queryParameters = <String, String>{'limit': limit.toString()};
    if (machineId != null && machineId.trim().isNotEmpty) {
      queryParameters['machineId'] = machineId.trim();
    }
    if (from != null && from.trim().isNotEmpty) {
      queryParameters['from'] = from.trim();
    }
    if (to != null && to.trim().isNotEmpty) {
      queryParameters['to'] = to.trim();
    }

    final json = await _requestJson(
      'GET',
      '/api/machine-log-sessions?${Uri(queryParameters: queryParameters).query}',
    );
    return (json['sessions'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(MachineLogSession.fromJson)
        .toList();
  }

  @override
  Future<MachineLogPage> getMachineLogs(MachineLogFilter filter) async {
    final query = Uri(queryParameters: filter.toQueryParameters()).query;
    final json = await _requestJson('GET', '/api/machine-logs?$query');
    return MachineLogPage.fromJson(json);
  }

  @override
  Future<String> exportMachineLogs(
    MachineLogFilter filter, {
    String format = 'text',
  }) async {
    final queryParameters = filter.toQueryParameters()..['format'] = format;
    final query = Uri(queryParameters: queryParameters).query;
    return _requestText('GET', '/api/machine-logs/export?$query');
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
  Future<void> changePassword(
    String currentPassword,
    String newPassword,
  ) async {
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

  // ---- Deployment API ----

  Future<Map<String, dynamic>> _uploadMultipart(
    String path, {
    required Map<String, String> fields,
    required Map<String, _MultipartFile> files,
  }) async {
    final boundary = '----FlutterFormBoundary${_random.nextInt(999999)}';
    final body = BytesBuilder();

    for (final entry in fields.entries) {
      body.add(utf8.encode('--$boundary\r\n'));
      body.add(
        utf8.encode(
          'Content-Disposition: form-data; name="${entry.key}"\r\n\r\n',
        ),
      );
      body.add(utf8.encode('${entry.value}\r\n'));
    }

    for (final entry in files.entries) {
      body.add(utf8.encode('--$boundary\r\n'));
      body.add(
        utf8.encode(
          'Content-Disposition: form-data; name="${entry.key}"; '
          'filename="${entry.value.fileName}"\r\n',
        ),
      );
      body.add(utf8.encode('Content-Type: ${entry.value.contentType}\r\n\r\n'));
      body.add(entry.value.bytes);
      body.add(utf8.encode('\r\n'));
    }

    body.add(utf8.encode('--$boundary--\r\n'));

    final request = await _client.postUrl(_resolve(path));
    request.headers.set(
      HttpHeaders.contentTypeHeader,
      'multipart/form-data; boundary=$boundary',
    );
    _applyCookies(request);
    request.add(body.toBytes());

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
  Future<List<DeploymentPackage>> getDeploymentPackages() async {
    final json = await _requestJson('GET', '/api/deployments/packages');
    return (json['packages'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(DeploymentPackage.fromJson)
        .toList();
  }

  @override
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
    await _uploadMultipart(
      '/api/deployments/packages',
      fields: <String, String>{
        'name': name,
        'version': version,
        'type': type,
        'signer': signer,
      },
      files: <String, _MultipartFile>{
        'package': _MultipartFile(
          fileName: packageFileName,
          bytes: packageBytes,
          contentType: 'application/zip',
        ),
        'signature': _MultipartFile(
          fileName: signatureFileName,
          bytes: signatureBytes,
          contentType: 'application/octet-stream',
        ),
      },
    );
  }

  @override
  Future<void> deleteDeploymentPackage(String packageId) async {
    await _requestJson(
      'DELETE',
      '/api/deployments/packages/${encodePathSegment(packageId)}',
    );
  }

  @override
  Future<void> deleteDeploymentTask(String taskId) async {
    await _requestJson(
      'DELETE',
      '/api/deployments/tasks/${encodePathSegment(taskId)}',
    );
  }

  @override
  Future<List<DeploymentTask>> getDeploymentTasks({
    String? machineId,
    String? status,
  }) async {
    final queryParameters = <String, String>{};
    if (machineId != null && machineId.trim().isNotEmpty) {
      queryParameters['machineId'] = machineId.trim();
    }
    if (status != null && status.trim().isNotEmpty) {
      queryParameters['status'] = status.trim();
    }

    final query = Uri(queryParameters: queryParameters).query;
    final path = query.isEmpty
        ? '/api/deployments/tasks'
        : '/api/deployments/tasks?$query';
    final json = await _requestJson('GET', path);
    return (json['tasks'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(DeploymentTask.fromJson)
        .toList();
  }

  @override
  Future<void> createDeploymentTask(
    String packageId,
    List<String> targetMachineIds, {
    String? scheduledAt,
  }) async {
    final body = <String, dynamic>{
      'packageId': packageId,
      'targetMachineIds': targetMachineIds,
    };
    if (scheduledAt != null && scheduledAt.trim().isNotEmpty) {
      body['scheduledAt'] = scheduledAt.trim();
    }
    await _requestJson('POST', '/api/deployments/tasks', body: body);
  }

  @override
  Future<List<DeploymentRecord>> getMachineDeploymentHistory(
    String machineId,
  ) async {
    final json = await _requestJson(
      'GET',
      '/api/machines/${encodePathSegment(machineId)}/deployments/history',
    );
    return (json['records'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(DeploymentRecord.fromJson)
        .toList();
  }

  @override
  Future<void> triggerUninstall(String machineId, String recordId) async {
    await _requestJson(
      'POST',
      '/api/machines/${encodePathSegment(machineId)}/deployments/${encodePathSegment(recordId)}/uninstall',
    );
  }
}
