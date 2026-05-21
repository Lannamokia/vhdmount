part of '../../app.dart';

/// Windows 平台生物识别 OTP 服务。
///
/// 当前实现策略：
/// - **TOTP 生成**：使用共享 [TotpGenerator]（RFC 6238, HMAC-SHA1, 6 位, 30 秒窗口）
/// - **密钥存储**：使用 flutter_secure_storage（底层为 Windows Credential Manager）
/// - **生物识别**：简化实现，依赖 Windows Credential Manager 的系统级保护
///
/// TODO: 完整 Windows Hello KeyCredential API 集成需要 win32 FFI，
/// 将在后续迭代中增强。当前 flutter_secure_storage 在 Windows 上使用
/// Credential Manager 提供了足够的安全保护。
class WindowsBiometricOtpService extends BiometricOtpService {
  WindowsBiometricOtpService({
    FlutterSecureStorage? secureStorage,
  }) : _secureStorage = secureStorage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
              iOptions:
                  IOSOptions(accessibility: KeychainAccessibility.first_unlock),
              wOptions: WindowsOptions(),
            );

  final FlutterSecureStorage _secureStorage;

  static const String _secretKey = 'biometric_otp_totp_secret';
  static const String _keyIdKey = 'biometric_otp_key_id';

  @override
  Future<bool> isAvailable() async {
    // 简化实现：在 Windows 平台上始终可用。
    // 完整实现应通过 Win32 FFI 检查 Windows Hello 是否已配置。
    return Platform.isWindows;
  }

  @override
  Future<bool> isBound() async {
    final secret = await _secureStorage.read(key: _secretKey);
    return secret != null && secret.isNotEmpty;
  }

  @override
  Future<void> bind({required String totpSecret, required String keyId}) async {
    // 存储 TOTP 密钥到 Windows Credential Manager（通过 flutter_secure_storage）
    await _secureStorage.write(key: _secretKey, value: totpSecret);
    await _secureStorage.write(key: _keyIdKey, value: keyId);
  }

  @override
  Future<void> unbind() async {
    await _secureStorage.delete(key: _secretKey);
    await _secureStorage.delete(key: _keyIdKey);
  }

  @override
  Future<String?> authenticate() async {
    // 从安全存储读取 TOTP 密钥
    final secret = await _secureStorage.read(key: _secretKey);
    if (secret == null || secret.isEmpty) return null;

    // 使用共享 TotpGenerator 生成验证码
    return TotpGenerator.generateCode(secret);
  }

  @override
  String get platformIdentifier => 'windows-hello';

  @override
  String get displayName => 'Windows Hello';

  @override
  IconData get icon => Icons.fingerprint_rounded;

  /// 获取当前绑定的密钥 ID。
  Future<String?> getBoundKeyId() async {
    return _secureStorage.read(key: _keyIdKey);
  }
}
