part of '../../app.dart';

/// 证书生成结果，包含所有输出文件路径。
class CertificateGeneratorResult {
  const CertificateGeneratorResult({
    required this.pfxPath,
    required this.pemPath,
    required this.trustJsonPath,
    required this.clientConfigPath,
  });

  final String pfxPath;
  final String pemPath;
  final String trustJsonPath;
  final String clientConfigPath;
}

/// 验证异常，用于输入参数校验失败。
class ValidationException implements Exception {
  ValidationException(this.message);
  final String message;

  @override
  String toString() => 'ValidationException: $message';
}

/// 证书生成异常。
class CertificateGenerationException implements Exception {
  CertificateGenerationException(this.message);
  final String message;

  @override
  String toString() => 'CertificateGenerationException: $message';
}

/// 预配置注册证书包生成服务。
///
/// 生成自签名 X.509 证书，导出 PFX/PEM/trust.json/client-config.ini。
/// 使用纯 Dart 实现（pointycastle + ASN.1 DER 编码）。
class CertificateGeneratorService {
  /// 默认包名称。
  static const String defaultBundleName = 'machine-registration';

  /// 默认证书主题通用名称。
  static const String defaultSubjectCN = 'VHDMount Machine Registration';

  /// 生成自签名 X.509 证书包。
  ///
  /// [bundleName] 包名称，默认为 "machine-registration"。
  /// [subjectCN] 证书主题 CN，默认为 "VHDMount Machine Registration"。
  /// [pfxPassword] PFX 密码，至少 8 字符。
  /// [validDays] 有效天数，1–3650。
  /// [outputDir] 输出目录，不存在时自动创建。
  /// [onProgress] 进度回调。
  Future<CertificateGeneratorResult> generate({
    String? bundleName,
    String? subjectCN,
    required String pfxPassword,
    required int validDays,
    required String outputDir,
    void Function(double progress, String step)? onProgress,
  }) async {
    void report(double progress, String step) {
      onProgress?.call(progress, step);
    }

    // 1. 验证输入
    report(0.0, '验证输入...');
    if (pfxPassword.length < 8) {
      throw ValidationException('PFX 密码长度至少为 8 位。');
    }
    if (validDays < 1 || validDays > 3650) {
      throw ValidationException('有效天数必须在 1 到 3650 之间。');
    }

    final sanitizer = FileNameSanitizer();
    final effectiveBundleName = sanitizer.sanitize(
      (bundleName != null && bundleName.isNotEmpty)
          ? bundleName
          : defaultBundleName,
    );
    final effectiveSubjectCN =
        (subjectCN != null && subjectCN.isNotEmpty)
            ? subjectCN
            : defaultSubjectCN;

    // 2. 生成 RSA 3072 位密钥对
    report(0.1, '生成 RSA 3072 位密钥对...');
    final keyPair = _generateRsaKeyPair(3072);
    final publicKey = keyPair.publicKey as pc.RSAPublicKey;
    final privateKey = keyPair.privateKey as pc.RSAPrivateKey;

    // 3. 构建 X.509 证书
    report(0.4, '构建 X.509 证书...');
    final now = DateTime.now().toUtc();
    final notBefore = now.subtract(const Duration(minutes: 5));
    final notAfter = notBefore.add(Duration(days: validDays));

    final certDer = _buildX509Certificate(
      publicKey: publicKey,
      privateKey: privateKey,
      subjectCN: effectiveSubjectCN,
      notBefore: notBefore,
      notAfter: notAfter,
    );

    // 4. 生成 PFX
    report(0.6, '生成 PFX 文件...');
    final pfxBytes = _buildPkcs12(
      certDer: certDer,
      privateKey: privateKey,
      password: pfxPassword,
    );

    // 5. 生成 PEM 证书
    report(0.7, '生成 PEM 证书...');
    final pemCodec = PemCodec();
    final certPem = pemCodec.encode(certDer, 'CERTIFICATE');

    // 6. 计算指纹
    final fingerprint = _sha256Hex(certDer).toUpperCase();

    // 7. 生成 trust.json
    report(0.75, '生成 trust.json...');
    final trustJson = const JsonEncoder.withIndent('  ').convert({
      'name': effectiveBundleName,
      'subject': 'CN=$effectiveSubjectCN',
      'fingerprint256': fingerprint,
      'validFrom': notBefore.toIso8601String(),
      'validTo': notAfter.toIso8601String(),
      'certificatePem': certPem,
    });

    // 8. 生成 client-config.ini
    report(0.8, '生成 client-config.ini...');
    final clientConfig = '; 将以下内容添加到 vhdmonter_config.ini\r\n'
        'RegistrationCertificatePath=$effectiveBundleName.pfx\r\n'
        'RegistrationCertificatePassword=$pfxPassword\r\n';

    // 9. 原子写入
    report(0.85, '写入文件...');
    final tempDir = Directory(
      '${Directory.systemTemp.path}${Platform.pathSeparator}'
      'vhd-certgen-${DateTime.now().millisecondsSinceEpoch}',
    );
    await tempDir.create(recursive: true);

    try {
      final pfxFileName = '$effectiveBundleName.pfx';
      final pemFileName = '$effectiveBundleName.pem';
      final trustFileName = '$effectiveBundleName.trust.json';
      final configFileName = '$effectiveBundleName.client-config.ini';

      // 写入临时文件
      await File('${tempDir.path}${Platform.pathSeparator}$pfxFileName')
          .writeAsBytes(pfxBytes);
      await File('${tempDir.path}${Platform.pathSeparator}$pemFileName')
          .writeAsString('$certPem\n', encoding: utf8);
      await File('${tempDir.path}${Platform.pathSeparator}$trustFileName')
          .writeAsString('$trustJson\n', encoding: utf8);
      await File('${tempDir.path}${Platform.pathSeparator}$configFileName')
          .writeAsString(clientConfig, encoding: utf8);

      // 确保输出目录存在，然后移动文件
      report(0.9, '移动文件到输出目录...');
      await Directory(outputDir).create(recursive: true);

      final finalPfxPath =
          '$outputDir${Platform.pathSeparator}$pfxFileName';
      final finalPemPath =
          '$outputDir${Platform.pathSeparator}$pemFileName';
      final finalTrustPath =
          '$outputDir${Platform.pathSeparator}$trustFileName';
      final finalConfigPath =
          '$outputDir${Platform.pathSeparator}$configFileName';

      await File('${tempDir.path}${Platform.pathSeparator}$pfxFileName')
          .copy(finalPfxPath);
      await File('${tempDir.path}${Platform.pathSeparator}$pemFileName')
          .copy(finalPemPath);
      await File('${tempDir.path}${Platform.pathSeparator}$trustFileName')
          .copy(finalTrustPath);
      await File('${tempDir.path}${Platform.pathSeparator}$configFileName')
          .copy(finalConfigPath);

      report(1.0, '完成');
      return CertificateGeneratorResult(
        pfxPath: finalPfxPath,
        pemPath: finalPemPath,
        trustJsonPath: finalTrustPath,
        clientConfigPath: finalConfigPath,
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
      try {
        if (await tempDir.exists()) {
          await tempDir.delete(recursive: true);
        }
      } catch (_) {}
    }
  }

  // ─── Private helpers ───────────────────────────────────────────────

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

  /// 计算 SHA-256 哈希并返回小写十六进制字符串。
  String _sha256Hex(Uint8List data) {
    final digest = pc.SHA256Digest();
    final hash = digest.process(data);
    return hash.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  }

  /// 构建自签名 X.509 v3 证书 DER 编码。
  Uint8List _buildX509Certificate({
    required pc.RSAPublicKey publicKey,
    required pc.RSAPrivateKey privateKey,
    required String subjectCN,
    required DateTime notBefore,
    required DateTime notAfter,
  }) {
    // 构建 TBSCertificate
    final tbsCert = _buildTbsCertificate(
      publicKey: publicKey,
      subjectCN: subjectCN,
      notBefore: notBefore,
      notAfter: notAfter,
    );

    // 签名 TBSCertificate
    final tbsBytes = _encodeAsn1Sequence(tbsCert);
    final signature = _signPkcs1Sha256(tbsBytes, privateKey);

    // 构建完整 Certificate
    // Certificate ::= SEQUENCE {
    //   tbsCertificate     TBSCertificate,
    //   signatureAlgorithm AlgorithmIdentifier,
    //   signatureValue     BIT STRING
    // }
    final certElements = <Uint8List>[
      tbsBytes,
      _encodeSha256WithRsaAlgorithmIdentifier(),
      _encodeBitString(signature),
    ];

    return _encodeAsn1Sequence(certElements);
  }

  /// 构建 TBSCertificate 的各元素列表。
  List<Uint8List> _buildTbsCertificate({
    required pc.RSAPublicKey publicKey,
    required String subjectCN,
    required DateTime notBefore,
    required DateTime notAfter,
  }) {
    final elements = <Uint8List>[];

    // version [0] EXPLICIT INTEGER v3 (2)
    elements.add(_encodeExplicitTag(0, _encodeAsn1Integer(BigInt.from(2))));

    // serialNumber INTEGER (random 16 bytes)
    final serialBytes = Uint8List.fromList(
      List.generate(16, (_) => Random.secure().nextInt(256)),
    );
    // Ensure positive by clearing high bit
    serialBytes[0] &= 0x7F;
    if (serialBytes[0] == 0) serialBytes[0] = 1;
    elements.add(
      _encodeAsn1Integer(
        BigInt.parse(
          serialBytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join(),
          radix: 16,
        ),
      ),
    );

    // signature AlgorithmIdentifier (sha256WithRSAEncryption)
    elements.add(_encodeSha256WithRsaAlgorithmIdentifier());

    // issuer Name (same as subject for self-signed)
    elements.add(_encodeX509Name(subjectCN));

    // validity Validity { notBefore, notAfter }
    elements.add(_encodeValidity(notBefore, notAfter));

    // subject Name
    elements.add(_encodeX509Name(subjectCN));

    // subjectPublicKeyInfo
    elements.add(_encodeSubjectPublicKeyInfo(publicKey));

    // extensions [3] EXPLICIT SEQUENCE { ... }
    elements.add(_encodeExplicitTag(3, _encodeExtensions(publicKey)));

    return elements;
  }

  /// SHA-256 RSA PKCS#1 v1.5 签名。
  Uint8List _signPkcs1Sha256(Uint8List data, pc.RSAPrivateKey privateKey) {
    final signer = pc.Signer('SHA-256/RSA');
    signer.init(
      true,
      pc.PrivateKeyParameter<pc.RSAPrivateKey>(privateKey),
    );
    final sig = signer.generateSignature(data) as pc.RSASignature;
    return sig.bytes;
  }

  /// 编码 sha256WithRSAEncryption AlgorithmIdentifier。
  /// OID 1.2.840.113549.1.1.11
  Uint8List _encodeSha256WithRsaAlgorithmIdentifier() {
    // OID 1.2.840.113549.1.1.11 = 2a 86 48 86 f7 0d 01 01 0b
    final oid = Uint8List.fromList([
      0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x0b,
    ]);
    final nullValue = Uint8List.fromList([0x05, 0x00]);
    return _encodeAsn1SequenceFromRaw([oid, nullValue]);
  }

  /// 编码 X.509 Name (仅 CN)。
  Uint8List _encodeX509Name(String cn) {
    // Name ::= SEQUENCE { SET { SEQUENCE { OID, UTF8String } } }
    // OID 2.5.4.3 (commonName) = 55 04 03
    final oid = Uint8List.fromList([0x06, 0x03, 0x55, 0x04, 0x03]);
    final cnBytes = utf8.encode(cn);
    final utf8String = _encodeAsn1Tagged(0x0C, Uint8List.fromList(cnBytes));
    final attrSeq = _encodeAsn1SequenceFromRaw([oid, utf8String]);
    final rdn = _encodeAsn1Set([attrSeq]);
    return _encodeAsn1SequenceFromRaw([rdn]);
  }

  /// 编码 Validity { notBefore UTCTime, notAfter UTCTime }。
  Uint8List _encodeValidity(DateTime notBefore, DateTime notAfter) {
    return _encodeAsn1SequenceFromRaw([
      _encodeUtcTime(notBefore),
      _encodeUtcTime(notAfter),
    ]);
  }

  /// 编码 UTCTime (用于 2000-2049 年份)。
  /// 格式: YYMMDDHHMMSSZ
  Uint8List _encodeUtcTime(DateTime dt) {
    final year = (dt.year % 100).toString().padLeft(2, '0');
    final month = dt.month.toString().padLeft(2, '0');
    final day = dt.day.toString().padLeft(2, '0');
    final hour = dt.hour.toString().padLeft(2, '0');
    final minute = dt.minute.toString().padLeft(2, '0');
    final second = dt.second.toString().padLeft(2, '0');
    final timeStr = '$year$month$day$hour$minute${second}Z';
    final bytes = Uint8List.fromList(ascii.encode(timeStr));
    return _encodeAsn1Tagged(0x17, bytes);
  }

  /// 编码 SubjectPublicKeyInfo。
  Uint8List _encodeSubjectPublicKeyInfo(pc.RSAPublicKey key) {
    // RSAPublicKey ::= SEQUENCE { modulus INTEGER, publicExponent INTEGER }
    final rsaPubKey = _encodeAsn1Sequence([
      _encodeAsn1Integer(key.modulus!),
      _encodeAsn1Integer(key.publicExponent!),
    ]);

    // SPKI 使用 rsaEncryption OID (1.2.840.113549.1.1.1)
    final rsaEncOid = Uint8List.fromList([
      0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01,
    ]);
    final nullValue = Uint8List.fromList([0x05, 0x00]);
    final spkiAlgId = _encodeAsn1SequenceFromRaw([rsaEncOid, nullValue]);

    final bitString = _encodeBitString(rsaPubKey);
    return _encodeAsn1SequenceFromRaw([spkiAlgId, bitString]);
  }

  /// 编码 X.509 v3 扩展。
  Uint8List _encodeExtensions(pc.RSAPublicKey publicKey) {
    final extensions = <Uint8List>[];

    // Basic Constraints (CA=false), critical
    // OID 2.5.29.19
    final bcOid = Uint8List.fromList([0x06, 0x03, 0x55, 0x1D, 0x13]);
    final bcValue = _encodeAsn1SequenceFromRaw([]); // empty = CA:false
    final bcOctet = _encodeAsn1Tagged(0x04, bcValue);
    final bcCritical = _encodeAsn1Boolean(true);
    extensions.add(_encodeAsn1SequenceFromRaw([bcOid, bcCritical, bcOctet]));

    // Key Usage (DigitalSignature), critical
    // OID 2.5.29.15
    final kuOid = Uint8List.fromList([0x06, 0x03, 0x55, 0x1D, 0x0F]);
    // DigitalSignature = bit 0 set → byte 0x80, 7 unused bits
    final kuBits = Uint8List.fromList([0x03, 0x02, 0x07, 0x80]);
    final kuOctet = _encodeAsn1Tagged(0x04, kuBits);
    final kuCritical = _encodeAsn1Boolean(true);
    extensions.add(_encodeAsn1SequenceFromRaw([kuOid, kuCritical, kuOctet]));

    // Subject Key Identifier
    // OID 2.5.29.14
    final skiOid = Uint8List.fromList([0x06, 0x03, 0x55, 0x1D, 0x0E]);
    // SKI = SHA-1 of public key BIT STRING value
    final pubKeyDer = _encodeAsn1Sequence([
      _encodeAsn1Integer(publicKey.modulus!),
      _encodeAsn1Integer(publicKey.publicExponent!),
    ]);
    final sha1Digest = pc.SHA1Digest();
    final ski = sha1Digest.process(pubKeyDer);
    final skiOctetInner = _encodeAsn1Tagged(0x04, ski); // OCTET STRING
    final skiOctet = _encodeAsn1Tagged(0x04, skiOctetInner);
    extensions.add(_encodeAsn1SequenceFromRaw([skiOid, skiOctet]));

    return _encodeAsn1SequenceFromRaw(extensions.map((e) => e).toList());
  }

  /// 构建简化的 PKCS#12 (PFX) DER 结构。
  ///
  /// 使用 PBE-SHA1-3DES (OID 1.2.840.113549.1.12.1.3) 加密私钥，
  /// SHA-1 HMAC 作为 MAC 完整性校验。
  Uint8List _buildPkcs12({
    required Uint8List certDer,
    required pc.RSAPrivateKey privateKey,
    required String password,
  }) {
    final passwordBytes = _pkcs12PasswordToBytes(password);

    // 编码私钥为 PKCS#8
    final privateKeyDer = _encodePrivateKeyPkcs8(privateKey);

    // 构建 SafeBag for private key (PKCS8ShroudedKeyBag)
    final keySalt = _randomBytes(8);
    final keyIterations = 2048;
    final encryptedKeyData = _pbeEncrypt3DesSha1(
      privateKeyDer, passwordBytes, keySalt, keyIterations,
    );
    final keyBag = _buildPkcs8ShroudedKeyBag(
      encryptedKeyData, keySalt, keyIterations,
    );

    // 构建 SafeBag for certificate (CertBag)
    final certBag = _buildCertBag(certDer);

    // 构建 AuthenticatedSafe (两个 ContentInfo)
    // ContentInfo 1: 加密的私钥 (EncryptedData)
    final keyContent = _buildEncryptedDataContentInfo(keyBag);
    // ContentInfo 2: 明文证书 (Data)
    final certSafeContents = _encodeAsn1SequenceFromRaw([certBag]);
    final certContent = _buildDataContentInfo(certSafeContents);

    final authSafe = _encodeAsn1SequenceFromRaw([keyContent, certContent]);

    // 构建 PFX 的 ContentInfo (data OID + authSafe)
    final pfxContentInfo = _buildDataContentInfo(authSafe);

    // 计算 MAC
    final macSalt = _randomBytes(8);
    const macIterations = 2048;
    final macKey = _pkcs12DeriveKey(
      passwordBytes, macSalt, macIterations, 20, 3, // id=3 for MAC key
    );
    final hmac = pc.HMac(pc.SHA1Digest(), 64);
    hmac.init(pc.KeyParameter(macKey));
    final macValue = hmac.process(authSafe);

    // MacData
    final macData = _buildMacData(macValue, macSalt, macIterations);

    // PFX ::= SEQUENCE { version INTEGER 3, authSafe ContentInfo, macData }
    final pfx = _encodeAsn1SequenceFromRaw([
      _encodeAsn1Integer(BigInt.from(3)),
      pfxContentInfo,
      macData,
    ]);

    return pfx;
  }

  /// 将密码转换为 PKCS#12 BMPString 格式（UTF-16BE + 双零终止符）。
  Uint8List _pkcs12PasswordToBytes(String password) {
    final result = <int>[];
    for (final codeUnit in password.codeUnits) {
      result.add((codeUnit >> 8) & 0xFF);
      result.add(codeUnit & 0xFF);
    }
    result.add(0);
    result.add(0);
    return Uint8List.fromList(result);
  }

  Uint8List _randomBytes(int length) {
    return Uint8List.fromList(
      List.generate(length, (_) => Random.secure().nextInt(256)),
    );
  }

  /// PKCS#12 密钥派生函数 (RFC 7292 Appendix B)。
  Uint8List _pkcs12DeriveKey(
    Uint8List password, Uint8List salt, int iterations, int keyLen, int id,
  ) {
    const hashLen = 20; // SHA-1
    const blockLen = 64; // SHA-1 block size

    // D = id repeated blockLen times
    final d = Uint8List(blockLen);
    for (var i = 0; i < blockLen; i++) {
      d[i] = id;
    }

    // S = salt padded to multiple of blockLen
    Uint8List s;
    if (salt.isEmpty) {
      s = Uint8List(0);
    } else {
      final sLen = blockLen * ((salt.length + blockLen - 1) ~/ blockLen);
      s = Uint8List(sLen);
      for (var i = 0; i < sLen; i++) {
        s[i] = salt[i % salt.length];
      }
    }

    // P = password padded to multiple of blockLen
    Uint8List p;
    if (password.isEmpty) {
      p = Uint8List(0);
    } else {
      final pLen = blockLen * ((password.length + blockLen - 1) ~/ blockLen);
      p = Uint8List(pLen);
      for (var i = 0; i < pLen; i++) {
        p[i] = password[i % password.length];
      }
    }

    // I = S || P
    final input = Uint8List(s.length + p.length);
    input.setRange(0, s.length, s);
    input.setRange(s.length, input.length, p);

    final result = Uint8List(keyLen);
    var offset = 0;

    while (offset < keyLen) {
      // Hash D || I
      var hash = Uint8List(blockLen + input.length);
      hash.setRange(0, blockLen, d);
      hash.setRange(blockLen, hash.length, input);

      final sha1 = pc.SHA1Digest();
      var a = sha1.process(hash);
      for (var i = 1; i < iterations; i++) {
        a = pc.SHA1Digest().process(a);
      }

      final toCopy = min(hashLen, keyLen - offset);
      result.setRange(offset, offset + toCopy, a);
      offset += toCopy;

      if (offset >= keyLen) break;

      // Update I
      final b = Uint8List(blockLen);
      for (var i = 0; i < blockLen; i++) {
        b[i] = a[i % hashLen];
      }
      for (var i = 0; i < input.length; i += blockLen) {
        var carry = 1;
        for (var j = blockLen - 1; j >= 0; j--) {
          final sum = input[i + j] + b[j] + carry;
          input[i + j] = sum & 0xFF;
          carry = sum >> 8;
        }
      }
    }

    return result;
  }

  /// PBE-SHA1-3DES 加密 (PKCS#12 pbeWithSHAAnd3-KeyTripleDES-CBC)。
  Uint8List _pbeEncrypt3DesSha1(
    Uint8List data, Uint8List password, Uint8List salt, int iterations,
  ) {
    // Derive 24-byte key (id=1) and 8-byte IV (id=2)
    final key = _pkcs12DeriveKey(password, salt, iterations, 24, 1);
    final iv = _pkcs12DeriveKey(password, salt, iterations, 8, 2);

    // PKCS#7 padding
    final blockSize = 8;
    final padLen = blockSize - (data.length % blockSize);
    final padded = Uint8List(data.length + padLen);
    padded.setRange(0, data.length, data);
    for (var i = data.length; i < padded.length; i++) {
      padded[i] = padLen;
    }

    // 3DES-CBC encrypt
    final cipher = pc.CBCBlockCipher(pc.DESedeEngine());
    cipher.init(true, pc.ParametersWithIV(pc.KeyParameter(key), iv));

    final encrypted = Uint8List(padded.length);
    for (var i = 0; i < padded.length; i += blockSize) {
      cipher.processBlock(padded, i, encrypted, i);
    }

    return encrypted;
  }

  /// 构建 PKCS8ShroudedKeyBag。
  Uint8List _buildPkcs8ShroudedKeyBag(
    Uint8List encryptedData, Uint8List salt, int iterations,
  ) {
    // EncryptedPrivateKeyInfo ::= SEQUENCE {
    //   encryptionAlgorithm AlgorithmIdentifier,
    //   encryptedData OCTET STRING
    // }
    // AlgorithmIdentifier for pbeWithSHAAnd3-KeyTripleDES-CBC:
    // OID 1.2.840.113549.1.12.1.3
    final pbeOid = Uint8List.fromList([
      0x06, 0x0A, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x0C, 0x01, 0x03,
    ]);
    final pbeParams = _encodeAsn1SequenceFromRaw([
      _encodeAsn1Tagged(0x04, salt), // salt OCTET STRING
      _encodeAsn1Integer(BigInt.from(iterations)),
    ]);
    final algId = _encodeAsn1SequenceFromRaw([pbeOid, pbeParams]);
    final encPrivKeyInfo = _encodeAsn1SequenceFromRaw([
      algId,
      _encodeAsn1Tagged(0x04, encryptedData),
    ]);

    // SafeBag ::= SEQUENCE {
    //   bagId OID (pkcs8ShroudedKeyBag 1.2.840.113549.1.12.10.1.2),
    //   bagValue [0] EXPLICIT EncryptedPrivateKeyInfo
    // }
    final bagOid = Uint8List.fromList([
      0x06, 0x0B, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D,
      0x01, 0x0C, 0x0A, 0x01, 0x02,
    ]);
    final bagValue = _encodeExplicitTag(0, encPrivKeyInfo);
    return _encodeAsn1SequenceFromRaw([bagOid, bagValue]);
  }

  /// 构建 CertBag。
  Uint8List _buildCertBag(Uint8List certDer) {
    // CertBag ::= SEQUENCE {
    //   certId OID (x509Certificate 1.2.840.113549.1.9.22.1),
    //   certValue [0] EXPLICIT OCTET STRING { cert DER }
    // }
    final certBagOid = Uint8List.fromList([
      0x06, 0x0B, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D,
      0x01, 0x0C, 0x0A, 0x01, 0x03,
    ]);
    final x509CertOid = Uint8List.fromList([
      0x06, 0x0A, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D,
      0x01, 0x09, 0x16, 0x01,
    ]);
    final certOctet = _encodeAsn1Tagged(0x04, certDer);
    final certValueExplicit = _encodeExplicitTag(0, certOctet);
    final certBagInner = _encodeAsn1SequenceFromRaw([
      x509CertOid, certValueExplicit,
    ]);

    // SafeBag for cert
    final bagValue = _encodeExplicitTag(0, certBagInner);
    return _encodeAsn1SequenceFromRaw([certBagOid, bagValue]);
  }

  /// 构建 EncryptedData ContentInfo (用于私钥 bag)。
  Uint8List _buildEncryptedDataContentInfo(Uint8List safeBagData) {
    // ContentInfo ::= SEQUENCE {
    //   contentType OID (encryptedData 1.2.840.113549.1.7.6),
    //   content [0] EXPLICIT EncryptedData
    // }
    // EncryptedData ::= SEQUENCE {
    //   version INTEGER 0,
    //   encryptedContentInfo EncryptedContentInfo
    // }
    // EncryptedContentInfo ::= SEQUENCE {
    //   contentType OID (data 1.2.840.113549.1.7.1),
    //   contentEncryptionAlgorithm AlgorithmIdentifier,
    //   encryptedContent [0] IMPLICIT OCTET STRING
    // }
    // 简化：将 safeBag 作为明文 data 包装（不再二次加密）
    // 实际上 .NET 可以接受明文 SafeContents 在 data ContentInfo 中
    // 所以我们把私钥 bag 也放在 data ContentInfo 中
    final safeContents = _encodeAsn1SequenceFromRaw([safeBagData]);
    return _buildDataContentInfo(safeContents);
  }

  /// 构建 Data ContentInfo。
  Uint8List _buildDataContentInfo(Uint8List data) {
    // ContentInfo ::= SEQUENCE {
    //   contentType OID (data 1.2.840.113549.1.7.1),
    //   content [0] EXPLICIT OCTET STRING { data }
    // }
    final dataOid = Uint8List.fromList([
      0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x01,
    ]);
    final octetData = _encodeAsn1Tagged(0x04, data);
    final content = _encodeExplicitTag(0, octetData);
    return _encodeAsn1SequenceFromRaw([dataOid, content]);
  }

  /// 构建 MacData。
  Uint8List _buildMacData(Uint8List macValue, Uint8List salt, int iterations) {
    // MacData ::= SEQUENCE {
    //   mac DigestInfo,
    //   macSalt OCTET STRING,
    //   iterations INTEGER
    // }
    // DigestInfo ::= SEQUENCE {
    //   digestAlgorithm AlgorithmIdentifier (SHA-1),
    //   digest OCTET STRING
    // }
    final sha1Oid = Uint8List.fromList([
      0x06, 0x05, 0x2B, 0x0E, 0x03, 0x02, 0x1A,
    ]);
    final nullValue = Uint8List.fromList([0x05, 0x00]);
    final algId = _encodeAsn1SequenceFromRaw([sha1Oid, nullValue]);
    final digestInfo = _encodeAsn1SequenceFromRaw([
      algId,
      _encodeAsn1Tagged(0x04, macValue),
    ]);
    return _encodeAsn1SequenceFromRaw([
      digestInfo,
      _encodeAsn1Tagged(0x04, salt),
      _encodeAsn1Integer(BigInt.from(iterations)),
    ]);
  }

  /// 将 RSA 私钥编码为 PKCS#8 DER 格式。
  Uint8List _encodePrivateKeyPkcs8(pc.RSAPrivateKey key) {
    // RSAPrivateKey
    final rsaPrivateKey = _encodeAsn1Sequence([
      _encodeAsn1Integer(BigInt.zero), // version
      _encodeAsn1Integer(key.modulus!),
      _encodeAsn1Integer(key.publicExponent!),
      _encodeAsn1Integer(key.privateExponent!),
      _encodeAsn1Integer(key.p!),
      _encodeAsn1Integer(key.q!),
      _encodeAsn1Integer(key.privateExponent! % (key.p! - BigInt.one)),
      _encodeAsn1Integer(key.privateExponent! % (key.q! - BigInt.one)),
      _encodeAsn1Integer(key.q!.modInverse(key.p!)),
    ]);

    // AlgorithmIdentifier (rsaEncryption)
    final rsaEncOid = Uint8List.fromList([
      0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01,
    ]);
    final nullValue = Uint8List.fromList([0x05, 0x00]);
    final algId = _encodeAsn1SequenceFromRaw([rsaEncOid, nullValue]);

    // PKCS#8 wrapper
    return _encodeAsn1SequenceFromRaw([
      _encodeAsn1Integer(BigInt.zero), // version
      algId,
      _encodeAsn1Tagged(0x04, rsaPrivateKey), // OCTET STRING
    ]);
  }

  // ─── Low-level ASN.1 DER encoding primitives ──────────────────────

  /// 编码 ASN.1 长度字段。
  Uint8List _encodeLength(int length) {
    if (length < 0x80) {
      return Uint8List.fromList([length]);
    } else if (length < 0x100) {
      return Uint8List.fromList([0x81, length]);
    } else if (length < 0x10000) {
      return Uint8List.fromList([0x82, (length >> 8) & 0xFF, length & 0xFF]);
    } else if (length < 0x1000000) {
      return Uint8List.fromList([
        0x83, (length >> 16) & 0xFF, (length >> 8) & 0xFF, length & 0xFF,
      ]);
    } else {
      return Uint8List.fromList([
        0x84,
        (length >> 24) & 0xFF,
        (length >> 16) & 0xFF,
        (length >> 8) & 0xFF,
        length & 0xFF,
      ]);
    }
  }

  /// 编码带标签的 ASN.1 值（原始标签字节 + 长度 + 内容）。
  Uint8List _encodeAsn1Tagged(int tag, Uint8List content) {
    final len = _encodeLength(content.length);
    final result = Uint8List(1 + len.length + content.length);
    result[0] = tag;
    result.setRange(1, 1 + len.length, len);
    result.setRange(1 + len.length, result.length, content);
    return result;
  }

  /// 编码 SEQUENCE，内容为已编码的元素列表。
  Uint8List _encodeAsn1Sequence(List<Uint8List> elements) {
    return _encodeAsn1SequenceFromRaw(elements);
  }

  /// 编码 SEQUENCE，内容为原始字节列表。
  Uint8List _encodeAsn1SequenceFromRaw(List<Uint8List> rawElements) {
    final totalLen = rawElements.fold<int>(0, (sum, e) => sum + e.length);
    final len = _encodeLength(totalLen);
    final result = Uint8List(1 + len.length + totalLen);
    result[0] = 0x30; // SEQUENCE tag
    result.setRange(1, 1 + len.length, len);
    var offset = 1 + len.length;
    for (final element in rawElements) {
      result.setRange(offset, offset + element.length, element);
      offset += element.length;
    }
    return result;
  }

  /// 编码 SET。
  Uint8List _encodeAsn1Set(List<Uint8List> rawElements) {
    final totalLen = rawElements.fold<int>(0, (sum, e) => sum + e.length);
    final len = _encodeLength(totalLen);
    final result = Uint8List(1 + len.length + totalLen);
    result[0] = 0x31; // SET tag
    result.setRange(1, 1 + len.length, len);
    var offset = 1 + len.length;
    for (final element in rawElements) {
      result.setRange(offset, offset + element.length, element);
      offset += element.length;
    }
    return result;
  }

  /// 编码 ASN.1 INTEGER。
  Uint8List _encodeAsn1Integer(BigInt value) {
    var bytes = _bigIntToBytes(value);
    // 确保正数的高位不是 1（否则会被解释为负数）
    if (value >= BigInt.zero && bytes.isNotEmpty && bytes[0] & 0x80 != 0) {
      bytes = Uint8List.fromList([0x00, ...bytes]);
    }
    return _encodeAsn1Tagged(0x02, bytes);
  }

  /// 编码 ASN.1 BOOLEAN。
  Uint8List _encodeAsn1Boolean(bool value) {
    return Uint8List.fromList([0x01, 0x01, value ? 0xFF : 0x00]);
  }

  /// 编码 BIT STRING（前置 0x00 表示无未使用位）。
  Uint8List _encodeBitString(Uint8List content) {
    final inner = Uint8List(content.length + 1);
    inner[0] = 0x00; // no unused bits
    inner.setRange(1, inner.length, content);
    return _encodeAsn1Tagged(0x03, inner);
  }

  /// 编码 EXPLICIT context tag [n]。
  Uint8List _encodeExplicitTag(int tagNumber, Uint8List content) {
    final tag = 0xA0 | tagNumber;
    final len = _encodeLength(content.length);
    final result = Uint8List(1 + len.length + content.length);
    result[0] = tag;
    result.setRange(1, 1 + len.length, len);
    result.setRange(1 + len.length, result.length, content);
    return result;
  }

  /// BigInt 转字节数组（大端序）。
  Uint8List _bigIntToBytes(BigInt value) {
    if (value == BigInt.zero) {
      return Uint8List.fromList([0]);
    }

    final isNegative = value < BigInt.zero;
    var v = isNegative ? -value : value;

    final bytes = <int>[];
    while (v > BigInt.zero) {
      bytes.add((v & BigInt.from(0xFF)).toInt());
      v >>= 8;
    }

    if (isNegative) {
      // Two's complement for negative
      for (var i = 0; i < bytes.length; i++) {
        bytes[i] = (~bytes[i]) & 0xFF;
      }
      var carry = 1;
      for (var i = 0; i < bytes.length && carry > 0; i++) {
        final sum = bytes[i] + carry;
        bytes[i] = sum & 0xFF;
        carry = sum >> 8;
      }
      if (carry > 0) bytes.add(1);
      // Ensure high bit is set for negative
      if (bytes.last & 0x80 == 0) bytes.add(0xFF);
    }

    return Uint8List.fromList(bytes.reversed.toList());
  }
}
