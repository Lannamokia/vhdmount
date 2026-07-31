// Snapshot test — 守住 `NavigationRailThemeData` 字段（R6.3 回归守门）。
//
// Validates: Requirements 6.3
//
// 任务 5.5 — `shell.dart` 中 `NavigationRailThemeData` 是 DesktopSidebar 视觉
// 表现的唯一来源。R6.3 要求本特性「除暴露共享 DestinationKey 契约的 refactor
// 外，DesktopSidebar 与 NavigationRailThemeData 在功能上保持不变」。这里
// 把 shell.dart 中 NavigationRailThemeData 的全部字段写成 baseline 期望值常量，
// 直接读取运行时 `Theme.of(...).navigationRailTheme`（即 MaterialApp.theme
// 上挂的同一份 ThemeData），逐字段断言相等。一旦未来有人调动 backgroundColor /
// indicatorColor / icon theme / label text style / 几何字段（minWidth /
// minExtendedWidth / groupAlignment）任意一项，本测试都会立刻失败，即作为
// R6.3 的源码级守门。
//
// 实施策略：
//   1. 用最轻量的 [FakeAdminApi] 走通 [AdminApp] 的首帧渲染，使 [MaterialApp]
//      被挂到 widget 树上。AdminApp.build 构造 MaterialApp 时已注入完整
//      ThemeData，与具体落到哪个 home 子页面（Splash / Initialization /
//      Dashboard）无关。
//   2. 不调用 `pumpAndSettle`，避免被 bootstrap 链路上的 OTP timer / 后台
//      action 影响；本测试只关心 ThemeData 这份**纯静态**配置。
//   3. 通过 `tester.widget<MaterialApp>(find.byType(MaterialApp)).theme` 直
//      接拿到运行时的 ThemeData，比从渲染树里 `Theme.of(context)` 更稳定，
//      也避开了 `OtpHostOverlay` / `Builder` 的 BuildContext 时序问题。
//   4. 对 [TextStyle] 不做对象级 == 比较——而是逐字段比对 fontSize /
//      fontWeight / height / color / fontFamily，避免 inherit / decoration /
//      package 等次要字段被悄悄改动时把这条 baseline 测试变得过敏。

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

