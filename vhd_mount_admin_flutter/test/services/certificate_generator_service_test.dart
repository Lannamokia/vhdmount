import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:pointycastle/export.dart' as pc;
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  // Feature: admin-tools-flutter-migration, Property 11: 证书指纹匹配 DER SHA-256
  // **Validates: Requirements 4.7, 4.10**
  group('Property 11: 证书指纹匹配 DER SHA-256', () {
    test(
        '对任意生成的证书包，trust.json 的 fingerprint256 应等于 PEM 文件解码后 DER 字节的大写十六进制 SHA-256 哈希',
        () async {
      final random = Random.secure();
      const iterations = 10;

      for (var i = 0; i < iterations; i++) {
        final tempDir = await Directory.systemTemp.createTemp(
          'certgen-prop11-$i-',
        );

        try {
          final service = CertificateGeneratorService();
          final validDays = random.nextInt(3650) + 1;
          final bundleName = 'test-cert-$i';
          final pfxPassword = 'TestPass${random.nextInt(99999999).toString().padLeft(8, '0')}';

          final result = await service.generate(
            bundleName: bundleName,
            subjectCN: 'Test CN $i',
            pfxPassword: pfxPassword,
            validDays: validDays,
            outputDir: tempDir.path,
          );

          // Read PEM file and decode to DER
          final pemContent = await File(result.pemPath).readAsString();
          final codec = PemCodec();
          final certDer = codec.decode(pemContent, 'CERTIFICATE');

          // Compute SHA-256 of DER bytes, uppercase hex
          final digest = pc.SHA256Digest();
          final hash = digest.process(certDer);
          final computedFingerprint = hash
              .map((b) => b.toRadixString(16).padLeft(2, '0'))
              .join()
              .toUpperCase();

          // Read trust.json and parse fingerprint256
          final trustJsonContent =
              await File(result.trustJsonPath).readAsString();
          final trustJson =
              jsonDecode(trustJsonContent) as Map<String, dynamic>;
          final trustFingerprint = trustJson['fingerprint256'] as String;

          // Verify they match
          expect(
            trustFingerprint,
            equals(computedFingerprint),
            reason: 'Iteration $i: trust.json fingerprint256 '
                '($trustFingerprint) should equal SHA-256 of DER '
                '($computedFingerprint)',
          );
        } finally {
          if (await tempDir.exists()) {
            await tempDir.delete(recursive: true);
          }
        }
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 12: 证书有效期匹配指定天数
  // **Validates: Requirements 4.7, 4.10**
  group('Property 12: 证书有效期匹配指定天数', () {
    test(
        '对任意 validDays 值(1-3650)，生成的证书 notAfter - notBefore 应恰好等于该天数',
        () async {
      final random = Random.secure();
      const iterations = 12;

      for (var i = 0; i < iterations; i++) {
        final tempDir = await Directory.systemTemp.createTemp(
          'certgen-prop12-$i-',
        );

        try {
          final service = CertificateGeneratorService();
          final validDays = random.nextInt(3650) + 1;
          final bundleName = 'validity-test-$i';
          final pfxPassword = 'ValidPass${random.nextInt(99999999).toString().padLeft(8, '0')}';

          final result = await service.generate(
            bundleName: bundleName,
            subjectCN: 'Validity Test $i',
            pfxPassword: pfxPassword,
            validDays: validDays,
            outputDir: tempDir.path,
          );

          // Read PEM file and decode to DER
          final pemContent = await File(result.pemPath).readAsString();
          final codec = PemCodec();
          final certDer = codec.decode(pemContent, 'CERTIFICATE');

          // Parse X.509 DER to extract notBefore and notAfter
          final validity = _parseX509Validity(certDer);

          // Compute the difference in days
          final duration = validity.notAfter.difference(validity.notBefore);
          final actualDays = duration.inDays;

          expect(
            actualDays,
            equals(validDays),
            reason: 'Iteration $i: certificate validity duration '
                '($actualDays days) should equal validDays ($validDays). '
                'notBefore=${validity.notBefore.toIso8601String()}, '
                'notAfter=${validity.notAfter.toIso8601String()}',
          );
        } finally {
          if (await tempDir.exists()) {
            await tempDir.delete(recursive: true);
          }
        }
      }
    });
  });
}

// ─── X.509 DER parsing helpers ─────────────────────────────────────────────

/// Parsed validity period from an X.509 certificate.
class _X509Validity {
  final DateTime notBefore;
  final DateTime notAfter;
  _X509Validity(this.notBefore, this.notAfter);
}

/// Parse the Validity field (notBefore, notAfter) from an X.509 certificate DER.
///
/// X.509 Certificate structure:
/// SEQUENCE {
///   TBSCertificate SEQUENCE {
///     version [0] EXPLICIT INTEGER (optional in v1)
///     serialNumber INTEGER
///     signature AlgorithmIdentifier
///     issuer Name
///     validity SEQUENCE { notBefore Time, notAfter Time }
///     ...
///   }
///   ...
/// }
_X509Validity _parseX509Validity(Uint8List certDer) {
  var offset = 0;

  // Outer Certificate SEQUENCE
  offset = _expectTag(certDer, offset, 0x30);
  final certLen = _parseDerLength(certDer, offset);
  offset = certLen.nextOffset;

  // TBSCertificate SEQUENCE
  offset = _expectTag(certDer, offset, 0x30);
  final tbsLen = _parseDerLength(certDer, offset);
  offset = tbsLen.nextOffset;

  // version [0] EXPLICIT (context tag 0xA0)
  if (certDer[offset] == 0xA0) {
    offset++; // skip tag
    final versionLen = _parseDerLength(certDer, offset);
    offset = versionLen.nextOffset + versionLen.length;
  }

  // serialNumber INTEGER
  offset = _expectTag(certDer, offset, 0x02);
  final serialLen = _parseDerLength(certDer, offset);
  offset = serialLen.nextOffset + serialLen.length;

  // signature AlgorithmIdentifier SEQUENCE
  offset = _expectTag(certDer, offset, 0x30);
  final sigAlgLen = _parseDerLength(certDer, offset);
  offset = sigAlgLen.nextOffset + sigAlgLen.length;

  // issuer Name SEQUENCE
  offset = _expectTag(certDer, offset, 0x30);
  final issuerLen = _parseDerLength(certDer, offset);
  offset = issuerLen.nextOffset + issuerLen.length;

  // validity SEQUENCE { notBefore, notAfter }
  offset = _expectTag(certDer, offset, 0x30);
  final validityLen = _parseDerLength(certDer, offset);
  offset = validityLen.nextOffset;

  // notBefore (UTCTime or GeneralizedTime)
  final notBefore = _parseTime(certDer, offset);
  offset = notBefore.nextOffset;

  // notAfter (UTCTime or GeneralizedTime)
  final notAfter = _parseTime(certDer, offset);

  return _X509Validity(notBefore.value, notAfter.value);
}

/// Result of parsing a time value from DER.
class _TimeParseResult {
  final DateTime value;
  final int nextOffset;
  _TimeParseResult(this.value, this.nextOffset);
}

/// Parse a UTCTime (tag 0x17) or GeneralizedTime (tag 0x18) from DER.
_TimeParseResult _parseTime(Uint8List data, int offset) {
  final tag = data[offset];
  offset++;
  final len = _parseDerLength(data, offset);
  offset = len.nextOffset;

  final timeBytes = data.sublist(offset, offset + len.length);
  final timeStr = ascii.decode(timeBytes);

  DateTime dt;
  if (tag == 0x17) {
    // UTCTime: YYMMDDHHMMSSZ
    dt = _parseUtcTime(timeStr);
  } else if (tag == 0x18) {
    // GeneralizedTime: YYYYMMDDHHMMSSZ
    dt = _parseGeneralizedTime(timeStr);
  } else {
    throw FormatException('Unexpected time tag: 0x${tag.toRadixString(16)}');
  }

  return _TimeParseResult(dt, offset + len.length);
}

/// Parse UTCTime string (YYMMDDHHMMSSZ) to DateTime.
DateTime _parseUtcTime(String s) {
  // Remove trailing 'Z'
  final clean = s.endsWith('Z') ? s.substring(0, s.length - 1) : s;
  final year = int.parse(clean.substring(0, 2));
  final month = int.parse(clean.substring(2, 4));
  final day = int.parse(clean.substring(4, 6));
  final hour = int.parse(clean.substring(6, 8));
  final minute = int.parse(clean.substring(8, 10));
  final second = int.parse(clean.substring(10, 12));

  // RFC 5280: years 00-49 → 2000-2049, years 50-99 → 1950-1999
  final fullYear = year >= 50 ? 1900 + year : 2000 + year;

  return DateTime.utc(fullYear, month, day, hour, minute, second);
}

/// Parse GeneralizedTime string (YYYYMMDDHHMMSSZ) to DateTime.
DateTime _parseGeneralizedTime(String s) {
  final clean = s.endsWith('Z') ? s.substring(0, s.length - 1) : s;
  final year = int.parse(clean.substring(0, 4));
  final month = int.parse(clean.substring(4, 6));
  final day = int.parse(clean.substring(6, 8));
  final hour = int.parse(clean.substring(8, 10));
  final minute = int.parse(clean.substring(10, 12));
  final second = int.parse(clean.substring(12, 14));

  return DateTime.utc(year, month, day, hour, minute, second);
}

/// Expect a specific tag at the given offset.
int _expectTag(Uint8List data, int offset, int expectedTag) {
  if (data[offset] != expectedTag) {
    throw FormatException(
      'Expected tag 0x${expectedTag.toRadixString(16)} at offset $offset, '
      'got 0x${data[offset].toRadixString(16)}',
    );
  }
  return offset + 1;
}

/// DER length parsing result.
class _DerLengthResult {
  final int length;
  final int nextOffset;
  _DerLengthResult(this.length, this.nextOffset);
}

/// Parse a DER length field starting at [offset] in [data].
_DerLengthResult _parseDerLength(Uint8List data, int offset) {
  final firstByte = data[offset];
  if (firstByte < 0x80) {
    return _DerLengthResult(firstByte, offset + 1);
  } else {
    final numBytes = firstByte & 0x7F;
    var length = 0;
    for (var i = 0; i < numBytes; i++) {
      length = (length << 8) | data[offset + 1 + i];
    }
    return _DerLengthResult(length, offset + 1 + numBytes);
  }
}
