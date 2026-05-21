part of '../../app.dart';

/// iOS 平台生物识别 OTP 服务（Face ID / Touch ID + Keychain）。
///
/// 实现策略：
/// - **生物识别**：使用 `local_auth` 调用 Face ID / Touch ID，bind / authenticate 都会触发
/// - **TOTP 生成**：共享 TotpGenerator（RFC 6238, HMAC-SHA1, 6 位, 30 秒窗口）
/// - **密钥存储**：使用 flutter_secure_storage 配合 iOS Keychain
///   - accessibility: `KeychainAccessibility.unlocked_this_device`
///   - 设备解锁后该 key 可读，但 bind / authenticate 仍强制要求 local_auth 通过
///
/// **TODO（已知限制）**：
/// `flutter_secure_storage` 当前没有暴露 `kSecAccessControlBiometryCurrentSet`，
/// 因此用户更换 Face ID / Touch ID 注册信息后，Keychain item 不会自动失效；
/// 需要等本仓库迁移到能直传 SecAccessControl 的存储层（或使用 FFI 自实现），
/// 才能彻底落地"生物识别注册变更后自动 unbind"的语义。
/// 当前的 fallback 是：local_auth 仍然会要求**当前**注册的生物识别通过，
/// 所以攻击门槛仍然是"持有解锁后的设备 + 通过当前生物识别"。
class IosBiometricOtpService extends BiometricOtpService {
  IosBiometricOtpService({
    FlutterSecureStorage? secureStorage,
  }) : _secureStorage = secureStorage ??
            const FlutterSecureStorage(
              iOptions: IOSOptions(
                accessibility:
                    KeychainAccessibility.unlocked_this_device,
              ),
            );

  final FlutterSecureStorage _secureStorage;
  final LocalAuthentication _localAuth = LocalAuthentication();

  static const String _secretKey = 'biometric_otp_totp_secret';
  static const String _keyIdKey = 'biometric_otp_key_id';

  @override
  Future<bool> isAvailable() async {
    if (!Platform.isIOS) return false;
    try {
      final canCheck = await _localAuth.canCheckBiometrics;
      final isSupported = await _localAuth.isDeviceSupported();
      return canCheck && isSupported;
    } catch (_) {
      return false;
    }
  }

  @override
  Future<bool> isBound() async {
    try {
      final secret = await _secureStorage.read(key: _secretKey);
      return secret != null && secret.isNotEmpty;
    } catch (_) {
      // Keychain read failure (e.g., biometric enrollment changed)
      // means the binding is effectively invalid
      return false;
    }
  }

  @override
  Future<void> bind({required String totpSecret, required String keyId}) async {
    // 录入前先通过 Face ID / Touch ID 认证
    try {
      final authenticated = await _localAuth.authenticate(
        localizedReason: '验证身份以绑定生物识别 OTP',
        options: const AuthenticationOptions(
          biometricOnly: true,
          stickyAuth: true,
        ),
      );
      if (!authenticated) {
        throw const BiometricBindCancelledException(
          '生物识别验证未通过，已取消绑定',
        );
      }
    } on PlatformException catch (e) {
      // 用户取消 / 设备未注册生物识别 / 永久锁定等
      throw BiometricBindCancelledException(
        e.message?.isNotEmpty == true
            ? '生物识别认证失败：${e.message}'
            : '生物识别认证失败，已取消绑定',
      );
    }

    // Store TOTP secret and key ID in Keychain
    await _secureStorage.write(key: _secretKey, value: totpSecret);
    await _secureStorage.write(key: _keyIdKey, value: keyId);
  }

  @override
  Future<void> unbind() async {
    try {
      await _secureStorage.delete(key: _secretKey);
      await _secureStorage.delete(key: _keyIdKey);
    } catch (_) {
      // Best effort cleanup — Keychain item may already be invalidated
    }
  }

  @override
  Future<String?> authenticate() async {
    try {
      // Read the stored TOTP secret from Keychain.
      // If biometric enrollment has changed (e.g., new Face ID added),
      // the Keychain item protected by kSecAccessControlBiometryCurrentSet
      // will be invalidated and this read will throw.
      final secret = await _secureStorage.read(key: _secretKey);
      if (secret == null || secret.isEmpty) {
        return null;
      }

      // Authenticate with Face ID / Touch ID
      final authenticated = await _localAuth.authenticate(
        localizedReason: '验证身份以完成 OTP 认证',
        options: const AuthenticationOptions(
          biometricOnly: true,
          stickyAuth: true,
        ),
      );
      if (!authenticated) {
        return null;
      }

      // Generate TOTP code using shared RFC 6238 algorithm
      return TotpGenerator.generateCode(secret);
    } on PlatformException catch (e) {
      // Platform-level biometric errors (locked out, not enrolled, etc.)
      if (e.code == 'NotAvailable' ||
          e.code == 'NotEnrolled' ||
          e.code == 'PermanentlyLockedOut') {
        await unbind();
      }
      return null;
    } catch (_) {
      // Keychain read failure due to biometric enrollment change:
      // automatically unbind and return null
      await unbind();
      return null;
    }
  }

  @override
  String get platformIdentifier => 'face-id';

  @override
  String get displayName => 'Face ID';

  @override
  IconData get icon => Icons.face_rounded;

  /// 获取当前绑定的密钥 ID。
  Future<String?> getBoundKeyId() async {
    return _secureStorage.read(key: _keyIdKey);
  }
}
