part of '../../app.dart';

/// 密钥生成结果，包含所有输出文件路径。
class KeyGeneratorResult {
  const KeyGeneratorResult({
    required this.privateKeyPath,
    required this.publicKeyPath,
    required this.trustedKeysPath,
  });

  final String privateKeyPath;
  final String publicKeyPath;
  final String trustedKeysPath;
}

/// 密钥标识符冲突异常。
class KeyIdConflictException implements Exception {
  KeyIdConflictException({required this.keyId})
    : message = "密钥标识符 '$keyId' 已被使用，请更换。";

  final String keyId;
  final String message;

  @override
  String toString() => 'KeyIdConflictException: $message';
}

/// 更新签名密钥生成服务。
///
/// 使用 pointycastle 生成 RSA 3072 位密钥对，
/// 导出 PKCS#8 PEM 私钥和 SPKI PEM 公钥，
/// 并将公钥追加到 trusted_keys.pem。
class KeyGeneratorService {
  /// 生成 RSA 3072 位密钥对并写入输出文件。
  ///
  /// [keyId] 为密钥标识符，默认为 'update-key-{yyyyMMdd}'。
  /// [outputDir] 为输出目录，不存在时自动创建。
  /// 如果 private_key_{keyId}.pem 已存在，抛出 [KeyIdConflictException]。
  /// 如果 keyId 包含无效字符，自动清理。
  Future<KeyGeneratorResult> generate({
    String? keyId,
    required String outputDir,
    void Function(double progress, String step)? onProgress,
  }) async {
    void report(double progress, String step) {
      onProgress?.call(progress, step);
    }

    // 1. 确定 keyId
    report(0.0, '验证输入...');
    final sanitizer = FileNameSanitizer();
    final now = DateTime.now().toUtc();
    final defaultKeyId =
        'update-key-${now.year.toString().padLeft(4, '0')}'
        '${now.month.toString().padLeft(2, '0')}'
        '${now.day.toString().padLeft(2, '0')}';
    final rawKeyId = keyId?.isNotEmpty == true ? keyId! : defaultKeyId;
    final sanitizedKeyId = sanitizer.sanitize(rawKeyId);

    // 2. 检查冲突
    final privateKeyFileName = 'private_key_$sanitizedKeyId.pem';
    final privateKeyFile = File(
      '$outputDir${Platform.pathSeparator}$privateKeyFileName',
    );
    if (await privateKeyFile.exists()) {
      throw KeyIdConflictException(keyId: sanitizedKeyId);
    }

    // 3. 生成 RSA 3072 位密钥对
    report(0.1, '生成 RSA 3072 位密钥对...');
    final keyPair = _generateRsaKeyPair(3072);
    final publicKey = keyPair.publicKey as pc.RSAPublicKey;
    final privateKey = keyPair.privateKey as pc.RSAPrivateKey;

    // 4. 编码密钥为 PEM
    report(0.7, '编码密钥...');
    final pemCodec = PemCodec();
    final privateKeyDer = _encodePrivateKeyPkcs8(privateKey);
    final publicKeyDer = _encodePublicKeySpki(publicKey);
    final privateKeyPem = pemCodec.encode(privateKeyDer, 'PRIVATE KEY');
    final publicKeyPem = pemCodec.encode(publicKeyDer, 'PUBLIC KEY');

    // 5. 原子写入：先写入临时目录，再移动到目标
    report(0.8, '写入文件...');
    final tempDir = Directory(
      '${Directory.systemTemp.path}${Platform.pathSeparator}'
      'vhd-keygen-${DateTime.now().millisecondsSinceEpoch}',
    );
    await tempDir.create(recursive: true);

    try {
      // 写入临时文件
      final tempPrivateKeyFile = File(
        '${tempDir.path}${Platform.pathSeparator}$privateKeyFileName',
      );
      final publicKeyFileName = 'public_key_$sanitizedKeyId.pem';
      final tempPublicKeyFile = File(
        '${tempDir.path}${Platform.pathSeparator}$publicKeyFileName',
      );
      final tempTrustedKeysFile = File(
        '${tempDir.path}${Platform.pathSeparator}trusted_keys.pem',
      );

      await tempPrivateKeyFile.writeAsString(
        '$privateKeyPem\n',
        encoding: utf8,
      );
      await tempPublicKeyFile.writeAsString(
        '$publicKeyPem\n',
        encoding: utf8,
      );

      // trusted_keys.pem：如果目标目录已有，先复制过来再追加
      final targetTrustedKeysFile = File(
        '$outputDir${Platform.pathSeparator}trusted_keys.pem',
      );
      if (await targetTrustedKeysFile.exists()) {
        final existingContent = await targetTrustedKeysFile.readAsString();
        final separator = existingContent.endsWith('\n') ? '' : '\n';
        await tempTrustedKeysFile.writeAsString(
          '$existingContent$separator$publicKeyPem\n',
          encoding: utf8,
        );
      } else {
        await tempTrustedKeysFile.writeAsString(
          '$publicKeyPem\n',
          encoding: utf8,
        );
      }

      // 6. 确保输出目录存在，然后移动文件
      report(0.9, '移动文件到输出目录...');
      await Directory(outputDir).create(recursive: true);

      final finalPrivateKeyPath =
          '$outputDir${Platform.pathSeparator}$privateKeyFileName';
      final finalPublicKeyPath =
          '$outputDir${Platform.pathSeparator}$publicKeyFileName';
      final finalTrustedKeysPath =
          '$outputDir${Platform.pathSeparator}trusted_keys.pem';

      await tempPrivateKeyFile.rename(finalPrivateKeyPath);
      await tempPublicKeyFile.rename(finalPublicKeyPath);
      // trusted_keys.pem 可能跨卷，用 copy + delete
      await tempTrustedKeysFile.copy(finalTrustedKeysPath);
      await tempTrustedKeysFile.delete();

      report(1.0, '完成');
      return KeyGeneratorResult(
        privateKeyPath: finalPrivateKeyPath,
        publicKeyPath: finalPublicKeyPath,
        trustedKeysPath: finalTrustedKeysPath,
      );
    } catch (e) {
      // 清理临时目录
      try {
        if (await tempDir.exists()) {
          await tempDir.delete(recursive: true);
        }
      } catch (_) {}
      rethrow;
    } finally {
      // 确保临时目录被清理
      try {
        if (await tempDir.exists()) {
          await tempDir.delete(recursive: true);
        }
      } catch (_) {}
    }
  }

