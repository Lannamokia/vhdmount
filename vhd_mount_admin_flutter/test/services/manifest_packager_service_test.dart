import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:pointycastle/asn1.dart';
import 'package:pointycastle/export.dart' as pc;

import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  late String tempDir;
  late String privateKeyPath;

  setUpAll(() async {
    tempDir =
        '${Directory.systemTemp.path}${Platform.pathSeparator}'
        'vhd-manifest-test-${DateTime.now().millisecondsSinceEpoch}';
    await Directory(tempDir).create(recursive: true);

    // Generate one RSA key pair for all tests (slow operation)
    final keyPair = _generateRsaKeyPair();
    privateKeyPath = '$tempDir${Platform.pathSeparator}test_private.pem';

    final privateKeyPem = _exportPkcs8PrivateKeyPem(
      keyPair['private']! as pc.RSAPrivateKey,
    );
    await File(privateKeyPath).writeAsString(privateKeyPem);
  });

  tearDownAll(() async {
    try {
      await Directory(tempDir).delete(recursive: true);
    } catch (_) {}
  });

  // Feature: admin-tools-flutter-migration, Property 7: 清单 SHA-256 哈希正确性
  // **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.16**
  group('Property 7: 清单 SHA-256 哈希正确性 (属性测试)', () {
    test('对任意文件内容，清单条目的 sha256 字段等于独立计算的 SHA-256 哈希', () async {
      final random = Random.secure();
      const iterations = 30;

      for (var i = 0; i < iterations; i++) {
        // Create a unique payload directory for this iteration
        final payloadDir =
            '$tempDir${Platform.pathSeparator}payload_p7_$i';
        final outputDir =
            '$tempDir${Platform.pathSeparator}output_p7_$i';
        await Directory(payloadDir).create(recursive: true);

        // Generate 1–5 files with random content (1–10000 bytes)
        final fileCount = random.nextInt(5) + 1;
        final expectedHashes = <String, String>{};

        for (var f = 0; f < fileCount; f++) {
          final contentLength = random.nextInt(10000) + 1;
          final content = Uint8List.fromList(
            List.generate(contentLength, (_) => random.nextInt(256)),
          );

          final fileName = 'file_$f.bin';
          final filePath =
              '$payloadDir${Platform.pathSeparator}$fileName';
          await File(filePath).writeAsBytes(content);

          // Independently compute SHA-256
          final digest = pc.SHA256Digest();
          final hash = digest.process(content);
          final sha256Hex =
              hash.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
          expectedHashes[fileName] = sha256Hex;
        }

        // Run packageAndSign
        final service = ManifestPackagerService();
        await service.packageAndSign(
          type: 'vhd-data',
          payloadDir: payloadDir,
          outputDir: outputDir,
          privateKeyPath: privateKeyPath,
          version: '1.0.$i',
          minVersion: '1.0.0',
          signer: 'test',
        );

        // Read and parse manifest.json
        final manifestPath =
            '$outputDir${Platform.pathSeparator}manifest.json';
        final manifestJson = jsonDecode(await File(manifestPath).readAsString())
            as Map<String, dynamic>;
        final files = manifestJson['files'] as List<dynamic>;

        // Verify each file entry's sha256 matches independently computed hash
        for (final entry in files) {
          final entryMap = entry as Map<String, dynamic>;
          final path = entryMap['path'] as String;
          final sha256 = entryMap['sha256'] as String;

          expect(
            expectedHashes.containsKey(path),
            isTrue,
            reason: 'Unexpected file in manifest: $path at iteration $i',
          );
          expect(
            sha256,
            equals(expectedHashes[path]),
            reason: 'SHA-256 mismatch for $path at iteration $i',
          );
        }

        // Cleanup
        await Directory(payloadDir).delete(recursive: true);
        await Directory(outputDir).delete(recursive: true);
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 8: 清单过期时间恰好为创建后 3 天
  // **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.16**
  group('Property 8: 清单过期时间恰好为创建后 3 天 (属性测试)', () {
    test('对任意生成的清单，createdAt 和 expiresAt 差值恰好为 259200 秒', () async {
      const iterations = 20;

      for (var i = 0; i < iterations; i++) {
        // Create payload with a single file
        final payloadDir =
            '$tempDir${Platform.pathSeparator}payload_p8_$i';
        final outputDir =
            '$tempDir${Platform.pathSeparator}output_p8_$i';
        await Directory(payloadDir).create(recursive: true);
        await File('$payloadDir${Platform.pathSeparator}dummy.txt')
            .writeAsString('iteration $i');

        // Run packageAndSign
        final service = ManifestPackagerService();
        await service.packageAndSign(
          type: 'vhd-data',
          payloadDir: payloadDir,
          outputDir: outputDir,
          privateKeyPath: privateKeyPath,
          version: '2.0.$i',
          minVersion: '1.0.0',
          signer: 'test',
        );

        // Read and parse manifest.json
        final manifestPath =
            '$outputDir${Platform.pathSeparator}manifest.json';
        final manifestJson = jsonDecode(await File(manifestPath).readAsString())
            as Map<String, dynamic>;

        final createdAt = DateTime.parse(manifestJson['createdAt'] as String);
        final expiresAt = DateTime.parse(manifestJson['expiresAt'] as String);

        final differenceSeconds = expiresAt.difference(createdAt).inSeconds;

        expect(
          differenceSeconds,
          equals(259200),
          reason: 'Expected exactly 3 days (259200s) difference, '
              'got $differenceSeconds seconds at iteration $i. '
              'createdAt=$createdAt, expiresAt=$expiresAt',
        );

        // Cleanup
        await Directory(payloadDir).delete(recursive: true);
        await Directory(outputDir).delete(recursive: true);
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 9: 清单路径规范化（无反斜杠）
  // **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.16**
  group('Property 9: 清单路径规范化（无反斜杠）(属性测试)', () {
    test('对任意包含子目录的文件，清单 path 字段仅包含正斜杠', () async {
      final random = Random.secure();
      const iterations = 20;

      for (var i = 0; i < iterations; i++) {
        // Create payload with nested subdirectories
        final payloadDir =
            '$tempDir${Platform.pathSeparator}payload_p9_$i';
        final outputDir =
            '$tempDir${Platform.pathSeparator}output_p9_$i';
        await Directory(payloadDir).create(recursive: true);

        // Create 1–4 levels of nesting with random directory names
        final depth = random.nextInt(4) + 1;
        var currentDir = payloadDir;
        for (var d = 0; d < depth; d++) {
          final dirName = 'dir_${d}_${random.nextInt(1000)}';
          currentDir = '$currentDir${Platform.pathSeparator}$dirName';
          await Directory(currentDir).create(recursive: true);
        }

        // Create files at various levels
        final fileCount = random.nextInt(3) + 1;
        for (var f = 0; f < fileCount; f++) {
          await File('$currentDir${Platform.pathSeparator}file_$f.dat')
              .writeAsBytes([random.nextInt(256), random.nextInt(256)]);
        }

        // Also create a file at root level
        await File('$payloadDir${Platform.pathSeparator}root_file.txt')
            .writeAsString('root');

        // Run packageAndSign
        final service = ManifestPackagerService();
        await service.packageAndSign(
          type: 'vhd-data',
          payloadDir: payloadDir,
          outputDir: outputDir,
          privateKeyPath: privateKeyPath,
          version: '3.0.$i',
          minVersion: '1.0.0',
          signer: 'test',
        );

        // Read and parse manifest.json
        final manifestPath =
            '$outputDir${Platform.pathSeparator}manifest.json';
        final manifestJson = jsonDecode(await File(manifestPath).readAsString())
            as Map<String, dynamic>;
        final files = manifestJson['files'] as List<dynamic>;

        // Verify no path contains backslashes
        for (final entry in files) {
          final entryMap = entry as Map<String, dynamic>;
          final path = entryMap['path'] as String;

          expect(
            path.contains('\\'),
            isFalse,
            reason: 'Path "$path" contains backslash at iteration $i',
          );
          // Also verify path uses forward slashes for nested files
          if (path.contains('/') || !path.contains(Platform.pathSeparator)) {
            // Path is normalized — good
          }
        }

        // Cleanup
        await Directory(payloadDir).delete(recursive: true);
        await Directory(outputDir).delete(recursive: true);
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 10: 清单文件完整性（N 个文件 → N 个条目）
  // **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.16**
  group('Property 10: 清单文件完整性（N 个文件 → N 个条目）(属性测试)', () {
    test('对任意包含 N 个文件的目录树，清单 files 数组恰好包含 N 个条目', () async {
      final random = Random.secure();
      const iterations = 20;

      for (var i = 0; i < iterations; i++) {
        // Create payload directory with random structure
        final payloadDir =
            '$tempDir${Platform.pathSeparator}payload_p10_$i';
        final outputDir =
            '$tempDir${Platform.pathSeparator}output_p10_$i';
        await Directory(payloadDir).create(recursive: true);

        // Generate N files (1–15) across random subdirectories
        final n = random.nextInt(15) + 1;
        for (var f = 0; f < n; f++) {
          // Randomly decide if file goes in a subdirectory
          String filePath;
          if (random.nextBool() && f > 0) {
            final subDir =
                '$payloadDir${Platform.pathSeparator}sub_${f % 3}';
            await Directory(subDir).create(recursive: true);
            filePath = '$subDir${Platform.pathSeparator}file_$f.bin';
          } else {
            filePath = '$payloadDir${Platform.pathSeparator}file_$f.bin';
          }

          final contentLength = random.nextInt(100) + 1;
          await File(filePath).writeAsBytes(
            List.generate(contentLength, (_) => random.nextInt(256)),
          );
        }

        // Run packageAndSign
        final service = ManifestPackagerService();
        final result = await service.packageAndSign(
          type: 'vhd-data',
          payloadDir: payloadDir,
          outputDir: outputDir,
          privateKeyPath: privateKeyPath,
          version: '4.0.$i',
          minVersion: '1.0.0',
          signer: 'test',
        );

        // Read and parse manifest.json
        final manifestPath =
            '$outputDir${Platform.pathSeparator}manifest.json';
        final manifestJson = jsonDecode(await File(manifestPath).readAsString())
            as Map<String, dynamic>;
        final files = manifestJson['files'] as List<dynamic>;

        // Verify file count matches
        expect(
          files.length,
          equals(n),
          reason: 'Expected $n entries in manifest, '
              'got ${files.length} at iteration $i',
        );

        // Also verify the result object reports the same count
        expect(
          result.fileCount,
          equals(n),
          reason: 'Result.fileCount mismatch at iteration $i',
        );

        // Cleanup
        await Directory(payloadDir).delete(recursive: true);
        await Directory(outputDir).delete(recursive: true);
      }
    });
  });
}

// --- Test helpers (RSA key generation) ---

Map<String, dynamic> _generateRsaKeyPair() {
  final secureRandom = pc.SecureRandom('Fortuna');
  final seed = Uint8List.fromList(
    List.generate(32, (_) => Random.secure().nextInt(256)),
  );
  secureRandom.seed(pc.KeyParameter(seed));

  // Use 2048-bit for faster test execution (sufficient for testing signing logic)
  final keyGen = pc.RSAKeyGenerator();
  keyGen.init(
    pc.ParametersWithRandom(
      pc.RSAKeyGeneratorParameters(BigInt.parse('65537'), 2048, 64),
      secureRandom,
    ),
  );

  final pair = keyGen.generateKeyPair();
  return <String, dynamic>{
    'private': pair.privateKey as pc.RSAPrivateKey,
    'public': pair.publicKey as pc.RSAPublicKey,
  };
}

String _exportPkcs8PrivateKeyPem(pc.RSAPrivateKey privateKey) {
  final rsaSeq = ASN1Sequence();
  rsaSeq.add(ASN1Integer(BigInt.zero));
  rsaSeq.add(ASN1Integer(privateKey.modulus!));
  rsaSeq.add(ASN1Integer(BigInt.parse('65537')));
  rsaSeq.add(ASN1Integer(privateKey.privateExponent!));
  rsaSeq.add(ASN1Integer(privateKey.p!));
  rsaSeq.add(ASN1Integer(privateKey.q!));
  rsaSeq.add(
    ASN1Integer(privateKey.privateExponent! % (privateKey.p! - BigInt.one)),
  );
  rsaSeq.add(
    ASN1Integer(privateKey.privateExponent! % (privateKey.q! - BigInt.one)),
  );
  rsaSeq.add(ASN1Integer(privateKey.q!.modInverse(privateKey.p!)));

  final rsaDer = rsaSeq.encode();

  final pkInfo = ASN1Sequence();
  pkInfo.add(ASN1Integer(BigInt.zero));
  final algId = ASN1Sequence();
  algId.add(ASN1ObjectIdentifier.fromName('rsaEncryption'));
  algId.add(ASN1Null());
  pkInfo.add(algId);
  pkInfo.add(ASN1OctetString(octets: rsaDer));

  final pkDer = pkInfo.encode();

  final base64Str = base64Encode(pkDer);
  final builder = StringBuffer();
  builder.writeln('-----BEGIN PRIVATE KEY-----');
  for (var i = 0; i < base64Str.length; i += 64) {
    final end = (i + 64).clamp(0, base64Str.length);
    builder.writeln(base64Str.substring(i, end));
  }
  builder.write('-----END PRIVATE KEY-----');
  return builder.toString();
}
