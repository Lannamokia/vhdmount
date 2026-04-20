part of '../app.dart';

class MachineLogsView extends StatefulWidget {
  const MachineLogsView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<MachineLogsView> createState() => _MachineLogsViewState();
}

class _MachineLogsViewState extends State<MachineLogsView> {
  late final TextEditingController _componentController;
  late final TextEditingController _eventKeyController;
  late final TextEditingController _queryController;
  late final TextEditingController _fromController;
  late final TextEditingController _toController;

  String? _selectedMachineId;
  String? _selectedSessionId;
  String? _selectedLevel;
  int? _selectedEntryId;

  @override
  void initState() {
    super.initState();
    _componentController = TextEditingController();
    _eventKeyController = TextEditingController();
    _queryController = TextEditingController();
    _fromController = TextEditingController();
    _toController = TextEditingController();
    _syncFromController(forceText: true);
  }

  @override
  void didUpdateWidget(covariant MachineLogsView oldWidget) {
    super.didUpdateWidget(oldWidget);
    _syncFromController();
  }

  @override
  void dispose() {
    _componentController.dispose();
    _eventKeyController.dispose();
    _queryController.dispose();
    _fromController.dispose();
    _toController.dispose();
    super.dispose();
  }

  void _syncFromController({bool forceText = false}) {
    final controller = widget.controller;
    _selectedMachineId = controller.machineLogSelectedMachineId;
    _selectedSessionId = controller.machineLogSelectedSessionId;
    _selectedLevel = controller.machineLogLevel;

    if (forceText || _componentController.text != (controller.machineLogComponent ?? '')) {
      _componentController.text = controller.machineLogComponent ?? '';
    }
    if (forceText || _eventKeyController.text != (controller.machineLogEventKey ?? '')) {
      _eventKeyController.text = controller.machineLogEventKey ?? '';
    }
    if (forceText || _queryController.text != (controller.machineLogQuery ?? '')) {
      _queryController.text = controller.machineLogQuery ?? '';
    }
    if (forceText || _fromController.text != (controller.machineLogFrom ?? '')) {
      _fromController.text = controller.machineLogFrom ?? '';
    }
    if (forceText || _toController.text != (controller.machineLogTo ?? '')) {
      _toController.text = controller.machineLogTo ?? '';
    }

    final currentEntryIds = controller.machineLogEntries
        .map((entry) => entry.id)
        .toSet();
    if (_selectedEntryId == null || !currentEntryIds.contains(_selectedEntryId)) {
      _selectedEntryId = controller.machineLogEntries.isNotEmpty
          ? controller.machineLogEntries.first.id
          : null;
    }
  }

  String? _normalizedText(TextEditingController controller) {
    final value = controller.text.trim();
    return value.isEmpty ? null : value;
  }

