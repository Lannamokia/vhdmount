part of '../app.dart';

/// MobileBottomNav 在 CompactLayout 下作为 [Scaffold.bottomNavigationBar]
/// 渲染的导航组件，替代当前直接平铺 7–8 项的 [NavigationBar] 用法。
///
/// 该 widget 不复用 [NavigationBar]，因为 design §5 要求「active key 落在
/// overflow 但 sheet 关闭」时溢出触发器为「未选中」视觉态——这与 Material
/// `NavigationBar` 强制 `selectedIndex ∈ [0, destinations.length)` 的行为冲突。
///
/// 选中视觉态由外部传入的 [activeKey] 驱动；MobileBottomNav 不持有「页面切换」
/// 副作用，只通过 [onDestinationSelected] 通知调用方。
///
/// 任务 2.2 在主槽序列后追加「更多」触发器：当 [MobileNavLayout.hasOverflow]
/// 为 true 时，触发器作为最后一个可见槽位渲染，点击后通过
/// [showModalBottomSheet] 打开扁平的 [_OverflowSheet]；触发器选中态遵循
/// design §5.2：`_sheetOpen && layout.isOverflow(activeKey)`。
class MobileBottomNav extends StatefulWidget {
  const MobileBottomNav({
    super.key,
    required this.layout,
    required this.activeKey,
    required this.onDestinationSelected,
  });

  /// 由 [DashboardDestinations.mobileNavLayout] 注入。
  final MobileNavLayout layout;

  /// 当前激活的 Destination；MobileBottomNav 用此驱动选中视觉态。
  final DestinationKey activeKey;

  /// 选中目的地时回调。MobileBottomNav 不持有「页面切换」副作用。
  final ValueChanged<DestinationKey> onDestinationSelected;

  /// 单一渲染高度来源；与 [NavigationBarThemeData.height] 解耦但保持相同值。
  ///
  /// 调用方需把 `Scaffold` 内部的滚动 `padding.bottom` 至少设为
  /// `kHeight + MediaQuery.viewPaddingOf(context).bottom` 以避免内容被遮挡。
  static const double kHeight = 76;

  @override
  State<MobileBottomNav> createState() => _MobileBottomNavState();
}

class _MobileBottomNavState extends State<MobileBottomNav> {
  /// 溢出 sheet 的开合状态。
  ///
  /// 由 [_openOverflowSheet] 在 sheet 打开/关闭时同步翻转，驱动「更多」
  /// 触发器的选中态视觉规则（design §5.2 / Property 6）。
  bool _sheetOpen = false;

  /// sheet 打开期间持有的 [NavigatorState] 引用。
  ///
  /// design §Error Handling 3 要求：用户在 Compact 下打开 overflow sheet 后，
  /// 若视口跨断点切换到非 Compact，[MobileBottomNav] 会从 widget 树移除，
  /// 但 `showModalBottomSheet` 注册到 [Navigator] 的 sheet 路由仍在栈顶。
  ///
  /// 这里在 sheet 打开时提前捕获 root [NavigatorState]，使 [dispose] 可以
  /// 在 [BuildContext] 已 deactivate 的情况下安全 pop 残留路由。`Navigator.of`
  /// 在 dispose 阶段重新解析 context 是不可靠的（context 可能已 unmount）。
  NavigatorState? _capturedNavigator;

  void _handlePrimaryTap(DashboardDestinationSpec spec) {
    widget.onDestinationSelected(spec.key);
  }

