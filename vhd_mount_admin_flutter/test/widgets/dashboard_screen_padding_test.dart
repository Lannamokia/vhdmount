// Widget test for 底部滚动 padding 完整覆盖 MobileBottomNav。
//
// Validates: Requirements 9.3
//
// Property 16 — 底部滚动 padding 完整覆盖 MobileBottomNav：
//   对任意 `viewPaddingBottom ∈ [0, 64]` 与 `kHeight ∈ {76, ...}`，在
//   `mobile == true` 下渲染 [DashboardScreen] 时，顶层 [SingleChildScrollView]
//   的 `padding.bottom` 必须 ≥ `MobileBottomNav.kHeight +
//   MediaQuery.viewPaddingOf(context).bottom`，且 `Scaffold.extendBody == true`。
//
// 任务 4.7 把这条不变量钉在 4 个典型的 viewPaddingBottom 值上：
// `{0, 24, 48, 64}`——分别覆盖：
//
//   * 0   → 无系统底部安全区（桌面 / 旧 Android）。
//   * 24  → 安卓导航条常见高度。
//   * 48  → iPhone home indicator 安全区。
//   * 64  → 折叠屏 / 大屏手机的极端值（接近 design 文档列出的上限）。
//
// 测试策略：
//
// 1. 用 `tester.view.physicalSize` 把视口设为 mobile（360×640）+ DPR=1.0，
//    保证 `LayoutClassification.classify` 解出 `mobile = true ∧ compact = true`，
//    走到 [DashboardScreen] 内 mobile 分支构造的顶层 [SingleChildScrollView]。
// 2. 用 `tester.view.viewPadding = FakeViewPadding.zero / FakeViewPadding(bottom:...)`
//    注入 `MediaQuery.viewPaddingOf(context).bottom`。注意：Flutter 在物理像素
//    层把 viewPadding 表达为 physical pixels，必须乘 `devicePixelRatio` 才会
//    被 `MediaQuery.viewPaddingOf` 解为相同的逻辑像素值；这里 DPR=1.0，因此
//    逻辑值与物理值一致。
// 3. 通过 [AdminApp] + 最小化的 [FakeAdminApi] 把 [DashboardScreen] 渲染出来
//    （直接构造 [DashboardScreen] 也能跑，但会绕过 [AdminApp] 的 ThemeData /
//    OTP host 等接线，不如复用现有 stub）。
// 4. 找到顶层 [SingleChildScrollView]——dashboard.dart mobile 分支构造的就是
//    第一个 `SingleChildScrollView`，其 `padding` 是 `EdgeInsets.only(bottom: …)`。
//    用 `find.byType(SingleChildScrollView).first` 拿到它。
// 5. 断言 `padding.bottom ≥ MobileBottomNav.kHeight + viewPaddingBottom`，并
//    断言外层 [Scaffold].extendBody == true。

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

const ServerStatus _readyServerStatus = ServerStatus(
  initialized: true,
  pendingInitialization: false,
  databaseReady: true,
  defaultVhdKeyword: 'SAFEBOOT',
  trustedRegistrationCertificateCount: 1,
);

const AuthStatus _authenticatedStatus = AuthStatus(
  initialized: true,
  isAuthenticated: true,
  otpVerified: true,
);

/// 任务 4.7 钉死的 viewPaddingBottom 值集合：
///
///   * 0   → 无系统底部安全区。
///   * 24  → Android 系统导航条常见高度。
///   * 48  → iPhone home indicator 安全区。
///   * 64  → 折叠屏 / 大屏手机的极端值。
const List<double> _viewPaddingBottoms = <double>[0, 24, 48, 64];

/// 用于 mobile 视口的尺寸：360dp 宽 × 640dp 高，确保
/// `LayoutClassification.classify` 解出 `mobile = compact = true`。
const Size _mobileViewport = Size(360, 640);