  Future<void> _handleMachineChanged(String? machineId) async {
    setState(() {
      _selectedMachineId = machineId;
      _selectedSessionId = null;
    });

    try {
      await widget.controller.loadMachineLogSessions(
        machineId: _selectedMachineId,
        from: _normalizedText(_fromController),
        to: _normalizedText(_toController),
        loadEntries: false,
      );
      setState(() {
        _selectedSessionId = widget.controller.machineLogSelectedSessionId;
      });
      await _applyFilters(reloadSessions: false);
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Future<void> _handleSessionChanged(String? sessionId) async {
    setState(() {
      _selectedSessionId = sessionId;
    });

    try {
      await widget.controller.loadMachineLogs(
        machineId: _selectedMachineId,
        sessionId: _selectedSessionId,
        level: _selectedLevel,
        component: _normalizedText(_componentController),
        eventKey: _normalizedText(_eventKeyController)?.toUpperCase(),
        query: _normalizedText(_queryController),
        from: _normalizedText(_fromController),
        to: _normalizedText(_toController),
      );
      setState(() {
        _syncFromController();
      });
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Future<void> _handleLevelChanged(String? level) async {
    setState(() {
      _selectedLevel = level;
    });
    await _applyFilters(reloadSessions: false);
  }

  Future<void> _applyFilters({bool reloadSessions = true}) async {
    try {
      if (reloadSessions) {
        await widget.controller.loadMachineLogSessions(
          machineId: _selectedMachineId,
          from: _normalizedText(_fromController),
          to: _normalizedText(_toController),
          loadEntries: false,
        );
        if (_selectedSessionId != null &&
            !widget.controller.machineLogSessions.any(
              (session) => session.sessionId == _selectedSessionId,
            )) {
          _selectedSessionId = widget.controller.machineLogSelectedSessionId;
        }
      }

      await widget.controller.loadMachineLogs(
        machineId: _selectedMachineId,
        sessionId: _selectedSessionId,
        level: _selectedLevel,
        component: _normalizedText(_componentController),
        eventKey: _normalizedText(_eventKeyController)?.toUpperCase(),
        query: _normalizedText(_queryController),
        from: _normalizedText(_fromController),
        to: _normalizedText(_toController),
      );
      if (!mounted) {
        return;
      }
      setState(() {
        _syncFromController();
      });
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Future<void> _clearFilters() async {
    setState(() {
      _selectedMachineId = null;
      _selectedSessionId = null;
      _selectedLevel = null;
      _selectedEntryId = null;
      _componentController.clear();
      _eventKeyController.clear();
      _queryController.clear();
      _fromController.clear();
      _toController.clear();
    });

    widget.controller.clearMachineLogFilters(notify: false);
    await _applyFilters();
  }

  Future<void> _exportLogs() async {
    try {
      final exported = await widget.controller.exportMachineLogs();
      if (!mounted) {
        return;
      }
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('导出日志结果'),
          content: SizedBox(
            width: 720,
            child: SingleChildScrollView(
              child: SelectableText(exported.isEmpty ? '当前筛选条件下没有日志。' : exported),
            ),
          ),
          actions: <Widget>[
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('关闭'),
            ),
          ],
        ),
      );
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Widget _buildSessionCard(MachineLogSession session) {
    final selected = session.sessionId == _selectedSessionId;
    return InkWell(
      borderRadius: BorderRadius.circular(20),
      onTap: () => _handleSessionChanged(session.sessionId),
      child: Card(
        elevation: selected ? 2 : 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: BorderSide(
            color: selected ? AppPalette.coral : AppPalette.border,
          ),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(
                session.sessionId,
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 8),
              Text('启动：${session.localizedStartedAt}'),
              const SizedBox(height: 4),
              Text('最近日志：${session.localizedLastEventAt}'),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: <Widget>[
                  StatusChip(
                    label: '总计 ${session.totalCount}',
                    color: AppPalette.sky,
                  ),
                  StatusChip(
                    label: 'Warn ${session.warnCount}',
                    color: AppPalette.sun,
                  ),
                  StatusChip(
                    label: 'Error ${session.errorCount}',
                    color: AppPalette.coral,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildEntryCard(MachineLogEntry entry) {
    final selected = entry.id == _selectedEntryId;
    return InkWell(
      borderRadius: BorderRadius.circular(20),
      onTap: () {
        setState(() {
          _selectedEntryId = entry.id;
        });
      },
      child: Card(
        elevation: selected ? 2 : 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: BorderSide(
            color: selected ? AppPalette.coral : AppPalette.border,
          ),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  AccentIconBadge(
                    icon: entry.level == 'error'
                        ? Icons.error_outline_rounded
                        : entry.level == 'warn'
                        ? Icons.warning_amber_rounded
                        : Icons.notes_rounded,
                    color: entry.level == 'error'
                        ? AppPalette.coral
                        : entry.level == 'warn'
                        ? AppPalette.sun
                        : AppPalette.sky,
                    size: 42,
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          entry.message.isEmpty ? entry.rawText : entry.message,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                        const SizedBox(height: 6),
                        Text(
                          '${entry.localizedOccurredAt} · ${entry.component} · ${entry.eventKey}',
                          style: Theme.of(
                            context,
                          ).textTheme.bodySmall?.copyWith(color: AppPalette.muted),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: <Widget>[
                  StatusChip(
                    label: entry.level.toUpperCase(),
                    color: entry.level == 'error'
                        ? AppPalette.coral
                        : entry.level == 'warn'
                        ? AppPalette.sun
                        : AppPalette.sky,
                  ),
                  StatusChip(label: 'Seq ${entry.seq}', color: AppPalette.mint),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final machineOptions = controller.machines
        .map((machine) => machine.machineId)
        .toSet()
        .toList()
      ..sort((left, right) => left.compareTo(right));
    final sessionOptions = controller.machineLogSessions
        .map((session) => session.sessionId)
        .toList();
    final selectedEntry = controller.machineLogEntries
        .where((entry) => entry.id == _selectedEntryId)
        .cast<MachineLogEntry?>()
        .firstOrNull;
    final compact = widget.embedInParentScroll || MediaQuery.of(context).size.width < 1180;
    final sessionCards = controller.machineLogSessions
        .map(_buildSessionCard)
        .toList();
    final entryCards = controller.machineLogEntries.map(_buildEntryCard).toList();
    final detailText = selectedEntry == null
        ? '选择一条日志后，这里会显示脱敏后的原始文本和 metadata。'
        : '${selectedEntry.rawText}\n\nMetadata:\n${const JsonEncoder.withIndent('  ').convert(selectedEntry.metadata)}';

    final filtersPanel = AppPanel(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: <Widget>[
              SizedBox(
                width: 260,
                child: DropdownMenu<String>(
                  width: 260,
                  key: ValueKey<String>(_selectedMachineId ?? '__all_logs_machine__'),
                  initialSelection: _selectedMachineId,
                  enableFilter: true,
                  enableSearch: true,
                  label: const Text('机台'),
                  hintText: '全部机台',
                  dropdownMenuEntries: machineOptions
                      .map(
                        (machineId) => DropdownMenuEntry<String>(
                          value: machineId,
                          label: machineId,
                        ),
                      )
                      .toList(),
                  onSelected: _handleMachineChanged,
                ),
              ),
              SizedBox(
                width: 320,
                child: DropdownMenu<String>(
                  width: 320,
                  key: ValueKey<String>(_selectedSessionId ?? '__all_logs_session__'),
                  initialSelection: _selectedSessionId,
                  enableFilter: true,
                  enableSearch: true,
                  label: const Text('会话'),
                  hintText: '全部会话',
                  dropdownMenuEntries: sessionOptions
                      .map(
                        (sessionId) => DropdownMenuEntry<String>(
                          value: sessionId,
                          label: sessionId,
                        ),
                      )
                      .toList(),
                  onSelected: _handleSessionChanged,
                ),
              ),
              SizedBox(
                width: 180,
                child: DropdownMenu<String>(
                  width: 180,
                  key: ValueKey<String>(_selectedLevel ?? '__all_logs_level__'),
                  initialSelection: _selectedLevel,
                  label: const Text('级别'),
                  hintText: '全部级别',
                  dropdownMenuEntries: const <DropdownMenuEntry<String>>[
                    DropdownMenuEntry(value: 'debug', label: 'debug'),
                    DropdownMenuEntry(value: 'info', label: 'info'),
                    DropdownMenuEntry(value: 'warn', label: 'warn'),
                    DropdownMenuEntry(value: 'error', label: 'error'),
                  ],
                  onSelected: _handleLevelChanged,
                ),
              ),
              SizedBox(
                width: 220,
                child: TextField(
                  controller: _componentController,
                  decoration: const InputDecoration(labelText: '组件'),
                ),
              ),
              SizedBox(
                width: 220,
                child: TextField(
                  controller: _eventKeyController,
                  decoration: const InputDecoration(labelText: '事件键'),
                ),
              ),
              SizedBox(
                width: 260,
                child: TextField(
                  controller: _queryController,
                  decoration: const InputDecoration(
                    labelText: '关键词',
                    hintText: '搜索 message、rawText、metadata',
                  ),
                ),
              ),
              SizedBox(
                width: 260,
                child: TextField(
                  controller: _fromController,
                  decoration: const InputDecoration(
                    labelText: '开始时间',
                    hintText: '2026-04-19T12:00:00Z',
                  ),
                ),
              ),
              SizedBox(
                width: 260,
                child: TextField(
                  controller: _toController,
                  decoration: const InputDecoration(
                    labelText: '结束时间',
                    hintText: '2026-04-19T13:00:00Z',
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: <Widget>[
              FilledButton.icon(
                onPressed: _applyFilters,
                icon: const Icon(Icons.filter_alt_rounded),
                label: const Text('应用筛选'),
              ),
              OutlinedButton.icon(
                onPressed: _clearFilters,
                icon: const Icon(Icons.filter_alt_off_rounded),
                label: const Text('清空筛选'),
              ),
              OutlinedButton.icon(
                onPressed: () => _applyFilters(),
                icon: const Icon(Icons.refresh_rounded),
                label: const Text('刷新日志'),
              ),
              FilledButton.tonalIcon(
                onPressed: _exportLogs,
                icon: const Icon(Icons.download_rounded),
                label: const Text('导出原始文本'),
              ),
            ],
          ),
        ],
      ),
    );

    final sessionsPanel = AppPanel(
      padding: const EdgeInsets.all(16),
      child: controller.machineLogSessions.isEmpty
          ? const Center(child: Text('当前筛选条件下没有会话。'))
          : widget.embedInParentScroll
          ? Column(children: sessionCards)
          : ListView.separated(
              itemCount: sessionCards.length,
              itemBuilder: (context, index) => sessionCards[index],
              separatorBuilder: (_, _) => const SizedBox(height: 12),
            ),
    );

    final entriesPanel = AppPanel(
      padding: const EdgeInsets.all(16),
      child: controller.machineLogEntries.isEmpty
          ? const Center(child: Text('当前筛选条件下没有日志明细。'))
          : widget.embedInParentScroll
          ? Column(children: entryCards)
          : ListView.separated(
              itemCount: entryCards.length,
              itemBuilder: (context, index) => entryCards[index],
              separatorBuilder: (_, _) => const SizedBox(height: 12),
            ),
    );

    final detailPanel = AppPanel(
      padding: const EdgeInsets.all(18),
      gradient: const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: <Color>[
          Color(0xFFFFF8F1),
          Color(0xFFF7FBFF),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              const AccentIconBadge(
                icon: Icons.description_outlined,
                color: AppPalette.coral,
                size: 46,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text('原始日志详情', style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 4),
                    Text(
                      selectedEntry == null
                          ? '尚未选择日志'
                          : '${selectedEntry.component} · ${selectedEntry.eventKey} · Seq ${selectedEntry.seq}',
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: AppPalette.muted,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          SelectableText(detailText),
        ],
      ),
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Machine Telemetry',
          title: '机台日志',
          subtitle: '服务端分页筛选会话与明细，原始文本只以纯文本展示。',
          actions: <Widget>[
            StatusChip(
              label: controller.machineLogHasMore ? '还有更多分页' : '已到末页',
              color: controller.machineLogHasMore
                  ? AppPalette.sun
                  : AppPalette.mint,
            ),
          ],
        ),
        const SizedBox(height: 16),
        filtersPanel,
        const SizedBox(height: 16),
        if (compact) ...<Widget>[
          sessionsPanel,
          const SizedBox(height: 16),
          entriesPanel,
          if (controller.machineLogHasMore) ...<Widget>[
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerLeft,
              child: OutlinedButton.icon(
                onPressed: widget.controller.loadMoreMachineLogs,
                icon: const Icon(Icons.expand_more_rounded),
                label: const Text('加载更多日志'),
              ),
            ),
          ],
          const SizedBox(height: 16),
          detailPanel,
        ] else ...<Widget>[
          Expanded(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                SizedBox(width: 300, child: sessionsPanel),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    children: <Widget>[
                      Expanded(child: entriesPanel),
                      if (controller.machineLogHasMore) ...<Widget>[
                        const SizedBox(height: 12),
                        Align(
                          alignment: Alignment.centerLeft,
                          child: OutlinedButton.icon(
                            onPressed: widget.controller.loadMoreMachineLogs,
                            icon: const Icon(Icons.expand_more_rounded),
                            label: const Text('加载更多日志'),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
                const SizedBox(width: 16),
                SizedBox(width: 360, child: detailPanel),
              ],
            ),
          ),
        ],
      ],
    );
  }
}