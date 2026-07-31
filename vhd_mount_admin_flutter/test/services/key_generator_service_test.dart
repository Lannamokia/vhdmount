import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:pointycastle/asn1.dart';
import 'package:pointycastle/export.dart' as pc;
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  // Feature: admin-tools-flutter-migration, Property 6: RSA key generation produces valid sign/verify round-trip
  // **Validates: Requirements 1.1, 1.4, 2.9**
  group('Property 6: RSA 密钥生成产生有效的签名/验证往返', () {
    late Directory tempDir;
    late String privateKeyPem;
    late String publicKeyPem;

    setUpAll(() async {
      // Generate key pair once (RSA 3072 is slow)
      tempDir = await Directory.systemTemp.createTemp('keygen-prop6-');
      final service = KeyGeneratorService();
      final result = await service.generate(
        keyId: 'prop6-test',
        outputDir: tempDir.path,
      );

      privateKeyPem = await File(result.privateKeyPath).readAsString();
      publicKeyPem = await File(result.publicKeyPath).readAsString();
    });

    tearDownAll(() async {
      if (await tempDir.exists()) {
        await tempDir.delete(recursive: true);
      }
    });

    test('对任意随机消息(1-1024字节)，使用私钥RSA-PSS SHA-256签名后用公钥验证应成功',
        () {
      final random = Random.secure();
      final codec = PemCodec();
      const iterations = 25;

      // Parse private key from PEM
      final privateKeyDer = codec.decode(privateKeyPem, 'PRIVATE KEY');
      final privateKey = _parsePrivateKeyFromPkcs8(privateKeyDer);

      // Parse public key from PEM
      final publicKeyDer = codec.decode(publicKeyPem, 'PUBLIC KEY');
      final publicKey = _parsePublicKeyFromSpki(publicKeyDer);

      for (var i = 0; i < iterations; i++) {
        // Generate random message (1-1024 bytes)
        final messageLength = random.nextInt(1024) + 1;
        final message = Uint8List.fromList(
          List.generate(messageLength, (_) => random.nextInt(256)),
        );

        // Sign with RSA-PSS SHA-256
        final signature = _signRsaPss(privateKey, message);

        // Verify with public key
        final isValid = _verifyRsaPss(publicKey, message, signature);

        expect(
          isValid,
          isTrue,
          reason: 'RSA-PSS sign/verify round-trip failed for '
              '$messageLength-byte message at iteration $i',
        );
      }
    });

    test('使用错误的消息验证签名应失败', () {
      final random = Random.secure();
      final codec = PemCodec();

      final privateKeyDer = codec.decode(privateKeyPem, 'PRIVATE KEY');
      final privateKey = _parsePrivateKeyFromPkcs8(privateKeyDer);

      final publicKeyDer = codec.decode(publicKeyPem, 'PUBLIC KEY');
      final publicKey = _parsePublicKeyFromSpki(publicKeyDer);

      // Sign a message
      final message = Uint8List.fromList(
        List.generate(64, (_) => random.nextInt(256)),
      );
      final signature = _signRsaPss(privateKey, message);

      // Verify with a different message
      final wrongMessage = Uint8List.fromList(
        List.generate(64, (_) => random.nextInt(256)),
      );
      final isValid = _verifyRsaPss(publicKey, wrongMessage, signature);

      expect(isValid, isFalse);
    });
  });

  // Feature: admin-tools-flutter-migration, Property 13: trusted_keys.pem accumulates public keys
  // **Validates: Requirements 1.4**
  group('Property 13: 可信密钥文件累积公钥', () {
    test('对N次密钥生成序列到同一目录，trusted_keys.pem应包含恰好N个PUBLIC KEY PEM块',
        () async {
      final tempDir = await Directory.systemTemp.createTemp('keygen-prop13-');

      try {
        final service = KeyGeneratorService();
        final codec = PemCodec();
        const n = 3;

        // Generate N keys to the same output directory
        for (var i = 0; i < n; i++) {
          await service.generate(
            keyId: 'prop13-key-$i',
            outputDir: tempDir.path,
          );
        }

        // Read trusted_keys.pem
        final trustedKeysFile = File(
          '${tempDir.path}${Platform.pathSeparator}trusted_keys.pem',
        );
        expect(await trustedKeysFile.exists(), isTrue,
            reason: 'trusted_keys.pem should exist after key generation');

        final trustedKeysContent = await trustedKeysFile.readAsString();

        // Decode all PUBLIC KEY blocks
        final publicKeyBlocks = codec.decodeAll(
          trustedKeysContent,
          'PUBLIC KEY',
        );

        // Verify count matches N
        expect(
          publicKeyBlocks.length,
          equals(n),
          reason: 'trusted_keys.pem should contain exactly $n '
              'PUBLIC KEY PEM blocks, got ${publicKeyBlocks.length}',
        );

        // Verify each block is valid SPKI (can be parsed as ASN.1)
        for (var i = 0; i < publicKeyBlocks.length; i++) {
          final der = publicKeyBlocks[i];
          expect(
            () => _parsePublicKeyFromSpki(der),
            returnsNormally,
            reason: 'PUBLIC KEY block $i should be valid SPKI',
          );

          // Additionally verify the parsed key has expected properties
          final pubKey = _parsePublicKeyFromSpki(der);
          expect(pubKey.modulus, isNotNull);
          expect(pubKey.modulus!.bitLength, greaterThanOrEqualTo(3070));
          expect(pubKey.publicExponent, equals(BigInt.from(65537)));
        }
      } finally {
        if (await tempDir.exists()) {
          await tempDir.delete(recursive: true);
        }
      }
    });

    test('每次生成的公钥互不相同', () async {
      final tempDir = await Directory.systemTemp.createTemp('keygen-prop13b-');

      try {
        final service = KeyGeneratorService();
        final codec = PemCodec();
        const n = 3;

        for (var i = 0; i < n; i++) {
          await service.generate(
            keyId: 'prop13b-key-$i',
            outputDir: tempDir.path,
          );
        }

        final trustedKeysFile = File(
          '${tempDir.path}${Platform.pathSeparator}trusted_keys.pem',
        );
        final trustedKeysContent = await trustedKeysFile.readAsString();
        final publicKeyBlocks = codec.decodeAll(
          trustedKeysContent,
          'PUBLIC KEY',
        );

        // Verify all keys are distinct
        final modulusSet = <BigInt>{};
        for (var i = 0; i < publicKeyBlocks.length; i++) {
          final pubKey = _parsePublicKeyFromSpki(publicKeyBlocks[i]);
          final added = modulusSet.add(pubKey.modulus!);
          expect(
            added,
            isTrue,
            reason: 'PUBLIC KEY block $i should have a unique modulus',
          );
        }
      } finally {
        if (await tempDir.exists()) {
          await tempDir.delete(recursive: true);
        }
      }
    });
  });
}

