part of '../app.dart';

/// 游戏内容更新（option 目录下发）管理视图。
///
/// 与普通软件/文件部署完全独立的页面：只展示和创建
/// game-option-deploy 类型的部署包与任务。
class GameUpdatesView extends StatefulWidget {
  const GameUpdatesView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<GameUpdatesView> createState() => _GameUpdatesViewState();
}

class _GameUpdatesViewState extends State<GameUpdatesView> {
  List<DeploymentPackage> get _gamePackages => widget
      .controller
      .deploymentPackages
      .where((pkg) => pkg.type == 'game-option-deploy')
      .toList();

  List<DeploymentTask> get _gameTasks {
    final packageIds = _gamePackages.map((pkg) => pkg.packageId).toSet();
    return widget.controller.deploymentTasks
        .where((task) => packageIds.contains(task.packageId))
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final embed = widget.embedInParentScroll;

    final packagesSection = _GamePackagesSection(
      packages: _gamePackages,
      controller: widget.controller,
      embedInParentScroll: embed,
    );
    final tasksSection = _GameTasksSection(
      tasks: _gameTasks,
      packages: _gamePackages,
      controller: widget.controller,
      embedInParentScroll: embed,
    );

    final content = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        packagesSection,
        const SizedBox(height: 24),
        tasksSection,
      ],
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Game Content Update',
          title: '游戏更新下发',
          subtitle: '向指定机台下发游戏内容更新，机台在游戏磁盘挂载后自动应用到启动脚本目录下的 option 目录。',
          actions: <Widget>[
            OutlinedButton.icon(
              onPressed: () async {
                await widget.controller.loadDeploymentPackages();
                await widget.controller.loadDeploymentTasks();
              },
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新'),
            ),
          ],
        ),
        const SizedBox(height: 20),
        if (embed) content else Expanded(child: SingleChildScrollView(child: content)),
      ],
    );
  }
}

class _GamePackagesSection extends StatelessWidget {
  const _GamePackagesSection({
    required this.packages,
    required this.controller,
    required this.embedInParentScroll,
  });

  final List<DeploymentPackage> packages;
  final AppController controller;
  final bool embedInParentScroll;

