part of '../app.dart';

class DashboardScreen extends StatelessWidget {
  const DashboardScreen({
    super.key,
    required this.controller,
    required this.selectedIndex,
    required this.onDestinationSelected,
  });

  final AppController controller;
  final int selectedIndex;
  final ValueChanged<int> onDestinationSelected;

  @override
  Widget build(BuildContext context) {
    Future<void> openLogsForMachine(String machineId) async {
      await controller.loadMachineLogSessions(machineId: machineId);
      onDestinationSelected(1);
    }

    Future<void> openAuditForMachine(String machineId) async {
      await controller.loadAudit(machineId: machineId);
      onDestinationSelected(3);
    }

    final destinations = <DashboardDestinationSpec>[
      const DashboardDestinationSpec(
        label: '机器管理',
        subtitle: '审批、保护、EVHD',
        icon: Icons.dns_rounded,
        color: AppPalette.coral,
      ),
      const DashboardDestinationSpec(
        label: '机台日志',
        subtitle: '会话、分页、详情',
        icon: Icons.receipt_long_rounded,
        color: AppPalette.sun,
      ),
      const DashboardDestinationSpec(
        label: '证书',
        subtitle: '信任链、PEM、移除',
        icon: Icons.verified_user_rounded,
        color: AppPalette.sky,
      ),
      const DashboardDestinationSpec(
        label: '审计',
        subtitle: '过滤、搜索、回溯',
        icon: Icons.history_rounded,
        color: AppPalette.mint,
      ),
      const DashboardDestinationSpec(
        label: '设置',
        subtitle: 'OTP、密码、默认值',
        icon: Icons.tune_rounded,
        color: AppPalette.sun,
      ),
      const DashboardDestinationSpec(
        label: '部署管理',
        subtitle: '包上传、任务下发、历史',
        icon: Icons.rocket_launch_rounded,
        color: AppPalette.coral,
      ),
    ];

    Future<void> openOtpDialog() async {
      final code = await showSingleInputDialog(
        context,
        title: 'OTP 二次验证',
        label: '验证码',
        obscureText: false,
      );
      if (code == null || code.trim().isEmpty) {
        return;
      }
      try {
        await controller.verifyOtp(code.trim());
        if (!context.mounted) {
          return;
        }
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('OTP 验证成功。')));
      } catch (error) {
        if (!context.mounted) {
          return;
        }
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(describeError(error))));
      }
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final mobile = constraints.maxWidth < 720;
        final compact =
            mobile ||
            constraints.maxWidth < 1100 ||
            constraints.maxHeight < 720;
        final extendBodyForBottomNav = mobile && compact;
        final showOverviewCaptions = !mobile;
        final overviewCards = <Widget>[
          OverviewStatCard(
            label: '数据库',
            value: controller.serverStatus?.databaseReady == true
                ? '已连接'
                : '异常',
            icon: Icons.storage_rounded,
            color: controller.serverStatus?.databaseReady == true
                ? AppPalette.mint
                : AppPalette.coral,
            caption: showOverviewCaptions ? '服务端数据库链路健康状态。' : null,
          ),
          OverviewStatCard(
            label: '默认关键词',
            value: controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
            icon: Icons.bolt_rounded,
            color: AppPalette.coral,
            caption: showOverviewCaptions ? '所有机台的默认启动关键字。' : null,
          ),
          OverviewStatCard(
            label: 'OTP',
            value: controller.otpVerified ? '已验证' : '待验证',
            icon: Icons.verified_user_rounded,
            color: controller.otpVerified ? AppPalette.mint : AppPalette.sun,
            caption: showOverviewCaptions
                ? (controller.otpVerified ? '高敏操作已解锁。' : '证书与敏感操作仍受保护。')
                : null,
          ),
          OverviewStatCard(
            label: '当前入口',
            value: controller.baseUrl.replaceFirst(RegExp(r'^https?://'), ''),
            icon: Icons.link_rounded,
            color: AppPalette.sky,
            caption: showOverviewCaptions ? '当前连接的管理服务地址。' : null,
          ),
        ];
        final pages = <Widget>[
          MachinesView(
            controller: controller,
            onOpenLogsForMachine: openLogsForMachine,
            onOpenAuditForMachine: openAuditForMachine,
            embedInParentScroll: mobile,
          ),
          MachineLogsView(controller: controller, embedInParentScroll: mobile),
          CertificatesView(controller: controller, embedInParentScroll: mobile),
          AuditView(controller: controller, embedInParentScroll: mobile),
          SettingsView(controller: controller, embedInParentScroll: mobile),
          DeploymentsView(
            key: const Key('deployments_view'),
            controller: controller,
            embedInParentScroll: mobile,
          ),
        ];
        final overviewSection = OverviewStatsGrid(
          cards: overviewCards,
          singleRow: compact && !mobile,
          forceTwoColumnGrid: mobile,
        );

        final pageBody = AnimatedSwitcher(
          duration: const Duration(milliseconds: 280),
          switchInCurve: Curves.easeOutCubic,
          switchOutCurve: Curves.easeInCubic,
          transitionBuilder: (child, animation) {
            final offset = Tween<Offset>(
              begin: const Offset(0.03, 0),
              end: Offset.zero,
            ).animate(animation);
            return FadeTransition(
              opacity: animation,
              child: SlideTransition(position: offset, child: child),
            );
          },
          child: KeyedSubtree(
            key: ValueKey<int>(selectedIndex),
            child: pages[selectedIndex],
          ),
        );

        final contentChildren = <Widget>[
          PageHeader(
            eyebrow: 'Control Room',
            title: 'VHD Mount Admin',
            subtitle: '',
            actions: <Widget>[
              FilledButton.tonalIcon(
                onPressed: openOtpDialog,
                icon: const Icon(Icons.key_rounded),
                label: Text(controller.otpVerified ? '重新验证 OTP' : '验证 OTP'),
              ),
              OutlinedButton.icon(
                onPressed: () async {
                  await controller.bootstrap();
                },
                icon: const Icon(Icons.sync_rounded),
                label: const Text('刷新'),
              ),
              TextButton.icon(
                onPressed: () async {
                  await controller.logout();
                },
                icon: const Icon(Icons.logout_rounded),
                label: const Text('登出'),
              ),
            ],
          ),
          const SizedBox(height: 18),
          overviewSection,
          if (controller.errorMessage != null) ...<Widget>[
            const SizedBox(height: 16),
            ErrorBanner(message: controller.errorMessage!),
          ],
          const SizedBox(height: 20),
          if (mobile) pageBody else Expanded(child: pageBody),
        ];

        final contentColumn = Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: contentChildren,
        );

        return Scaffold(
          backgroundColor: AppPalette.canvasWarm,
          extendBody: extendBodyForBottomNav,
          bottomNavigationBar: compact
              ? NavigationBar(
                  selectedIndex: selectedIndex,
                  onDestinationSelected: onDestinationSelected,
                  destinations: destinations
                      .map(
                        (item) => NavigationDestination(
                          icon: Icon(item.icon),
                          label: item.label,
                        ),
                      )
                      .toList(),
                )
              : null,
          body: AdminBackdrop(
            child: SafeArea(
              child: Padding(
                padding: EdgeInsets.all(mobile ? 16 : 20),
                child: mobile
                    ? SingleChildScrollView(
                        padding: EdgeInsets.only(
                          bottom: extendBodyForBottomNav ? 112 : 20,
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: contentChildren,
                        ),
                      )
                    : compact
                    ? contentColumn
                    : Row(
                        children: <Widget>[
                          SizedBox(
                            width: 260,
                            child: AppPanel(
                              padding: const EdgeInsets.fromLTRB(
                                16,
                                18,
                                16,
                                18,
                              ),
                              gradient: const LinearGradient(
                                begin: Alignment.topLeft,
                                end: Alignment.bottomRight,
                                colors: <Color>[
                                  Color(0xFFFFF5EA),
                                  Color(0xFFF6FFF9),
                                ],
                              ),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  const AccentIconBadge(
                                    icon: Icons.auto_awesome_rounded,
                                    color: AppPalette.coral,
                                    size: 54,
                                  ),
                                  const SizedBox(height: 16),
                                  Text(
                                    '工作区',
                                    style: Theme.of(context)
                                        .textTheme
                                        .labelLarge
                                        ?.copyWith(color: AppPalette.coralDeep),
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    '安全控制面板',
                                    style: Theme.of(
                                      context,
                                    ).textTheme.titleLarge,
                                  ),
                                  const SizedBox(height: 20),
                                  Expanded(
                                    child: SingleChildScrollView(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.stretch,
                                        children: <Widget>[
                                          for (
                                            int index = 0;
                                            index < destinations.length;
                                            index++
                                          ) ...<Widget>[
                                            DashboardSidebarButton(
                                              label: destinations[index].label,
                                              subtitle:
                                                  destinations[index].subtitle,
                                              icon: destinations[index].icon,
                                              color: destinations[index].color,
                                              selected: selectedIndex == index,
                                              onTap: () =>
                                                  onDestinationSelected(index),
                                            ),
                                            if (index !=
                                                destinations.length - 1)
                                              const SizedBox(height: 12),
                                          ],
                                          const SizedBox(height: 16),
                                          InfoPanel(
                                            title: controller.otpVerified
                                                ? '高敏操作已解锁'
                                                : '等待 OTP 验证',
                                            icon: controller.otpVerified
                                                ? Icons.verified_rounded
                                                : Icons.lock_clock_rounded,
                                            color: controller.otpVerified
                                                ? AppPalette.mint
                                                : AppPalette.sun,
                                            body: Text(
                                              controller.otpVerified
                                                  ? '证书管理、密码读取等高敏操作当前可用。'
                                                  : '先完成 OTP 验证，再执行证书管理和敏感操作。',
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                          const SizedBox(width: 24),
                          Expanded(child: contentColumn),
                        ],
                      ),
              ),
            ),
          ),
        );
      },
    );
  }
}

class MachinesView extends StatelessWidget {
  const MachinesView({
    super.key,
    required this.controller,
    required this.onOpenLogsForMachine,
    required this.onOpenAuditForMachine,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final Future<void> Function(String machineId) onOpenLogsForMachine;
  final Future<void> Function(String machineId) onOpenAuditForMachine;
  final bool embedInParentScroll;

  @override
  Widget build(BuildContext context) {
    final emptyState = Center(
      child: SizedBox(
        width: 420,
        child: const InfoPanel(
          title: '当前还没有机器记录',
          body: Text('可以先创建机台，再继续做审批、保护和 EVHD 配置。'),
          icon: Icons.add_business_rounded,
          color: AppPalette.sky,
        ),
      ),
    );
    final machinesList = ListView.separated(
      itemCount: controller.machines.length,
      shrinkWrap: embedInParentScroll,
      physics: embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final machine = controller.machines[index];
        return Card(
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    AccentIconBadge(
                      icon: machine.approved
                          ? Icons.memory_rounded
                          : Icons.pending_actions_rounded,
                      color: machine.approved
                          ? AppPalette.mint
                          : AppPalette.sun,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            machine.machineId,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            machine.keyType == null
                                ? '尚未提交注册密钥'
                                : '密钥类型：${machine.keyType}',
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    StatusChip(
                      label: machine.approved ? '已审批' : '待审批',
                      color: machine.approved
                          ? AppPalette.mint
                          : AppPalette.sun,
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: <Widget>[
                    StatusChip(
                      label: '当前启动关键词 ${machine.vhdKeyword}',
                      color: AppPalette.sky,
                    ),
                    StatusChip(
                      label: machine.protectedState ? '保护开启' : '保护关闭',
                      color: machine.protectedState
                          ? AppPalette.coral
                          : AppPalette.mint,
                    ),
                    StatusChip(
                      label: machine.evhdPasswordConfigured
                          ? 'EVHD 已配置'
                          : 'EVHD 未配置',
                      color: machine.evhdPasswordConfigured
                          ? AppPalette.mint
                          : AppPalette.sun,
                    ),
                    StatusChip(
                      label: controller.describeMachineLogRetention(machine),
                      color: machine.logRetentionActiveDaysOverride != null
                          ? AppPalette.coral
                          : AppPalette.sky,
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Text('Key ID: ${machine.keyId ?? '未注册'}'),
                Text('最后在线: ${machine.lastSeen ?? '未知'}'),
                if (machine.registrationCertFingerprint != null)
                  Text('注册证书: ${machine.registrationCertFingerprint}'),
                const SizedBox(height: 16),
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: <Widget>[
                    FilledButton.tonal(
                      onPressed: () async {
                        try {
                          await controller.setMachineApproval(
                            machine.machineId,
                            !machine.approved,
                          );
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(
                              content: Text(
                                machine.approved ? '已取消审批。' : '已审批通过。',
                              ),
                            ),
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: Text(machine.approved ? '取消审批' : '审批通过'),
                    ),
                    OutlinedButton(
                      onPressed: () async {
                        try {
                          await controller.setMachineProtection(
                            machine.machineId,
                            !machine.protectedState,
                          );
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(
                              content: Text(
                                machine.protectedState ? '已关闭保护。' : '已开启保护。',
                              ),
                            ),
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: Text(machine.protectedState ? '关闭保护' : '开启保护'),
                    ),
                    OutlinedButton(
                      onPressed: () async {
                        try {
                          await controller.resetMachineRegistration(
                            machine.machineId,
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: const Text('重置注册'),
                    ),
                    OutlinedButton(
                      onPressed: () async {
                        final value = await showSingleInputDialog(
                          context,
                          title: '设置启动关键词',
                          label: '启动关键词',
                          initialValue: machine.vhdKeyword,
                        );
                        if (value == null || value.trim().isEmpty) {
                          return;
                        }
                        try {
                          await controller.setMachineVhd(
                            machine.machineId,
                            value.trim().toUpperCase(),
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: const Text('设置启动关键词'),
                    ),
                    OutlinedButton.icon(
                      onPressed: () async {
                        try {
                          await onOpenLogsForMachine(machine.machineId);
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      icon: const Icon(Icons.receipt_long_rounded),
                      label: const Text('查看机台日志'),
                    ),
                    OutlinedButton.icon(
                      onPressed: () async {
                        try {
                          await onOpenAuditForMachine(machine.machineId);
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      icon: const Icon(Icons.history_rounded),
                      label: const Text('查阅审计日志'),
                    ),
                    OutlinedButton(
                      onPressed: () async {
                        final initialValue =
                            machine.logRetentionActiveDaysOverride
                                ?.toString() ??
                            '';
                        final value = await showSingleInputDialog(
                          context,
                          title: '设置日志保留活动日覆盖值',
                          label: '留空表示继承全局默认值',
                          initialValue: initialValue,
                        );
                        if (value == null) {
                          return;
                        }

                        final trimmed = value.trim();
                        final overrideValue = trimmed.isEmpty
                            ? null
                            : int.tryParse(trimmed);
                        if (trimmed.isNotEmpty && overrideValue == null) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(content: Text('请输入有效的正整数，或留空恢复继承。')),
                          );
                          return;
                        }

                        try {
                          await controller.setMachineLogRetentionOverride(
                            machine.machineId,
                            overrideValue,
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: const Text('日志保留'),
                    ),
                    OutlinedButton(
                      onPressed: () async {
                        final value = await showSingleInputDialog(
                          context,
                          title: '设置 EVHD 密码',
                          label: 'EVHD 密码',
                          obscureText: true,
                        );
                        if (value == null || value.isEmpty) {
                          return;
                        }
                        try {
                          await controller.setMachineEvhdPassword(
                            machine.machineId,
                            value,
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: const Text('设置 EVHD'),
                    ),
                    FilledButton(
                      onPressed: () async {
                        final reason = await showSingleInputDialog(
                          context,
                          title: '读取 EVHD 明文',
                          label: '查询原因',
                          initialValue: 'support investigation',
                        );
                        if (reason == null || reason.trim().isEmpty) {
                          return;
                        }
                        try {
                          final password = await controller
                              .readPlainEvhdPassword(
                                machine.machineId,
                                reason.trim(),
                              );
                          if (!context.mounted) {
                            return;
                          }
                          await showDialog<void>(
                            context: context,
                            builder: (context) => AlertDialog(
                              title: const Text('EVHD 明文'),
                              content: SelectableText(password),
                              actions: <Widget>[
                                TextButton(
                                  onPressed: () => Navigator.of(context).pop(),
                                  child: const Text('关闭'),
                                ),
                              ],
                            ),
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      child: const Text('读取明文'),
                    ),
                    OutlinedButton.icon(
                      onPressed: () async {
                        final confirmed = await showConfirmDialog(
                          context,
                          title: '删除机台',
                          message:
                              '确认删除 ${machine.machineId} 吗？这会移除该机台的管理记录与已保存的 EVHD 密码。',
                          confirmLabel: '删除',
                        );
                        if (confirmed != true) {
                          return;
                        }
                        try {
                          await controller.deleteMachine(machine.machineId);
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(
                              content: Text('机台 ${machine.machineId} 已删除。'),
                            ),
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      icon: const Icon(Icons.delete_outline_rounded),
                      label: const Text('删除机台'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        );
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Machine Fleet',
          title: '机器管理',
          subtitle: '审批、保护、注册重置与 EVHD 配置都集中在这里。',
          actions: <Widget>[
            FilledButton.icon(
              onPressed: () async {
                final draft = await showAddMachineDialog(
                  context,
                  defaultVhdKeyword:
                      controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
                );
                if (draft == null) {
                  return;
                }
                try {
                  await controller.addMachine(draft);
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text('机台 ${draft.machineId} 已添加。')),
                  );
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(SnackBar(content: Text(describeError(error))));
                }
              },
              icon: const Icon(Icons.add_rounded),
              label: const Text('添加机台'),
            ),
            OutlinedButton.icon(
              onPressed: controller.loadMachines,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新列表'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (controller.machines.isEmpty)
          if (embedInParentScroll) emptyState else Expanded(child: emptyState)
        else if (embedInParentScroll)
          machinesList
        else
          Expanded(child: machinesList),
      ],
    );
  }
}

class CertificatesView extends StatelessWidget {
  const CertificatesView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  Widget build(BuildContext context) {
    final emptyState = Center(
      child: SizedBox(
        width: 420,
        child: const InfoPanel(
          title: '当前没有可信注册证书',
          body: Text('可以在完成 OTP 验证后导入第一张机台注册证书。'),
          icon: Icons.verified_user_rounded,
          color: AppPalette.sky,
        ),
      ),
    );
    final certificatesList = ListView.separated(
      itemCount: controller.certificates.length,
      shrinkWrap: embedInParentScroll,
      physics: embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final certificate = controller.certificates[index];
        return Card(
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    const AccentIconBadge(
                      icon: Icons.verified_user_rounded,
                      color: AppPalette.sky,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            certificate.name,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            certificate.subject,
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    IconButton(
                      onPressed: () async {
                        try {
                          await controller.removeTrustedCertificate(
                            certificate.fingerprint256,
                          );
                        } catch (error) {
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text(describeError(error))),
                          );
                        }
                      },
                      icon: const Icon(Icons.delete_outline_rounded),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Text('Fingerprint: ${certificate.fingerprint256}'),
                Text(
                  'Valid: ${certificate.validFrom} -> ${certificate.validTo}',
                ),
              ],
            ),
          ),
        );
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Trust Store',
          title: '可信注册证书',
          subtitle: '这里管理用于机台注册审批链路的可信证书。',
          actions: <Widget>[
            OutlinedButton.icon(
              onPressed: controller.otpVerified
                  ? controller.loadCertificates
                  : null,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新证书'),
            ),
            FilledButton.icon(
              onPressed: () async {
                if (!controller.otpVerified) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('添加可信证书前请先完成 OTP 验证。')),
                  );
                  return;
                }

                final values = await showTwoFieldDialog(
                  context,
                  title: '添加可信证书',
                  firstLabel: '名称',
                  secondLabel: 'PEM 证书',
                  secondMinLines: 10,
                );
                if (values == null) {
                  return;
                }
                try {
                  await controller.addTrustedCertificate(
                    values.first.trim(),
                    values.last.trim(),
                  );
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(SnackBar(content: Text(describeError(error))));
                }
              },
              icon: const Icon(Icons.add_rounded),
              label: const Text('导入证书'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (!controller.otpVerified)
          const InfoPanel(
            title: '需要 OTP',
            body: Text('证书管理属于高敏感操作。请先在右上角完成 OTP 验证，再刷新证书列表。'),
            icon: Icons.lock_clock_rounded,
            color: AppPalette.sun,
          )
        else if (controller.certificates.isEmpty)
          if (embedInParentScroll) emptyState else Expanded(child: emptyState)
        else if (embedInParentScroll)
          certificatesList
        else
          Expanded(child: certificatesList),
      ],
    );
  }
}

class AuditView extends StatefulWidget {
  const AuditView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<AuditView> createState() => _AuditViewState();
}

class _AuditViewState extends State<AuditView> {
  final TextEditingController _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _searchController.addListener(_handleSearchChanged);
  }

  @override
  void dispose() {
    _searchController.removeListener(_handleSearchChanged);
    _searchController.dispose();
    super.dispose();
  }

  void _handleSearchChanged() {
    setState(() {});
  }

  Future<void> _reloadAudit({String? machineId}) async {
    try {
      await widget.controller.loadAudit(machineId: machineId);
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final machineOptions = buildAuditMachineOptions(
      controller.machines,
      controller.auditEntries,
    ).toList();
    final selectedMachineId = controller.auditFilterMachineId;
    if (selectedMachineId != null &&
        !machineOptions.contains(selectedMachineId)) {
      machineOptions.insert(0, selectedMachineId);
    }
    final searchQuery = _searchController.text.trim().toLowerCase();
    final visibleEntries = searchQuery.isEmpty
        ? controller.auditEntries
        : controller.auditEntries
              .where((entry) => entry.searchableText.contains(searchQuery))
              .toList();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Audit Trail',
          title: '审计日志',
          subtitle: '按机台过滤、全文搜索，并快速回溯初始化与敏感操作记录。',
          actions: <Widget>[
            OutlinedButton.icon(
              onPressed: () =>
                  _reloadAudit(machineId: controller.auditFilterMachineId),
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新审计'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: <Widget>[
            SizedBox(
              width: 320,
              child: DropdownMenu<String>(
                key: ValueKey<String>(
                  controller.auditFilterMachineId ?? '__all__',
                ),
                width: 320,
                enableFilter: true,
                enableSearch: true,
                label: const Text('按机台过滤'),
                hintText: '全部机台',
                initialSelection: controller.auditFilterMachineId,
                dropdownMenuEntries: machineOptions
                    .map(
                      (machineId) => DropdownMenuEntry<String>(
                        value: machineId,
                        label: machineId,
                      ),
                    )
                    .toList(),
                onSelected: (value) => _reloadAudit(machineId: value),
              ),
            ),
            SizedBox(
              width: 320,
              child: TextField(
                controller: _searchController,
                decoration: InputDecoration(
                  labelText: '搜索审计内容',
                  hintText: '输入机台 ID、事件键、原因等',
                  prefixIcon: const Icon(Icons.search_rounded),
                  suffixIcon: searchQuery.isEmpty
                      ? null
                      : IconButton(
                          onPressed: _searchController.clear,
                          icon: const Icon(Icons.close_rounded),
                        ),
                ),
              ),
            ),
            if (controller.auditFilterMachineId != null)
              TextButton.icon(
                onPressed: () => _reloadAudit(machineId: null),
                icon: const Icon(Icons.filter_alt_off_rounded),
                label: const Text('清除机台过滤'),
              ),
          ],
        ),
        const SizedBox(height: 12),
        if (controller.auditFilterMachineId != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: Text(
              '当前仅显示机台 ${controller.auditFilterMachineId} 的审计记录。',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ),
        if (controller.auditEntries.isEmpty)
          if (widget.embedInParentScroll)
            Center(
              child: Text(
                controller.auditFilterMachineId == null
                    ? '暂时没有审计记录。'
                    : '所选机台暂时没有审计记录。',
              ),
            )
          else
            Expanded(
              child: Center(
                child: Text(
                  controller.auditFilterMachineId == null
                      ? '暂时没有审计记录。'
                      : '所选机台暂时没有审计记录。',
                ),
              ),
            )
        else if (visibleEntries.isEmpty)
          if (widget.embedInParentScroll)
            const Center(child: Text('没有匹配搜索条件的审计记录。'))
          else
            const Expanded(child: Center(child: Text('没有匹配搜索条件的审计记录。')))
        else if (widget.embedInParentScroll)
          ListView.separated(
            itemCount: visibleEntries.length,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            padding: EdgeInsets.zero,
            separatorBuilder: (_, _) => const SizedBox(height: 12),
            itemBuilder: (context, index) {
              final entry = visibleEntries[index];
              final presentation = entry.presentation;
              return Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          AccentIconBadge(
                            icon: entry.result == 'success'
                                ? Icons.task_alt_rounded
                                : Icons.error_outline_rounded,
                            color: entry.result == 'success'
                                ? AppPalette.mint
                                : AppPalette.coral,
                          ),
                          const SizedBox(width: 14),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Text(
                                  presentation.title,
                                  style: Theme.of(
                                    context,
                                  ).textTheme.titleMedium,
                                ),
                                const SizedBox(height: 6),
                                Text(
                                  presentation.description,
                                  style: Theme.of(context).textTheme.bodyMedium,
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 10),
                      Text(
                        '时间：${entry.localizedTimestamp}',
                        style: Theme.of(context).textTheme.bodyMedium,
                      ),
                      const SizedBox(height: 2),
                      Text(
                        '操作主体：${entry.localizedActor} · 结果：${entry.localizedResult} · 来源：${entry.normalizedIp}',
                        style: Theme.of(context).textTheme.bodyMedium,
                      ),
                      if (entry.machineId != null) ...<Widget>[
                        const SizedBox(height: 2),
                        Text(
                          '机台：${entry.machineId}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                      ],
                      const SizedBox(height: 2),
                      Text(
                        '接口：${entry.displayPath}',
                        style: Theme.of(context).textTheme.bodyMedium,
                      ),
                      const SizedBox(height: 2),
                      Text(
                        '事件键：${entry.type.isEmpty ? 'unknown' : entry.type}',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              );
            },
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: visibleEntries.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final entry = visibleEntries[index];
                final presentation = entry.presentation;
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            AccentIconBadge(
                              icon: entry.result == 'success'
                                  ? Icons.task_alt_rounded
                                  : Icons.error_outline_rounded,
                              color: entry.result == 'success'
                                  ? AppPalette.mint
                                  : AppPalette.coral,
                            ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  Text(
                                    presentation.title,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.titleMedium,
                                  ),
                                  const SizedBox(height: 6),
                                  Text(
                                    presentation.description,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.bodyMedium,
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Text(
                          '时间：${entry.localizedTimestamp}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '操作主体：${entry.localizedActor} · 结果：${entry.localizedResult} · 来源：${entry.normalizedIp}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                        if (entry.machineId != null) ...<Widget>[
                          const SizedBox(height: 2),
                          Text(
                            '机台：${entry.machineId}',
                            style: Theme.of(context).textTheme.bodyMedium,
                          ),
                        ],
                        const SizedBox(height: 2),
                        Text(
                          '接口：${entry.displayPath}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '事件键：${entry.type.isEmpty ? 'unknown' : entry.type}',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class SettingsView extends StatefulWidget {
  const SettingsView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<SettingsView> createState() => _SettingsViewState();
}

class _SettingsViewState extends State<SettingsView> {
  late final TextEditingController _defaultVhdController;
  late final TextEditingController _logRetentionDaysController;
  late final TextEditingController _logInspectionHourController;
  late final TextEditingController _logInspectionMinuteController;
  late final TextEditingController _logTimezoneController;
  final TextEditingController _currentPasswordController =
      TextEditingController();
  final TextEditingController _newPasswordController = TextEditingController();
  final TextEditingController _confirmPasswordController =
      TextEditingController();
  final TextEditingController _otpRotateCurrentCodeController =
      TextEditingController();
  final TextEditingController _otpRotateIssuerController =
      TextEditingController();
  final TextEditingController _otpRotateAccountController =
      TextEditingController();
  final TextEditingController _otpRotateNewCodeController =
      TextEditingController();

  bool _isLikelyIanaTimeZone(String value) {
    final trimmed = value.trim();
    if (trimmed.isEmpty) {
      return false;
    }
    if (trimmed == 'UTC' || trimmed == 'GMT') {
      return true;
    }

    return RegExp(r'^[A-Za-z_]+(?:/[A-Za-z0-9_.+-]+)+$').hasMatch(trimmed);
  }

  @override
  void initState() {
    super.initState();
    _defaultVhdController = TextEditingController(
      text: widget.controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
    );
    _logRetentionDaysController = TextEditingController(
      text:
          (widget.controller.logRetentionSettings?.defaultRetentionActiveDays ??
                  7)
              .toString(),
    );
    _logInspectionHourController = TextEditingController(
      text: (widget.controller.logRetentionSettings?.dailyInspectionHour ?? 3)
          .toString(),
    );
    _logInspectionMinuteController = TextEditingController(
      text: (widget.controller.logRetentionSettings?.dailyInspectionMinute ?? 0)
          .toString(),
    );
    _logTimezoneController = TextEditingController(
      text: widget.controller.logRetentionSettings?.timezone ?? 'UTC',
    );
  }

  @override
  void didUpdateWidget(covariant SettingsView oldWidget) {
    super.didUpdateWidget(oldWidget);
    final logRetention = widget.controller.logRetentionSettings;
    if (logRetention != null) {
      final nextDays = logRetention.defaultRetentionActiveDays.toString();
      final nextHour = logRetention.dailyInspectionHour.toString();
      final nextMinute = logRetention.dailyInspectionMinute.toString();
      if (_logRetentionDaysController.text != nextDays) {
        _logRetentionDaysController.text = nextDays;
      }
      if (_logInspectionHourController.text != nextHour) {
        _logInspectionHourController.text = nextHour;
      }
      if (_logInspectionMinuteController.text != nextMinute) {
        _logInspectionMinuteController.text = nextMinute;
      }
      if (_logTimezoneController.text != logRetention.timezone) {
        _logTimezoneController.text = logRetention.timezone;
      }
    }
  }

  @override
  void dispose() {
    _defaultVhdController.dispose();
    _logRetentionDaysController.dispose();
    _logInspectionHourController.dispose();
    _logInspectionMinuteController.dispose();
    _logTimezoneController.dispose();
    _currentPasswordController.dispose();
    _newPasswordController.dispose();
    _confirmPasswordController.dispose();
    _otpRotateCurrentCodeController.dispose();
    _otpRotateIssuerController.dispose();
    _otpRotateAccountController.dispose();
    _otpRotateNewCodeController.dispose();
    super.dispose();
  }

  Widget _buildOtpRotationImportPanel(InitializationPreparation preparation) {
    final otpauthUrl = normalizeOtpauthUrl(preparation.otpauthUrl);
    final isMobile = Platform.isIOS || Platform.isAndroid;

    return InfoPanel(
      title: '新的 OTP 绑定信息',
      icon: Icons.qr_code_scanner_rounded,
      color: AppPalette.coral,
      body: LayoutBuilder(
        builder: (context, constraints) {
          final available = constraints.maxWidth.isFinite
              ? constraints.maxWidth
              : MediaQuery.of(context).size.width;
          final infoMaxWidth = available < 520.0 ? available : 520.0;
          final infoMinWidth = available < 280.0 ? 0.0 : 280.0;
          return Wrap(
            spacing: 20,
            runSpacing: 20,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: <Widget>[
              Container(
                width: available < 232.0 ? available : 232.0,
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: <Color>[
                      Colors.white.withValues(alpha: 0.94),
                      AppPalette.coral.withValues(alpha: 0.08),
                    ],
                  ),
                  borderRadius: BorderRadius.circular(22),
                  border: Border.all(
                    color: AppPalette.coral.withValues(alpha: 0.18),
                  ),
                ),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    const Text(
                      '扫描二维码导入',
                      style: TextStyle(
                        fontFamily: _miSansFamily700,
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (otpauthUrl.isNotEmpty)
                      QrImageView(
                        data: otpauthUrl,
                        version: QrVersions.auto,
                        size: 180,
                        backgroundColor: Colors.white,
                        eyeStyle: const QrEyeStyle(
                          eyeShape: QrEyeShape.square,
                          color: AppPalette.ink,
                        ),
                        dataModuleStyle: const QrDataModuleStyle(
                          dataModuleShape: QrDataModuleShape.square,
                          color: AppPalette.coralDeep,
                        ),
                      )
                    else
                      Container(
                        width: 180,
                        height: 180,
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          color: Colors.white,
                          borderRadius: BorderRadius.circular(18),
                          border: Border.all(color: AppPalette.border),
                        ),
                        child: const Text('未返回 otpauth URI'),
                      ),
                    const SizedBox(height: 12),
                    if (isMobile)
                      FilledButton.tonalIcon(
                        onPressed: () async {
                          final success = await launchOtpauthUrl(
                            secret: preparation.totpSecret,
                            account: preparation.accountName,
                            issuer: preparation.issuer,
                          );
                          if (!context.mounted) return;
                          if (!success) {
                            ScaffoldMessenger.of(context).showSnackBar(
                              const SnackBar(
                                content: Text('无法打开系统验证器，请使用扫码或手动绑定。'),
                              ),
                            );
                          }
                        },
                        icon: const Icon(Icons.open_in_new_rounded),
                        label: Text(
                          Platform.isIOS ? '绑定到 iCloud 密码' : '绑定到验证器',
                        ),
                      )
                    else
                      Text(
                        '旧绑定会一直保留，直到你使用新的绑定密钥生成验证码并验证通过。',
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: AppPalette.muted,
                          height: 1.45,
                        ),
                      ),
                  ],
                ),
              ),
              ConstrainedBox(
                constraints: BoxConstraints(
                  minWidth: infoMinWidth,
                  maxWidth: infoMaxWidth,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      isMobile
                          ? '点击左侧按钮可直接唤起系统验证器自动填充密钥；如果验证器不支持自动导入，再使用扫码或下方密钥手动添加。'
                          : '如果验证器不支持扫码，可以使用下面的参数手动绑定。',
                      style: Theme.of(
                        context,
                      ).textTheme.bodyMedium?.copyWith(height: 1.5),
                    ),
                    const SizedBox(height: 12),
                    SelectableText('Issuer: ${preparation.issuer}'),
                    const SizedBox(height: 6),
                    SelectableText('Account: ${preparation.accountName}'),
                    const SizedBox(height: 6),
                    SelectableText('Secret: ${preparation.totpSecret}'),
                    const SizedBox(height: 6),
                    SelectableText('URI: $otpauthUrl'),
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final status = widget.controller.serverStatus;
    final logRetention = widget.controller.logRetentionSettings;
    final rotationPreparation = widget.controller.otpRotationPreparation;
    final children = <Widget>[
      const PageHeader(
        eyebrow: 'Security Settings',
        title: '服务设置',
        subtitle: '调整默认启动关键词、OTP 绑定与管理员密码。',
      ),
      const SizedBox(height: 18),
      SectionPanel(
        title: '日志保留策略',
        subtitle: '按活动日志日配置全局默认值与每日巡检时间。',
        icon: Icons.schedule_send_rounded,
        color: AppPalette.sun,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            const InfoPanel(
              title: '活动日志日说明',
              body: Text(
                '这里的保留单位不是自然日，而是“有日志写入的活动日”。机台长时间离线时，不会因为自然时间流逝而提前清理旧日志。',
              ),
              icon: Icons.calendar_month_rounded,
              color: AppPalette.sun,
            ),
            const SizedBox(height: 12),
            LayoutBuilder(
              builder: (context, constraints) {
                final narrow = constraints.maxWidth < 460;
                final retentionField = TextField(
                  controller: _logRetentionDaysController,
                  decoration: const InputDecoration(labelText: '默认保留活动日数'),
                );
                final hourField = TextField(
                  controller: _logInspectionHourController,
                  decoration: const InputDecoration(labelText: '每日巡检小时'),
                );
                final minuteField = TextField(
                  controller: _logInspectionMinuteController,
                  decoration: const InputDecoration(labelText: '每日巡检分钟'),
                );
                if (narrow) {
                  return Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: <Widget>[
                      retentionField,
                      const SizedBox(height: 12),
                      hourField,
                      const SizedBox(height: 12),
                      minuteField,
                    ],
                  );
                }
                return Row(
                  children: <Widget>[
                    Expanded(child: retentionField),
                    const SizedBox(width: 12),
                    Expanded(child: hourField),
                    const SizedBox(width: 12),
                    Expanded(child: minuteField),
                  ],
                );
              },
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _logTimezoneController,
              decoration: const InputDecoration(
                labelText: '服务端时区',
                hintText: 'UTC 或 Asia/Shanghai',
                helperText:
                    '仅支持 IANA 时区，不支持 China Standard Time 这类 Windows 时区名。',
              ),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: () async {
                final retentionDays = int.tryParse(
                  _logRetentionDaysController.text.trim(),
                );
                final inspectionHour = int.tryParse(
                  _logInspectionHourController.text.trim(),
                );
                final inspectionMinute = int.tryParse(
                  _logInspectionMinuteController.text.trim(),
                );
                final timezone = _logTimezoneController.text.trim();

                if (retentionDays == null || retentionDays <= 0) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('默认保留活动日数必须是正整数。')),
                  );
                  return;
                }
                if (inspectionHour == null ||
                    inspectionHour < 0 ||
                    inspectionHour > 23) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('每日巡检小时必须在 0-23 之间。')),
                  );
                  return;
                }
                if (inspectionMinute == null ||
                    inspectionMinute < 0 ||
                    inspectionMinute > 59) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('每日巡检分钟必须在 0-59 之间。')),
                  );
                  return;
                }
                if (timezone.isEmpty) {
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(const SnackBar(content: Text('服务端时区不能为空。')));
                  return;
                }
                if (!_isLikelyIanaTimeZone(timezone)) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(
                      content: Text('服务端时区必须是 IANA 时区，例如 UTC 或 Asia/Shanghai。'),
                    ),
                  );
                  return;
                }

                try {
                  await widget.controller.updateLogRetentionSettings(
                    defaultRetentionActiveDays: retentionDays,
                    dailyInspectionHour: inspectionHour,
                    dailyInspectionMinute: inspectionMinute,
                    timezone: timezone,
                  );
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(const SnackBar(content: Text('日志保留策略已更新。')));
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(SnackBar(content: Text(describeError(error))));
                }
              },
              icon: const Icon(Icons.save_rounded),
              label: const Text('保存日志保留策略'),
            ),
            if (logRetention != null) ...<Widget>[
              const SizedBox(height: 12),
              Text('当前默认保留：${logRetention.defaultRetentionActiveDays} 个活动日志日'),
              Text(
                '当前巡检时间：${logRetention.inspectionScheduleLabel} · 时区 ${logRetention.timezone}',
              ),
              Text('最近一次巡检：${logRetention.localizedLastInspectionAt}'),
            ],
          ],
        ),
      ),
      const SizedBox(height: 16),
      SectionPanel(
        title: '服务设置',
        subtitle: '更新默认启动关键词。',
        icon: Icons.tune_rounded,
        color: AppPalette.sky,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            TextField(
              controller: _defaultVhdController,
              decoration: const InputDecoration(labelText: '默认启动关键词'),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: () async {
                try {
                  await widget.controller.updateDefaultVhd(
                    _defaultVhdController.text.trim().toUpperCase(),
                  );
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(SnackBar(content: Text(describeError(error))));
                }
              },
              icon: const Icon(Icons.save_rounded),
              label: const Text('保存默认启动关键词'),
            ),
            if (status != null) ...<Widget>[
              const SizedBox(height: 12),
              Text('数据库状态: ${status.databaseReady ? '正常' : '异常'}'),
              Text('可信注册证书数量: ${status.trustedRegistrationCertificateCount}'),
            ],
          ],
        ),
      ),
      const SizedBox(height: 16),
      SectionPanel(
        title: '更换 OTP 绑定密钥',
        subtitle: '先验证旧 OTP，再导入并验证新的 OTP 绑定密钥。',
        icon: Icons.qr_code_2_rounded,
        color: AppPalette.coral,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            if (rotationPreparation == null)
              const InfoPanel(
                title: '流程说明',
                body: Text(
                  '提交旧 OTP 验证码后，系统会生成新的绑定密钥。只有在你使用新密钥生成的 OTP 验证成功后，旧绑定才会被替换。',
                ),
                icon: Icons.swap_horiz_rounded,
                color: AppPalette.sun,
              )
            else
              _buildOtpRotationImportPanel(rotationPreparation),
            const SizedBox(height: 12),
            TextField(
              controller: _otpRotateCurrentCodeController,
              decoration: const InputDecoration(labelText: '旧 OTP 验证码'),
            ),
            const SizedBox(height: 12),
            LayoutBuilder(
              builder: (context, constraints) {
                final narrow = constraints.maxWidth < 460;
                final issuerField = TextField(
                  controller: _otpRotateIssuerController,
                  decoration: const InputDecoration(
                    labelText: '新的 OTP Issuer（可选）',
                  ),
                );
                final accountField = TextField(
                  controller: _otpRotateAccountController,
                  decoration: const InputDecoration(
                    labelText: '新的 OTP Account（可选）',
                  ),
                );
                if (narrow) {
                  return Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: <Widget>[
                      issuerField,
                      const SizedBox(height: 12),
                      accountField,
                    ],
                  );
                }
                return Row(
                  children: <Widget>[
                    Expanded(child: issuerField),
                    const SizedBox(width: 12),
                    Expanded(child: accountField),
                  ],
                );
              },
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: <Widget>[
                FilledButton.icon(
                  onPressed: () async {
                    final currentCode = _otpRotateCurrentCodeController.text
                        .trim();
                    if (currentCode.isEmpty) {
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('请输入旧 OTP 验证码。')),
                      );
                      return;
                    }
                    try {
                      await widget.controller.prepareOtpRotation(
                        currentCode: currentCode,
                        issuer: _otpRotateIssuerController.text.trim(),
                        accountName: _otpRotateAccountController.text.trim(),
                      );
                      _otpRotateNewCodeController.clear();
                      if (!context.mounted) {
                        return;
                      }
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(
                          content: Text('新的 OTP 绑定密钥已生成，请导入后验证新验证码。'),
                        ),
                      );
                    } catch (error) {
                      if (!context.mounted) {
                        return;
                      }
                      ScaffoldMessenger.of(context).showSnackBar(
                        SnackBar(content: Text(describeError(error))),
                      );
                    }
                  },
                  icon: const Icon(Icons.qr_code_2_rounded),
                  label: Text(
                    rotationPreparation == null ? '生成新的绑定密钥' : '重新生成绑定密钥',
                  ),
                ),
                if (rotationPreparation != null)
                  TextButton.icon(
                    onPressed: () {
                      widget.controller.clearOtpRotationPreparation();
                      _otpRotateNewCodeController.clear();
                    },
                    icon: const Icon(Icons.close_rounded),
                    label: const Text('取消本次更换'),
                  ),
              ],
            ),
            if (rotationPreparation != null) ...<Widget>[
              const SizedBox(height: 16),
              TextField(
                controller: _otpRotateNewCodeController,
                decoration: const InputDecoration(labelText: '新 OTP 验证码'),
              ),
              const SizedBox(height: 12),
              FilledButton.tonalIcon(
                onPressed: () async {
                  final newCode = _otpRotateNewCodeController.text.trim();
                  if (newCode.isEmpty) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('请输入新的 OTP 验证码。')),
                    );
                    return;
                  }
                  try {
                    await widget.controller.completeOtpRotation(newCode);
                    _otpRotateCurrentCodeController.clear();
                    _otpRotateNewCodeController.clear();
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('OTP 绑定密钥已更换。')),
                    );
                  } catch (error) {
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(content: Text(describeError(error))),
                    );
                  }
                },
                icon: const Icon(Icons.verified_rounded),
                label: const Text('验证新绑定并替换旧绑定'),
              ),
            ],
          ],
        ),
      ),
      const SizedBox(height: 16),
      SectionPanel(
        title: '修改管理员密码',
        subtitle: '服务端要求新密码长度至少 12 位。',
        icon: Icons.password_rounded,
        color: AppPalette.mint,
        child: Column(
          children: <Widget>[
            buildSecureTextField(
              controller: _currentPasswordController,
              autofillHints: const <String>[AutofillHints.password],
              decoration: const InputDecoration(labelText: '当前密码'),
            ),
            const SizedBox(height: 12),
            buildSecureTextField(
              controller: _newPasswordController,
              autofillHints: const <String>[AutofillHints.newPassword],
              decoration: const InputDecoration(labelText: '新密码'),
            ),
            const SizedBox(height: 12),
            buildSecureTextField(
              controller: _confirmPasswordController,
              autofillHints: const <String>[AutofillHints.newPassword],
              decoration: const InputDecoration(labelText: '确认新密码'),
            ),
            const SizedBox(height: 12),
            FilledButton.tonalIcon(
              onPressed: () async {
                if (_newPasswordController.text !=
                    _confirmPasswordController.text) {
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(const SnackBar(content: Text('两次输入的新密码不一致。')));
                  return;
                }
                try {
                  await widget.controller.changePassword(
                    _currentPasswordController.text,
                    _newPasswordController.text,
                  );
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(const SnackBar(content: Text('管理员密码已更新。')));
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(SnackBar(content: Text(describeError(error))));
                }
              },
              icon: const Icon(Icons.password_rounded),
              label: const Text('更新密码'),
            ),
          ],
        ),
      ),
    ];

    if (widget.embedInParentScroll) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: children,
      );
    }

    return ListView(children: children);
  }
}

class SectionPanel extends StatelessWidget {
  const SectionPanel({
    super.key,
    required this.title,
    required this.subtitle,
    required this.child,
    this.icon = Icons.auto_awesome_rounded,
    this.color = AppPalette.coral,
  });

  final String title;
  final String subtitle;
  final Widget child;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return AppPanel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              AccentIconBadge(icon: icon, color: color),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      title,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 6),
                    Text(
                      subtitle,
                      style: Theme.of(
                        context,
                      ).textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 18),
          child,
        ],
      ),
    );
  }
}

class InfoPanel extends StatelessWidget {
  const InfoPanel({
    super.key,
    required this.title,
    required this.body,
    this.icon = Icons.tips_and_updates_rounded,
    this.color = AppPalette.sky,
  });

  final String title;
  final Widget body;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            color.withValues(alpha: 0.16),
            Colors.white.withValues(alpha: 0.84),
          ],
        ),
        borderRadius: BorderRadius.circular(22),
        border: Border.all(color: color.withValues(alpha: 0.2)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              AccentIconBadge(icon: icon, color: color, size: 42),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(title, style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 6),
                    body,
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class ErrorBanner extends StatelessWidget {
  const ErrorBanner({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            AppPalette.danger.withValues(alpha: 0.16),
            Colors.white.withValues(alpha: 0.84),
          ],
        ),
        borderRadius: BorderRadius.circular(22),
        border: Border.all(color: AppPalette.danger.withValues(alpha: 0.22)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          const AccentIconBadge(
            icon: Icons.error_outline_rounded,
            color: AppPalette.danger,
            size: 42,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              message,
              style: Theme.of(
                context,
              ).textTheme.bodyMedium?.copyWith(color: AppPalette.ink),
            ),
          ),
        ],
      ),
    );
  }
}

class StatusChip extends StatelessWidget {
  const StatusChip({super.key, required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final screenWidth = MediaQuery.of(context).size.width;
    final compact = screenWidth < 360;
    return Container(
      padding: EdgeInsets.symmetric(horizontal: compact ? 8 : 12, vertical: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        border: Border.all(color: color.withValues(alpha: 0.22)),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 8),
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: compact ? 112 : 180),
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontFamily: _miSansFamily700,
                color: color,
                fontWeight: FontWeight.w700,
                fontSize: compact ? 11 : 13,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
