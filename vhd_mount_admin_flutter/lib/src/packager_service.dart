part of '../app.dart';

class PackagerResult {
  const PackagerResult({
    required this.zipPath,
    required this.sigPath,
    required this.zipSize,
  });

  final String zipPath;
  final String sigPath;
  final int zipSize;
}

class DeploymentPackager {
  Future<PackagerResult> packAndSign({
    required String type,
    String? installScriptPath,
    String? uninstallScriptPath,
    String? payloadDir,
    required String name,
    required String version,
    required String signer,
    String? targetPath,
    bool requiresAdmin = false,
    required String privateKeyPath,
    required String outputDir,
    void Function(double progress, String step)? onProgress,
  }) async {
    void report(double progress, String step) {
      onProgress?.call(progress, step);
    }

    // 1. 验证输入
    report(0.0, '验证输入...');
    final isSoftwareDeploy = type == 'software-deploy';
    final isFileDeploy = type == 'file-deploy';
    if (!isSoftwareDeploy && !isFileDeploy) {
      throw Exception('未知的部署类型: $type');
    }
    if (name.isEmpty) {
      throw Exception('包名称不能为空');
    }
    if (version.isEmpty) {
      throw Exception('版本号不能为空');
    }
    if (signer.isEmpty) {
      throw Exception('签名者不能为空');
    }
    if (!await File(privateKeyPath).exists()) {
      throw Exception('私钥文件不存在');
    }
    if (isSoftwareDeploy) {
      if (installScriptPath == null ||
          installScriptPath.isEmpty ||
          !await File(installScriptPath).exists()) {
        throw Exception('software-deploy 类型必须提供 install.ps1');
      }
      if (uninstallScriptPath == null ||
          uninstallScriptPath.isEmpty ||
          !await File(uninstallScriptPath).exists()) {
        throw Exception('software-deploy 类型必须提供 uninstall.ps1');
      }
    }
    if (isFileDeploy) {
      if (targetPath == null || targetPath.trim().isEmpty) {
        throw Exception('file-deploy 类型必须指定目标部署路径');
      }
      if (payloadDir == null ||
          payloadDir.isEmpty ||
          !await Directory(payloadDir).exists()) {
        throw Exception('file-deploy 类型必须提供存在的文件负载目录');
      }
    }
    if (payloadDir != null &&
        payloadDir.isNotEmpty &&
        !await Directory(payloadDir).exists()) {
      throw Exception('文件负载目录不存在');
    }

    // 2. 创建临时 staging 目录
    report(0.05, '准备临时目录...');
    final stagingDir = Directory(
      '${Directory.systemTemp.path}${Platform.pathSeparator}'
      'vhd-deploy-pack-${DateTime.now().millisecondsSinceEpoch}',
    );
    await stagingDir.create(recursive: true);

    try {
      // 3. 复制文件负载
      // software-deploy: 负载与 install/uninstall 脚本平铺到 staging 根目录
      // file-deploy: 复制到 staging/payload/ 子目录,机台端按此约定查找
      final payloadFiles = <File>[];
      if (payloadDir != null &&
          payloadDir.isNotEmpty &&
          await Directory(payloadDir).exists()) {
        await for (final entity in Directory(
          payloadDir,
        ).list(recursive: true)) {
          if (entity is File) payloadFiles.add(entity);
        }
      }

      final payloadDestRoot = isFileDeploy
          ? '${stagingDir.path}${Platform.pathSeparator}payload'
          : stagingDir.path;
      if (isFileDeploy) {
        await Directory(payloadDestRoot).create(recursive: true);
      }

      var copied = 0;
      for (final file in payloadFiles) {
        final relativePath = file.path.substring(payloadDir!.length + 1);
        final destPath =
            '$payloadDestRoot${Platform.pathSeparator}$relativePath';
        await File(destPath).parent.create(recursive: true);
        await file.copy(destPath);
        copied++;
        report(
          0.10 + (0.40 * copied / payloadFiles.length),
          '复制文件负载 ($copied/${payloadFiles.length})...',
        );
      }

      // 4. software-deploy 类型：复制脚本到根目录
      report(0.50, '复制脚本...');
      if (isSoftwareDeploy && installScriptPath != null) {
        final destPath =
            '${stagingDir.path}${Platform.pathSeparator}install.ps1';
        await File(installScriptPath).copy(destPath);
      }
      if (isSoftwareDeploy && uninstallScriptPath != null) {
        final destPath =
            '${stagingDir.path}${Platform.pathSeparator}uninstall.ps1';
        await File(uninstallScriptPath).copy(destPath);
      }

      // 5. 生成 deploy.json 元数据
      report(0.60, '生成 deploy.json...');
      final trimmedTargetPath = targetPath?.trim() ?? '';
      final manifest = <String, dynamic>{
        'name': name,
        'version': version,
        'type': type,
        'signer': signer,
        'createdAt': DateTime.now().toUtc().toIso8601String(),
        if (trimmedTargetPath.isNotEmpty) 'targetPath': trimmedTargetPath,
        if (requiresAdmin) 'requiresAdmin': true,
        if (isSoftwareDeploy) 'installScript': 'install.ps1',
        if (isSoftwareDeploy) 'uninstallScript': 'uninstall.ps1',
      };
      final manifestJson = const JsonEncoder.withIndent('  ').convert(manifest);
      final manifestPath =
          '${stagingDir.path}${Platform.pathSeparator}deploy.json';
      await File(manifestPath).writeAsString('$manifestJson\n', encoding: utf8);

      // 6. 打包成 ZIP
      report(0.70, '打包 ZIP...');
      final safeName = _sanitizeFileName('$name-$version');
      final zipPath = '$outputDir${Platform.pathSeparator}$safeName.zip';
      final sigPath = '$outputDir${Platform.pathSeparator}$safeName.zip.sig';

      await Directory(outputDir).create(recursive: true);
      if (await File(zipPath).exists()) {
        await File(zipPath).delete();
      }

      final archive = Archive();
      await for (final entity in stagingDir.list(recursive: true)) {
        if (entity is File) {
          var relativePath = entity.path.substring(stagingDir.path.length + 1);
          relativePath = relativePath.replaceAll('\\', '/');
          final bytes = await entity.readAsBytes();
          archive.addFile(ArchiveFile(relativePath, bytes.length, bytes));
        }
      }
      final zipEncoder = ZipEncoder();
      final zipBytes = zipEncoder.encode(archive);
      await File(zipPath).writeAsBytes(zipBytes);

      // 7. 签名 ZIP
      report(0.90, 'RSA-PSS 签名...');
      final privateKeyBytes = _readPem(privateKeyPath, 'PRIVATE KEY');
      final privateKey = _parsePkcs8PrivateKey(privateKeyBytes);
      final zipData = await File(zipPath).readAsBytes();
      final signature = _signPssSha256(Uint8List.fromList(zipData), privateKey);
      await File(
        sigPath,
      ).writeAsString(base64Encode(signature), encoding: utf8);

      // 8. 输出
      report(0.95, '写入输出文件...');
      final zipSize = await File(zipPath).length();

      report(1.0, '完成');
      return PackagerResult(
        zipPath: zipPath,
        sigPath: sigPath,
        zipSize: zipSize,
      );
    } finally {
      try {
        if (await stagingDir.exists()) {
          await stagingDir.delete(recursive: true);
        }
      } catch (_) {}
    }
  }

  String _sanitizeFileName(String value) {
    final invalidChars = RegExp(r'[<>:"/\|?*]');
    return value.replaceAll(invalidChars, '_');
  }

  Uint8List _readPem(String path, String type) {
    final text = File(path).readAsStringSync();
    final start = '-----BEGIN $type-----';
    final end = '-----END $type-----';
    final startIndex = text.indexOf(start);
    final endIndex = text.indexOf(end);
    if (startIndex < 0 || endIndex < 0) {
      throw Exception('PEM 文件中未找到 $type');
    }
    final base64Text = text
        .substring(startIndex + start.length, endIndex)
        .replaceAll('\r', '')
        .replaceAll('\n', '')
        .trim();
    return base64Decode(base64Text);
  }

  pc.RSAPrivateKey _parsePkcs8PrivateKey(Uint8List derBytes) {
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

  Uint8List _signPssSha256(Uint8List data, pc.RSAPrivateKey privateKey) {
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
