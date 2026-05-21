import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:pointycastle/export.dart' as pc;
import 'package:vhd_mount_admin_flutter/app.dart';

/// Feature: admin-tools-flutter-migration, Property 16: 证书页面生成后自动导入
/// **Validates: Requirements 7.3, 7.4**
///
/// For any certificate generated via the certificates page "生成证书" button,
/// after generation succeeds, `addTrustedCertificate` should be automatically
/// called, and the certificatePem parameter should match the generated PEM file
/// content.
///
/// We test the integration logic at the service level:
/// 1. Generate a certificate using CertificateGeneratorService
/// 2. Read the PEM file
/// 3. Verify the PEM content is valid (can be decoded by PemCodec as CERTIFICATE)
/// 4. Verify the trust.json certificatePem field matches the PEM file content
///
/// This validates that the data flow from generation → auto-import would work
/// correctly.
void main() {
  group('Property 16: 证书页面生成后自动导入', () {
    test(
      '对任意生成的证书包，PEM 文件内容应为有效 CERTIFICATE PEM，'
      '且 trust.json 的 certificatePem 字段应与 PEM 文件内容一致',
      () async {
        final random = Random.secure();
        const iterations = 10;
        final codec = PemCodec();

        for (var i = 0; i < iterations; i++) {
          final tempDir = await Directory.systemTemp.createTemp(
            'certgen-prop16-$i-',
          );

          try {
            final service = CertificateGeneratorService();
            final validDays = random.nextInt(3650) + 1;
            final bundleName = 'auto-import-test-$i';
            final pfxPassword =
                'IntegPass${random.nextInt(99999999).toString().padLeft(8, '0')}';

            // Step 1: Generate certificate using CertificateGeneratorService
            final result = await service.generate(
              bundleName: bundleName,
              subjectCN: 'Auto Import Test $i',
              pfxPassword: pfxPassword,
              validDays: validDays,
              outputDir: tempDir.path,
            );

            // Step 2: Read the PEM file
            final pemFileContent = await File(result.pemPath).readAsString();
            // Trim trailing whitespace/newlines for comparison
            final pemFileTrimmed = pemFileContent.trim();

            // Step 3: Verify the PEM content is valid (can be decoded as CERTIFICATE)
            final derBytes = codec.decode(pemFileTrimmed, 'CERTIFICATE');
            expect(
              derBytes,
              isNotEmpty,
              reason: 'Iteration $i: PEM file should decode to non-empty DER bytes',
            );

            // Verify it starts with a SEQUENCE tag (0x30) — basic X.509 structure check
            expect(
              derBytes[0],
              equals(0x30),
              reason: 'Iteration $i: DER bytes should start with SEQUENCE tag (0x30)',
            );

            // Step 4: Read trust.json and verify certificatePem matches PEM file
            final trustJsonContent =
                await File(result.trustJsonPath).readAsString();
            final trustJson =
                jsonDecode(trustJsonContent) as Map<String, dynamic>;
            final trustCertPem = (trustJson['certificatePem'] as String).trim();

            expect(
              trustCertPem,
              equals(pemFileTrimmed),
              reason: 'Iteration $i: trust.json certificatePem should match '
                  'the PEM file content exactly. This ensures '
                  'addTrustedCertificate receives the correct PEM data.',
            );

            // Additional validation: verify the trust.json name matches bundleName
            expect(
              trustJson['name'],
              equals(bundleName),
              reason: 'Iteration $i: trust.json name should match bundleName, '
                  'which is the first argument to addTrustedCertificate.',
            );

            // Verify round-trip: re-encode the decoded DER should produce
            // equivalent PEM (proves the PEM is well-formed)
            final reEncoded = codec.encode(derBytes, 'CERTIFICATE');
            final reDecoded = codec.decode(reEncoded, 'CERTIFICATE');
            expect(
              reDecoded,
              equals(derBytes),
              reason: 'Iteration $i: PEM round-trip should preserve DER bytes',
            );
          } finally {
            if (await tempDir.exists()) {
              await tempDir.delete(recursive: true);
            }
          }
        }
      },
    );

    test(
      '生成的 certificatePem 可直接作为 addTrustedCertificate 的参数使用',
      () async {
        // This test simulates the exact data flow that happens in the UI:
        // CertificateGeneratorService.generate() → read trust.json →
        // extract name + certificatePem → call addTrustedCertificate(name, pem)
        final tempDir = await Directory.systemTemp.createTemp(
          'certgen-prop16-flow-',
        );

        try {
          final service = CertificateGeneratorService();
          final codec = PemCodec();

          final result = await service.generate(
            bundleName: 'flow-test',
            subjectCN: 'Flow Test CN',
            pfxPassword: 'FlowTest12345',
            validDays: 365,
            outputDir: tempDir.path,
          );

          // Simulate what the UI does after generation:
          // 1. Read trust.json to get name and certificatePem
          final trustJsonContent =
              await File(result.trustJsonPath).readAsString();
          final trustJson =
              jsonDecode(trustJsonContent) as Map<String, dynamic>;
          final name = trustJson['name'] as String;
          final certificatePem = trustJson['certificatePem'] as String;

          // 2. Verify the PEM is a valid certificate that can be decoded
          final derBytes = codec.decode(certificatePem, 'CERTIFICATE');
          expect(derBytes, isNotEmpty);

          // 3. Verify the fingerprint in trust.json matches the DER SHA-256
          final digest = pc.SHA256Digest();
          final hash = digest.process(derBytes);
          final computedFingerprint = hash
              .map((b) => b.toRadixString(16).padLeft(2, '0'))
              .join()
              .toUpperCase();
          expect(
            trustJson['fingerprint256'],
            equals(computedFingerprint),
            reason: 'trust.json fingerprint256 should match SHA-256 of the '
                'certificate DER bytes from certificatePem',
          );

          // 4. Verify the PEM file on disk matches what's in trust.json
          final pemFileContent =
              (await File(result.pemPath).readAsString()).trim();
          expect(
            certificatePem.trim(),
            equals(pemFileContent),
            reason: 'certificatePem in trust.json should match the .pem file',
          );

          // 5. Verify name matches bundleName (the first arg to addTrustedCertificate)
          expect(name, equals('flow-test'));
        } finally {
          if (await tempDir.exists()) {
            await tempDir.delete(recursive: true);
          }
        }
      },
    );
  });
}
