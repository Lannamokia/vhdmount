part of '../app.dart';

/// 可信 RustDesk 主控端管理子页面（任务 17.2 / Requirement 13.1 / 15.2 / 15.3）。
///
/// 列表按 scope 分组：global 优先，`machine:<id>` 按 machineId 排序；支持创建 / 编辑 / 删除。
/// 写操作走 [AppController._runAction]，OTP step-up 由全局拦截器自动处理。
class TrustedRustDeskControllersView extends StatefulWidget {
  const TrustedRustDeskControllersView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<TrustedRustDeskControllersView> createState() =>
      _TrustedRustDeskControllersViewState();
}

class _TrustedRustDeskControllersViewState
    extends State<TrustedRustDeskControllersView> {
  bool _initialLoaded = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!_initialLoaded) {
        _initialLoaded = true;
        try {
          await widget.controller.loadTrustedRustDeskControllers();
          // 同时把机台列表加载好以填充 scope 下拉
          if (widget.controller.machines.isEmpty) {
            await widget.controller.loadMachines();
          }
        } catch (_) {
          // 错误已写入 errorMessage
        }
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final entries = _groupAndSort(controller.trustedRustDeskControllers);

    final emptyState = Center(
      child: SizedBox(
        width: 420,
        child: const InfoPanel(
          title: '尚未配置可信 RustDesk 主控端',
          body: Text('点击右上角"新增"录入第一条可信主控端记录。控制者通过 RustDesk 拨打机台时\n会按此列表查表确认是否放行。'),
          icon: Icons.shield_moon_rounded,
          color: AppPalette.sky,
        ),
      ),
    );

    final list = ListView.separated(
      itemCount: entries.length,
      shrinkWrap: widget.embedInParentScroll,
      physics: widget.embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final ctl = entries[index];
        return _ControllerCard(
          controller: controller,
          record: ctl,
          onEdit: () => _showUpsertDialog(context, controller, ctl),
          onDelete: () async {
            final confirmed = await showConfirmDialog(
              context,
              title: '删除可信主控端',
              message:
                  '确认删除主控端 ${ctl.controllerId} (scope=${ctl.scope}) 吗？\n'
                  '删除后该主控端的 RustDesk 拨打请求将被拒绝，且会触发反向通知。',
              confirmLabel: '删除',
            );
            if (confirmed != true) return;
            try {
              await controller.deleteTrustedRustDeskController(ctl.id);
              if (!context.mounted) return;
              ScaffoldMessenger.of(context).showSnackBar(
                SnackBar(content: Text('主控端 ${ctl.controllerId} 已删除')),
              );
            } catch (error) {
              if (!context.mounted) return;
              ScaffoldMessenger.of(context).showSnackBar(
                SnackBar(content: Text(describeError(error))),
              );
            }
          },
        );
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'RustDesk 远程控制',
          title: '可信主控端',
          subtitle: '维护可以拨打机台的 RustDesk 控制者列表。global 作用全机台，machine:<id> 仅作用单机。',
          actions: <Widget>[
            FilledButton.icon(
              onPressed: () => _showUpsertDialog(context, controller, null),
              icon: const Icon(Icons.add_rounded),
              label: const Text('新增'),
            ),
            OutlinedButton.icon(
              onPressed: () async {
                try {
                  await controller.loadTrustedRustDeskControllers();
                } catch (_) {}
              },
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (controller.errorMessage != null) ...<Widget>[
          ErrorBanner(message: controller.errorMessage!),
          const SizedBox(height: 12),
        ],
        if (entries.isEmpty)
          widget.embedInParentScroll ? emptyState : Expanded(child: emptyState)
        else if (widget.embedInParentScroll)
          list
        else
          Expanded(child: list),
      ],
    );
  }

  /// global 优先；其余按 scope（machineId）字典序，再按 controllerId。
  List<TrustedRustDeskController> _groupAndSort(
    List<TrustedRustDeskController> input,
  ) {
    final copy = List<TrustedRustDeskController>.from(input);
    copy.sort((a, b) {
      if (a.isGlobalScope && !b.isGlobalScope) return -1;
      if (!a.isGlobalScope && b.isGlobalScope) return 1;
      final scopeCmp = a.scope.compareTo(b.scope);
      if (scopeCmp != 0) return scopeCmp;
      return a.controllerId.compareTo(b.controllerId);
    });
    return copy;
  }

  Future<void> _showUpsertDialog(
    BuildContext context,
    AppController controller,
    TrustedRustDeskController? existing,
  ) async {
    final draft = await showDialog<TrustedRustDeskControllerDraft>(
      context: context,
      builder: (context) => _UpsertTrustedControllerDialog(
        controller: controller,
        existing: existing,
      ),
    );
    if (draft == null) return;
    try {
      await controller.upsertTrustedRustDeskController(draft);
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(
          existing == null ? '主控端已新增' : '主控端已更新',
        )),
      );
    } catch (error) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
    }
  }
}

