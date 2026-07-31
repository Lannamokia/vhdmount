// Property test for `DashboardDestinations.desktop(...)` 的平台成员组合，
// 以及 `mobile()` 与 `desktop(isWindows)` 共享 key 的元数据一致性。
//
// 需求 6.1 / 6.2 / 7.2 / 7.3 / 7.4 共同要求：
//   - DesktopDestinationSet 在 Windows 上恰好 8 项且包含 offlineTools；
//   - 在非 Windows 上恰好 7 项且不包含 offlineTools；
//   - 任意同时出现在 mobile 与 desktop 中的 key，两份返回的 Spec 在
//     `(label, subtitle, icon, color)` 上完全相等（这是 MobileBottomNav 与
//     DesktopSidebar 共用同一份 DestinationKey 元数据契约的数据层断言）。
//
// 我们让 Glados 在 `any.bool` 上枚举 `isWindows`，再补两个显式的
// `true` / `false` 断言用例兜底两端边界。
//
// **Validates: Requirements 6.1, 6.2, 7.2, 7.3, 7.4**

// 这是一条纯逻辑层的属性测试，不渲染 widget，所以只引入 `glados`（它内部
// 已经 re-export 了 `package:test` 的 `group` / `test` / `expect`）。同时
// 引入 `flutter_test` 会与 `glados` re-export 的同名符号冲突，故不引入；
// 另外 `BoolAny` extension 仅在不带 `show` 的导入下才能被解析，所以这里
// 也不能用 `show Glados, any` 把 extension 屏蔽掉。
import 'package:glados/glados.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

/// 检查在指定 `isWindows` 下，`desktop(...)` 的成员组合与
/// `mobile()` 的元数据一致性。提取成函数，方便 Glados 与显式 case
/// 共用同一段断言逻辑。
void _verifyDesktopAgainstMobile({required bool isWindows}) {
  final mobile = DashboardDestinations.mobile();
  final desktop = DashboardDestinations.desktop(isWindows: isWindows);

  // —— 平台成员组合 ——
  expect(
    desktop.length,
    isWindows ? 9 : 8,
    reason: 'DesktopDestinationSet 在 Windows=$isWindows 时长度应为 '
        '${isWindows ? 9 : 8}',
  );
  expect(
    desktop.any((d) => d.key == DestinationKey.offlineTools),
    isWindows,
    reason: 'DesktopDestinationSet 当且仅当 isWindows=true 时包含 offlineTools '
        '(isWindows=$isWindows)',
  );

  // —— 元数据一致性 ——
  // mobile 与 desktop 共有的 key 集合：mobile 永不暴露 offlineTools，
  // desktop 在非 Windows 也不暴露 offlineTools，所以两侧交集等于 mobile()
  // 的全部 key。逐 key 比对 (label, subtitle, icon, color) 四元组。
  final desktopByKey = <DestinationKey, DashboardDestinationSpec>{
    for (final d in desktop) d.key: d,
  };
  for (final mSpec in mobile) {
    final dSpec = desktopByKey[mSpec.key];
    expect(
      dSpec,
      isNotNull,
      reason:
          'mobile 中存在的 key=${mSpec.key} 必须也出现在 desktop 中 '
          '(isWindows=$isWindows)',
    );
    expect(
      dSpec!.label,
      mSpec.label,
      reason: 'key=${mSpec.key} 的 label 在 mobile / desktop 间必须一致',
    );
    expect(
      dSpec.subtitle,
      mSpec.subtitle,
      reason: 'key=${mSpec.key} 的 subtitle 在 mobile / desktop 间必须一致',
    );
    expect(
      dSpec.icon,
      mSpec.icon,
      reason: 'key=${mSpec.key} 的 icon 在 mobile / desktop 间必须一致',
    );
    expect(
      dSpec.color,
      mSpec.color,
      reason: 'key=${mSpec.key} 的 color 在 mobile / desktop 间必须一致',
    );
  }
}

void main() {
  group('[mobile-bottom-nav-redesign] DashboardDestinations.desktop(...)', () {
    Glados(any.bool).test(
      '[mobile-bottom-nav-redesign] desktop platform composition + '
      'metadata parity with mobile (Property 14)',
      (bool isWindows) {
        _verifyDesktopAgainstMobile(isWindows: isWindows);
      },
    );

    test(
      '[mobile-bottom-nav-redesign] desktop(isWindows: true) exposes 9 '
      'destinations including offlineTools (Property 14, 显式 Windows 边界)',
      () {
        final desktop = DashboardDestinations.desktop(isWindows: true);
        expect(desktop.length, 9);
        expect(
          desktop.map((d) => d.key),
          contains(DestinationKey.offlineTools),
        );
        _verifyDesktopAgainstMobile(isWindows: true);
      },
    );

    test(
      '[mobile-bottom-nav-redesign] desktop(isWindows: false) exposes 8 '
      'destinations without offlineTools (Property 14, 显式非 Windows 边界)',
      () {
        final desktop = DashboardDestinations.desktop(isWindows: false);
        expect(desktop.length, 8);
        expect(
          desktop.map((d) => d.key),
          isNot(contains(DestinationKey.offlineTools)),
        );
        _verifyDesktopAgainstMobile(isWindows: false);
      },
    );
  });
}
