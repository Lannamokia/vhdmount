part of '../app.dart';

enum _DeploymentTab { packages, tasks, history }

class DeploymentsView extends StatefulWidget {
  const DeploymentsView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<DeploymentsView> createState() => _DeploymentsViewState();
}

class _DeploymentsViewState extends State<DeploymentsView> {
  _DeploymentTab _selectedTab = _DeploymentTab.packages;

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Software Deployment',
          title: '部署管理',
          subtitle: '上传部署包、向机台下发任务、查看部署历史与触发卸载。',
          actions: <Widget>[
            SegmentedButton<_DeploymentTab>(
              segments: const <ButtonSegment<_DeploymentTab>>[
                ButtonSegment<_DeploymentTab>(
                  value: _DeploymentTab.packages,
                  label: Text('部署包'),
                  icon: Icon(Icons.folder_zip_rounded),
                ),
                ButtonSegment<_DeploymentTab>(
                  value: _DeploymentTab.tasks,
                  label: Text('部署任务'),
                  icon: Icon(Icons.assignment_rounded),
                ),
                ButtonSegment<_DeploymentTab>(
                  value: _DeploymentTab.history,
                  label: Text('机台历史'),
                  icon: Icon(Icons.history_rounded),
                ),
              ],
              selected: <_DeploymentTab>{_selectedTab},
              onSelectionChanged: (Set<_DeploymentTab> value) {
                setState(() {
                  _selectedTab = value.first;
                });
                if (_selectedTab == _DeploymentTab.packages) {
                  controller.loadDeploymentPackages();
                } else if (_selectedTab == _DeploymentTab.tasks) {
                  controller.loadDeploymentTasks();
                }
              },
            ),
          ],
        ),
        const SizedBox(height: 16),
        switch (_selectedTab) {
          _DeploymentTab.packages => _PackagesTab(
            controller: controller,
            embedInParentScroll: widget.embedInParentScroll,
          ),
          _DeploymentTab.tasks => _TasksTab(
            controller: controller,
            embedInParentScroll: widget.embedInParentScroll,
          ),
          _DeploymentTab.history => _HistoryTab(
            controller: controller,
            embedInParentScroll: widget.embedInParentScroll,
          ),
        },
      ],
    );
  }
}

