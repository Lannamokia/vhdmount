part of '../../app.dart';

/// Windows 平台生物识别 OTP 服务（Windows Hello + Credential Manager + DPAPI）。
///
/// 实现策略：
/// - **TOTP 生成**：使用共享 [TotpGenerator]（RFC 6238, HMAC-SHA1, 6 位, 30 秒窗口）
/// - **密钥存储**：使用 flutter_secure_storage（底层为 Windows Credential Manager + DPAPI），
///   secret 与当前 Windows 用户 SID 绑定，其它用户读不出来
/// - **生物识别**：通过 `local_auth`（local_auth_windows 插件）触发 Windows Hello UAC，
///   bind / authenticate 都会强制要求用户通过 Hello（指纹 / 面部 / PIN）
///
/// **TODO**：未来若需要硬件级密钥保护（TPM 绑定），应改用 Win32 KeyCredential API
/// （`Microsoft.Security.Authentication.Credentials`）通过 FFI 直接操作。
/// 用于测试的生物识别认证回调签名。
/// 生产环境会注入一个真正调用 [LocalAuthentication.authenticate] 的实现；
/// 单元测试可以注入一个总是返回 true 的 fake。
typedef BiometricAuthGate = Future<bool> Function(String localizedReason);

class WindowsBiometricOtpService extends BiometricOtpService {
  WindowsBiometricOtpService({
    FlutterSecureStorage? secureStorage,
    LocalAuthentication? localAuth,
    BiometricAuthGate? authGate,
  })  : _secureStorage = secureStorage ??
            const FlutterSecureStorage(
              wOptions: WindowsOptions(),
            ),
        _localAuth = localAuth ?? LocalAuthentication(),
        _authGate = authGate;

  final FlutterSecureStorage _secureStorage;
  final LocalAuthentication _localAuth;
  final BiometricAuthGate? _authGate;

  static const String _secretKey = 'biometric_otp_totp_secret';
  static const String _keyIdKey = 'biometric_otp_key_id';

  /// 调用注入的 [_authGate]（测试用）或真实的 [LocalAuthentication]。
  Future<bool> _runAuthGate(String reason) async {
    final gate = _authGate;
    if (gate != null) return gate(reason);
    return _localAuth.authenticate(
      localizedReason: reason,
      options: const AuthenticationOptions(
        biometricOnly: false, // Windows Hello 允许 PIN 兜底
        stickyAuth: true,
      ),
    );
  }

  @override
  Future<bool> isAvailable() async {
    if (!Platform.isWindows) return false;
    if (_authGate != null) return true; // 测试桩
    try {
      final isSupported = await _localAuth.isDeviceSupported();
      final canCheck = await _localAuth.canCheckBiometrics;
      return isSupported && canCheck;
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
    // 先通过 Windows Hello 验证用户身份
    try {
      final authenticated = await _runAuthGate(
        '通过 Windows Hello 验证以绑定 OTP 密钥',
      );
      if (!authenticated) {
        throw const BiometricBindCancelledException(
          'Windows Hello 验证未通过，已取消绑定',
        );
      }
    } on PlatformException catch (e) {
      throw BiometricBindCancelledException(
        e.message?.isNotEmpty == true
            ? 'Windows Hello 认证失败：${e.message}'
            : 'Windows Hello 认证失败，已取消绑定',
      );
    }

    // 存储 TOTP 密钥到 Windows Credential Manager
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
    // 1) 先确认本地有已绑定的密钥，避免无意义弹框
    final secret = await _secureStorage.read(key: _secretKey);
    if (secret == null || secret.isEmpty) return null;

    // 2) Windows Hello 验证
    try {
      final authenticated = await _runAuthGate(
        '通过 Windows Hello 完成 OTP 验证',
      );
      if (!authenticated) return null;
    } on PlatformException catch (e) {
      if (e.code == 'NotAvailable' ||
          e.code == 'NotEnrolled' ||
          e.code == 'PermanentlyLockedOut') {
        await unbind();
      }
      return null;
    }

    // 3) 生成 RFC 6238 TOTP 验证码
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
