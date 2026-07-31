// [mobile-bottom-nav-redesign] R3.4 源码级守门：
// 禁止 dashboard.dart / shell.dart / state.dart 出现
// `onDestinationSelected(<整数>)` 形式的旧式索引派发。
//
// 该测试直接读源文件做正则扫描；它不依赖 Flutter widget 框架，
// 但放在 `flutter_test` 包下统一由 `flutter test` 执行。

import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    '[mobile-bottom-nav-redesign] no legacy integer dispatch (R3.4)',
    () {
      final regex = RegExp(r'onDestinationSelected\(([0-7])\)');
      const sources = <String>[
        'lib/src/dashboard.dart',
        'lib/src/shell.dart',
        'lib/src/state.dart',
      ];

      for (final relPath in sources) {
        final file = File(relPath);
        expect(
          file.existsSync(),
          isTrue,
          reason: '源文件不存在：$relPath（cwd=${Directory.current.path}）',
        );

        final content = file.readAsStringSync();
        final matches = regex.allMatches(content).toList(growable: false);
        expect(
          matches,
          isEmpty,
          reason:
              '$relPath 不应包含 onDestinationSelected(整数) 字面量；'
              '找到 ${matches.length} 处: '
              '${matches.map((m) => m.group(0)).join(', ')}',
        );
      }
    },
  );
}
