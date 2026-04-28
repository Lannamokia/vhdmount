import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:archive/archive.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pointycastle/asn1.dart';
import 'package:pointycastle/export.dart' as pc;

import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  late String tempDir;
  late String privateKeyPath;
  late String publicKeyPath;
  late String payloadDir;
  late String outputDir;

  setUpAll(() async {
    tempDir =
        '${Directory.systemTemp.path}${Platform.pathSeparator}'
        'vhd-packager-test-${DateTime.now().millisecondsSinceEpoch}';
    await Directory(tempDir).create(recursive: true);

    final keyPair = _generateRsaKeyPair();
    privateKeyPath = '$tempDir${Platform.pathSeparator}test_key.pem';
    publicKeyPath = '$tempDir${Platform.pathSeparator}test_pub.pem';

    final privateKeyPem = _exportPkcs8PrivateKeyPem(
      keyPair['private']! as pc.RSAPrivateKey,
    );
    final publicKeyPem = _exportPublicKeyPem(
      keyPair['public']! as pc.RSAPublicKey,
    );

    await File(privateKeyPath).writeAsString(privateKeyPem);
    await File(publicKeyPath).writeAsString(publicKeyPem);

    payloadDir = '$tempDir${Platform.pathSeparator}payload';
    await Directory(payloadDir).create(recursive: true);
    await File(
      '$payloadDir${Platform.pathSeparator}data.txt',
    ).writeAsString('test data');
    await Directory(
      '$payloadDir${Platform.pathSeparator}subdir',
    ).create(recursive: true);
    await File(
      '$payloadDir${Platform.pathSeparator}subdir'
      '${Platform.pathSeparator}nested.txt',
    ).writeAsString('nested data');
  });

  tearDownAll(() async {
    try {
      await Directory(tempDir).delete(recursive: true);
    } catch (_) {}
  });

  setUp(() async {
    outputDir =
        '$tempDir${Platform.pathSeparator}out-'
        '${DateTime.now().millisecondsSinceEpoch}';
    await Directory(outputDir).create(recursive: true);
  });

  tearDown(() async {
    try {
      await Directory(outputDir).delete(recursive: true);
    } catch (_) {}
  });

  test('PEM 解析提取 PKCS#8 私钥', () async {
    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'file-deploy',
      payloadDir: payloadDir,
      name: 'test',
      version: '1.0.0',
      signer: 'test',
      targetPath: r'C:\Test',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );
    expect(await File(result.zipPath).exists(), isTrue);
    expect(await File(result.sigPath).exists(), isTrue);
  });

  test('完整打包 software-deploy 类型', () async {
    final installScript = '$tempDir${Platform.pathSeparator}install.ps1';
    final uninstallScript = '$tempDir${Platform.pathSeparator}uninstall.ps1';
    await File(installScript).writeAsString('Write-Host "install"');
    await File(uninstallScript).writeAsString('Write-Host "uninstall"');

    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'software-deploy',
      installScriptPath: installScript,
      uninstallScriptPath: uninstallScript,
      payloadDir: payloadDir,
      name: 'TestApp',
      version: '2.0.0',
      signer: 'admin',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );

    expect(result.zipPath, contains('TestApp-2.0.0.zip'));
    expect(result.sigPath, contains('TestApp-2.0.0.zip.sig'));
    expect(await File(result.zipPath).exists(), isTrue);
    expect(await File(result.sigPath).exists(), isTrue);

    final zipBytes = await File(result.zipPath).readAsBytes();
    final archive = ZipDecoder().decodeBytes(zipBytes);
    final fileNames = archive.map((f) => f.name).toList();
    expect(fileNames, contains('deploy.json'));
    expect(fileNames, contains('install.ps1'));
    expect(fileNames, contains('uninstall.ps1'));
    expect(fileNames, contains('data.txt'));
    expect(fileNames, contains('subdir/nested.txt'));
  });

  test('file-deploy 类型不含脚本', () async {
    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'file-deploy',
      payloadDir: payloadDir,
      name: 'FilePack',
      version: '1.0.0',
      signer: 'admin',
      targetPath: r'C:\Games\MyGame',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );

    final zipBytes = await File(result.zipPath).readAsBytes();
    final archive = ZipDecoder().decodeBytes(zipBytes);
    final fileNames = archive.map((f) => f.name).toList();
    expect(fileNames, isNot(contains('install.ps1')));
    expect(fileNames, contains('deploy.json'));
  });

  test('签名可验证', () async {
    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'file-deploy',
      payloadDir: payloadDir,
      name: 'Signed',
      version: '1.0.0',
      signer: 'admin',
      targetPath: r'C:\Test',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );

    final zipBytes = await File(result.zipPath).readAsBytes();
    final sigBase64 = await File(result.sigPath).readAsString();
    final signature = base64Decode(sigBase64);

    final publicKey = _readPemPublicKey(publicKeyPath);
    final verifier = pc.PSSSigner(
      pc.RSAEngine(),
      pc.SHA256Digest(),
      pc.SHA256Digest(),
    );

    final random = pc.SecureRandom('Fortuna');
    random.seed(pc.KeyParameter(Uint8List(32)));

    verifier.init(
      false,
      pc.ParametersWithSaltConfiguration(
        pc.PublicKeyParameter<pc.RSAPublicKey>(publicKey),
        random,
        32,
      ),
    );

    final isValid = verifier.verifySignature(
      Uint8List.fromList(zipBytes),
      pc.PSSSignature(signature),
    );
    expect(isValid, isTrue);
  });

  test('deploy.json 字段正确', () async {
    final installScript = '$tempDir${Platform.pathSeparator}install.ps1';
    final uninstallScript = '$tempDir${Platform.pathSeparator}uninstall.ps1';
    await File(installScript).writeAsString('Write-Host "install"');
    await File(uninstallScript).writeAsString('Write-Host "uninstall"');

    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'software-deploy',
      installScriptPath: installScript,
      uninstallScriptPath: uninstallScript,
      name: 'MyApp',
      version: '3.1.4',
      signer: 'tester',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );

    final zipBytes = await File(result.zipPath).readAsBytes();
    final archive = ZipDecoder().decodeBytes(zipBytes);
    final deployFile = archive.firstWhere((f) => f.name == 'deploy.json');
    final deployJson =
        jsonDecode(utf8.decode(deployFile.content as List<int>))
            as Map<String, dynamic>;

    expect(deployJson['name'], 'MyApp');
    expect(deployJson['version'], '3.1.4');
    expect(deployJson['type'], 'software-deploy');
    expect(deployJson['signer'], 'tester');
    expect(deployJson['createdAt'], isNotNull);
    expect(deployJson['installScript'], 'install.ps1');
    expect(deployJson['uninstallScript'], 'uninstall.ps1');
  });

  test('deploy.json 包含 targetPath 和 requiresAdmin', () async {
    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'file-deploy',
      payloadDir: payloadDir,
      name: 'FilePack',
      version: '1.0.0',
      signer: 'admin',
      targetPath: r'C:\Games\MyGame',
      requiresAdmin: true,
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );

    final zipBytes = await File(result.zipPath).readAsBytes();
    final archive = ZipDecoder().decodeBytes(zipBytes);
    final deployFile = archive.firstWhere((f) => f.name == 'deploy.json');
    final deployJson =
        jsonDecode(utf8.decode(deployFile.content as List<int>))
            as Map<String, dynamic>;

    expect(deployJson['targetPath'], r'C:\Games\MyGame');
    expect(deployJson['requiresAdmin'], isTrue);
  });

  test('file-deploy payload 文件在 payload/ 子目录下', () async {
    final packager = DeploymentPackager();
    final result = await packager.packAndSign(
      type: 'file-deploy',
      payloadDir: payloadDir,
      name: 'FilePack',
      version: '1.0.0',
      signer: 'admin',
      targetPath: r'C:\Games\MyGame',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
    );

    final zipBytes = await File(result.zipPath).readAsBytes();
    final archive = ZipDecoder().decodeBytes(zipBytes);
    final fileNames = archive.map((f) => f.name).toList();

    expect(fileNames, contains('payload/data.txt'));
    expect(fileNames, contains('payload/subdir/nested.txt'));
    expect(fileNames, isNot(contains('data.txt')));
  });

  test('file-deploy 无 targetPath 抛出异常', () async {
    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'file-deploy',
        payloadDir: payloadDir,
        name: 'test',
        version: '1.0.0',
        signer: 'test',
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
      ),
      throwsException,
    );
  });

  test('进度回调被调用', () async {
    final progressUpdates = <double>[];
    final steps = <String>[];

    final packager = DeploymentPackager();
    await packager.packAndSign(
      type: 'file-deploy',
      payloadDir: payloadDir,
      name: 'Progress',
      version: '1.0.0',
      signer: 'test',
      targetPath: r'C:\Test',
      privateKeyPath: privateKeyPath,
      outputDir: outputDir,
      onProgress: (progress, step) {
        progressUpdates.add(progress);
        steps.add(step);
      },
    );

    expect(progressUpdates, isNotEmpty);
    expect(progressUpdates.first, 0.0);
    expect(progressUpdates.last, 1.0);
    expect(steps, isNotEmpty);
  });

  test('空包名抛出异常', () async {
    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'file-deploy',
        name: '',
        version: '1.0.0',
        signer: 'test',
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
      ),
      throwsException,
    );
  });

  test('software-deploy 无 install.ps1 抛出异常', () async {
    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'software-deploy',
        name: 'test',
        version: '1.0.0',
        signer: 'test',
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
      ),
      throwsException,
    );
  });

  test('software-deploy 无 uninstall.ps1 抛出异常', () async {
    final installScript = '$tempDir${Platform.pathSeparator}install.ps1';
    await File(installScript).writeAsString('Write-Host "install"');

    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'software-deploy',
        installScriptPath: installScript,
        name: 'test',
        version: '1.0.0',
        signer: 'test',
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
      ),
      throwsException,
    );
  });

  test('无效私钥文件抛出异常', () async {
    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'file-deploy',
        name: 'test',
        version: '1.0.0',
        signer: 'test',
        privateKeyPath: '$tempDir${Platform.pathSeparator}nonexistent.pem',
        outputDir: outputDir,
      ),
      throwsException,
    );
  });

  test('空版本号抛出异常', () async {
    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'file-deploy',
        name: 'test',
        version: '',
        signer: 'test',
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
      ),
      throwsException,
    );
  });

  test('空签名者抛出异常', () async {
    final packager = DeploymentPackager();
    expect(
      () => packager.packAndSign(
        type: 'file-deploy',
        name: 'test',
        version: '1.0.0',
        signer: '',
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
      ),
      throwsException,
    );
  });
}