void _setMobileView(WidgetTester tester, double viewPaddingBottom) {
  tester.view.physicalSize = _mobileViewport;
  tester.view.devicePixelRatio = 1.0;
  // FakeViewPadding 的字段是 physical pixels；DPR=1.0 时与逻辑像素一致。
  tester.view.viewPadding = FakeViewPadding(
    left: 0,
    top: 0,
    right: 0,
    bottom: viewPaddingBottom,
  );
  // padding 与 viewPadding 在 SafeArea / MediaQuery.viewPaddingOf 的语义里
  // 不同：viewPadding 表示「设备物理切边」，padding 表示「未被键盘遮挡的
  // 切边」。dashboard.dart 读的是 `MediaQuery.viewPaddingOf(context).bottom`，
  // 因此只需注入 viewPadding。padding 也同步设一份避免 SafeArea 行为偏移。
  tester.view.padding = FakeViewPadding(
    left: 0,
    top: 0,
    right: 0,
    bottom: viewPaddingBottom,
  );
}

void _resetView(WidgetTester tester) {
  tester.view.resetPhysicalSize();
  tester.view.resetDevicePixelRatio();
  tester.view.resetViewPadding();
  tester.view.resetPadding();
}

void main() {
  for (final viewPaddingBottom in _viewPaddingBottoms) {
    testWidgets(
      '[mobile-bottom-nav-redesign] Property 16: '
      '顶层 SingleChildScrollView padding.bottom ≥ kHeight + viewPaddingBottom '
      '且 Scaffold.extendBody == true '
      '@ viewPaddingBottom=${viewPaddingBottom.toInt()}',
      (tester) async {
        _setMobileView(tester, viewPaddingBottom);
        addTearDown(() => _resetView(tester));

        final controller = AppController(
          api: FakeAdminApi(
            serverStatus: _readyServerStatus,
            authStatus: _authenticatedStatus,
          ),
          clientConfigStore: FakeClientConfigStore(),
        );

        await tester.pumpWidget(AdminApp(controller: controller));
        await tester.pumpAndSettle();

        // mobile 分支下，dashboard.dart 第一个 SingleChildScrollView 就是
        // 顶层包住 contentChildren 的那一个，其 padding 是
        // `EdgeInsets.only(bottom: kHeight + viewPaddingBottom + 36)`。
        final scrollViewFinder = find.byType(SingleChildScrollView);
        expect(
          scrollViewFinder,
          findsAtLeastNWidgets(1),
          reason: 'mobile 分支必须渲染顶层 SingleChildScrollView',
        );

        final scrollView = tester.widget<SingleChildScrollView>(
          scrollViewFinder.first,
        );
        final padding = scrollView.padding;
        expect(
          padding,
          isA<EdgeInsets>(),
          reason: '顶层 SingleChildScrollView.padding 应为 EdgeInsets',
        );
        final edgeInsets = padding! as EdgeInsets;
        final expectedMinimumBottom =
            MobileBottomNav.kHeight + viewPaddingBottom;
        expect(
          edgeInsets.bottom,
          greaterThanOrEqualTo(expectedMinimumBottom),
          reason: '顶层 SingleChildScrollView.padding.bottom 必须 ≥ '
              'MobileBottomNav.kHeight (${MobileBottomNav.kHeight}) '
              '+ viewPaddingBottom (${viewPaddingBottom.toInt()}) '
              '= $expectedMinimumBottom；实测 ${edgeInsets.bottom}',
        );

        // mobile && compact 分支下 Scaffold.extendBody 必须为 true，让 page
        // body 延伸到 MobileBottomNav 之下。
        final scaffoldFinder = find.byType(Scaffold);
        expect(
          scaffoldFinder,
          findsAtLeastNWidgets(1),
          reason: 'DashboardScreen 必须挂在 Scaffold 上',
        );
        final scaffold = tester.widget<Scaffold>(scaffoldFinder.first);
        expect(
          scaffold.extendBody,
          isTrue,
          reason: 'mobile 分支必须把 Scaffold.extendBody 设为 true，'
              '否则页面正文会被 MobileBottomNav 挤掉一段',
        );
        // 同时确认底栏挂的是 MobileBottomNav（避免误把 NavigationRail 等
        // 桌面导航当作通过条件）。
        expect(
          find.byType(MobileBottomNav),
          findsOneWidget,
          reason: 'mobile 分支必须把 MobileBottomNav 接到 '
              'Scaffold.bottomNavigationBar',
        );

        expect(tester.takeException(), isNull);
      },
    );
  }
}
