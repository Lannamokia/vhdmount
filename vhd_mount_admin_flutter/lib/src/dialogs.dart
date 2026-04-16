part of '../app.dart';

Future<String?> showSingleInputDialog(
  BuildContext context, {
  required String title,
  required String label,
  String initialValue = '',
  bool obscureText = false,
}) async {
  final controller = TextEditingController(text: initialValue);
  final result = await showDialog<String>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: obscureText
          ? buildSecureTextField(
              controller: controller,
              autofillHints: const <String>[AutofillHints.password],
              decoration: InputDecoration(labelText: label),
            )
          : TextField(
              controller: controller,
              decoration: InputDecoration(labelText: label),
            ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(controller.text),
          child: const Text('确定'),
        ),
      ],
    ),
  );
  controller.dispose();
  return result;
}

Future<List<String>?> showTwoFieldDialog(
  BuildContext context, {
  required String title,
  required String firstLabel,
  required String secondLabel,
  int secondMinLines = 1,
}) async {
  final firstController = TextEditingController();
  final secondController = TextEditingController();
  final result = await showDialog<List<String>>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: SizedBox(
        width: 560,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            TextField(
              controller: firstController,
              decoration: InputDecoration(labelText: firstLabel),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: secondController,
              minLines: secondMinLines,
              maxLines: secondMinLines + 4,
              decoration: InputDecoration(
                labelText: secondLabel,
                alignLabelWithHint: true,
              ),
            ),
          ],
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(
            context,
          ).pop(<String>[firstController.text, secondController.text]),
          child: const Text('确定'),
        ),
      ],
    ),
  );
  firstController.dispose();
  secondController.dispose();
  return result;
}

Future<MachineDraft?> showAddMachineDialog(
  BuildContext context, {
  required String defaultVhdKeyword,
}) async {
  final machineIdController = TextEditingController();
  final vhdController = TextEditingController(text: defaultVhdKeyword);
  final evhdPasswordController = TextEditingController();
  bool protectedState = false;

  final result = await showDialog<MachineDraft>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, setState) => AlertDialog(
        title: const Text('添加机台'),
        content: SizedBox(
          width: 560,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              TextField(
                controller: machineIdController,
                decoration: const InputDecoration(labelText: '机台 ID'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: vhdController,
                decoration: const InputDecoration(labelText: '初始启动关键词'),
              ),
              const SizedBox(height: 12),
              buildSecureTextField(
                controller: evhdPasswordController,
                decoration: const InputDecoration(labelText: '初始 EVHD 密码（可选）'),
              ),
              const SizedBox(height: 12),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('创建后立即开启保护'),
                value: protectedState,
                onChanged: (value) {
                  setState(() {
                    protectedState = value;
                  });
                },
              ),
            ],
          ),
        ),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.of(context).pop(
                MachineDraft(
                  machineId: machineIdController.text.trim(),
                  vhdKeyword: vhdController.text.trim().toUpperCase(),
                  protectedState: protectedState,
                  evhdPassword: evhdPasswordController.text.isEmpty
                      ? null
                      : evhdPasswordController.text,
                ),
              );
            },
            child: const Text('创建'),
          ),
        ],
      ),
    ),
  );

  machineIdController.dispose();
  vhdController.dispose();
  evhdPasswordController.dispose();
  return result;
}

Future<bool?> showConfirmDialog(
  BuildContext context, {
  required String title,
  required String message,
  String confirmLabel = '确定',
}) {
  return showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: Text(message),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
}