Map<String, dynamic> _generateRsaKeyPair() {
  final secureRandom = pc.SecureRandom('Fortuna');
  final seed = Uint8List.fromList(
    List.generate(32, (_) => Random.secure().nextInt(256)),
  );
  secureRandom.seed(pc.KeyParameter(seed));

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
  return _toPem('PRIVATE KEY', pkDer);
}

String _exportPublicKeyPem(pc.RSAPublicKey publicKey) {
  final rsaSeq = ASN1Sequence();
  rsaSeq.add(ASN1Integer(publicKey.modulus!));
  rsaSeq.add(ASN1Integer(publicKey.exponent!));

  final rsaDer = rsaSeq.encode();
  return _toPem('PUBLIC KEY', rsaDer);
}

String _toPem(String type, Uint8List der) {
  final base64Str = base64Encode(der);
  final builder = StringBuffer();
  builder.writeln('-----BEGIN $type-----');
  for (var i = 0; i < base64Str.length; i += 64) {
    final end = (i + 64).clamp(0, base64Str.length);
    builder.writeln(base64Str.substring(i, end));
  }
  builder.writeln('-----END $type-----');
  return builder.toString();
}

pc.RSAPublicKey _readPemPublicKey(String path) {
  final text = File(path).readAsStringSync();
  const start = '-----BEGIN PUBLIC KEY-----';
  const end = '-----END PUBLIC KEY-----';
  final startIndex = text.indexOf(start);
  final endIndex = text.indexOf(end);
  final base64Text = text
      .substring(startIndex + start.length, endIndex)
      .replaceAll('\r', '')
      .replaceAll('\n', '')
      .trim();
  final der = base64Decode(base64Text);

  final parser = ASN1Parser(der);
  final seq = parser.nextObject() as ASN1Sequence;
  final modulus = (seq.elements![0] as ASN1Integer).integer!;
  final exponent = (seq.elements![1] as ASN1Integer).integer!;

  return pc.RSAPublicKey(modulus, exponent);
}