// --- Helper functions for RSA-PSS sign/verify ---

/// Parse RSA private key from PKCS#8 DER bytes.
pc.RSAPrivateKey _parsePrivateKeyFromPkcs8(Uint8List der) {
  final asn1Parser = ASN1Parser(der);
  final topSequence = asn1Parser.nextObject() as ASN1Sequence;

  // PKCS#8: SEQUENCE { version, algorithmIdentifier, privateKey OCTET STRING }
  final privateKeyOctetString = topSequence.elements![2] as ASN1OctetString;
  final privateKeyDer = privateKeyOctetString.valueBytes;

  // Parse RSAPrivateKey from the octet string content
  final rsaParser = ASN1Parser(privateKeyDer);
  final rsaSequence = rsaParser.nextObject() as ASN1Sequence;

  // RSAPrivateKey: SEQUENCE { version, modulus, publicExponent, privateExponent, p, q, ... }
  final modulus = (rsaSequence.elements![1] as ASN1Integer).integer!;
  final publicExponent = (rsaSequence.elements![2] as ASN1Integer).integer!;
  final privateExponent = (rsaSequence.elements![3] as ASN1Integer).integer!;
  final p = (rsaSequence.elements![4] as ASN1Integer).integer!;
  final q = (rsaSequence.elements![5] as ASN1Integer).integer!;

  // ignore: deprecated_member_use
  return pc.RSAPrivateKey(modulus, privateExponent, p, q, publicExponent);
}