class _ControllerCard extends StatelessWidget {
  const _ControllerCard({
    required this.controller,
    required this.record,
    required this.onEdit,
    required this.onDelete,
  });

  final AppController controller;
  final TrustedRustDeskController record;
  final VoidCallback onEdit;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
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
                  icon: record.enabled
                      ? Icons.shield_moon_rounded
                      : Icons.block_rounded,
                  color: record.enabled ? AppPalette.mint : AppPalette.muted,
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        record.label?.isNotEmpty == true
                            ? record.label!
                            : record.controllerId,
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        '控制者 ID：${record.controllerId}',
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                              color: AppPalette.muted,
                            ),
                      ),
                    ],
                  ),
                ),
                StatusChip(
                  label: record.enabled ? '已启用' : '已停用',
                  color: record.enabled ? AppPalette.mint : AppPalette.muted,
                ),
              ],
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: <Widget>[
                StatusChip(label: '作用域 ${record.scopeLabel}', color: AppPalette.sky),
                if (record.controllerHwidHash != null)
                  StatusChip(
                    label: 'HWID 哈希前 8 位 ${record.controllerHwidHash!.substring(0, 8)}',
                    color: AppPalette.coral,
                  )
                else
                  const StatusChip(label: '不限定 HWID', color: AppPalette.muted),
                StatusChip(
                  label: '到期 ${record.localizedExpiresAt}',
                  color: record.expiresAt == null
                      ? AppPalette.muted
                      : AppPalette.sun,
                ),
              ],
            ),
            const SizedBox(height: 10),
            Text('创建于 ${record.localizedCreatedAt}'),
            if (record.auditNote != null && record.auditNote!.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('审计备注: ${record.auditNote}'),
              ),
            const SizedBox(height: 16),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: <Widget>[
                OutlinedButton.icon(
                  onPressed: onEdit,
                  icon: const Icon(Icons.edit_rounded),
                  label: const Text('编辑'),
                ),
                OutlinedButton.icon(
                  onPressed: onDelete,
                  icon: const Icon(Icons.delete_outline_rounded),
                  label: const Text('删除'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _UpsertTrustedControllerDialog extends StatefulWidget {
  const _UpsertTrustedControllerDialog({
    required this.controller,
    required this.existing,
  });

  final AppController controller;
  final TrustedRustDeskController? existing;

  @override
  State<_UpsertTrustedControllerDialog> createState() =>
      _UpsertTrustedControllerDialogState();
}

class _UpsertTrustedControllerDialogState
    extends State<_UpsertTrustedControllerDialog> {
  late final TextEditingController _controllerIdCtl;
  late final TextEditingController _hwidHashCtl;
  late final TextEditingController _labelCtl;
  late final TextEditingController _auditNoteCtl;
  late final TextEditingController _expiresAtCtl;
  String _scopeKind = 'global'; // 'global' | 'machine'
  String? _scopedMachineId;
  late bool _enabled;

  @override
  void initState() {
    super.initState();
    final e = widget.existing;
    _controllerIdCtl = TextEditingController(text: e?.controllerId ?? '');
    _hwidHashCtl = TextEditingController(text: e?.controllerHwidHash ?? '');
    _labelCtl = TextEditingController(text: e?.label ?? '');
    _auditNoteCtl = TextEditingController(text: e?.auditNote ?? '');
    _expiresAtCtl = TextEditingController(text: e?.expiresAt ?? '');
    if (e != null) {
      if (e.isGlobalScope) {
        _scopeKind = 'global';
      } else {
        _scopeKind = 'machine';
        _scopedMachineId = e.scopedMachineId;
      }
    }
    _enabled = e?.enabled ?? true;
  }

  @override
  void dispose() {
    _controllerIdCtl.dispose();
    _hwidHashCtl.dispose();
    _labelCtl.dispose();
    _auditNoteCtl.dispose();
    _expiresAtCtl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final machines = widget.controller.machines;
    return AlertDialog(
      title: Text(widget.existing == null ? '新增可信主控端' : '编辑可信主控端'),
      content: SizedBox(
        width: 520,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              TextField(
                controller: _controllerIdCtl,
                decoration: const InputDecoration(
                  labelText: '控制者 ID（必填）',
                  helperText: 'RustDesk 客户端的稳定标识（数字或主机名样式）',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _labelCtl,
                decoration: const InputDecoration(
                  labelText: '显示名（可选）',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _hwidHashCtl,
                decoration: const InputDecoration(
                  labelText: 'HWID SHA-256（可选，64 位 hex）',
                  helperText: '留空表示不限定 HWID；填入后必须严格匹配',
                ),
                inputFormatters: <TextInputFormatter>[
                  FilteringTextInputFormatter.allow(RegExp(r'[0-9a-fA-F]')),
                  LengthLimitingTextInputFormatter(64),
                ],
              ),
              const SizedBox(height: 12),
              Text('作用域', style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 6),
              RadioGroup<String>(
                groupValue: _scopeKind,
                onChanged: (v) => setState(() => _scopeKind = v ?? 'global'),
                child: Row(
                  children: const <Widget>[
                    Expanded(
                      child: RadioListTile<String>(
                        title: Text('全局（global）'),
                        value: 'global',
                      ),
                    ),
                    Expanded(
                      child: RadioListTile<String>(
                        title: Text('指定机台'),
                        value: 'machine',
                      ),
                    ),
                  ],
                ),
              ),
              if (_scopeKind == 'machine')
                DropdownButtonFormField<String>(
                  initialValue: _scopedMachineId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: '选择机台',
                  ),
                  items: machines.isEmpty
                      ? const <DropdownMenuItem<String>>[]
                      : machines
                          .map((m) => DropdownMenuItem<String>(
                                value: m.machineId,
                                child: Text(m.machineId, overflow: TextOverflow.ellipsis),
                              ))
                          .toList(),
                  onChanged: (v) => setState(() => _scopedMachineId = v),
                ),
              const SizedBox(height: 12),
              SwitchListTile(
                value: _enabled,
                title: const Text('启用'),
                subtitle: const Text('停用后该主控端的 RustDesk 拨打请求将被拒绝'),
                onChanged: (v) => setState(() => _enabled = v),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _expiresAtCtl,
                decoration: const InputDecoration(
                  labelText: '到期时间（可选，ISO 8601）',
                  hintText: '例：2025-12-31T23:59:59Z',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _auditNoteCtl,
                maxLines: 3,
                decoration: const InputDecoration(
                  labelText: '审计备注（可选）',
                ),
              ),
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(null),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: _onSubmit,
          child: const Text('保存'),
        ),
      ],
    );
  }

  void _onSubmit() {
    final controllerId = _controllerIdCtl.text.trim();
    if (controllerId.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('控制者 ID 不能为空')),
      );
      return;
    }

    final hwidRaw = _hwidHashCtl.text.trim().toLowerCase();
    if (hwidRaw.isNotEmpty && !RegExp(r'^[0-9a-f]{64}$').hasMatch(hwidRaw)) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('HWID SHA-256 必须是 64 位小写十六进制')),
      );
      return;
    }

    String scope;
    if (_scopeKind == 'global') {
      scope = 'global';
    } else {
      if (_scopedMachineId == null || _scopedMachineId!.isEmpty) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('请选择目标机台')),
        );
        return;
      }
      scope = 'machine:$_scopedMachineId';
    }

    final draft = TrustedRustDeskControllerDraft(
      id: widget.existing?.id,
      controllerId: controllerId,
      controllerHwidHash: hwidRaw.isEmpty ? null : hwidRaw,
      label: _labelCtl.text.trim().isEmpty ? null : _labelCtl.text.trim(),
      scope: scope,
      enabled: _enabled,
      expiresAt: _expiresAtCtl.text.trim().isEmpty ? null : _expiresAtCtl.text.trim(),
      auditNote: _auditNoteCtl.text.trim().isEmpty ? null : _auditNoteCtl.text.trim(),
    );
    Navigator.of(context).pop(draft);
  }
}
