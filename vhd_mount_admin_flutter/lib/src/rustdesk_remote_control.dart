part of '../app.dart';

/// "RustDesk 远程控制"父级页面（任务 17.4 / Requirement 13.1 / 15.4）。
///
/// 仅作为 Tab 容器：
/// - Tab 1 → [TrustedRustDeskControllersView]（可信主控端管理）
/// - Tab 2 → [BridgeSecretView]（命名管道交互密钥）
///
/// shell.dart 导航挂载在 Wave 5 任务 17.5 接入；本波先创建页面，**不**触动 shell。
class RustDeskRemoteControlView extends StatefulWidget {
  const RustDeskRemoteControlView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<RustDeskRemoteControlView> createState() =>
      _RustDeskRemoteControlViewState();
}

class _RustDeskRemoteControlViewState extends State<RustDeskRemoteControlView>
    with SingleTickerProviderStateMixin {
  late final TabController _tabController;

  static const _tabs = <String>['可信主控端', '命名管道交互密钥', '上报记录'];

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: _tabs.length, vsync: this);
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        const PageHeader(
          eyebrow: 'RustDesk 远程控制',
          title: 'RustDesk 远程控制',
          subtitle: '可信主控端列表与 RustDeskClientSharedSecret 版本管理。两个子 Tab 均要求 OTP 二次验证。',
        ),
        const SizedBox(height: 16),
        Material(
          color: Colors.transparent,
          child: TabBar(
            controller: _tabController,
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            indicatorSize: TabBarIndicatorSize.label,
            dividerColor: AppPalette.border.withValues(alpha: 0.5),
            labelStyle: Theme.of(context).textTheme.labelLarge?.copyWith(
              fontWeight: FontWeight.w600,
            ),
            unselectedLabelStyle: Theme.of(context).textTheme.labelLarge,
            tabs: _tabs
                .map((label) => Tab(text: label))
                .toList(growable: false),
          ),
        ),
        const SizedBox(height: 16),
        Expanded(
          child: TabBarView(
            controller: _tabController,
            children: <Widget>[
              TrustedRustDeskControllersView(
                controller: widget.controller,
                embedInParentScroll: widget.embedInParentScroll,
              ),
              BridgeSecretView(
                controller: widget.controller,
                embedInParentScroll: widget.embedInParentScroll,
              ),
              RustDeskReportsView(
                controller: widget.controller,
                embedInParentScroll: widget.embedInParentScroll,
              ),
            ],
          ),
        ),
      ],
    );
  }
}