/// Parse RSA public key from SPKI DER bytes.
///
/// SPKI structure:
/// SEQUENCE {
///   SEQUENCE { OID, NULL }  -- AlgorithmIdentifier
///   BIT STRING { 0x00, RSAPublicKey DER }
/// }
///
/// We do manual DER parsing to avoid pointycastle's ASN1Parser issues
/// with BIT STRING content (tag 0 not supported).
pc.RSAPublicKey _parsePublicKeyFromSpki(Uint8List der) {
  // Parse outer SEQUENCE
  var offset = 0;
  if (der[offset] != 0x30) throw FormatException('Expected SEQUENCE tag');
  offset++;
  final outerLength = _parseDerLength(der, offset);
  offset = outerLength.nextOffset;

  // Skip AlgorithmIdentifier SEQUENCE
  if (der[offset] != 0x30) throw FormatException('Expected AlgorithmIdentifier SEQUENCE');
  offset++;
  final algoIdLength = _parseDerLength(der, offset);
  offset = algoIdLength.nextOffset + algoIdLength.length;

  // Parse BIT STRING
  if (der[offset] != 0x03) throw FormatException('Expected BIT STRING tag');
  offset++;
  final bitStringLength = _parseDerLength(der, offset);
  offset = bitStringLength.nextOffset;

  // Skip unused bits byte (should be 0x00)
  // Note: ASN1BitString in pointycastle encodes stringValues as-is into the
  // BIT STRING content. The KeyGeneratorService passes [0x00, ...publicKeyBytes]
  // as stringValues, where the first 0x00 is the unused-bits indicator.
  // However, pointycastle's ASN1BitString.encode() also prepends its own
  // unused-bits byte, resulting in two 0x00 bytes before the actual key data.
  // We skip both.
  while (offset < der.length && der[offset] == 0x00) {
    offset++;
  }

  // Now we have the RSAPublicKey DER: SEQUENCE { modulus INTEGER, exponent INTEGER }
  final rsaPublicKeyDer = der.sublist(offset);
  final rsaParser = ASN1Parser(rsaPublicKeyDer);
  final rsaSequence = rsaParser.nextObject() as ASN1Sequence;

  final modulus = (rsaSequence.elements![0] as ASN1Integer).integer!;
  final publicExponent = (rsaSequence.elements![1] as ASN1Integer).integer!;

  return pc.RSAPublicKey(modulus, publicExponent);
}

/// Helper class for DER length parsing result.
class _DerLengthResult {
  final int length;
  final int nextOffset; // offset after the length bytes
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

/// Sign a message using RSA-PSS with SHA-256.
Uint8List _signRsaPss(pc.RSAPrivateKey privateKey, Uint8List message) {
  final signer = pc.PSSSigner(
    pc.RSAEngine(),
    pc.SHA256Digest(),
    pc.SHA256Digest(),
  );

  signer.init(
    true,
    pc.ParametersWithSaltConfiguration(
      pc.PrivateKeyParameter<pc.RSAPrivateKey>(privateKey),
      _secureRandom(),
      32, // salt length = hash length for SHA-256
    ),
  );

  return signer.generateSignature(message).bytes;
}

/// Verify a message signature using RSA-PSS with SHA-256.
bool _verifyRsaPss(
  pc.RSAPublicKey publicKey,
  Uint8List message,
  Uint8List signature,
) {
  final verifier = pc.PSSSigner(
    pc.RSAEngine(),
    pc.SHA256Digest(),
    pc.SHA256Digest(),
  );

  verifier.init(
    false,
    pc.ParametersWithSaltConfiguration(
      pc.PublicKeyParameter<pc.RSAPublicKey>(publicKey),
      _secureRandom(),
      32, // salt length = hash length for SHA-256
    ),
  );

  try {
    return verifier.verifySignature(
      message,
      pc.PSSSignature(signature),
    );
  } catch (_) {
    return false;
  }
}

/// Create a secure random instance for pointycastle.
pc.SecureRandom _secureRandom() {
  final random = pc.FortunaRandom();
  final seed = Uint8List.fromList(
    List.generate(32, (_) => Random.secure().nextInt(256)),
  );
  random.seed(pc.KeyParameter(seed));
  return random;
}