class _PackagesTab extends StatelessWidget {
  const _PackagesTab({
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
          title: '当前没有部署包',
          body: Text('点击上方按钮上传第一个部署包。部署包需为 ZIP 格式并附带签名文件。'),
          icon: Icons.folder_zip_rounded,
          color: AppPalette.sky,
        ),
      ),
    );

    final packagesList = ListView.separated(
      itemCount: controller.deploymentPackages.length,
      shrinkWrap: embedInParentScroll,
      physics: embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final pkg = controller.deploymentPackages[index];
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
                      icon: pkg.type == 'software-deploy'
                          ? Icons.install_desktop_rounded
                          : Icons.folder_copy_rounded,
                      color: AppPalette.coral,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            pkg.name,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '版本 ${pkg.version} · ${pkg.displayType}',
                            style: Theme.of(context)
                                .textTheme
                                .bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    StatusChip(
                      label: pkg.displayType,
                      color: pkg.type == 'software-deploy'
                          ? AppPalette.coral
                          : AppPalette.sky,
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: <Widget>[
                    StatusChip(
                      label: '大小 ${pkg.displaySize}',
                      color: AppPalette.mint,
                    ),
                    StatusChip(
                      label: '文件 ${pkg.fileName}',
                      color: AppPalette.sky,
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Text('签名者: ${pkg.signer}'),
                Text('ID: ${pkg.packageId}'),
                Text('创建时间: ${formatAuditTimestamp(pkg.createdAt)}'),
                const SizedBox(height: 16),
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: <Widget>[
                    OutlinedButton.icon(
                      onPressed: () async {
                        final confirmed = await showConfirmDialog(
                          context,
                          title: '删除部署包',
                          message: '确认删除部署包 ${pkg.name} v${pkg.version} 吗？\n'
                              '已下发的任务和机台历史记录不会被删除。',
                          confirmLabel: '删除',
                        );
                        if (confirmed != true) {
                          return;
                        }
                        try {
                          await controller.deleteDeploymentPackage(pkg.packageId);
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text('部署包 ${pkg.name} 已删除。')),
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
                      label: const Text('删除'),
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
        Wrap(
          spacing: 10,
          runSpacing: 10,
          children: <Widget>[
            FilledButton.icon(
              onPressed: () async {
                final result = await showDialog<_UploadPackageResult>(
                  context: context,
                  builder: (context) => const _UploadPackageDialog(),
                );
                if (result == null) {
                  return;
                }
                try {
                  await controller.uploadDeploymentPackage(
                    name: result.name,
                    version: result.version,
                    type: result.type,
                    signer: result.signer,
                    packageBytes: result.packageBytes,
                    packageFileName: result.packageFileName,
                    signatureBytes: result.signatureBytes,
                    signatureFileName: result.signatureFileName,
                  );
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('部署包上传成功。')),
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
              icon: const Icon(Icons.upload_file_rounded),
              label: const Text('上传部署包'),
            ),
            OutlinedButton.icon(
              onPressed: controller.loadDeploymentPackages,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新列表'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (controller.deploymentPackages.isEmpty)
          if (embedInParentScroll) emptyState else Expanded(child: emptyState)
        else
          if (embedInParentScroll)
            packagesList
          else
            Expanded(child: packagesList),
      ],
    );
  }
}

class _UploadPackageResult {
  const _UploadPackageResult({
    required this.name,
    required this.version,
    required this.type,
    required this.signer,
    required this.packageBytes,
    required this.packageFileName,
    required this.signatureBytes,
    required this.signatureFileName,
  });

  final String name;
  final String version;
  final String type;
  final String signer;
  final List<int> packageBytes;
  final String packageFileName;
  final List<int> signatureBytes;
  final String signatureFileName;
}

class _UploadPackageDialog extends StatefulWidget {
  const _UploadPackageDialog();

  @override
  State<_UploadPackageDialog> createState() => _UploadPackageDialogState();
}

class _UploadPackageDialogState extends State<_UploadPackageDialog> {
  final TextEditingController _nameController = TextEditingController();
  final TextEditingController _versionController = TextEditingController();
  final TextEditingController _signerController = TextEditingController();
  String _type = 'software-deploy';
  String? _packagePath;
  String? _signaturePath;

  @override
  void dispose() {
    _nameController.dispose();
    _versionController.dispose();
    _signerController.dispose();
    super.dispose();
  }

  Future<void> _pickPackageFile() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: <String>['zip'],
      allowMultiple: false,
      withData: false,
    );
    if (result != null && result.files.isNotEmpty) {
      setState(() {
        _packagePath = result.files.first.path;
      });
    }
  }

  Future<void> _pickSignatureFile() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.any,
      allowMultiple: false,
      withData: false,
    );
    if (result != null && result.files.isNotEmpty) {
      setState(() {
        _signaturePath = result.files.first.path;
      });
    }
  }

  Future<void> _submit() async {
    final name = _nameController.text.trim();
    final version = _versionController.text.trim();
    final signer = _signerController.text.trim();

    if (name.isEmpty || version.isEmpty || signer.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('请填写名称、版本和签名者。')),
      );
      return;
    }
    if (_packagePath == null || _signaturePath == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('请选择 ZIP 包文件和签名文件。')),
      );
      return;
    }

    final packageFile = File(_packagePath!);
    final signatureFile = File(_signaturePath!);

    final packageExists = await packageFile.exists();
    final signatureExists = await signatureFile.exists();
    if (!mounted) {
      return;
    }
    if (!packageExists || !signatureExists) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('所选文件不存在。')),
      );
      return;
    }

    final packageBytes = await packageFile.readAsBytes();
    final signatureBytes = await signatureFile.readAsBytes();
    if (!mounted) {
      return;
    }

    Navigator.of(context).pop(_UploadPackageResult(
      name: name,
      version: version,
      type: _type,
      signer: signer,
      packageBytes: packageBytes,
      packageFileName: packageFile.uri.pathSegments.last,
      signatureBytes: signatureBytes,
      signatureFileName: signatureFile.uri.pathSegments.last,
    ));
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('上传部署包'),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 520),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              TextField(
                controller: _nameController,
                decoration: const InputDecoration(labelText: '包名称'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _versionController,
                decoration: const InputDecoration(labelText: '版本号'),
              ),
              const SizedBox(height: 12),
              DropdownMenu<String>(
                width: 480,
                label: const Text('包类型'),
                initialSelection: _type,
                dropdownMenuEntries: const <DropdownMenuEntry<String>>[
                  DropdownMenuEntry<String>(
                    value: 'software-deploy',
                    label: '软件部署包（含安装/卸载脚本）',
                  ),
                  DropdownMenuEntry<String>(
                    value: 'file-deploy',
                    label: '文件部署包（直接解压）',
                  ),
                ],
                onSelected: (value) {
                  if (value != null) {
                    setState(() {
                      _type = value;
                    });
                  }
                },
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _signerController,
                decoration: const InputDecoration(labelText: '签名者'),
              ),
              const SizedBox(height: 16),
              _buildFilePicker(
                label: 'ZIP 包文件',
                path: _packagePath,
                onPick: _pickPackageFile,
              ),
              const SizedBox(height: 12),
              _buildFilePicker(
                label: '签名文件 (.sig)',
                path: _signaturePath,
                onPick: _pickSignatureFile,
              ),
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: _submit,
          child: const Text('上传'),
        ),
      ],
    );
  }

  Widget _buildFilePicker({
    required String label,
    required String? path,
    required VoidCallback onPick,
  }) {
    return Row(
      children: <Widget>[
        Expanded(
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.78),
              borderRadius: BorderRadius.circular(20),
              border: Border.all(
                color: AppPalette.border.withValues(alpha: 0.7),
              ),
            ),
            child: Text(
              path?.split(Platform.pathSeparator).last ?? '未选择文件',
              style: TextStyle(
                color: path == null ? AppPalette.muted : AppPalette.ink,
              ),
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ),
        const SizedBox(width: 10),
        OutlinedButton.icon(
          onPressed: onPick,
          icon: const Icon(Icons.folder_open_rounded),
          label: Text(label),
        ),
      ],
    );
  }
}

