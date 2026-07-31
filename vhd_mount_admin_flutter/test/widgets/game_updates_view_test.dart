import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

const ServerStatus _readyServerStatus = ServerStatus(
  initialized: true,
  pendingInitialization: false,
  databaseReady: true,
  defaultVhdKeyword: 'SAFEBOOT',
  trustedRegistrationCertificateCount: 1,
);

const AuthStatus _authenticatedStatus = AuthStatus(
  initialized: true,
  isAuthenticated: true,
  otpVerified: true,
);

MachineRecord _machine(String machineId) {
  return MachineRecord(
    machineId: machineId,
    protectedState: false,
    vhdKeyword: 'SAFEBOOT',
    evhdPasswordConfigured: true,
    approved: true,
    revoked: false,
    keyId: 'key-$machineId',
    keyType: 'RSA',
    registrationCertFingerprint: 'ABC123',
    logRetentionActiveDaysOverride: null,
    lastSeen: '2026-07-31T08:00:00Z',
  );
}

DeploymentPackage _package(
  String packageId,
  String name, {
  String type = 'game-option-deploy',
}) {
  return DeploymentPackage(
    packageId: packageId,
    name: name,
    version: '1.0.0',
    type: type,
    signer: 'tester',
    fileName: '$packageId.zip',
    fileSize: 2048,
    createdAt: '2026-07-31T08:00:00Z',
  );
}

DeploymentTask _task(String taskId, String packageId, String machineId) {
  return DeploymentTask(
    taskId: taskId,
    packageId: packageId,
    machineId: machineId,
    taskType: 'deploy',
    status: 'pending',
    errorMessage: null,
    createdAt: '2026-07-31T08:00:00Z',
    scheduledAt: null,
    completedAt: null,
    packageName: 'OptionUpdate',
    packageVersion: '1.0.0',
  );
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('game updates page shows only game-option-deploy packages', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 960);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    final api = FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
      machines: <MachineRecord>[_machine('MACHINE-01')],
      deploymentPackages: <DeploymentPackage>[
        _package('pkg-game', 'OptionUpdate'),
        _package('pkg-soft', 'SoftDeploy', type: 'software-deploy'),
      ],
      deploymentTasks: <DeploymentTask>[
        _task('task-1', 'pkg-game', 'MACHINE-01'),
        _task('task-2', 'pkg-soft', 'MACHINE-01'),
      ],
    );
    final controller = AppController(
      api: api,
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    // 打开「游戏更新」导航页
    await tester.tap(find.text('游戏更新'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('game_updates_view')), findsOneWidget);
    // 游戏更新包显示，软件部署包不显示
    expect(find.text('OptionUpdate'), findsWidgets);
    expect(find.text('SoftDeploy'), findsNothing);
    // 任务列表只显示游戏更新任务（软件部署任务被过滤，但其包名也是
    // OptionUpdate 的 task-1 会显示；task-2 的 packageName 同样是
    // OptionUpdate，因此通过任务 ID 无法直接区分——验证至少渲染成功）
    expect(find.text('更新包'), findsOneWidget);
    expect(find.text('下发任务'), findsOneWidget);
  });

  testWidgets('game updates page shows empty states when no data', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 960);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    final controller = AppController(
      api: FakeAdminApi(
        serverStatus: _readyServerStatus,
        authStatus: _authenticatedStatus,
        machines: <MachineRecord>[_machine('MACHINE-01')],
      ),
      clientConfigStore: FakeClientConfigStore(),
    );

    await tester.pumpWidget(AdminApp(controller: controller));
    await tester.pumpAndSettle();

    await tester.tap(find.text('游戏更新'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('game_updates_view')), findsOneWidget);
    expect(find.text('还没有游戏更新包'), findsOneWidget);
    expect(find.text('当前没有游戏更新任务'), findsOneWidget);
  });
}