  @override
  Widget build(BuildContext context) {
    final emptyState = const InfoPanel(
      title: '还没有游戏更新包',
      body: Text('点击上方按钮上传 ZIP 更新包与签名文件。'),
      icon: Icons.games_rounded,
      color: AppPalette.sun,
    );

    final list = ListView.separated(
      itemCount: packages.length,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final pkg = packages[index];
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
                      icon: Icons.games_rounded,
                      color: AppPalette.mint,
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
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    StatusChip(label: pkg.displayType, color: AppPalette.mint),
                  ],
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: <Widget>[
                    StatusChip(
                      label: '大小 ${pkg.displaySize}',
                      color: AppPalette.sky,
                    ),
                    StatusChip(
                      label: '文件 ${pkg.fileName}',
                      color: AppPalette.sun,
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
                          title: '删除更新包',
                          message:
                              '确认删除游戏更新包 ${pkg.name} v${pkg.version} 吗？\n'
                              '已下发的任务和机台历史记录不会被删除。',
                          confirmLabel: '删除',
                        );
                        if (confirmed != true) {
                          return;
                        }
                        try {
                          await controller.deleteDeploymentPackage(
                            pkg.packageId,
                          );
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text('更新包 ${pkg.name} 已删除。')),
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
          crossAxisAlignment: WrapCrossAlignment.center,
          children: <Widget>[
            Text('更新包', style: Theme.of(context).textTheme.titleLarge),
            FilledButton.icon(
              onPressed: () async {
                final result = await showDialog<_GameUploadResult>(
                  context: context,
                  builder: (context) => const _GameUploadDialog(),
                );
                if (result == null) {
                  return;
                }
                try {
                  await controller.uploadDeploymentPackage(
                    name: result.name,
                    version: result.version,
                    type: 'game-option-deploy',
                    signer: result.signer,
                    packagePath: result.packagePath,
                    packageFileName: result.packageFileName,
                    signaturePath: result.signaturePath,
                    signatureFileName: result.signatureFileName,
                  );
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(const SnackBar(content: Text('更新包上传成功。')));
                } catch (error) {
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(SnackBar(content: Text(describeError(error))));
                }
              },
              icon: const Icon(Icons.upload_file_rounded),
              label: const Text('上传更新包'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (packages.isEmpty) emptyState else list,
      ],
    );
  }
}

class _GameTasksSection extends StatelessWidget {
  const _GameTasksSection({
    required this.tasks,
    required this.packages,
    required this.controller,
    required this.embedInParentScroll,
  });

  final List<DeploymentTask> tasks;
  final List<DeploymentPackage> packages;
  final AppController controller;
  final bool embedInParentScroll;

  @override
  Widget build(BuildContext context) {
    final emptyState = const InfoPanel(
      title: '当前没有游戏更新任务',
      body: Text('选择更新包和目标机台，创建下发任务。'),
      icon: Icons.assignment_rounded,
      color: AppPalette.sky,
    );

    final list = ListView.separated(
      itemCount: tasks.length,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final task = tasks[index];
        Color statusColor;
        switch (task.status) {
          case 'success':
            statusColor = AppPalette.mint;
          case 'running':
            statusColor = AppPalette.mintDeep;
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
                      icon: Icons.system_update_alt_rounded,
                      color: statusColor,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            task.packageName ?? task.packageId,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '机台: ${task.machineId}',
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(color: AppPalette.muted),
                          ),
                        ],
                      ),
                    ),
                    StatusChip(label: task.displayStatus, color: statusColor),
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
                    const StatusChip(
                      label: '游戏内容更新',
                      color: AppPalette.mint,
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Text('任务 ID: ${task.taskId}'),
                Text('创建时间: ${formatAuditTimestamp(task.createdAt)}'),
                if (task.completedAt != null)
                  Text('完成时间: ${formatAuditTimestamp(task.completedAt!)}'),
                if (task.errorMessage != null && task.errorMessage!.isNotEmpty)
                  Text(
                    '错误: ${task.errorMessage}',
                    style: const TextStyle(color: AppPalette.danger),
                  ),
                const SizedBox(height: 16),
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: <Widget>[
                    OutlinedButton.icon(
                      onPressed: () async {
                        final confirmed = await showConfirmDialog(
                          context,
                          title: '删除更新任务',
                          message:
                              '确认删除任务 ${task.taskId} 吗？\n'
                              '机台 ${task.machineId} 将不再收到该更新。',
                          confirmLabel: '删除',
                        );
                        if (confirmed != true) {
                          return;
                        }
                        try {
                          await controller.deleteDeploymentTask(task.taskId);
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(content: Text('更新任务已删除。')),
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
          crossAxisAlignment: WrapCrossAlignment.center,
          children: <Widget>[
            Text('下发任务', style: Theme.of(context).textTheme.titleLarge),
            FilledButton.icon(
              onPressed: () async {
                final result = await showDialog<_GameCreateTaskResult>(
                  context: context,
                  builder: (context) => _GameCreateTaskDialog(
                    packages: packages,
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
                  );
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text(
                        '已向 ${result.targetMachineIds.length} 个机台下发更新任务。',
                      ),
                    ),
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
              icon: const Icon(Icons.add_task_rounded),
              label: const Text('创建下发任务'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (tasks.isEmpty) emptyState else list,
      ],
    );
  }
}

class _GameUploadResult {
  const _GameUploadResult({
    required this.name,
    required this.version,
    required this.signer,
    required this.packagePath,
    required this.packageFileName,
    required this.signaturePath,
    required this.signatureFileName,
  });

  final String name;
  final String version;
  final String signer;
  final String packagePath;
  final String packageFileName;
  final String signaturePath;
  final String signatureFileName;
}

class _GameUploadDialog extends StatefulWidget {
  const _GameUploadDialog();

  @override
  State<_GameUploadDialog> createState() => _GameUploadDialogState();
}

class _GameUploadDialogState extends State<_GameUploadDialog> {
  final TextEditingController _nameController = TextEditingController();
  final TextEditingController _versionController = TextEditingController();
  final TextEditingController _signerController = TextEditingController();
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
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('请填写名称、版本和签名者。')));
      return;
    }
    if (_packagePath == null || _signaturePath == null) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('请选择 ZIP 更新包和签名文件。')));
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
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('所选文件不存在。')));
      return;
    }

    Navigator.of(context).pop(
      _GameUploadResult(
        name: name,
        version: version,
        signer: signer,
        packagePath: packageFile.path,
        packageFileName: packageFile.uri.pathSegments.last,
        signaturePath: signatureFile.path,
        signatureFileName: signatureFile.uri.pathSegments.last,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final dialogWidth = min(520.0, MediaQuery.of(context).size.width - 48);
    return AlertDialog(
      title: const Text('上传游戏更新包'),
      content: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: dialogWidth),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              const InfoPanel(
                title: '更新内容说明',
                body: Text(
                  'ZIP 包内 payload/ 目录的内容将作为游戏启动脚本所在目录下 option 目录的完整新内容。'
                  '机台会在游戏磁盘挂载完成后、游戏启动前自动应用。',
                ),
                icon: Icons.info_outline_rounded,
                color: AppPalette.sky,
              ),
              const SizedBox(height: 16),
              TextField(
                controller: _nameController,
                decoration: const InputDecoration(labelText: '更新包名称'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _versionController,
                decoration: const InputDecoration(labelText: '版本号'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _signerController,
                decoration: const InputDecoration(labelText: '签名者'),
              ),
              const SizedBox(height: 16),
              _buildFilePicker(
                label: 'ZIP 更新包',
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
        FilledButton(onPressed: _submit, child: const Text('上传')),
      ],
    );
  }

  Widget _buildFilePicker({
    required String label,
    required String? path,
    required VoidCallback onPick,
  }) {
    final availableWidth = MediaQuery.of(context).size.width - 48;
    final narrow = availableWidth < 430;
    final filePreview = Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.78),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppPalette.border.withValues(alpha: 0.7)),
      ),
      child: Text(
        path?.split(Platform.pathSeparator).last ?? '未选择文件',
        style: TextStyle(
          color: path == null ? AppPalette.muted : AppPalette.ink,
        ),
        overflow: TextOverflow.ellipsis,
      ),
    );
    final pickButton = OutlinedButton.icon(
      onPressed: onPick,
      icon: const Icon(Icons.folder_open_rounded),
      label: Text(label),
    );

    if (narrow) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          filePreview,
          const SizedBox(height: 10),
          Align(alignment: Alignment.centerLeft, child: pickButton),
        ],
      );
    }

    return Row(
      children: <Widget>[
        Expanded(child: filePreview),
        const SizedBox(width: 10),
        pickButton,
      ],
    );
  }
}