class _TasksTab extends StatelessWidget {
  const _TasksTab({
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
          title: '当前没有部署任务',
          body: Text('点击上方按钮创建新的部署任务。'),
          icon: Icons.assignment_rounded,
          color: AppPalette.sky,
        ),
      ),
    );

    final tasksList = ListView.separated(
      itemCount: controller.deploymentTasks.length,
      shrinkWrap: embedInParentScroll,
      physics: embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final task = controller.deploymentTasks[index];
        Color statusColor;
        switch (task.status) {
          case 'success':
            statusColor = AppPalette.mint;
          case 'failed':
            statusColor = AppPalette.coral;
          case 'downloading':
            statusColor = AppPalette.sky;
          default:
            statusColor = AppPalette.sun;
        }

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
                      icon: task.taskType == 'deploy'
                          ? Icons.rocket_launch_rounded
                          : Icons.delete_outline_rounded,
                      color: statusColor,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            '${task.displayType} · ${task.packageName ?? task.packageId}',
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '机台: ${task.machineId}',
                            style: Theme.of(context)
                                .textTheme
                                .bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    StatusChip(
                      label: task.displayStatus,
                      color: statusColor,
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: <Widget>[
                    StatusChip(
                      label: '版本 ${task.packageVersion ?? '-'}',
                      color: AppPalette.sky,
                    ),
                    StatusChip(
                      label: '类型 ${task.displayType}',
                      color: AppPalette.mint,
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Text('任务 ID: ${task.taskId}'),
                Text('创建时间: ${formatAuditTimestamp(task.createdAt)}'),
                if (task.scheduledAt != null)
                  Text('计划执行: ${formatAuditTimestamp(task.scheduledAt!)}'),
                if (task.completedAt != null)
                  Text('完成时间: ${formatAuditTimestamp(task.completedAt!)}'),
                if (task.errorMessage != null && task.errorMessage!.isNotEmpty)
                  Text('错误: ${task.errorMessage}',
                      style: const TextStyle(color: AppPalette.danger)),
              ],
            ),
          ),
        );
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Wrap(
          spacing: 10,
          runSpacing: 10,
          children: <Widget>[
            FilledButton.icon(
              onPressed: () async {
                final result = await showDialog<_CreateTaskResult>(
                  context: context,
                  builder: (context) => _CreateTaskDialog(
                    packages: controller.deploymentPackages,
                    machines: controller.machines,
                  ),
                );
                if (result == null) {
                  return;
                }
                try {
                  await controller.createDeploymentTask(
                    result.packageId,
                    result.targetMachineIds,
                    scheduledAt: result.scheduledAt,
                  );
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text(
                        '已向 ${result.targetMachineIds.length} 个机台下发${result.targetMachineIds.length > 1 ? '' : ''}任务。',
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
              icon: const Icon(Icons.add_task_rounded),
              label: const Text('创建部署任务'),
            ),
            OutlinedButton.icon(
              onPressed: controller.loadDeploymentTasks,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新列表'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (controller.deploymentTasks.isEmpty)
          if (embedInParentScroll) emptyState else Expanded(child: emptyState)
        else
          if (embedInParentScroll)
            tasksList
          else
            Expanded(child: tasksList),
      ],
    );
  }
}

class _CreateTaskResult {
  const _CreateTaskResult({
    required this.packageId,
    required this.targetMachineIds,
    this.scheduledAt,
  });

  final String packageId;
  final List<String> targetMachineIds;
  final String? scheduledAt;
}

class _CreateTaskDialog extends StatefulWidget {
  const _CreateTaskDialog({
    required this.packages,
    required this.machines,
  });

  final List<DeploymentPackage> packages;
  final List<MachineRecord> machines;

  @override
  State<_CreateTaskDialog> createState() => _CreateTaskDialogState();
}

class _CreateTaskDialogState extends State<_CreateTaskDialog> {
  String? _selectedPackageId;
  final Set<String> _selectedMachineIds = <String>{};
  final TextEditingController _scheduledAtController = TextEditingController();

  @override
  void dispose() {
    _scheduledAtController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final packageEntries = widget.packages
        .map(
          (pkg) => DropdownMenuEntry<String>(
            value: pkg.packageId,
            label: '${pkg.name} v${pkg.version} (${pkg.displayType})',
          ),
        )
        .toList();

    return AlertDialog(
      title: const Text('创建部署任务'),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560, maxHeight: 600),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              if (widget.packages.isEmpty)
                const InfoPanel(
                  title: '没有可用部署包',
                  body: Text('请先上传部署包，再创建任务。'),
                  icon: Icons.warning_rounded,
                  color: AppPalette.sun,
                )
              else
                DropdownMenu<String>(
                  width: 520,
                  label: const Text('选择部署包'),
                  enableSearch: true,
                  dropdownMenuEntries: packageEntries,
                  onSelected: (value) {
                    setState(() {
                      _selectedPackageId = value;
                    });
                  },
                ),
              const SizedBox(height: 16),
              Text(
                '选择目标机台',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 8),
              if (widget.machines.isEmpty)
                const Text('没有可用机台。')
              else
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: widget.machines.map((machine) {
                    final selected = _selectedMachineIds.contains(machine.machineId);
                    return FilterChip(
                      label: Text(machine.machineId),
                      selected: selected,
                      onSelected: (value) {
                        setState(() {
                          if (value) {
                            _selectedMachineIds.add(machine.machineId);
                          } else {
                            _selectedMachineIds.remove(machine.machineId);
                          }
                        });
                      },
                    );
                  }).toList(),
                ),
              const SizedBox(height: 16),
              TextField(
                controller: _scheduledAtController,
                decoration: const InputDecoration(
                  labelText: '计划执行时间（可选）',
                  hintText: 'ISO 8601 格式，例如 2026-04-26T10:00:00Z',
                ),
              ),
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: (_selectedPackageId == null || _selectedMachineIds.isEmpty)
              ? null
              : () {
                  final scheduledAt = _scheduledAtController.text.trim();
                  Navigator.of(context).pop(_CreateTaskResult(
                    packageId: _selectedPackageId!,
                    targetMachineIds: _selectedMachineIds.toList(),
                    scheduledAt: scheduledAt.isEmpty ? null : scheduledAt,
                  ));
                },
          child: const Text('创建任务'),
        ),
      ],
    );
  }
}

