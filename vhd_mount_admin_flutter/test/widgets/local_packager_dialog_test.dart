import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  Future<void> openDialog(WidgetTester tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () {
                showDialog<void>(
                  context: context,
                  builder: (_) => const LocalPackagerDialog(),
                );
              },
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
  }

  testWidgets('本地打包器 Dialog 表单字段存在', (tester) async {
    await openDialog(tester);

    expect(find.text('本地打包器'), findsOneWidget);
    expect(find.byType(DropdownMenu<String>), findsOneWidget);
    expect(find.text('安装脚本'), findsOneWidget);
    expect(find.text('卸载脚本'), findsOneWidget);
    expect(find.text('文件负载目录'), findsOneWidget);
    expect(find.text('包名称'), findsOneWidget);
    expect(find.text('版本号'), findsOneWidget);
    expect(find.text('签名者'), findsOneWidget);
    expect(find.text('私钥文件'), findsOneWidget);
    expect(find.text('输出目录'), findsOneWidget);
    expect(find.text('目标部署路径'), findsOneWidget);
    expect(find.text('需要管理员权限'), findsOneWidget);
    expect(find.text('打包并签名'), findsOneWidget);
    expect(find.text('关闭'), findsOneWidget);
  });

  testWidgets('software-deploy 默认选中', (tester) async {
    await openDialog(tester);

    final dropdown = tester.widget<DropdownMenu<String>>(
      find.byType(DropdownMenu<String>),
    );
    expect(dropdown.initialSelection, 'software-deploy');
  });

  testWidgets('包名称、版本号、签名者有默认值', (tester) async {
    await openDialog(tester);

    final nameField = find.ancestor(
      of: find.text('配套工具'),
      matching: find.byType(TextField),
    );
    final versionField = find.ancestor(
      of: find.text('1.0.0'),
      matching: find.byType(TextField),
    );
    final signerField = find.ancestor(
      of: find.text('admin'),
      matching: find.byType(TextField),
    );

    expect(nameField, findsOneWidget);
    expect(versionField, findsOneWidget);
    expect(signerField, findsOneWidget);
  });

  testWidgets('点击关闭按钮关闭 Dialog', (tester) async {
    await openDialog(tester);
    expect(find.text('本地打包器'), findsOneWidget);

    await tester.tap(find.text('关闭'));
    await tester.pumpAndSettle();

    expect(find.text('本地打包器'), findsNothing);
  });

  testWidgets('空必填字段点击打包显示错误', (tester) async {
    await openDialog(tester);

    // 清空默认值
    await tester.enterText(
      find.ancestor(
        of: find.text('配套工具'),
        matching: find.byType(TextField),
      ),
      '',
    );
    await tester.pump();

    await tester.tap(find.text('打包并签名'));
    await tester.pump();

    expect(find.textContaining('不能为空'), findsOneWidget);
  });

  testWidgets('进度条不在初始状态显示', (tester) async {
    await openDialog(tester);
    expect(find.byType(LinearProgressIndicator), findsNothing);
  });
}
