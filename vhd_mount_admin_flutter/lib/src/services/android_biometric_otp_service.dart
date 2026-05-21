part of '../../app.dart';

/// Android 平台生物识别 OTP 服务。
///
/// 实现策略：
/// - **生物识别**：使用 `local_auth` 调用 Android BiometricPrompt
/// - **TOTP 生成**：使用共享 [TotpGenerator]（RFC 6238, HMAC-SHA1, 6 位, 30 秒窗口）
/// - **密钥存储**：使用 flutter_secure_storage（底层为 EncryptedSharedPreferences）
/// - **安全模型**：通过 local_auth 前置认证 + EncryptedSharedPreferences 存储
///
/// Android Keystore 配合 BiometricPrompt 认证绑定：
/// - flutter_secure_storage 在 Android 上默认使用 EncryptedSharedPreferences
/// - 生物识别绑定通过 local_auth 认证后才允许读取密钥
/// - 用户注册新指纹后，捕获存储异常并触发自动失效
class AndroidBiometricOtpService extends BiometricOtpService {
  AndroidBiometricOtpService({
    FlutterSecureStorage? secureStorage,
  }) : _secureStorage = secureStorage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            );

  final FlutterSecureStorage _secureStorage;

  static const String _secretKey = 'biometric_otp_totp_secret';
  static const String _keyIdKey = 'biometric_otp_key_id';

  @override
  Future<bool> isAvailable() async {
    if (!Platform.isAndroid) return false;
    try {
      final localAuth = LocalAuthentication();
      final canCheck = await localAuth.canCheckBiometrics;
      final isSupported = await localAuth.isDeviceSupported();
      return canCheck && isSupported;
    } catch (_) {
      return false;
    }
  }

  @override
  Future<bool> isBound() async {
    final secret = await _secureStorage.read(key: _secretKey);
    return secret != null && secret.isNotEmpty;
  }

  @override
  Future<void> bind({required String totpSecret, required String keyId}) async {
    // 存储 TOTP 密钥到 EncryptedSharedPreferences（通过 flutter_secure_storage）
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
    // 先通过 BiometricPrompt 认证
    try {
      final localAuth = LocalAuthentication();
      final authenticated = await localAuth.authenticate(
        localizedReason: '验证指纹以完成 OTP 认证',
        options: const AuthenticationOptions(
          biometricOnly: true,
          stickyAuth: true,
        ),
      );
      if (!authenticated) return null;
    } on PlatformException catch (e) {
      // 生物识别注册信息变更导致密钥失效
      if (e.code == 'NotAvailable' ||
          e.code == 'NotEnrolled' ||
          e.code == 'PermanentlyLockedOut') {
        await unbind();
        return null;
      }
      return null;
    }

    // 认证通过后，从安全存储读取 TOTP 密钥
    try {
      final secret = await _secureStorage.read(key: _secretKey);
      if (secret == null || secret.isEmpty) return null;

      // 使用共享 TotpGenerator 生成验证码
      return TotpGenerator.generateCode(secret);
    } catch (_) {
      // 存储读取失败（可能因生物识别注册变更导致密钥失效）
      await unbind();
      return null;
    }
  }

  @override
  String get platformIdentifier => 'android-biometric';

  @override
  String get displayName => '指纹验证';

  @override
  IconData get icon => Icons.fingerprint_rounded;

  /// 获取当前绑定的密钥 ID。
  Future<String?> getBoundKeyId() async {
    return _secureStorage.read(key: _keyIdKey);
  }
}
