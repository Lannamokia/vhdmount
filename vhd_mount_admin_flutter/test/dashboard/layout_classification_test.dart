// Property test for `LayoutClassification.classify`：把 `LayoutBuilder` 报告的
// `(maxWidth, maxHeight)` 折叠成 `(mobile, compact)` 两个布尔值的纯函数。
//
// 该测试覆盖断点定义本身（设计文档 §Architecture 与需求 4.1 / 4.2）：
//
//   mobile  ≡ maxWidth  < 720
//   compact ≡ mobile || maxWidth < 1100 || maxHeight < 720
//
// PBT 的输入空间限定为非负实数 `(maxWidth, maxHeight) ∈ ℝ⁺²` —— `LayoutBuilder`
// 报告的 constraints 永远是非负的；除此之外不对取值再做任何裁剪，让 Glados 在
// 边界附近自由采样并 shrink 出最小反例。
//
// 此外，我们显式列出 720 / 1100 / 边界值的单元测试，把「严格小于」语义钉死：
// `<` 而不是 `<=`，所以 `maxWidth == 720` / `maxWidth == 1100` / `maxHeight == 720`
// 都不应该触发 mobile / compact 的对应分支。
//
// **Validates: Requirements 4.1, 4.2**

// `glados/glados.dart` 已经 re-export `package:test/test.dart`（提供 `test` /
// `group` / `expect` / matchers），所以这里不再 import `flutter_test` 以避免
// `expect` 在 `flutter_test` 与 `matcher` 之间的双重导入冲突。该测试不依赖
// 任何 widget testing 工具，纯值层 PBT。
import 'package:glados/glados.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

/// 重新计算期望值，与被测函数相互印证：用两份独立写出的实现去交叉验证布尔
/// 表达式的优先级，避免「测试和实现共抄同一个错误」。
({bool mobile, bool compact}) _expected(double maxWidth, double maxHeight) {
  final bool mobile = maxWidth < 720;
  final bool compact = mobile || maxWidth < 1100 || maxHeight < 720;
  return (mobile: mobile, compact: compact);
}

void main() {
  group(
    '[mobile-bottom-nav-redesign] LayoutClassification.classify (Property 8)',
    () {
      // ---- PBT：在 (maxWidth, maxHeight) ∈ ℝ⁺² 上随机采样 ----
      //
      // 用 `positiveDoubleOrZero` 让 Glados 把 `0.0` 也纳入采样空间——
      // `LayoutBuilder` 在极端约束下确实会下发 0 维度（例如折叠态 / SizedBox
      // 包裹空间）。`size` 自动随迭代增长，能扫到远超 1100 的宽屏。
      Glados2(any.positiveDoubleOrZero, any.positiveDoubleOrZero).test(
        '[mobile-bottom-nav-redesign] mobile == (maxWidth < 720) ∧ '
        'compact == mobile || maxWidth < 1100 || maxHeight < 720',
        (double maxWidth, double maxHeight) {
          final result = LayoutClassification.classify(maxWidth, maxHeight);
          final expected = _expected(maxWidth, maxHeight);

          expect(
            result.mobile,
            expected.mobile,
            reason:
                'mobile 必须等价于 (maxWidth < 720); '
                'maxWidth=$maxWidth, maxHeight=$maxHeight',
          );
          expect(
            result.compact,
            expected.compact,
            reason:
                'compact 必须等价于 mobile || maxWidth < 1100 || maxHeight < 720; '
                'maxWidth=$maxWidth, maxHeight=$maxHeight',
          );

          // mobile 蕴含 compact：直接用 R4.2 的语义反推，作为额外的健全性检查。
          if (result.mobile) {
            expect(
              result.compact,
              isTrue,
              reason:
                  'mobile 为真时 compact 必为真; '
                  'maxWidth=$maxWidth, maxHeight=$maxHeight',
            );
          }
        },
      );

      // ---- 显式边界用例 ----
      //
      // 这里把「严格小于」边界写成 example tests，让 PBT 万一遗漏 720 / 1100
      // 这种「单点不连续」时也能立刻报警；同时它们也是 R4.1 / R4.2 的字面化
      // 见证。所有 case 既覆盖宽侧也覆盖高侧的边界。

      group('[mobile-bottom-nav-redesign] explicit boundary cases', () {
        test(
          '[mobile-bottom-nav-redesign] (719.99, 600) → mobile=true, compact=true',
          () {
            final r = LayoutClassification.classify(719.99, 600);
            expect(r.mobile, isTrue);
            expect(r.compact, isTrue);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (720, 600) → mobile=false, compact=true '
          '(width 边界严格小于 720，高度 < 720 触发 compact)',
          () {
            final r = LayoutClassification.classify(720, 600);
            expect(r.mobile, isFalse);
            expect(r.compact, isTrue);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (720.01, 600) → mobile=false, compact=true',
          () {
            final r = LayoutClassification.classify(720.01, 600);
            expect(r.mobile, isFalse);
            expect(r.compact, isTrue);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (1099.99, 720) → mobile=false, compact=true',
          () {
            final r = LayoutClassification.classify(1099.99, 720);
            expect(r.mobile, isFalse);
            expect(r.compact, isTrue);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (1100, 720) → mobile=false, compact=false '
          '(width / height 同时踩到非严格边界)',
          () {
            final r = LayoutClassification.classify(1100, 720);
            expect(r.mobile, isFalse);
            expect(r.compact, isFalse);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (1100, 719.99) → mobile=false, compact=true '
          '(height 严格小于 720 触发 compact)',
          () {
            final r = LayoutClassification.classify(1100, 719.99);
            expect(r.mobile, isFalse);
            expect(r.compact, isTrue);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (1100.01, 720) → mobile=false, compact=false',
          () {
            final r = LayoutClassification.classify(1100.01, 720);
            expect(r.mobile, isFalse);
            expect(r.compact, isFalse);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (1100.01, 720.01) → mobile=false, compact=false '
          '(刚好越过两个断点)',
          () {
            final r = LayoutClassification.classify(1100.01, 720.01);
            expect(r.mobile, isFalse);
            expect(r.compact, isFalse);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (0, 0) → mobile=true, compact=true '
          '(原点：折叠态 / 空 constraint)',
          () {
            final r = LayoutClassification.classify(0, 0);
            expect(r.mobile, isTrue);
            expect(r.compact, isTrue);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (1440, 900) → mobile=false, compact=false '
          '(典型桌面 viewport)',
          () {
            final r = LayoutClassification.classify(1440, 900);
            expect(r.mobile, isFalse);
            expect(r.compact, isFalse);
          },
        );

        test(
          '[mobile-bottom-nav-redesign] (360, 640) → mobile=true, compact=true '
          '(典型 iPhone-mini viewport)',
          () {
            final r = LayoutClassification.classify(360, 640);
            expect(r.mobile, isTrue);
            expect(r.compact, isTrue);
          },
        );
      });
    },
  );
}
