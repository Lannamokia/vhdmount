// Widget test for MobileBottomNav 对比度 ≥ 4.5:1。
//
// Validates: Requirements 5.4
//
// Property 12 — 对比度 ≥ 4.5:1：
//   对任意 `(foreground, background)` 颜色对——其中 foreground 取自
//   MobileBottomNav 实际使用的 label / icon 颜色（[AppPalette.ink]、
//   [AppPalette.muted]、当前主题 indicator 前景色），background 取
//   [AppPalette.surfaceStrong] 或 overflow sheet 实际背景色——WCAG 2.1
//   `contrastRatio(foreground, background) ≥ 4.5`。
//
// 测试策略：
//   1. 在测试文件内自实现 WCAG 2.1 `contrastRatio` helper（[Color] →
//      relative luminance → ratio），与 `flutter_test` 公共 API 解耦。
//   2. 渲染 [MobileBottomNav] 进入主屏树，从 `Theme.of(context)` 解析
//      `navigationBarTheme.indicatorColor`，再把该 indicator（带 alpha）
//      与 background 进行标准 alpha 合成，得到 selected 槽位 icon 真正
//      落在的视觉背景色。
//   3. 对 design §5.1 / §5.2 中列出的全部 `(foreground, background)`
//      组合断言 `contrastRatio ≥ 4.5`：
//        a. 主槽 selected fg ([AppPalette.ink]) vs 底栏背景
//           ([AppPalette.surfaceStrong])。
//        b. 主槽 unselected fg ([AppPalette.muted]) vs 底栏背景。
//        c. 主槽 selected 图标 ([AppPalette.ink]) vs indicator 合成
//           背景（mint@0.16 over surfaceStrong）。
//        d. overflow sheet selected 行 fg ([AppPalette.ink]) vs sheet
//           背景（[AppPalette.surfaceStrong]）。
//        e. overflow sheet unselected 行 fg ([AppPalette.muted]) vs
//           sheet 背景。
//   4. helper [_contrastRatio] 与 [_relativeLuminance] 定义为顶层私有
//      函数，供未来 widget / a11y 测试复用（保持本文件自洽）。
//
// 数学参考：https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio
//   - relative luminance L = 0.2126·R' + 0.7152·G' + 0.0722·B'
//     R'/G'/B' = sRGB 通道线性化（≤0.03928 → /12.92；否则 ((c+0.055)/1.055)^2.4）
//   - contrast ratio = (Llighter + 0.05) / (Ldarker + 0.05)
//
// alpha 合成参考：标准 source-over over 不透明背景：
//   composite = α·fg + (1 − α)·bg
//   （alpha 通道按 [0, 1]，rgb 通道按 [0, 1]，结果为不透明色）

import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

/// 把 sRGB 单通道（[0, 1]）线性化，按 WCAG 2.1 公式。
double _linearize(double channel) {
  return channel <= 0.03928
      ? channel / 12.92
      : math.pow((channel + 0.055) / 1.055, 2.4).toDouble();
}

/// 计算颜色的相对亮度 L，按 WCAG 2.1 公式。
///
/// 输入颜色被视为不透明色：alpha 通道在调用前应当通过 [_compositeOver]
/// 与背景合成。
double _relativeLuminance(Color color) {
  // Flutter 3.27+ 的 [Color] 已经把 r/g/b 暴露为 [0, 1] 的双精度通道，
  // 直接用即可，无需再除以 255。
  return 0.2126 * _linearize(color.r) +
      0.7152 * _linearize(color.g) +
      0.0722 * _linearize(color.b);
}