class _GameCreateTaskResult {
  const _GameCreateTaskResult({
    required this.packageId,
    required this.targetMachineIds,
  });

  final String packageId;
  final List<String> targetMachineIds;
}

class _GameCreateTaskDialog extends StatefulWidget {
  const _GameCreateTaskDialog({required this.packages, required this.machines});

  final List<DeploymentPackage> packages;
  final List<MachineRecord> machines;

  @override
  State<_GameCreateTaskDialog> createState() => _GameCreateTaskDialogState();
}

class _GameCreateTaskDialogState extends State<_GameCreateTaskDialog> {
  String? _selectedPackageId;
  final Set<String> _selectedMachineIds = <String>{};

  @override
  Widget build(BuildContext context) {
    final packageEntries = widget.packages
        .map(
          (pkg) => DropdownMenuEntry<String>(
            value: pkg.packageId,
            label: '${pkg.name} v${pkg.version}',
          ),
        )
        .toList();

    final approvedMachines = widget.machines
        .where((machine) => machine.approved && !machine.revoked)
        .toList();

    return AlertDialog(
      title: const Text('创建游戏更新下发任务'),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560),
        child: LayoutBuilder(
          builder: (context, constraints) {
            final available = constraints.maxWidth.isFinite
                ? constraints.maxWidth
                : 520.0;
            final dropdownWidth = min(520.0, available);
            return SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  if (widget.packages.isEmpty)
                    const InfoPanel(
                      title: '没有可用更新包',
                      body: Text('请先上传游戏更新包，再创建下发任务。'),
                      icon: Icons.warning_rounded,
                      color: AppPalette.sun,
                    )
                  else
                    DropdownMenu<String>(
                      width: dropdownWidth,
                      label: const Text('选择更新包'),
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
                  if (approvedMachines.isEmpty)
                    const InfoPanel(
                      title: '没有已审批机台',
                      body: Text('机台需完成注册审批后才能接收游戏内容更新。'),
                      icon: Icons.dns_rounded,
                      color: AppPalette.sun,
                    )
                  else
                    ...approvedMachines.map((machine) {
                      final selected = _selectedMachineIds.contains(
                        machine.machineId,
                      );
                      return CheckboxListTile(
                        value: selected,
                        dense: true,
                        contentPadding: EdgeInsets.zero,
                        title: Text(machine.machineId),
                        subtitle: machine.lastSeen != null
                            ? Text(
                                '最近在线: ${formatAuditTimestamp(machine.lastSeen!)}',
                              )
                            : null,
                        onChanged: (value) {
                          setState(() {
                            if (value == true) {
                              _selectedMachineIds.add(machine.machineId);
                            } else {
                              _selectedMachineIds.remove(machine.machineId);
                            }
                          });
                        },
                      );
                    }),
                ],
              ),
            );
          },
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () {
            if (_selectedPackageId == null) {
              ScaffoldMessenger.of(
                context,
              ).showSnackBar(const SnackBar(content: Text('请选择更新包。')));
              return;
            }
            if (_selectedMachineIds.isEmpty) {
              ScaffoldMessenger.of(
                context,
              ).showSnackBar(const SnackBar(content: Text('请选择至少一个目标机台。')));
              return;
            }
            Navigator.of(context).pop(
              _GameCreateTaskResult(
                packageId: _selectedPackageId!,
                targetMachineIds: _selectedMachineIds.toList(),
              ),
            );
          },
          child: const Text('创建任务'),
        ),
      ],
    );
  }
}