  /// 生成 RSA 密钥对。
  pc.AsymmetricKeyPair<pc.PublicKey, pc.PrivateKey> _generateRsaKeyPair(
    int bitLength,
  ) {
    final random = pc.FortunaRandom();
    final seed = Uint8List.fromList(
      List.generate(32, (_) => Random.secure().nextInt(256)),
    );
    random.seed(pc.KeyParameter(seed));

    final keyGen = pc.RSAKeyGenerator()..init(
      pc.ParametersWithRandom(
        pc.RSAKeyGeneratorParameters(BigInt.from(65537), bitLength, 64),
        random,
      ),
    );

    return keyGen.generateKeyPair();
  }

  /// 将 RSA 私钥编码为 PKCS#8 DER 格式。
  ///
  /// PKCS#8 结构：
  /// SEQUENCE {
  ///   INTEGER 0 (version)
  ///   SEQUENCE { OID 1.2.840.113549.1.1.1, NULL } (AlgorithmIdentifier)
  ///   OCTET STRING { RSAPrivateKey }
  /// }
  Uint8List _encodePrivateKeyPkcs8(pc.RSAPrivateKey key) {
    // 编码 RSAPrivateKey (RFC 3447)
    final rsaPrivateKey = ASN1Sequence();
    rsaPrivateKey.add(ASN1Integer(BigInt.zero)); // version
    rsaPrivateKey.add(ASN1Integer(key.modulus!)); // modulus
    rsaPrivateKey.add(ASN1Integer(key.publicExponent!)); // publicExponent
    rsaPrivateKey.add(ASN1Integer(key.privateExponent!)); // privateExponent
    rsaPrivateKey.add(ASN1Integer(key.p!)); // prime1
    rsaPrivateKey.add(ASN1Integer(key.q!)); // prime2
    rsaPrivateKey.add(ASN1Integer(
      key.privateExponent! % (key.p! - BigInt.one),
    )); // exponent1
    rsaPrivateKey.add(ASN1Integer(
      key.privateExponent! % (key.q! - BigInt.one),
    )); // exponent2
    rsaPrivateKey.add(ASN1Integer(key.q!.modInverse(key.p!))); // coefficient

    // OID 1.2.840.113549.1.1.1 (rsaEncryption)
    final algorithmIdentifier = ASN1Sequence();
    algorithmIdentifier.add(ASN1ObjectIdentifier.fromName('rsaEncryption'));
    algorithmIdentifier.add(ASN1Null());

    // PKCS#8 wrapper
    final pkcs8 = ASN1Sequence();
    pkcs8.add(ASN1Integer(BigInt.zero)); // version
    pkcs8.add(algorithmIdentifier);
    pkcs8.add(ASN1OctetString(octets: rsaPrivateKey.encode()));

    return pkcs8.encode();
  }

  /// 将 RSA 公钥编码为 SPKI (SubjectPublicKeyInfo) DER 格式。
  ///
  /// SPKI 结构：
  /// SEQUENCE {
  ///   SEQUENCE { OID 1.2.840.113549.1.1.1, NULL } (AlgorithmIdentifier)
  ///   BIT STRING { RSAPublicKey }
  /// }
  Uint8List _encodePublicKeySpki(pc.RSAPublicKey key) {
    // 编码 RSAPublicKey (RFC 3447)
    final rsaPublicKey = ASN1Sequence();
    rsaPublicKey.add(ASN1Integer(key.modulus!)); // modulus
    rsaPublicKey.add(ASN1Integer(key.publicExponent!)); // publicExponent

    // OID 1.2.840.113549.1.1.1 (rsaEncryption)
    final algorithmIdentifier = ASN1Sequence();
    algorithmIdentifier.add(ASN1ObjectIdentifier.fromName('rsaEncryption'));
    algorithmIdentifier.add(ASN1Null());

    // BIT STRING wrapping: prepend 0x00 (no unused bits)
    final publicKeyBytes = rsaPublicKey.encode();
    final bitString = ASN1BitString(
      stringValues: Uint8List.fromList([0x00, ...publicKeyBytes]),
    );

    // SPKI wrapper
    final spki = ASN1Sequence();
    spki.add(algorithmIdentifier);
    spki.add(bitString);

    return spki.encode();
  }
}
