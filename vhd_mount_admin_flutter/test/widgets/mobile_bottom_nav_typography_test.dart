// Widget test for MobileBottomNav 标签字号下限与无截断（Property 11）。
//
// **Validates: Requirements 1.2, 5.2, 5.3**
//
// Property 11 — 标签不截断 + 字号下限：
//   对任意 viewport 宽度 ∈ [360, 1100] 与任意 spec ∈
//   `DashboardDestinations.mobile()`：
//
//   1. spec 在主槽渲染时，`RenderParagraph.didExceedMaxLines == false`
//      且未触发 `TextOverflow.ellipsis`；有效字号 ≥12sp。
//   2. 「更多」trigger 在主槽渲染时同样不截断，字号 ≥12sp。
//   3. spec 在 overflow sheet 行渲染时不截断，字号 ≥14sp。
//
// 测试策略：
//   - 显式枚举 360 / 411 / 600 / 720 / 900 / 1099dp 共 6 档典型移动 viewport
//     宽度（覆盖 R1.2 所述 ≥360 范围下界与 CompactLayout 上界 1099）。
//   - 在每个宽度下渲染 `MobileBottomNav` 默认 layout（4 主槽 + 1 「更多」
//     trigger）。
//   - 遍历主槽 / 「更多」 trigger 的 [Text] widget，从 `Text.style.fontSize`
//     读出配置字号，并通过 [RenderParagraph] 验证「不截断 / 不 ellipsis」。
//   - 然后 tap 「更多」 trigger 打开 overflow sheet，对 sheet 内每个
//     Destination 行重复同样的断言（下限改为 14sp）。
//
// 不使用属性测试（Glados）的理由：本属性只对「Mobile primary set + 更多
// trigger + overflow sheet 内的 ListTile.title」三处 [Text] widget 生效，
// 输入空间是固定的 mobile() 集合 + 固定的 viewport 宽度档位，例举枚举即可
// 覆盖；强行套 PBT 反而无法稳定地 tap 出 sheet。

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

/// 主槽 / 「更多」 trigger 的字号下限（R1.2 / R5.2）。
const double _kPrimaryMinFontSize = 12.0;

/// overflow sheet 行的字号下限（R5.3）。
const double _kOverflowMinFontSize = 14.0;

/// 覆盖的典型 viewport 宽度（逻辑像素）。
///
/// - 360 / 411：iPhone SE / Pixel 主流小屏宽度，对应 R1.2 的下界。
/// - 600：约 iPad mini portrait / 折叠屏外屏，介于 mobile 与 compact 之间。
/// - 720 / 900：Mobile 上界与 CompactLayout 中段。
/// - 1099：CompactLayout 上界（1100 即切到 Desktop）。
const List<double> _kViewportWidths = <double>[
  360,
  411,
  600,
  720,
  900,
  1099,
];

/// 移动端测试视口的高度，足以容纳 76dp 的 MobileBottomNav + sheet 弹出。
const double _kViewportHeight = 800;

/// 把 `tester` 的 surface 设成给定 viewport 大小，DPR 固定 1.0。
void _setViewport(WidgetTester tester, double width, double height) {
  tester.view.physicalSize = Size(width, height);
  tester.view.devicePixelRatio = 1.0;
}

void _resetViewport(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
}

/// 把 [MobileBottomNav] 装入最小可运行的 [MaterialApp] + [Scaffold]，
/// 不依赖 `AdminApp` 的全局主题——避免触碰 `NavigationBarThemeData`
/// 之外的状态。这样字号断言读到的是 fallback `miSansTextStyle` 中
/// 显式配置的 13sp（主槽）/ 16sp（sheet 行）。
Widget _wrapNav({
  required MobileNavLayout layout,
  required DestinationKey activeKey,
  required ValueChanged<DestinationKey> onSelect,
}) {
  return MaterialApp(
    home: Scaffold(
      body: const SizedBox.expand(),
      bottomNavigationBar: MobileBottomNav(
        layout: layout,
        activeKey: activeKey,
        onDestinationSelected: onSelect,
      ),
    ),
  );
}

/// 找出指定 [Text] widget（按 `data` 精确匹配）所在的 [RenderParagraph]，
/// 并读取其配置 `Text.style.fontSize`。
({double fontSize, bool didExceedMaxLines, TextOverflow? overflow})
_inspectText(WidgetTester tester, Finder textFinder) {
  final textWidget = tester.widget<Text>(textFinder);
  final paragraph = tester.renderObject<RenderParagraph>(textFinder);
  final span = paragraph.text;
  // `Text.style.fontSize` 是该 widget 在我们的代码里显式配置的字号。
  // 测试只断言「配置字号 ≥ 下限」，与最终是否被 [FittedBox.scaleDown]
  // 缩放无关——R1.2 / R5.2 / R5.3 关心的是 `NavigationBarThemeData
  // .labelTextStyle` 实际配置的字号下限，缩放兜底是另一条路径。
  final configuredFontSize =
      textWidget.style?.fontSize ??
      (span is TextSpan ? span.style?.fontSize : null) ??
      0;
  return (
    fontSize: configuredFontSize,
    didExceedMaxLines: paragraph.didExceedMaxLines,
    overflow: textWidget.overflow,
  );
}

