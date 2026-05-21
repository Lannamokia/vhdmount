import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  late FileNameSanitizer sanitizer;

  setUp(() {
    sanitizer = FileNameSanitizer();
  });

  group('FileNameSanitizer.sanitize', () {
    test('有效文件名不变', () {
      expect(sanitizer.sanitize('hello.txt'), equals('hello.txt'));
      expect(sanitizer.sanitize('my_file-2024.pem'), equals('my_file-2024.pem'));
    });

    test('将无效字符替换为下划线', () {
      expect(sanitizer.sanitize('file<name>.txt'), equals('file_name_.txt'));
      expect(sanitizer.sanitize('a:b'), equals('a_b'));
      expect(sanitizer.sanitize('a"b'), equals('a_b'));
      expect(sanitizer.sanitize('a/b'), equals('a_b'));
      expect(sanitizer.sanitize(r'a\b'), equals('a_b'));
      expect(sanitizer.sanitize('a|b'), equals('a_b'));
      expect(sanitizer.sanitize('a?b'), equals('a_b'));
      expect(sanitizer.sanitize('a*b'), equals('a_b'));
    });

    test('将控制字符 U+0000–U+001F 替换为下划线', () {
      expect(sanitizer.sanitize('a\x00b'), equals('a_b'));
      expect(sanitizer.sanitize('a\x01b'), equals('a_b'));
      expect(sanitizer.sanitize('a\x1Fb'), equals('a_b'));
      expect(sanitizer.sanitize('a\tb'), equals('a_b')); // \t = 0x09
      expect(sanitizer.sanitize('a\nb'), equals('a_b')); // \n = 0x0A
    });

    test('清理后为空时回退到 _output', () {
      // 只有输入本身为空时，清理后才为空
      expect(sanitizer.sanitize(''), equals('_output'));
    });

    test('全部为无效字符时替换为下划线（不为空）', () {
      // "***" → "___"，不为空，不回退
      expect(sanitizer.sanitize('***'), equals('___'));
      expect(sanitizer.sanitize('<>:'), equals('___'));
    });

    test('仅含无效字符加扩展名时回退基本名称', () {
      // "***" 清理后为 "___"，不为空，所以不回退
      expect(sanitizer.sanitize('*a*.txt'), equals('_a_.txt'));
    });

    test('基本名称截断为最大 200 字符', () {
      final longName = 'a' * 250;
      final result = sanitizer.sanitize('$longName.txt');
      // 基本名称应为 200 字符
      expect(result, equals('${'a' * 200}.txt'));
    });

    test('无扩展名时基本名称截断为 200 字符', () {
      final longName = 'b' * 300;
      final result = sanitizer.sanitize(longName);
      expect(result, equals('b' * 200));
    });

    test('扩展名在截断后保留', () {
      final longName = 'x' * 250;
      final result = sanitizer.sanitize('$longName.pem');
      expect(result, equals('${'x' * 200}.pem'));
    });

    test('幂等性：sanitize(sanitize(x)) == sanitize(x)', () {
      final inputs = [
        'hello.txt',
        'file<name>.txt',
        '***',
        '',
        'a' * 300,
        'test\x00file.dat',
        'normal',
      ];
      for (final input in inputs) {
        final once = sanitizer.sanitize(input);
        final twice = sanitizer.sanitize(once);
        expect(twice, equals(once), reason: 'Not idempotent for input: $input');
      }
    });

    test('点号在位置 0 时视为无扩展名', () {
      // ".hidden" → dotIndex == 0，视为无扩展名
      expect(sanitizer.sanitize('.hidden'), equals('.hidden'));
    });

    test('多个点号时取最后一个作为扩展名分隔', () {
      expect(sanitizer.sanitize('file.name.txt'), equals('file.name.txt'));
      final longBase = 'a' * 250;
      final result = sanitizer.sanitize('$longBase.name.txt');
      // lastIndexOf('.') 找到 ".txt" 前的点
      expect(result.endsWith('.txt'), isTrue);
    });
  });

  // =========================================================================
  // Property-Based Tests
  // =========================================================================

  // Feature: admin-tools-flutter-migration, Property 3: Sanitizer removes all invalid characters
  // **Validates: Requirements 13.1, 13.2**
  group('Property 3: 清理器移除所有无效字符', () {
    /// 无效字符正则：< > : " / \ | ? * 以及 U+0000–U+001F
    final invalidCharsPattern = RegExp(r'[<>:"/\\|?*\x00-\x1F]');

    test('对于任意输入字符串，sanitize() 输出不包含任何无效字符 (200 iterations)',
        () {
      final random = Random.secure();

      for (var i = 0; i < 200; i++) {
        final input = _generateRandomString(random, maxLength: 500);
        final result = sanitizer.sanitize(input);

        expect(
          invalidCharsPattern.hasMatch(result),
          isFalse,
          reason:
              'Iteration $i: sanitize() output "$result" contains invalid '
              'characters for input of length ${input.length}',
        );
      }
    });

    test('包含大量无效字符的字符串清理后无无效字符 (100 iterations)', () {
      final random = Random.secure();

      for (var i = 0; i < 100; i++) {
        final input = _generateStringWithInvalidChars(random, maxLength: 300);
        final result = sanitizer.sanitize(input);

        expect(
          invalidCharsPattern.hasMatch(result),
          isFalse,
          reason:
              'Iteration $i: sanitize() output "$result" still contains '
              'invalid characters',
        );
      }
    });

    test('仅含控制字符的字符串清理后无控制字符 (100 iterations)', () {
      final random = Random.secure();

      for (var i = 0; i < 100; i++) {
        final length = random.nextInt(50) + 1;
        final chars = List.generate(
          length,
          (_) => random.nextInt(0x20), // U+0000 to U+001F
        );
        final input = String.fromCharCodes(chars);
        final result = sanitizer.sanitize(input);

        expect(
          invalidCharsPattern.hasMatch(result),
          isFalse,
          reason: 'Iteration $i: output contains control characters',
        );
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 4: Sanitizer idempotency
  // **Validates: Requirements 13.3**
  group('Property 4: 清理器幂等性', () {
    test('对于任意输入字符串，sanitize(sanitize(x)) == sanitize(x) (200 iterations)',
        () {
      final random = Random.secure();

      for (var i = 0; i < 200; i++) {
        final input = _generateRandomString(random, maxLength: 500);
        final once = sanitizer.sanitize(input);
        final twice = sanitizer.sanitize(once);

        expect(
          twice,
          equals(once),
          reason:
              'Iteration $i: not idempotent. '
              'sanitize(x)="$once", sanitize(sanitize(x))="$twice" '
              'for input of length ${input.length}',
        );
      }
    });

    test('含扩展名的随机字符串幂等性 (100 iterations)', () {
      final random = Random.secure();
      const extensions = ['.txt', '.pem', '.json', '.pfx', '.ini', '.dat', ''];

      for (var i = 0; i < 100; i++) {
        final base = _generateRandomString(random, maxLength: 300);
        final ext = extensions[random.nextInt(extensions.length)];
        final input = '$base$ext';

        final once = sanitizer.sanitize(input);
        final twice = sanitizer.sanitize(once);

        expect(
          twice,
          equals(once),
          reason:
              'Iteration $i: not idempotent for input with extension "$ext"',
        );
      }
    });

    test('全部为无效字符的字符串幂等性 (100 iterations)', () {
      final random = Random.secure();

      for (var i = 0; i < 100; i++) {
        final input = _generateStringWithInvalidChars(random, maxLength: 200);
        final once = sanitizer.sanitize(input);
        final twice = sanitizer.sanitize(once);

        expect(
          twice,
          equals(once),
          reason: 'Iteration $i: not idempotent for all-invalid input',
        );
      }
    });
  });

  // Feature: admin-tools-flutter-migration, Property 5: Sanitizer length limit
  // **Validates: Requirements 13.4, 13.5**
  group('Property 5: 清理器长度限制', () {
    test(
        '对于任意输入字符串，sanitize() 输出基本名称长度 ≤ 200 或等于 _output (200 iterations)',
        () {
      final random = Random.secure();

      for (var i = 0; i < 200; i++) {
        final input = _generateRandomString(random, maxLength: 600);
        final result = sanitizer.sanitize(input);

        // 分离基本名称和扩展名
        final dotIndex = result.lastIndexOf('.');
        final String baseName;
        if (dotIndex > 0) {
          baseName = result.substring(0, dotIndex);
        } else {
          baseName = result;
        }

        // 基本名称长度 ≤ 200，或结果为 '_output' 系列
        final isWithinLimit = baseName.length <= 200;
        final isFallback = baseName == '_output';

        expect(
          isWithinLimit || isFallback,
          isTrue,
          reason:
              'Iteration $i: base name length ${baseName.length} exceeds 200 '
              'and is not fallback. Result: "$result"',
        );
      }
    });

    test('超长输入（500+ 字符）基本名称截断为 200 (100 iterations)', () {
      final random = Random.secure();

      for (var i = 0; i < 100; i++) {
        // 生成 201-600 字符的有效基本名称（无无效字符）
        final length = 201 + random.nextInt(400);
        final validChars = 'abcdefghijklmnopqrstuvwxyz0123456789_-';
        final base = String.fromCharCodes(
          List.generate(
            length,
            (_) => validChars.codeUnitAt(random.nextInt(validChars.length)),
          ),
        );
        final ext = random.nextBool() ? '.txt' : '';
        final input = '$base$ext';

        final result = sanitizer.sanitize(input);
        final dotIdx = result.lastIndexOf('.');
        final String resultBase;
        if (dotIdx > 0) {
          resultBase = result.substring(0, dotIdx);
        } else {
          resultBase = result;
        }

        expect(
          resultBase.length,
          lessThanOrEqualTo(200),
          reason:
              'Iteration $i: base name length ${resultBase.length} > 200 '
              'for input length $length',
        );
      }
    });

    test('空输入返回 _output (确认回退行为)', () {
      final result = sanitizer.sanitize('');
      expect(result, equals('_output'));
    });
  });
}

// =============================================================================
// Random string generators for property-based testing
// =============================================================================

/// 生成随机字符串，包含各种 Unicode 字符（含控制字符、特殊字符）。
String _generateRandomString(Random random, {int maxLength = 500}) {
  final length = random.nextInt(maxLength + 1); // 0 to maxLength
  if (length == 0) return '';

  final chars = <int>[];
  for (var i = 0; i < length; i++) {
    final category = random.nextInt(10);
    switch (category) {
      case 0:
        // 控制字符 U+0000–U+001F
        chars.add(random.nextInt(0x20));
        break;
      case 1:
        // 无效文件名字符
        const invalid = '<>:"/\\|?*';
        chars.add(invalid.codeUnitAt(random.nextInt(invalid.length)));
        break;
      case 2:
        // ASCII 可打印字符
        chars.add(0x20 + random.nextInt(0x5F)); // 0x20-0x7E
        break;
      case 3:
        // 点号（扩展名分隔符）
        chars.add(0x2E); // '.'
        break;
      case 4:
        // 下划线
        chars.add(0x5F); // '_'
        break;
      case 5:
        // 中文字符范围
        chars.add(0x4E00 + random.nextInt(0x51)); // 一些常用汉字
        break;
      case 6:
        // 日文假名
        chars.add(0x3040 + random.nextInt(0x60)); // 平假名+片假名
        break;
      case 7:
        // 表情符号（BMP 范围内）
        chars.add(0x2600 + random.nextInt(0x100)); // 杂项符号
        break;
      case 8:
        // 字母数字
        const alphaNum =
            'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
        chars.add(alphaNum.codeUnitAt(random.nextInt(alphaNum.length)));
        break;
      default:
        // 空格和常见标点
        const misc = ' -_()[]{}~!@#\$%^&+=';
        chars.add(misc.codeUnitAt(random.nextInt(misc.length)));
        break;
    }
  }
  return String.fromCharCodes(chars);
}

/// 生成主要包含无效字符的字符串。
String _generateStringWithInvalidChars(Random random, {int maxLength = 200}) {
  final length = random.nextInt(maxLength) + 1; // 1 to maxLength
  final chars = <int>[];
  for (var i = 0; i < length; i++) {
    final category = random.nextInt(3);
    switch (category) {
      case 0:
        // 控制字符
        chars.add(random.nextInt(0x20));
        break;
      case 1:
        // 无效文件名字符
        const invalid = '<>:"/\\|?*';
        chars.add(invalid.codeUnitAt(random.nextInt(invalid.length)));
        break;
      default:
        // 偶尔混入有效字符
        if (random.nextInt(4) == 0) {
          chars.add(0x61 + random.nextInt(26)); // a-z
        } else {
          const invalid = '<>:"/\\|?*';
          chars.add(invalid.codeUnitAt(random.nextInt(invalid.length)));
        }
        break;
    }
  }
  return String.fromCharCodes(chars);
}