class _HistoryTab extends StatefulWidget {
  const _HistoryTab({
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<_HistoryTab> createState() => _HistoryTabState();
}

class _HistoryTabState extends State<_HistoryTab> {
  String? _selectedMachineId;

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final machineOptions = controller.machines
        .map((m) => m.machineId)
        .where((id) => id.trim().isNotEmpty)
        .toList();

    final emptyState = Center(
      child: SizedBox(
        width: 420,
        child: const InfoPanel(
          title: '请选择机台',
          body: Text('从下拉菜单中选择一个机台，查看该机台的部署历史记录。'),
          icon: Icons.history_rounded,
          color: AppPalette.sky,
        ),
      ),
    );

    final noRecordsState = Center(
      child: SizedBox(
        width: 420,
        child: const InfoPanel(
          title: '该机台暂无部署记录',
          body: Text('该机台尚未上报任何部署记录。'),
          icon: Icons.inventory_2_rounded,
          color: AppPalette.mint,
        ),
      ),
    );

    final recordsList = ListView.separated(
      itemCount: controller.deploymentRecords.length,
      shrinkWrap: widget.embedInParentScroll,
      physics: widget.embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final record = controller.deploymentRecords[index];
        final isUninstalled = record.status == 'uninstalled';

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
                      icon: record.type == 'software-deploy'
                          ? Icons.install_desktop_rounded
                          : Icons.folder_copy_rounded,
                      color: isUninstalled ? AppPalette.muted : AppPalette.coral,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            record.name,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '版本 ${record.version} · ${record.displayType}',
                            style: Theme.of(context)
                                .textTheme
                                .bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    StatusChip(
                      label: record.displayStatus,
                      color: isUninstalled ? AppPalette.muted : AppPalette.mint,
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Text('包 ID: ${record.packageId}'),
                Text('部署时间: ${formatAuditTimestamp(record.deployedAt)}'),
                if (record.targetPath != null)
                  Text('目标路径: ${record.targetPath}'),
                if (record.uninstalledAt != null)
                  Text('卸载时间: ${formatAuditTimestamp(record.uninstalledAt!)}'),
                if (!isUninstalled) ...<Widget>[
                  const SizedBox(height: 16),
                  OutlinedButton.icon(
                    onPressed: () async {
                      final confirmed = await showConfirmDialog(
                        context,
                        title: '触发卸载',
                        message: '确认对机台 ${_selectedMachineId ?? record.machineId} 上的 ${record.name} v${record.version} 执行卸载吗？',
                        confirmLabel: '卸载',
                      );
                      if (confirmed != true) {
                        return;
                      }
                      try {
                        await controller.triggerUninstall(
                          record.machineId,
                          record.recordId,
                        );
                        if (!context.mounted) {
                          return;
                        }
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('卸载任务已创建。')),
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
                    label: const Text('触发卸载'),
                  ),
                ],
              ],
            ),
          ),
        );
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: <Widget>[
            SizedBox(
              width: 320,
              child: DropdownMenu<String>(
                width: 320,
                enableFilter: true,
                enableSearch: true,
                label: const Text('选择机台'),
                hintText: '全部机台',
                dropdownMenuEntries: machineOptions
                    .map(
                      (id) => DropdownMenuEntry<String>(
                        value: id,
                        label: id,
                      ),
                    )
                    .toList(),
                onSelected: (value) async {
                  setState(() {
                    _selectedMachineId = value;
                  });
                  if (value != null) {
                    await controller.loadMachineDeploymentHistory(value);
                  }
                },
              ),
            ),
            OutlinedButton.icon(
              onPressed: _selectedMachineId == null
                  ? null
                  : () => controller.loadMachineDeploymentHistory(
                        _selectedMachineId!,
                      ),
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新历史'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (_selectedMachineId == null)
          if (widget.embedInParentScroll)
            emptyState
          else
            Expanded(child: emptyState)
        else if (controller.deploymentRecords.isEmpty)
          if (widget.embedInParentScroll)
            noRecordsState
          else
            Expanded(child: noRecordsState)
        else if (widget.embedInParentScroll)
          recordsList
        else
          Expanded(child: recordsList),
      ],
    );
  }
}
