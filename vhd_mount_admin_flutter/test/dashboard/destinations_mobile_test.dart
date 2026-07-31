// Property test for `DashboardDestinations.mobile()` 永不暴露 OfflineTools。
//
// `mobile()` 返回的 Destination 集合与运行平台无关——它没有 `isWindows`
// 入参。但需求 2.2 / 7.1 / 9.5 共同要求该结论对任意宿主平台都成立，
// 所以这里仍然让 Glados 在 `bool` 上枚举 `isWindows`，以「输入空间穷举」
// 的形式把「平台无关」这件事写成可被 PBT 框架反复验证的不变量。
//
// **Validates: Requirements 2.2, 7.1, 9.5**

// 这是一条纯逻辑层的属性测试，不渲染 widget，所以只引入 `glados`（它内部
// 已经 re-export 了 `package:test` 的 `group` / `test` / `expect`）。同时
// 引入 `flutter_test` 会与 `glados` re-export 的同名符号冲突，故不引入。
import 'package:glados/glados.dart';
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  group('[mobile-bottom-nav-redesign] DashboardDestinations.mobile()', () {
    Glados(any.bool).test(
      '[mobile-bottom-nav-redesign] never exposes OfflineTools regardless of host platform (Property 3)',
      (bool isWindows) {
        // `mobile()` 没有 platform 入参；`isWindows` 在这里只是为了把「平台
        // 无关」写成显式的输入空间——无论是 Windows 还是非 Windows，
        // mobile destination set 都不应暴露 OfflineTools。
        final mobile = DashboardDestinations.mobile();

        expect(
          mobile.every((d) => d.key != DestinationKey.offlineTools),
          isTrue,
          reason:
              'MobileDestinationSet 不得包含 offlineTools (isWindows=$isWindows)',
        );
        expect(
          mobile.length,
          8,
          reason:
              'MobileDestinationSet 总数恒为 8，与 isWindows 无关 '
              '(isWindows=$isWindows)',
        );
      },
    );
  });
}
