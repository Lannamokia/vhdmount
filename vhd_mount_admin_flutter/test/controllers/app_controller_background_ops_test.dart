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

AppController _createController() {
  return AppController(
    api: FakeAdminApi(
      serverStatus: _readyServerStatus,
      authStatus: _authenticatedStatus,
    ),
    clientConfigStore: FakeClientConfigStore(),
  );
}

void main() {
  group('BackgroundOperation model', () {
    test('typeLabel returns correct labels for each type', () {
      expect(
        BackgroundOperation(
          type: BackgroundOperationType.keyGeneration,
          status: BackgroundOperationStatus.running,
        ).typeLabel,
        '密钥生成',
      );
      expect(
        BackgroundOperation(
          type: BackgroundOperationType.manifestPackaging,
          status: BackgroundOperationStatus.running,
        ).typeLabel,
        '清单打包',
      );
      expect(
        BackgroundOperation(
          type: BackgroundOperationType.certificateGeneration,
          status: BackgroundOperationStatus.running,
        ).typeLabel,
        '证书生成',
      );
      expect(
        BackgroundOperation(
          type: BackgroundOperationType.deploymentPackaging,
          status: BackgroundOperationStatus.running,
        ).typeLabel,
        '部署打包',
      );
    });

    test('copyWith preserves unchanged fields', () {
      final op = BackgroundOperation(
        type: BackgroundOperationType.keyGeneration,
        status: BackgroundOperationStatus.running,
        progress: 0.5,
        step: '生成密钥...',
      );
      final updated = op.copyWith(progress: 0.8, step: '写入文件...');
      expect(updated.type, BackgroundOperationType.keyGeneration);
      expect(updated.status, BackgroundOperationStatus.running);
      expect(updated.progress, 0.8);
      expect(updated.step, '写入文件...');
      expect(updated.timestamp, op.timestamp);
    });
  });

  group('AppController background operations', () {
    test('startBackgroundOperation adds a running operation', () {
      final controller = _createController();
      final index = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '准备开始...',
      );

      expect(index, 0);
      expect(controller.backgroundOperations.length, 1);
      expect(
        controller.backgroundOperations[0].status,
        BackgroundOperationStatus.running,
      );
      expect(controller.backgroundOperations[0].step, '准备开始...');
      expect(
        controller.backgroundOperations[0].type,
        BackgroundOperationType.keyGeneration,
      );
    });

    test('activeOperation returns the running operation', () {
      final controller = _createController();
      expect(controller.activeOperation, isNull);

      controller.startBackgroundOperation(
        BackgroundOperationType.manifestPackaging,
        '扫描文件...',
      );

      expect(controller.activeOperation, isNotNull);
      expect(
        controller.activeOperation!.type,
        BackgroundOperationType.manifestPackaging,
      );
    });

    test('updateBackgroundOperationProgress updates progress and step', () {
      final controller = _createController();
      final index = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '准备开始...',
      );

      controller.updateBackgroundOperationProgress(index, 0.5, '生成密钥...');

      expect(controller.backgroundOperations[index].progress, 0.5);
      expect(controller.backgroundOperations[index].step, '生成密钥...');
      expect(
        controller.backgroundOperations[index].status,
        BackgroundOperationStatus.running,
      );
    });

    test('completeBackgroundOperation marks operation as completed', () {
      final controller = _createController();
      final index = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '准备开始...',
      );

      controller.completeBackgroundOperation(index, '密钥已生成到 /output');

      expect(
        controller.backgroundOperations[index].status,
        BackgroundOperationStatus.completed,
      );
      expect(controller.backgroundOperations[index].progress, 1.0);
      expect(
        controller.backgroundOperations[index].resultMessage,
        '密钥已生成到 /output',
      );
      expect(controller.backgroundOperations[index].isError, false);
      expect(controller.activeOperation, isNull);
    });

    test('failBackgroundOperation marks operation as failed', () {
      final controller = _createController();
      final index = controller.startBackgroundOperation(
        BackgroundOperationType.certificateGeneration,
        '准备开始...',
      );

      controller.failBackgroundOperation(index, '密码过短');

      expect(
        controller.backgroundOperations[index].status,
        BackgroundOperationStatus.failed,
      );
      expect(
        controller.backgroundOperations[index].resultMessage,
        '密码过短',
      );
      expect(controller.backgroundOperations[index].isError, true);
      expect(controller.activeOperation, isNull);
    });

    test('lastCompletedOperation returns the most recent non-running op', () {
      final controller = _createController();
      expect(controller.lastCompletedOperation, isNull);

      final idx1 = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '开始',
      );
      controller.completeBackgroundOperation(idx1, '第一次完成');

      final idx2 = controller.startBackgroundOperation(
        BackgroundOperationType.manifestPackaging,
        '开始',
      );
      controller.failBackgroundOperation(idx2, '第二次失败');

      expect(controller.lastCompletedOperation, isNotNull);
      expect(
        controller.lastCompletedOperation!.type,
        BackgroundOperationType.manifestPackaging,
      );
      expect(controller.lastCompletedOperation!.isError, true);
    });

    test('clearCompletedBackgroundOperations removes non-running ops', () {
      final controller = _createController();

      final idx1 = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '开始',
      );
      controller.completeBackgroundOperation(idx1, '完成');

      controller.startBackgroundOperation(
        BackgroundOperationType.manifestPackaging,
        '运行中',
      );

      expect(controller.backgroundOperations.length, 2);

      controller.clearCompletedBackgroundOperations();

      expect(controller.backgroundOperations.length, 1);
      expect(
        controller.backgroundOperations[0].status,
        BackgroundOperationStatus.running,
      );
    });

    test('dismissBackgroundOperation removes a specific completed op', () {
      final controller = _createController();

      final idx = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '开始',
      );
      controller.completeBackgroundOperation(idx, '完成');

      expect(controller.backgroundOperations.length, 1);

      controller.dismissBackgroundOperation(idx);

      expect(controller.backgroundOperations.length, 0);
    });

    test('dismissBackgroundOperation does not remove running ops', () {
      final controller = _createController();

      final idx = controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '运行中',
      );

      controller.dismissBackgroundOperation(idx);

      // Should not be removed since it's still running
      expect(controller.backgroundOperations.length, 1);
    });

    test('notifyListeners is called on state changes', () {
      final controller = _createController();
      int notifyCount = 0;
      controller.addListener(() => notifyCount++);

      controller.startBackgroundOperation(
        BackgroundOperationType.keyGeneration,
        '开始',
      );
      expect(notifyCount, 1);

      controller.updateBackgroundOperationProgress(0, 0.5, '进行中');
      expect(notifyCount, 2);

      controller.completeBackgroundOperation(0, '完成');
      expect(notifyCount, 3);

      controller.clearCompletedBackgroundOperations();
      expect(notifyCount, 4);
    });

    test('out-of-bounds index is safely ignored', () {
      final controller = _createController();

      // These should not throw
      controller.updateBackgroundOperationProgress(-1, 0.5, 'test');
      controller.updateBackgroundOperationProgress(99, 0.5, 'test');
      controller.completeBackgroundOperation(-1, 'test');
      controller.completeBackgroundOperation(99, 'test');
      controller.failBackgroundOperation(-1, 'test');
      controller.failBackgroundOperation(99, 'test');
      controller.dismissBackgroundOperation(-1);
      controller.dismissBackgroundOperation(99);

      expect(controller.backgroundOperations.length, 0);
    });
  });
}