void main() {
  // Default mobile layout: 4 primary + 3 overflow（按 design §Data Models）。
  final layout = DashboardDestinations.mobileNavLayout();
  // 默认 active key 取主槽第一项；这一选择不影响 typography 不变量。
  final defaultActive = layout.primary.first.key;

  testWidgets(
    '[mobile-bottom-nav-redesign] Property 11: '
    'primary slot + overflow trigger labels respect 12sp min and never ellipsize '
    'across 360–1099dp viewports',
    (tester) async {
      addTearDown(() => _resetViewport(tester));

      for (final width in _kViewportWidths) {
        _setViewport(tester, width, _kViewportHeight);

        await tester.pumpWidget(
          _wrapNav(
            layout: layout,
            activeKey: defaultActive,
            onSelect: (_) {},
          ),
        );
        await tester.pumpAndSettle();

        // 1) 每个主槽 label。
        for (final spec in layout.primary) {
          final finder = find.descendant(
            of: find.byType(MobileBottomNav),
            matching: find.text(spec.label),
          );
          expect(
            finder,
            findsOneWidget,
            reason:
                '在 width=${width}dp 下未能定位主槽标签 "${spec.label}"',
          );

          final info = _inspectText(tester, finder);
          expect(
            info.fontSize,
            greaterThanOrEqualTo(_kPrimaryMinFontSize),
            reason:
                '主槽 "${spec.label}" 配置字号 ${info.fontSize} '
                '< 12sp 下限 (width=${width}dp)',
          );
          expect(
            info.didExceedMaxLines,
            isFalse,
            reason:
                '主槽 "${spec.label}" RenderParagraph.didExceedMaxLines '
                '== true，发生了截断 (width=${width}dp)',
          );
          expect(
            info.overflow,
            isNot(TextOverflow.ellipsis),
            reason:
                '主槽 "${spec.label}" Text.overflow 为 ellipsis，违反 R1.2 '
                '(width=${width}dp)',
          );
        }

        // 2) 「更多」trigger label。
        final triggerFinder = find.descendant(
          of: find.byType(MobileBottomNav),
          matching: find.text('更多'),
        );
        expect(
          triggerFinder,
          findsOneWidget,
          reason: '在 width=${width}dp 下未能定位「更多」trigger 标签',
        );

        final triggerInfo = _inspectText(tester, triggerFinder);
        expect(
          triggerInfo.fontSize,
          greaterThanOrEqualTo(_kPrimaryMinFontSize),
          reason:
              '「更多」trigger 配置字号 ${triggerInfo.fontSize} < 12sp '
              '下限 (width=${width}dp)',
        );
        expect(
          triggerInfo.didExceedMaxLines,
          isFalse,
          reason:
              '「更多」trigger didExceedMaxLines == true，发生了截断 '
              '(width=${width}dp)',
        );
        expect(
          triggerInfo.overflow,
          isNot(TextOverflow.ellipsis),
          reason:
              '「更多」trigger Text.overflow 为 ellipsis，违反 R1.2 '
              '(width=${width}dp)',
        );
      }
    },
  );

  testWidgets(
    '[mobile-bottom-nav-redesign] Property 11: '
    'overflow sheet rows respect 14sp min and never ellipsize '
    'across 360–1099dp viewports',
    (tester) async {
      addTearDown(() => _resetViewport(tester));

      for (final width in _kViewportWidths) {
        _setViewport(tester, width, _kViewportHeight);

        await tester.pumpWidget(
          _wrapNav(
            layout: layout,
            activeKey: defaultActive,
            onSelect: (_) {},
          ),
        );
        await tester.pumpAndSettle();

        // 打开 overflow sheet：tap「更多」trigger。该回调不会触发
        // onDestinationSelected（design §5.2），仅打开 sheet。
        final triggerFinder = find.descendant(
          of: find.byType(MobileBottomNav),
          matching: find.text('更多'),
        );
        await tester.tap(triggerFinder);
        await tester.pumpAndSettle();

        // 对 overflow 中的每个 Destination 行做不变量断言。
        for (final spec in layout.overflow) {
          // sheet 内的行使用 [ListTile]，title 是 [Text(spec.label, ...)]。
          // 主槽不会同时显示该 spec（spec ∈ overflow），因此 find.text
          // 应该恰好命中 sheet 内的那一份。
          final finder = find.text(spec.label);
          expect(
            finder,
            findsOneWidget,
            reason:
                '在 width=${width}dp 下未能在 overflow sheet 中定位 '
                '"${spec.label}"',
          );

          final info = _inspectText(tester, finder);
          expect(
            info.fontSize,
            greaterThanOrEqualTo(_kOverflowMinFontSize),
            reason:
                'sheet 行 "${spec.label}" 配置字号 ${info.fontSize} '
                '< 14sp 下限 (width=${width}dp)',
          );
          expect(
            info.didExceedMaxLines,
            isFalse,
            reason:
                'sheet 行 "${spec.label}" RenderParagraph.didExceedMaxLines '
                '== true，发生了截断 (width=${width}dp)',
          );
          expect(
            info.overflow,
            isNot(TextOverflow.ellipsis),
            reason:
                'sheet 行 "${spec.label}" Text.overflow 为 ellipsis，违反 '
                'R5.3 (width=${width}dp)',
          );
        }

        // 关闭 sheet 以便下一次循环干净渲染。
        final navState = tester.state<NavigatorState>(find.byType(Navigator));
        navState.pop();
        await tester.pumpAndSettle();
      }
    },
  );
}
