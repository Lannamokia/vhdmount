part of '../../app.dart';

/// 清单打包结果。
class ManifestPackagerResult {
  const ManifestPackagerResult({
    required this.manifestPath,
    required this.signaturePath,
    required this.fileCount,
    required this.totalBytes,
  });

  final String manifestPath;
  final String signaturePath;
  final int fileCount;
  final int totalBytes;
}

/// Payload 目录不存在或不可访问时抛出。
class DirectoryNotFoundException implements Exception {
  DirectoryNotFoundException(this.message);
  final String message;

  @override
  String toString() => 'DirectoryNotFoundException: $message';
}

/// app-update 类型 payload 超过 1 GB 限制时抛出。
class PayloadTooLargeException implements Exception {
  PayloadTooLargeException(this.message);
  final String message;

  @override
  String toString() => 'PayloadTooLargeException: $message';
}

/// 清单打包与签名服务。
///
/// 扫描 payload 目录，生成 manifest.json 并用 RSA-PSS SHA-256 签名。
class ManifestPackagerService {
  /// app-update 类型的最大 payload 大小：1 GB。
  static const int maxAppUpdateBytes = 1073741824;

  /// 扫描 payload 目录，生成 manifest.json 并签名。
  ///
  /// [type] 为 'app-update' 或 'vhd-data'。
  /// [payloadDir] 为 payload 目录路径。
  /// [outputDir] 为输出目录路径。
  /// [privateKeyPath] 为 PKCS#8 PEM 私钥文件路径。
  /// [version] 为版本号。
  /// [minVersion] 为最小版本号。
  /// [signer] 为签名者标识。
  /// [onProgress] 为可选的进度回调。
  Future<ManifestPackagerResult> packageAndSign({
    required String type,
    required String payloadDir,
    required String outputDir,
    required String privateKeyPath,
    required String version,
    required String minVersion,
    required String signer,
    void Function(double progress, String step)? onProgress,
  }) async {
    void report(double progress, String step) {
      onProgress?.call(progress, step);
    }

    // 1. 验证输入
    report(0.0, '验证输入...');

    // 验证 payload 目录存在且可访问
    final payloadDirectory = Directory(payloadDir);
    if (!await payloadDirectory.exists()) {
      throw DirectoryNotFoundException(
        'Payload 目录不存在或不可访问: $payloadDir',
      );
    }

    // 验证私钥文件存在
    final keyFile = File(privateKeyPath);
    if (!await keyFile.exists()) {
      throw FileSystemException('未找到指定的私钥文件', privateKeyPath);
    }

    // 加载并验证私钥
    report(0.05, '加载私钥...');
    final pemCodec = PemCodec();
    final pemText = await keyFile.readAsString();
    final Uint8List privateKeyBytes;
    try {
      privateKeyBytes = pemCodec.decode(pemText, 'PRIVATE KEY');
    } on PemFormatException catch (e) {
      throw PemFormatException('私钥文件无效: ${e.message}');
    }
    final privateKey = _parseManifestPkcs8PrivateKey(privateKeyBytes);

    // 2. 递归扫描 payload 目录中的所有文件
    report(0.10, '扫描文件...');
    final files = <File>[];
    await for (final entity in payloadDirectory.list(recursive: true)) {
      if (entity is File) {
        files.add(entity);
      }
    }

    // 3. 计算每个文件的 SHA-256 哈希和大小
    report(0.15, '计算文件哈希...');
    final fileEntries = <Map<String, dynamic>>[];
    var totalBytes = 0;

    for (var i = 0; i < files.length; i++) {
      final file = files[i];
      final fileBytes = await file.readAsBytes();
      final fileSize = fileBytes.length;
      totalBytes += fileSize;

      // app-update 类型强制最大 1 GB 限制
      if (type == 'app-update' && totalBytes > maxAppUpdateBytes) {
        throw PayloadTooLargeException(
          'app-update 总 payload 大小超过 1 GB 限制 '
          '(当前: $totalBytes 字节)。请改用 vhd-data 类型。',
        );
      }

      // 计算 SHA-256 哈希
      final digest = pc.SHA256Digest();
      final hash = digest.process(Uint8List.fromList(fileBytes));
      final sha256Hex = hash
          .map((b) => b.toRadixString(16).padLeft(2, '0'))
          .join();

      // 规范化路径为正斜杠
      var relativePath = file.path.substring(payloadDir.length);
      if (relativePath.startsWith(Platform.pathSeparator) ||
          relativePath.startsWith('/') ||
          relativePath.startsWith('\\')) {
        relativePath = relativePath.substring(1);
      }
      relativePath = relativePath.replaceAll('\\', '/');

      fileEntries.add({
        'path': relativePath,
        'target': relativePath,
        'size': fileSize,
        'sha256': sha256Hex,
      });

      // 报告进度
      final scanProgress = 0.15 + (0.55 * (i + 1) / files.length);
      report(scanProgress, '计算文件哈希 (${i + 1}/${files.length})...');
    }

    // 4. 生成 manifest.json
    report(0.75, '生成 manifest.json...');
    final createdAt = DateTime.now().toUtc();
    final expiresAt = createdAt.add(const Duration(days: 3));

    final manifest = <String, dynamic>{
      'version': version,
      'minVersion': minVersion,
      'type': type,
      'signer': signer,
      'createdAt': createdAt.toIso8601String(),
      'expiresAt': expiresAt.toIso8601String(),
      'files': fileEntries,
    };

    final manifestJson = const JsonEncoder.withIndent('  ').convert(manifest);
    final manifestBytes = utf8.encode(manifestJson);

    // 5. 写入 manifest.json
    report(0.80, '写入 manifest.json...');
    await Directory(outputDir).create(recursive: true);
    final manifestPath =
        '$outputDir${Platform.pathSeparator}manifest.json';
    await File(manifestPath).writeAsString(manifestJson, encoding: utf8);

    // 6. RSA-PSS SHA-256 签名 manifest.json 字节
    report(0.90, 'RSA-PSS 签名...');
    final signature = _signManifestPssSha256(
      Uint8List.fromList(manifestBytes),
      privateKey,
    );

    // 7. 将签名以 base64 写入 manifest.sig
    final signaturePath =
        '$outputDir${Platform.pathSeparator}manifest.sig';
    await File(signaturePath).writeAsString(
      base64Encode(signature),
      encoding: utf8,
    );

    report(1.0, '完成');
    return ManifestPackagerResult(
      manifestPath: manifestPath,
      signaturePath: signaturePath,
      fileCount: files.length,
      totalBytes: totalBytes,
    );
  }

