part of '../../app.dart';

/// RFC 6238 TOTP 生成器（共享实现）。
///
/// 所有平台的 BiometricOtpService 实现共用此算法：
/// - 时间步长：30 秒
/// - 哈希算法：HMAC-SHA1
/// - 输出位数：6 位
/// - 计数器：floor(unixTimestamp / 30)
class TotpGenerator {
  TotpGenerator._();

  /// 根据 base32 编码的 TOTP 密钥生成当前时间窗口的验证码。
  ///
  /// [secret] 为 base32 编码的 TOTP 密钥。
  /// [timestamp] 可选，用于测试时注入固定时间。
  static String generateCode(String secret, {DateTime? timestamp}) {
    final time = timestamp ?? DateTime.now();
    final unixSeconds = time.millisecondsSinceEpoch ~/ 1000;
    final counter = unixSeconds ~/ 30;
    return _generateHotpCode(base32Decode(secret), counter, 6);
  }

  /// HOTP 核心算法（RFC 4226）。
  static String _generateHotpCode(
      Uint8List secretBytes, int counter, int digits) {
    // 将 counter 编码为 8 字节大端序
    final counterBytes = Uint8List(8);
    var c = counter;
    for (var i = 7; i >= 0; i--) {
      counterBytes[i] = c & 0xff;
      c >>= 8;
    }

    // HMAC-SHA1
    final hmac = pc.HMac(pc.SHA1Digest(), 64);
    hmac.init(pc.KeyParameter(secretBytes));
    final hash = Uint8List(hmac.macSize);
    hmac.update(counterBytes, 0, counterBytes.length);
    hmac.doFinal(hash, 0);

    // 动态截断（Dynamic Truncation）
    final offset = hash[hash.length - 1] & 0x0f;
    final binary = ((hash[offset] & 0x7f) << 24) |
        ((hash[offset + 1] & 0xff) << 16) |
        ((hash[offset + 2] & 0xff) << 8) |
        (hash[offset + 3] & 0xff);

    // 取模得到指定位数的 OTP
    final otp = binary % _pow10(digits);
    return otp.toString().padLeft(digits, '0');
  }

  /// 10 的 n 次方。
  static int _pow10(int n) {
    var result = 1;
    for (var i = 0; i < n; i++) {
      result *= 10;
    }
    return result;
  }

  /// Base32 解码（RFC 4648）。
  /// 支持大小写字母，忽略填充字符 '=' 和空格。
  static Uint8List base32Decode(String input) {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
    final cleaned = input.toUpperCase().replaceAll('=', '').replaceAll(' ', '');

    final output = <int>[];
    var buffer = 0;
    var bitsLeft = 0;

    for (var i = 0; i < cleaned.length; i++) {
      final value = alphabet.indexOf(cleaned[i]);
      if (value < 0) {
        throw FormatException(
            'Invalid base32 character: ${cleaned[i]} at position $i');
      }
      buffer = (buffer << 5) | value;
      bitsLeft += 5;
      if (bitsLeft >= 8) {
        bitsLeft -= 8;
        output.add((buffer >> bitsLeft) & 0xff);
      }
    }

    return Uint8List.fromList(output);
  }
}