  /// 打开溢出 sheet。
  ///
  /// 配置遵循 design §5.2：`useSafeArea: true`、`isScrollControlled: false`、
  /// 顶部 24 圆角、背景色 [AppPalette.surfaceStrong]。打开期间标记
  /// [_sheetOpen] 让触发器进入选中态；sheet 关闭后（无论是用户点击行、
  /// 滑下还是路由 pop）恢复未选中态。
  ///
  /// 打开前提前捕获 root [NavigatorState]，便于 [dispose] 在跨断点拆除
  /// 时安全 maybePop 遗留路由（design §Error Handling 3）。
  Future<void> _openOverflowSheet() async {
    if (_sheetOpen) {
      return;
    }
    _capturedNavigator = Navigator.of(context, rootNavigator: true);
    setState(() {
      _sheetOpen = true;
    });
    try {
      await showModalBottomSheet<void>(
        context: context,
        backgroundColor: AppPalette.surfaceStrong,
        useSafeArea: true,
        isScrollControlled: false,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
        ),
        builder: (sheetContext) => _OverflowSheet(
          overflow: widget.layout.overflow,
          activeKey: widget.activeKey,
          onSelect: widget.onDestinationSelected,
        ),
      );
    } finally {
      if (mounted) {
        setState(() {
          _sheetOpen = false;
        });
      }
      _capturedNavigator = null;
    }
  }

  @override
  void didUpdateWidget(MobileBottomNav oldWidget) {
    super.didUpdateWidget(oldWidget);
    // R8.2：当 activeKey 因外部 CrossPageNavigation 变化时，MobileBottomNav 必须
    // 同帧刷新选中槽位。Flutter 在父级重建并下发新 widget 时会自动调度本 State 的
    // build，而 build 内 `triggerSelected` 与每个主槽 `isSelected` 都直接读取
    // `widget.activeKey`，因此选中态在同一帧内被重算，无需显式 setState。
    //
    // 这里仍保留 didUpdateWidget 重写：
    //   - 把「activeKey 变更同帧重建 trigger 选中态」的契约固化为代码锚点；
    //   - 给后续 Property 6 / R8.2 widget 测试一个稳定的断言点（测试可以
    //     pump 新 activeKey 后立刻 expect 选中态切换，而不必等下一帧）。
    assert(() {
      if (oldWidget.activeKey != widget.activeKey) {
        // 仅在 debug 模式下做不变量检查：activeKey 切换不应留下脏的
        // _sheetOpen——sheet 自管开合，外部 activeKey 切换不会篡改它。
        // 这里不做断言强约束，仅保留逻辑入口。
      }
      return true;
    }());
  }

  @override
  void dispose() {
    // design §Error Handling 3：跨断点拆除 MobileBottomNav 时，若 overflow sheet
    // 仍开着，需要主动关闭以免留下孤儿模态。Navigator.of(context, ...) 在
    // dispose 阶段不可靠（context 可能已 deactivate），因此使用提前捕获的
    // [_capturedNavigator]。maybePop 在没有可 pop 的路由时是 no-op，安全。
    if (_sheetOpen) {
      try {
        _capturedNavigator?.maybePop();
      } catch (_) {
        // dispose 阶段 Navigator 可能已经无法 pop（NavigatorState unmount /
        // 路由栈已空），按 design 契约吞掉异常，State 拆除流程不能被打断。
      }
    }
    _capturedNavigator = null;
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final layout = widget.layout;
    final activeKey = widget.activeKey;
    final triggerSelected = _sheetOpen && layout.isOverflow(activeKey);

    return Material(
      color: AppPalette.surfaceStrong,
      elevation: 0,
      child: SafeArea(
        top: false,
        left: false,
        right: false,
        child: SizedBox(
          height: MobileBottomNav.kHeight,
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceEvenly,
            crossAxisAlignment: CrossAxisAlignment.center,
            children: <Widget>[
              for (final spec in layout.primary)
                Expanded(
                  child: _MobileBottomNavSlot(
                    icon: spec.icon,
                    label: spec.label,
                    isSelected: spec.key == activeKey,
                    onTap: () => _handlePrimaryTap(spec),
                  ),
                ),
              if (layout.hasOverflow)
                Expanded(
                  child: _MobileBottomNavSlot(
                    icon: Icons.more_horiz_rounded,
                    label: '更多',
                    isSelected: triggerSelected,
                    onTap: _openOverflowSheet,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// MobileBottomNav 主槽 / 溢出触发器槽的统一渲染单元。
///
/// 视觉契约（与 design §5.1 / Property 10 / Property 11 / Property 13 对齐）：
///
/// - 外壳 [SizedBox] 固定 `height = 56`，并以 [ConstrainedBox] 保底
///   `minWidth = 56`，对应 R1.3。
/// - [InkWell] 内部 `EdgeInsets.symmetric(vertical: 8, horizontal: 4)`
///   把命中区限制在 ≥48×48，对应 R5.1。
/// - 标签使用 [NavigationBarThemeData.labelTextStyle] 解析后的 [TextStyle]，
///   保留 13sp 与选中态字重切换；外层 [FittedBox] 仅作截断兜底。
/// - 选中态指示器复用 [NavigationBarThemeData.indicatorColor]，避免重复
///   定义主题（R6.3 不破坏）。
/// - 顶层 [Semantics] 节点暴露 `label = label`、`button = true`、
///   `selected = isSelected`，并以 `excludeSemantics: true` 屏蔽装饰性
///   icon / Text 子节点（R5.5）。
///
/// 该 slot 同时服务于：
///   1. 主槽：`label`/`icon` 来自 [DashboardDestinationSpec]；
///   2. 溢出触发器：`label = '更多'`、`icon = Icons.more_horiz_rounded`。
class _MobileBottomNavSlot extends StatelessWidget {
  const _MobileBottomNavSlot({
    required this.icon,
    required this.label,
    required this.isSelected,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final navTheme = theme.navigationBarTheme;
    final indicatorColor =
        navTheme.indicatorColor ?? AppPalette.mint.withValues(alpha: 0.16);
    final resolvedLabelStyle = navTheme.labelTextStyle?.resolve(<WidgetState>{
      if (isSelected) WidgetState.selected,
    });
    final fallbackLabelStyle = miSansTextStyle(
      fontSize: 13,
      fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
      height: 1.2,
      color: isSelected ? AppPalette.ink : AppPalette.muted,
    );
    final labelStyle = resolvedLabelStyle ?? fallbackLabelStyle;
    final iconColor = isSelected ? AppPalette.ink : AppPalette.muted;

    return Semantics(
      container: true,
      button: true,
      selected: isSelected,
      label: label,
      excludeSemantics: true,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minWidth: 56),
        child: SizedBox(
          height: 56,
          child: InkWell(
            onTap: onTap,
            borderRadius: BorderRadius.circular(16),
            // 几何预算（design §5.1 / Property 10 / Property 11）：
            //
            //   外壳 SizedBox.height = 56
            //   - Padding.vertical = 6 × 2 = 12
            //   = 内层 Column 可用 44dp
            //
            //   Column 自然高：Icon 22 + spacer 2 + Text 13sp×1.2 ≈ 15.6
            //                = 39.6dp ≤ 44dp ✓（留 ~4dp 头空给字体 metrics 抖动）
            //
            // 旧版本 vertical:8 + spacer:4 让 Column 自然高 ≈ 41.6 略超过 40dp
            // 内层预算，触发 ~2px RenderFlex overflow。Property 11 typography
            // 测试在 360–1099dp 视口下直接抛 5×framework exception 把测试整体
            // 拉黑。这里用「padding 6 + spacer 2」把预算抬到 44dp 同时保留
            // 视觉间距，InkWell 命中盒仍由外层 SizedBox 56×≥56 决定，R5.1 的
            // 48×48 触摸下限不受影响。
            child: Padding(
              padding: const EdgeInsets.symmetric(
                vertical: 6,
                horizontal: 4,
              ),
              child: ConstrainedBox(
                constraints: const BoxConstraints(minWidth: 48),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: <Widget>[
                    Stack(
                      alignment: Alignment.center,
                      children: <Widget>[
                        if (isSelected)
                          Container(
                            width: 32,
                            height: 22,
                            decoration: BoxDecoration(
                              color: indicatorColor,
                              borderRadius: BorderRadius.circular(11),
                            ),
                          ),
                        Icon(icon, size: 22, color: iconColor),
                      ],
                    ),
                    const SizedBox(height: 2),
                    FittedBox(
                      fit: BoxFit.scaleDown,
                      child: Text(
                        label,
                        maxLines: 1,
                        softWrap: false,
                        style: labelStyle,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// 溢出面板：纯 [Column] + 每个 overflow Destination 一行 [ListTile]。
///
/// 严格保持 design §5.3 / Property 4 的「单层扁平」契约：
///
/// - 不允许 [ExpansionTile] / [TabBar] / [PageView] / 内层
///   [showModalBottomSheet] 等任何嵌套子菜单形态。
/// - 行数恒等于 `overflow.length`，渲染顺序与 [MobileNavLayout.overflow]
///   一致。
/// - 每行 label 字号 ≥14sp（[ListTile] 默认走主题 `bodyLarge` ≈16sp，
///   超过 R5.3 的 14sp 下限）。
/// - 每行外层 [Semantics] 暴露 `label = spec.label`、`button = true`、
///   `selected = (spec.key == activeKey)`，与主槽 Semantics 语义一致。
///
/// 点击行的副作用顺序：先 `Navigator.pop`（关闭 sheet），再调用
/// `onSelect(key)`。`try/catch/finally` 保证即使 `pop` 抛出异常，
/// `onSelect` 仍被精确调用一次（R8.5 / Property 7）。
class _OverflowSheet extends StatelessWidget {
  const _OverflowSheet({
    required this.overflow,
    required this.activeKey,
    required this.onSelect,
  });

  /// 溢出槽内的 Destination 列表，由 [MobileNavLayout.overflow] 注入。
  final List<DashboardDestinationSpec> overflow;

  /// 当前激活的 Destination；用于把活动行渲染为 selected 视觉态。
  final DestinationKey activeKey;

  /// 用户点击溢出行后回调，语义与
  /// [MobileBottomNav.onDestinationSelected] 完全一致。
  final ValueChanged<DestinationKey> onSelect;

  void _handleTap(BuildContext rowContext, DestinationKey key) {
    // 顺序：先 pop 后 callback；用 try/finally 包裹保证即便 pop 抛出，
    // onSelect 仍被调用一次（R8.5 / Property 7）。
    try {
      Navigator.of(rowContext).pop();
    } catch (_) {
      // 吞掉 pop 异常；选择不能因为 dismiss 失败而被吃掉。
    } finally {
      onSelect(key);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        for (final spec in overflow)
          Semantics(
            container: true,
            button: true,
            selected: spec.key == activeKey,
            label: spec.label,
            excludeSemantics: true,
            child: ListTile(
              leading: Icon(
                spec.icon,
                color: spec.key == activeKey
                    ? AppPalette.ink
                    : AppPalette.muted,
              ),
              title: Text(
                spec.label,
                style: miSansTextStyle(
                  fontSize: 16,
                  fontWeight: spec.key == activeKey
                      ? FontWeight.w700
                      : FontWeight.w500,
                  color: AppPalette.ink,
                ),
              ),
              selected: spec.key == activeKey,
              onTap: () => _handleTap(context, spec.key),
            ),
          ),
      ],
    );
  }
}