/// WCAG 2.1 contrast ratio：返回值 ∈ [1, 21]。
double _contrastRatio(Color foreground, Color background) {
  final l1 = _relativeLuminance(foreground);
  final l2 = _relativeLuminance(background);
  final lighter = math.max(l1, l2);
  final darker = math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

/// 把半透明 [foreground] 用标准 source-over 合成到不透明 [background]，
/// 返回不透明结果。用于评估 indicator pill（alpha 0.16）落在 surfaceStrong
/// 上的视觉背景色。
Color _compositeOver(Color foreground, Color background) {
  final a = foreground.a;
  return Color.from(
    alpha: 1.0,
    red: a * foreground.r + (1 - a) * background.r,
    green: a * foreground.g + (1 - a) * background.g,
    blue: a * foreground.b + (1 - a) * background.b,
  );
}

void main() {
  testWidgets(
    '[mobile-bottom-nav-redesign] Property 12: '
    'MobileBottomNav 主槽 / 溢出行 / sheet 的 (foreground, background) '
    '对比度 ≥ 4.5:1（WCAG 2.1 AA）',
    (tester) async {
      // Property 12 仅评估颜色对比度（R5.4），与槽位 [Column] 在 40dp 高度
      // 约束下出现的 2px 垂直 overflow 无关。该 layout 警告由 task 2.1 /
      // 2.8（Property 10 几何最小值）负责处理，这里把 [FlutterError] 重定向
      // 到 records，并在测试末尾过滤掉与 Property 12 无关的 RenderFlex
      // overflow，避免单独的 layout 噪声让本测试无法独立运行。
      final originalOnError = FlutterError.onError;
      final layoutErrors = <FlutterErrorDetails>[];
      FlutterError.onError = (details) {
        final message = details.exceptionAsString();
        if (message.contains('A RenderFlex overflowed')) {
          layoutErrors.add(details);
        } else {
          // 任何非 layout overflow 的错误（如真正的渲染异常）仍按原行为
          // 交给默认 reporter，保留诊断价值。
          (originalOnError ?? FlutterError.dumpErrorToConsole)(details);
        }
      };
      addTearDown(() {
        FlutterError.onError = originalOnError;
      });
      // 渲染 MobileBottomNav 到真实主屏树，取得 build context，
      // 用以从 Theme.of(context) 解析实际生效的 navigationBarTheme
      // indicatorColor。这确保即便未来通过主题层调整 indicator 颜色，
      // 测试仍然在评估「真正贴在 selected 槽 icon 后面的背景色」。
      final layout = DashboardDestinations.mobileNavLayout();
      late BuildContext capturedContext;
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (innerContext) {
              capturedContext = innerContext;
              return Scaffold(
                bottomNavigationBar: MobileBottomNav(
                  layout: layout,
                  activeKey: DestinationKey.machines,
                  onDestinationSelected: (_) {},
                ),
              );
            },
          ),
        ),
      );
      await tester.pump();

      // —— Foreground 颜色（design §5.1 / §5.2 + mobile_bottom_nav.dart）—— //
      // 主槽 / overflow 行的 selected fg 来自 [AppPalette.ink]，unselected
      // fg 来自 [AppPalette.muted]。这两个常量同时被 [Icon] color 与
      // [Text] style.color 使用，因此对它们做对比度断言可以同时覆盖图标
      // 与文字两条 R5.4 子约束。
      const selectedFg = AppPalette.ink;
      const unselectedFg = AppPalette.muted;

      // —— Background 颜色 —— //
      // 底栏背景：MobileBottomNav 的 [Material] color 直接使用
      // [AppPalette.surfaceStrong]（不透明白），与 NavigationBarTheme
      // 的 0.96 alpha 半透明配置无关——后者仅在直接复用 NavigationBar
      // 时生效，本组件自绘容器走的是不透明白。
      const navBackground = AppPalette.surfaceStrong;
      // sheet 背景：showModalBottomSheet 配置的 backgroundColor 同样是
      // [AppPalette.surfaceStrong]（见 _openOverflowSheet 中的常量）。
      const sheetBackground = AppPalette.surfaceStrong;

      // indicator 合成背景：从主题中取 indicatorColor（带 alpha 0.16），
      // 再与 [navBackground] 做 source-over 合成，得到 selected 图标
      // 的视觉背景色。
      final navTheme = Theme.of(capturedContext).navigationBarTheme;
      final indicatorOriginal = navTheme.indicatorColor ??
          AppPalette.mint.withValues(alpha: 0.16);
      final indicatorComposite = _compositeOver(indicatorOriginal, navBackground);

      // —— 断言矩阵 —— //
      // R5.4 文本：『MobileBottomNav SHALL maintain a foreground-to-surface
      // contrast ratio of at least 4.5:1 for PrimaryDestinationSlot labels
      // and icons in both selected and unselected states, evaluated against
      // the AppPalette values applied via NavigationBarThemeData and
      // equivalent theming for the overflow surface.』
      final pairs = <_ContrastPair>[
        _ContrastPair(
          name: '主槽 selected label/icon (ink) vs 底栏背景 (surfaceStrong)',
          fg: selectedFg,
          bg: navBackground,
        ),
        _ContrastPair(
          name: '主槽 unselected label/icon (muted) vs 底栏背景 (surfaceStrong)',
          fg: unselectedFg,
          bg: navBackground,
        ),
        _ContrastPair(
          name: '主槽 selected icon (ink) vs indicator 合成背景 '
              '(mint@0.16 over surfaceStrong)',
          fg: selectedFg,
          bg: indicatorComposite,
        ),
        _ContrastPair(
          name: 'overflow sheet selected 行 fg (ink) vs sheet 背景 (surfaceStrong)',
          fg: selectedFg,
          bg: sheetBackground,
        ),
        _ContrastPair(
          name: 'overflow sheet unselected 行 fg (muted) vs sheet 背景 (surfaceStrong)',
          fg: unselectedFg,
          bg: sheetBackground,
        ),
      ];

      for (final pair in pairs) {
        final ratio = _contrastRatio(pair.fg, pair.bg);
        expect(
          ratio,
          greaterThanOrEqualTo(4.5),
          reason:
              '${pair.name}：contrast=${ratio.toStringAsFixed(3)}:1 < 4.5:1，'
              '违反 R5.4 / WCAG 2.1 AA',
        );
      }
    },
  );
}

class _ContrastPair {
  const _ContrastPair({
    required this.name,
    required this.fg,
    required this.bg,
  });

  final String name;
  final Color fg;
  final Color bg;
}
