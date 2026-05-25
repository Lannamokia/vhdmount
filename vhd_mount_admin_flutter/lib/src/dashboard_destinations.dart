part of '../app.dart';

/// 集中所有 Dashboard Destination 的元数据与平台相关派生集合。
///
/// 该 helper 是纯函数式 API：不依赖 [BuildContext]、`Platform`、`MediaQuery`
/// 或任何 widget 树状态，输入相同则输出完全相同，便于属性测试覆盖。
/// 平台相关分支（例如 Windows 是否暴露 OfflineTools）通过显式的 `isWindows`
/// 入参表达，调用方在 widget 层按 `Platform.isWindows` 注入。
class DashboardDestinations {
  const DashboardDestinations._();

  /// 全部 Destination 的元数据（含 OfflineTools），是 [mobile] / [desktop]
  /// 等派生集合的 single source of truth。
  ///
  /// 顺序与 [DestinationKey] 枚举顺序一致，为 [mobile] / [desktop] 派生
  /// 集合的可见顺序提供稳定基线。
  static const List<DashboardDestinationSpec> all = <DashboardDestinationSpec>[
    DashboardDestinationSpec(
      key: DestinationKey.machines,
      label: '机器管理',
      subtitle: '审批、保护、EVHD',
      icon: Icons.dns_rounded,
      color: AppPalette.coral,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.machineLogs,
      label: '机台日志',
      subtitle: '会话、分页、详情',
      icon: Icons.receipt_long_rounded,
      color: AppPalette.sun,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.certificates,
      label: '证书',
      subtitle: '信任链、PEM、移除',
      icon: Icons.verified_user_rounded,
      color: AppPalette.sky,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.audit,
      label: '审计',
      subtitle: '过滤、搜索、回溯',
      icon: Icons.history_rounded,
      color: AppPalette.mint,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.settings,
      label: '设置',
      subtitle: 'OTP、密码、默认值',
      icon: Icons.tune_rounded,
      color: AppPalette.sun,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.deployments,
      label: '部署管理',
      subtitle: '包上传、任务下发、历史',
      icon: Icons.rocket_launch_rounded,
      color: AppPalette.coral,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.rustDeskRemoteControl,
      label: 'RustDesk 远程控制',
      subtitle: '可信主控端、命名管道密钥',
      icon: Icons.cast_rounded,
      color: AppPalette.sky,
    ),
    DashboardDestinationSpec(
      key: DestinationKey.offlineTools,
      label: '离线工具',
      subtitle: '密钥、清单、证书',
      icon: Icons.build_circle_rounded,
      color: AppPalette.sky,
    ),
  ];

  /// MobileBottomNav 在 CompactLayout 下暴露的 Destination 集合。
  ///
  /// 与运行平台无关，恒为 7 项，永远不包含 [DestinationKey.offlineTools]。
  static List<DashboardDestinationSpec> mobile() => all
      .where((d) => d.key != DestinationKey.offlineTools)
      .toList(growable: false);

  /// DesktopSidebar 在非 CompactLayout 下暴露的 Destination 集合。
  ///
  /// Windows 上为 8 项（包含 [DestinationKey.offlineTools]），其他平台 7 项。
  static List<DashboardDestinationSpec> desktop({required bool isWindows}) =>
      isWindows
          ? all.toList(growable: false)
          : all
                .where((d) => d.key != DestinationKey.offlineTools)
                .toList(growable: false);

  /// 由 [mobile] 派生的 [MobileBottomNav] 槽位划分。
  static MobileNavLayout mobileNavLayout() =>
      MobileNavLayout.fromMobileSet(mobile());

  /// 把 [DestinationKey] 解析回对应的 [DashboardDestinationSpec]。
  ///
  /// `all.length` 上限为 8，线性扫描即可；找不到时按 [Iterable.firstWhere]
  /// 抛出 [StateError]——key 来自 enum，理论上不会越界。
  static DashboardDestinationSpec specOf(DestinationKey key) =>
      all.firstWhere((d) => d.key == key);
}

/// MobileBottomNav 的「主槽 vs 溢出槽」决策结果。
///
/// 这是一个纯值对象 + 纯函数构造，没有任何 widget 依赖；它把「哪些
/// Destination 进入顶层主槽、哪些落入溢出面板」的分发规则抽离成可被属性
/// 测试穷举的输入到输出映射。
class MobileNavLayout {
  const MobileNavLayout._({required this.primary, required this.overflow});

