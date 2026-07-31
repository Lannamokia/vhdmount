// Property test for `MobileNavLayout` 结构不变量。
//
// Validates: Requirements 1.1, 1.4
//
// Property 1 — Layout 结构不变量：
//   对任意 MobileDestinationSet（即 `DashboardDestinations.mobile()` 的任意
//   非空有序子集），`MobileNavLayout.fromMobileSet` 构造的 layout 都应满足：
//
//     * primary.length ≤ 4
//     * visibleSlotCount ≤ 5（primary + 1 个 overflow trigger）
//     * primary ∩ overflow == ∅
//     * primary ∪ overflow == 输入集合
//     * hasOverflow == (overflow.isNotEmpty)
//
// 生成器策略：Glados 生成长度恰为 `mobile().length` 的 `List<bool>` 掩码，
// 按位过滤得到「保持原有相对顺序的有序子集」；空子集直接跳过（Property 1
// 关注非空子集）。
import 'package:flutter_test/flutter_test.dart';
// `package:glados/glados.dart` re-exports `package:test`, which collides with
// `flutter_test` 的 `expect` / `test`。隐藏其转出符号，统一走 flutter_test。
import 'package:glados/glados.dart' hide expect, test, group, setUp, tearDown;
import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  // 基线：当前 mobile set 应为 7 项（不含 OfflineTools），生成器使用此长度。
  final mobileBase = DashboardDestinations.mobile();
  assert(
    mobileBase.length == 8,
    'mobile() 当前应为 8 项；如需调整请同步更新生成器长度。',
  );

  Glados<List<bool>>(
    any.listWithLength(mobileBase.length, any.bool),
  ).test(
    '[mobile-bottom-nav-redesign] Property 1: '
    'MobileNavLayout 结构不变量在任意非空 mobile 子集上成立',
    (mask) {
      // 按位掩码裁出输入子集，保持 mobile() 中的相对顺序。
      final subset = <DashboardDestinationSpec>[
        for (var i = 0; i < mobileBase.length; i++)
          if (mask[i]) mobileBase[i],
      ];
      // Property 1 显式限定 "非空子集"，空集走 Property 14 类的全集不变量，
      // 这里直接跳过以缩小搜索空间。
      if (subset.isEmpty) return;

      final layout = MobileNavLayout.fromMobileSet(subset);

      // (1) 主槽数量上限：≤4。
      expect(
        layout.primary.length,
        lessThanOrEqualTo(4),
        reason: 'primary.length 必须 ≤4，实际=${layout.primary.length}',
      );

      // (2) 顶层可见槽位总数：≤5（含 overflow trigger）。
      expect(
        layout.visibleSlotCount,
        lessThanOrEqualTo(5),
        reason:
            'visibleSlotCount 必须 ≤5，实际=${layout.visibleSlotCount}',
      );

      final primaryKeys = layout.primary.map((d) => d.key).toSet();
      final overflowKeys = layout.overflow.map((d) => d.key).toSet();

      // (3) primary ∩ overflow == ∅。
      expect(
        primaryKeys.intersection(overflowKeys),
        isEmpty,
        reason: 'primary 与 overflow 不应有共同 key',
      );

      // (4) primary ∪ overflow == 输入集合。
      final inputKeys = subset.map((d) => d.key).toSet();
      expect(
        primaryKeys.union(overflowKeys),
        equals(inputKeys),
        reason: 'primary ∪ overflow 必须恰好覆盖输入集合',
      );

      // 同时确保 primary 与 overflow 列表本身没有重复（避免集合化掩盖列表重复）。
      expect(
        layout.primary.length,
        equals(primaryKeys.length),
        reason: 'primary 列表不应含重复项',
      );
      expect(
        layout.overflow.length,
        equals(overflowKeys.length),
        reason: 'overflow 列表不应含重复项',
      );
      expect(
        layout.primary.length + layout.overflow.length,
        equals(subset.length),
        reason: 'primary + overflow 项数必须等于输入子集大小',
      );

      // (5) hasOverflow 与 overflow.isNotEmpty 等价。
      expect(
        layout.hasOverflow,
        equals(layout.overflow.isNotEmpty),
        reason: 'hasOverflow 应当严格等于 overflow.isNotEmpty',
      );
    },
  );
}
