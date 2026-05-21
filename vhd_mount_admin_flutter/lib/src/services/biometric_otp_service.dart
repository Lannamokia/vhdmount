part of '../../app.dart';

/// 跨平台生物识别 OTP 服务抽象接口。
/// 各平台提供具体实现，统一 API 供 OtpGuard 和设置页面调用。
abstract class BiometricOtpService {
  /// 检查当前设备是否支持生物识别。
  Future<bool> isAvailable();

  /// 检查是否已绑定 TOTP 密钥（本地有加密存储的密钥）。
  Future<bool> isBound();

  /// 绑定：将服务端返回的 TOTP 密钥通过生物识别认证后加密存储到本地。
  Future<void> bind({required String totpSecret, required String keyId});

  /// 解除绑定，清除本地存储的 TOTP 密钥。
  Future<void> unbind();

  /// 通过生物识别认证并自动生成 TOTP 验证码。
  /// 返回生成的 TOTP 验证码字符串，可直接用于服务端验证。
  /// 如果认证失败或被取消，返回 null。
  Future<String?> authenticate();

  /// 获取当前平台的生物识别类型标识。
  String get platformIdentifier;

  /// 获取当前平台的生物识别显示名称（用于 UI）。
  String get displayName;

  /// 获取当前平台的生物识别图标。
  IconData get icon;
}

/// 根据当前平台创建对应的 BiometricOtpService 实现。
/// 不支持的平台返回 null。
BiometricOtpService? createBiometricOtpService() {
  if (Platform.isWindows) return WindowsBiometricOtpService();
  if (Platform.isIOS) return IosBiometricOtpService();
  if (Platform.isAndroid) return AndroidBiometricOtpService();
  return null;
}

// WindowsBiometricOtpService 已移至 windows_biometric_otp_service.dart

/// iOS 平台生物识别 OTP 服务（Face ID / Touch ID + Keychain）。
/// 完整实现在 ios_biometric_otp_service.dart 中。

/// Android 平台生物识别 OTP 服务（BiometricPrompt + Android Keystore）。
/// 完整实现在 android_biometric_otp_service.dart 中。