  /// 顶层可见的真实 Destination 槽（不含 overflow trigger），长度 ≤ 4。
  ///
  /// 渲染顺序与 [_primaryOrder] 中各 key 的相对顺序一致。
  final List<DashboardDestinationSpec> primary;

  /// 溢出面板内列出的 Destination，长度 = `mobileSet.length - primary.length`。
  ///
  /// 渲染顺序与传入 `mobileSet` 中各 key 的相对顺序一致。
  final List<DashboardDestinationSpec> overflow;

  /// 顶层可见槽位总数：`primary.length + 1`（含 overflow trigger）。
  ///
  /// 即使 [overflow] 为空，仍按 design §5 的设定保留触发器槽位。若调用方
  /// 想跳过触发器渲染，应自行检查 [hasOverflow]。
  int get visibleSlotCount => primary.length + 1;

  /// 给定的 mobile set 是否需要溢出面板（即 [overflow] 非空）。
  bool get hasOverflow => overflow.isNotEmpty;

  /// 主入口列表：按 [_primaryOrder] 命中顺序裁剪，剩余进入 [overflow]。
  ///
  /// 该常量是 MobileBottomNav 配置的单一来源，未来调整移动端主入口频次
  /// 或排序只改这一个数组，不动 widget 实现。
  static const List<DestinationKey> _primaryOrder = <DestinationKey>[
    DestinationKey.machines,
    DestinationKey.machineLogs,
    DestinationKey.audit,
    DestinationKey.deployments,
  ];

  /// 由 mobile set 构造 layout。
  ///
  /// - `primary`：按 [_primaryOrder] 中出现的 key 与 `mobileSet` 取交集，
  ///   保持 [_primaryOrder] 的相对顺序。
  /// - `overflow`：`mobileSet` 中除 `primary` 之外的剩余项，保持 `mobileSet`
  ///   的相对顺序。
  ///
  /// 不变量：`primary ∩ overflow == ∅`、`primary ∪ overflow == mobileSet`。
  factory MobileNavLayout.fromMobileSet(
    List<DashboardDestinationSpec> mobileSet,
  ) {
    final byKey = <DestinationKey, DashboardDestinationSpec>{
      for (final d in mobileSet) d.key: d,
    };
    final primary = <DashboardDestinationSpec>[
      for (final k in _primaryOrder)
        if (byKey.containsKey(k)) byKey[k]!,
    ];
    final primaryKeys = primary.map((d) => d.key).toSet();
    final overflow = <DashboardDestinationSpec>[
      for (final d in mobileSet)
        if (!primaryKeys.contains(d.key)) d,
    ];
    assert(
      primary.length + 1 <= 5,
      'PrimaryDestinationSlots + overflow trigger 必须 ≤5',
    );
    return MobileNavLayout._(
      primary: List<DashboardDestinationSpec>.unmodifiable(primary),
      overflow: List<DashboardDestinationSpec>.unmodifiable(overflow),
    );
  }

  /// 给定当前激活的 key，是否落在主槽。
  bool isPrimary(DestinationKey key) => primary.any((d) => d.key == key);

  /// 给定当前激活的 key，是否落在溢出槽。
  bool isOverflow(DestinationKey key) => overflow.any((d) => d.key == key);
}

/// `LayoutBuilder` 输入到「mobile / compact」分类的纯函数包装。
///
/// 与 [DashboardScreen.build] 内的判定逻辑等价，抽离出来以便属性测试在
/// `(maxWidth, maxHeight) ∈ ℝ⁺²` 上覆盖断点边界。
class LayoutClassification {
  const LayoutClassification._();

  /// 根据 `LayoutBuilder` 报告的 `maxWidth` / `maxHeight` 计算 mobile / compact。
  ///
  /// - `mobile`：`maxWidth < 720`
  /// - `compact`：`mobile || maxWidth < 1100 || maxHeight < 720`
  static ({bool mobile, bool compact}) classify(
    double maxWidth,
    double maxHeight,
  ) {
    final mobile = maxWidth < 720;
    final compact = mobile || maxWidth < 1100 || maxHeight < 720;
    return (mobile: mobile, compact: compact);
  }
}