void main() {
  testWidgets(
    '[mobile-bottom-nav-redesign] R6.3 NavigationRailThemeData baseline snapshot',
    (tester) async {
      // —— 最小化 fake 后端：让 AdminApp 完成 MaterialApp 的首帧构建即可。 ——
      final controller = AppController(
        api: FakeAdminApi(
          serverStatus: const ServerStatus(
            initialized: false,
            pendingInitialization: false,
            databaseReady: false,
            defaultVhdKeyword: 'SDEZ',
            trustedRegistrationCertificateCount: 0,
          ),
          authStatus: const AuthStatus(
            initialized: false,
            isAuthenticated: false,
            otpVerified: false,
          ),
        ),
        clientConfigStore: FakeClientConfigStore(),
      );

      await tester.pumpWidget(AdminApp(controller: controller));
      // 走完 bootstrap + UI 重建，避免离开测试时残留 pending timer。
      await tester.pumpAndSettle();

      // 直接从挂在树上的 MaterialApp 读 theme，避开 home 分支化的 BuildContext。
      final materialApp = tester.widget<MaterialApp>(find.byType(MaterialApp));
      final railTheme = materialApp.theme!.navigationRailTheme;

      // ─── Baseline: shell.dart 中 NavigationRailThemeData 的全部配置字段 ───
      //
      // 这些常量与 shell.dart 内 `navigationRailTheme: NavigationRailThemeData(
      //   backgroundColor: Colors.transparent,
      //   useIndicator: true,
      //   indicatorColor: AppPalette.coral.withValues(alpha: 0.18),
      //   selectedIconTheme: const IconThemeData(color: AppPalette.coralDeep),
      //   unselectedIconTheme: const IconThemeData(color: AppPalette.muted),
      //   selectedLabelTextStyle: textTheme.labelLarge?.copyWith(
      //     color: AppPalette.coralDeep,
      //   ),
      //   unselectedLabelTextStyle: textTheme.bodySmall?.copyWith(
      //     color: AppPalette.muted,
      //   ),
      //   minWidth: 84,
      //   minExtendedWidth: 220,
      //   groupAlignment: -1,
      // )` 一一对应；同步修改 shell.dart 时必须同步修改本文件。

      expect(
        railTheme.backgroundColor,
        Colors.transparent,
        reason: 'R6.3: NavigationRail backgroundColor 必须保持透明（让 AppPanel '
            '渐变作为视觉背景）',
      );
      expect(
        railTheme.useIndicator,
        isTrue,
        reason: 'R6.3: useIndicator 必须为 true，否则选中态指示器消失',
      );
      expect(
        railTheme.indicatorColor,
        AppPalette.coral.withValues(alpha: 0.18),
        reason: 'R6.3: indicatorColor 必须为 AppPalette.coral @ alpha 0.18',
      );
      expect(
        railTheme.selectedIconTheme,
        const IconThemeData(color: AppPalette.coralDeep),
        reason: 'R6.3: selectedIconTheme.color 必须为 AppPalette.coralDeep',
      );
      expect(
        railTheme.unselectedIconTheme,
        const IconThemeData(color: AppPalette.muted),
        reason: 'R6.3: unselectedIconTheme.color 必须为 AppPalette.muted',
      );
      expect(railTheme.minWidth, 84, reason: 'R6.3: minWidth 必须保持 84');
      expect(
        railTheme.minExtendedWidth,
        220,
        reason: 'R6.3: minExtendedWidth 必须保持 220',
      );
      expect(
        railTheme.groupAlignment,
        -1,
        reason: 'R6.3: groupAlignment 必须保持 -1（顶端对齐）',
      );

      // ─── selectedLabelTextStyle ───
      // 来源：textTheme.labelLarge = miSansTextStyle(fontSize: 14,
      //   fontWeight: FontWeight.w600, height: 1.2, color: AppPalette.ink)
      // 之后被 .copyWith(color: AppPalette.coralDeep) 覆写颜色；
      // miSansTextStyle 的 weight→family 映射对 w600 给出 'MiSans600'。
      final selectedLabel = railTheme.selectedLabelTextStyle;
      expect(
        selectedLabel,
        isNotNull,
        reason: 'R6.3: selectedLabelTextStyle 不可被设为 null',
      );
      expect(
        selectedLabel!.fontSize,
        14,
        reason: 'R6.3: selectedLabel fontSize 必须保持 14',
      );
      expect(
        selectedLabel.fontWeight,
        FontWeight.w600,
        reason: 'R6.3: selectedLabel fontWeight 必须保持 w600',
      );
      expect(
        selectedLabel.height,
        1.2,
        reason: 'R6.3: selectedLabel height 必须保持 1.2',
      );
      expect(
        selectedLabel.color,
        AppPalette.coralDeep,
        reason: 'R6.3: selectedLabel color 必须保持 AppPalette.coralDeep',
      );
      expect(
        selectedLabel.fontFamily,
        'MiSans600',
        reason: 'R6.3: selectedLabel fontFamily 必须保持 MiSans600（w600 映射）',
      );

      // ─── unselectedLabelTextStyle ───
      // 来源：textTheme.bodySmall = miSansTextStyle(fontSize: 12,
      //   fontWeight: FontWeight.w500, height: 1.45, color: AppPalette.muted)
      // 之后被 .copyWith(color: AppPalette.muted)（颜色与原值相同，仍写出
      // copyWith 是为了显式表达「未选中前景色 = AppPalette.muted」契约）；
      // miSansTextStyle 的 weight→family 映射对 w500 给出 'MiSans500'。
      final unselectedLabel = railTheme.unselectedLabelTextStyle;
      expect(
        unselectedLabel,
        isNotNull,
        reason: 'R6.3: unselectedLabelTextStyle 不可被设为 null',
      );
      expect(
        unselectedLabel!.fontSize,
        12,
        reason: 'R6.3: unselectedLabel fontSize 必须保持 12',
      );
      expect(
        unselectedLabel.fontWeight,
        FontWeight.w500,
        reason: 'R6.3: unselectedLabel fontWeight 必须保持 w500',
      );
      expect(
        unselectedLabel.height,
        1.45,
        reason: 'R6.3: unselectedLabel height 必须保持 1.45',
      );
      expect(
        unselectedLabel.color,
        AppPalette.muted,
        reason: 'R6.3: unselectedLabel color 必须保持 AppPalette.muted',
      );
      expect(
        unselectedLabel.fontFamily,
        'MiSans500',
        reason: 'R6.3: unselectedLabel fontFamily 必须保持 MiSans500（w500 映射）',
      );
    },
  );
}
