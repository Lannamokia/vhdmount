import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  group('buildOtpauthUri', () {
    const secret = 'JBSWY3DPEHPK3PXP';
    const account = 'admin@example.com';
    const issuer = 'VHDMountServer';

    test('uses apple-otpauth scheme on iOS/macOS', () {
      final uri = buildOtpauthUri(
        secret: secret,
        account: account,
        issuer: issuer,
        scheme: 'apple-otpauth',
      );

      expect(uri.scheme, 'apple-otpauth');
      expect(uri.host, 'totp');
      expect(uri.path, '/VHDMountServer:admin%40example.com');
      expect(uri.queryParameters['secret'], secret);
      expect(uri.queryParameters['issuer'], issuer);
      expect(uri.queryParameters['digits'], '6');
      expect(uri.queryParameters['period'], '30');
    });

    test('uses otpauth scheme on Android', () {
      final uri = buildOtpauthUri(
        secret: secret,
        account: account,
        issuer: issuer,
        scheme: 'otpauth',
      );

      expect(uri.scheme, 'otpauth');
      expect(uri.host, 'totp');
      expect(uri.path, '/VHDMountServer:admin%40example.com');
    });

    test('uses otpauth scheme on Windows by default', () {
      // 不传入 scheme，在 Windows host 上测试默认行为。
      final uri = buildOtpauthUri(
        secret: secret,
        account: account,
        issuer: issuer,
      );

      expect(uri.scheme, 'otpauth');
    });

    test('encodes special characters in issuer and account', () {
      final uri = buildOtpauthUri(
        secret: secret,
        account: 'user@domain.com',
        issuer: 'My App/Org',
        scheme: 'otpauth',
      );

      expect(uri.path, '/My%20App%2FOrg:user%40domain.com');
    });

    test('supports custom digits and period', () {
      final uri = buildOtpauthUri(
        secret: secret,
        account: account,
        issuer: issuer,
        digits: 8,
        period: 60,
        scheme: 'otpauth',
      );

      expect(uri.queryParameters['digits'], '8');
      expect(uri.queryParameters['period'], '60');
    });

    test('produces valid full URI string for iOS', () {
      final uri = buildOtpauthUri(
        secret: secret,
        account: account,
        issuer: issuer,
        scheme: 'apple-otpauth',
      );

      expect(
        uri.toString(),
        'apple-otpauth://totp/VHDMountServer:admin%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=VHDMountServer&digits=6&period=30',
      );
    });

    test('produces valid full URI string for Android', () {
      final uri = buildOtpauthUri(
        secret: secret,
        account: account,
        issuer: issuer,
        scheme: 'otpauth',
      );

      expect(
        uri.toString(),
        'otpauth://totp/VHDMountServer:admin%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=VHDMountServer&digits=6&period=30',
      );
    });
  });

  group('launchOtpauthUrl', () {
    test('returns false when url_launcher cannot handle the URI', () async {
      // 使用一个不可能被系统处理的 scheme 来模拟跳转失败兜底。
      final result = await launchOtpauthUrl(
        secret: 'TESTSECRET',
        account: 'test',
        issuer: 'Test',
        scheme: 'nonexistent-scheme-that-no-one-handles',
      );

      expect(result, isFalse);
    });
  });
}