  /// 解析 PKCS#8 DER 编码的 RSA 私钥。
  pc.RSAPrivateKey _parseManifestPkcs8PrivateKey(Uint8List derBytes) {
    final parser = ASN1Parser(derBytes);
    final topLevelSeq = parser.nextObject() as ASN1Sequence;

    final privateKeyOctet = topLevelSeq.elements![2] as ASN1OctetString;

    final rsaParser = ASN1Parser(privateKeyOctet.octets!);
    final rsaSeq = rsaParser.nextObject() as ASN1Sequence;

    final modulus = (rsaSeq.elements![1] as ASN1Integer).integer!;
    final privateExponent = (rsaSeq.elements![3] as ASN1Integer).integer!;
    final p = (rsaSeq.elements![4] as ASN1Integer).integer!;
    final q = (rsaSeq.elements![5] as ASN1Integer).integer!;

    return pc.RSAPrivateKey(modulus, privateExponent, p, q);
  }

  /// 使用 RSA-PSS SHA-256 签名数据。
  Uint8List _signManifestPssSha256(
    Uint8List data,
    pc.RSAPrivateKey privateKey,
  ) {
    final signer = pc.PSSSigner(
      pc.RSAEngine(),
      pc.SHA256Digest(),
      pc.SHA256Digest(),
    );

    final random = pc.FortunaRandom();
    final seed = Uint8List.fromList(
      List.generate(32, (_) => Random.secure().nextInt(256)),
    );
    random.seed(pc.KeyParameter(seed));

    signer.init(
      true,
      pc.ParametersWithSaltConfiguration(
        pc.PrivateKeyParameter<pc.RSAPrivateKey>(privateKey),
        random,
        32,
      ),
    );

    final sig = signer.generateSignature(data);
    return sig.bytes;
  }
}
