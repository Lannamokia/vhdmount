import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  late PemCodec codec;

  setUp(() {
    codec = PemCodec();
  });

  group('PemCodec.encode', () {
    test('生成标准 BEGIN/END 头尾行', () {
      final bytes = Uint8List.fromList([1, 2, 3]);
      final pem = codec.encode(bytes, 'PRIVATE KEY');

      expect(pem, startsWith('-----BEGIN PRIVATE KEY-----'));
      expect(pem, endsWith('-----END PRIVATE KEY-----'));
    });

    test('base64 正文每行不超过 64 字符', () {
      // 生成足够长的数据以产生多行 base64
      final bytes = Uint8List.fromList(List.generate(100, (i) => i));
      final pem = codec.encode(bytes, 'CERTIFICATE');

      final lines = pem.split('\n');
      // 跳过头尾行，检查正文行
      for (var i = 1; i < lines.length - 1; i++) {
        if (lines[i].isNotEmpty) {
          expect(lines[i].length, lessThanOrEqualTo(64));
        }
      }
    });

    test('支持 PRIVATE KEY 类型', () {
      final bytes = Uint8List.fromList([10, 20, 30]);
      final pem = codec.encode(bytes, 'PRIVATE KEY');
      expect(pem, contains('-----BEGIN PRIVATE KEY-----'));
      expect(pem, contains('-----END PRIVATE KEY-----'));
    });

    test('支持 PUBLIC KEY 类型', () {
      final bytes = Uint8List.fromList([10, 20, 30]);
      final pem = codec.encode(bytes, 'PUBLIC KEY');
      expect(pem, contains('-----BEGIN PUBLIC KEY-----'));
      expect(pem, contains('-----END PUBLIC KEY-----'));
    });

    test('支持 CERTIFICATE 类型', () {
      final bytes = Uint8List.fromList([10, 20, 30]);
      final pem = codec.encode(bytes, 'CERTIFICATE');
      expect(pem, contains('-----BEGIN CERTIFICATE-----'));
      expect(pem, contains('-----END CERTIFICATE-----'));
    });
  });

  group('PemCodec.decode', () {
    test('正确解码有效 PEM', () {
      final original = Uint8List.fromList([1, 2, 3, 4, 5]);
      final pem = codec.encode(original, 'PRIVATE KEY');
      final decoded = codec.decode(pem, 'PRIVATE KEY');
      expect(decoded, equals(original));
    });

    test('剥离空白字符后解码', () {
      // 用正确的 base64 测试
      final original = Uint8List.fromList([1, 2, 3, 4, 5, 6]);
      final base64Str = base64Encode(original);
      // 插入空白
      final withWhitespace = base64Str.split('').join(' ');
      final pem2 = '-----BEGIN PUBLIC KEY-----\n'
          '$withWhitespace\n'
          '-----END PUBLIC KEY-----';
      final decoded = codec.decode(pem2, 'PUBLIC KEY');
      expect(decoded, equals(original));
    });

    test('类型标签不匹配时抛出 PemFormatException', () {
      final pem = codec.encode(Uint8List.fromList([1, 2, 3]), 'PRIVATE KEY');
      expect(
        () => codec.decode(pem, 'PUBLIC KEY'),
        throwsA(isA<PemFormatException>()),
      );
    });

    test('缺少 END 标记时抛出 PemFormatException', () {
      const pem = '-----BEGIN PRIVATE KEY-----\nAQIDBA==\n';
      expect(
        () => codec.decode(pem, 'PRIVATE KEY'),
        throwsA(isA<PemFormatException>()),
      );
    });

    test('无效 base64 内容时抛出 PemFormatException', () {
      const pem = '-----BEGIN PRIVATE KEY-----\n'
          '!!!invalid-base64!!!\n'
          '-----END PRIVATE KEY-----';
      expect(
        () => codec.decode(pem, 'PRIVATE KEY'),
        throwsA(isA<PemFormatException>()),
      );
    });

    test('空正文时抛出 PemFormatException', () {
      const pem = '-----BEGIN PRIVATE KEY-----\n'
          '-----END PRIVATE KEY-----';
      expect(
        () => codec.decode(pem, 'PRIVATE KEY'),
        throwsA(isA<PemFormatException>()),
      );
    });

    test('处理包含制表符和回车符的 PEM', () {
      final original = Uint8List.fromList([10, 20, 30, 40, 50]);
      final base64Str = base64Encode(original);
      final pem = '-----BEGIN CERTIFICATE-----\r\n'
          '\t$base64Str\t\r\n'
          '-----END CERTIFICATE-----';
      final decoded = codec.decode(pem, 'CERTIFICATE');
      expect(decoded, equals(original));
    });
  });

  group('PemCodec.decodeAll', () {
    test('解码多个 PEM 块', () {
      final block1 = Uint8List.fromList([1, 2, 3]);
      final block2 = Uint8List.fromList([4, 5, 6]);
      final block3 = Uint8List.fromList([7, 8, 9]);

      final pem = [
        codec.encode(block1, 'CERTIFICATE'),
        codec.encode(block2, 'CERTIFICATE'),
        codec.encode(block3, 'CERTIFICATE'),
      ].join('\n');

      final results = codec.decodeAll(pem, 'CERTIFICATE');
      expect(results.length, 3);
      expect(results[0], equals(block1));
      expect(results[1], equals(block2));
      expect(results[2], equals(block3));
    });

    test('单个 PEM 块返回单元素列表', () {
      final block = Uint8List.fromList([1, 2, 3]);
      final pem = codec.encode(block, 'PUBLIC KEY');

      final results = codec.decodeAll(pem, 'PUBLIC KEY');
      expect(results.length, 1);
      expect(results[0], equals(block));
    });

    test('无匹配块时抛出 PemFormatException', () {
      final pem = codec.encode(Uint8List.fromList([1, 2, 3]), 'PRIVATE KEY');
      expect(
        () => codec.decodeAll(pem, 'CERTIFICATE'),
        throwsA(isA<PemFormatException>()),
      );
    });

    test('某块缺少 END 标记时抛出 PemFormatException', () {
      final block1 = codec.encode(Uint8List.fromList([1, 2, 3]), 'PUBLIC KEY');
      const block2Broken = '-----BEGIN PUBLIC KEY-----\nAQIDBA==\n';
      final pem = '$block1\n$block2Broken';

      expect(
        () => codec.decodeAll(pem, 'PUBLIC KEY'),
        throwsA(isA<PemFormatException>()),
      );
    });
  });

  group('PemCodec 往返完整性', () {
    test('encode 后 decode 产生原始字节', () {
      final original = Uint8List.fromList(
        List.generate(256, (i) => i % 256),
      );
      final pem = codec.encode(original, 'PRIVATE KEY');
      final decoded = codec.decode(pem, 'PRIVATE KEY');
      expect(decoded, equals(original));
    });

    test('单字节往返', () {
      final original = Uint8List.fromList([42]);
      final pem = codec.encode(original, 'CERTIFICATE');
      final decoded = codec.decode(pem, 'CERTIFICATE');
      expect(decoded, equals(original));
    });

    test('大数据往返', () {
      final original = Uint8List.fromList(
        List.generate(4096, (i) => i % 256),
      );
      final pem = codec.encode(original, 'PUBLIC KEY');
      final decoded = codec.decode(pem, 'PUBLIC KEY');
      expect(decoded, equals(original));
    });
  });

  // Feature: admin-tools-flutter-migration, Property 1: PEM round-trip integrity
  // **Validates: Requirements 3.1, 3.2, 3.5, 3.6, 3.7, 3.9**
  group('Property 1: PEM 往返完整性 (属性测试)', () {
    test('对任意有效字节序列和支持的类型标签，encode→decode 产生原始字节', () {
      final random = Random.secure();
      const typeLabels = ['PRIVATE KEY', 'PUBLIC KEY', 'CERTIFICATE'];
      const iterations = 150;

      for (var i = 0; i < iterations; i++) {
        // 生成 1–4096 字节的随机数据
        final length = random.nextInt(4096) + 1;
        final bytes = Uint8List.fromList(
          List.generate(length, (_) => random.nextInt(256)),
        );

        // 随机选择类型标签
        final type = typeLabels[random.nextInt(typeLabels.length)];

        // 编码为 PEM
        final pem = codec.encode(bytes, type);

        // 解码回来
        final decoded = codec.decode(pem, type);

        // 验证往返完整性
        expect(
          decoded,
          equals(bytes),
          reason: 'Round-trip failed for $length bytes with type "$type" '
              'at iteration $i',
        );
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 2: PEM multi-block round-trip
  // **Validates: Requirements 3.1, 3.2, 3.5, 3.6, 3.7, 3.8, 3.9**
  group('Property 2: PEM 多块往返 (属性测试)', () {
    test('对任意 1–10 个字节序列和类型标签，encode 各块拼接后 decodeAll 按序返回原始序列',
        () {
      final random = Random.secure();
      const typeLabels = ['PRIVATE KEY', 'PUBLIC KEY', 'CERTIFICATE'];
      const iterations = 150;

      for (var i = 0; i < iterations; i++) {
        // 随机选择类型标签
        final type = typeLabels[random.nextInt(typeLabels.length)];

        // 生成 1–10 个随机字节序列
        final blockCount = random.nextInt(10) + 1;
        final originalBlocks = <Uint8List>[];

        for (var j = 0; j < blockCount; j++) {
          final length = random.nextInt(4096) + 1;
          originalBlocks.add(
            Uint8List.fromList(
              List.generate(length, (_) => random.nextInt(256)),
            ),
          );
        }

        // 编码每个块并拼接
        final concatenated = originalBlocks
            .map((block) => codec.encode(block, type))
            .join('\n');

        // 使用 decodeAll 解码
        final decoded = codec.decodeAll(concatenated, type);

        // 验证块数量
        expect(
          decoded.length,
          equals(blockCount),
          reason: 'Block count mismatch: expected $blockCount, '
              'got ${decoded.length} at iteration $i',
        );

        // 验证每个块的内容
        for (var j = 0; j < blockCount; j++) {
          expect(
            decoded[j],
            equals(originalBlocks[j]),
            reason: 'Block $j content mismatch at iteration $i '
                'with type "$type"',
          );
        }
      }
    });
  });
}
