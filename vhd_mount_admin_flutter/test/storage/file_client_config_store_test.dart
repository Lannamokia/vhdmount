import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  test('load returns empty config when config file is missing', () async {
    final tempDir = await Directory.systemTemp.createTemp('client-config-');
    addTearDown(() => tempDir.delete(recursive: true));

    final store = FileClientConfigStore(
      directoryProvider: () async => tempDir,
      fileName: 'missing.json',
    );

    final config = await store.load();

    expect(config.lastBaseUrl, isEmpty);
    expect(config.serverHistory, isEmpty);
  });

  test('load throws diagnostic error when config json is malformed', () async {
    final tempDir = await Directory.systemTemp.createTemp('client-config-');
    addTearDown(() => tempDir.delete(recursive: true));
    final file = File('${tempDir.path}${Platform.pathSeparator}broken.json');
    await file.writeAsString('{broken-json');

    final store = FileClientConfigStore(
      directoryProvider: () async => tempDir,
      fileName: 'broken.json',
    );

    await expectLater(
      store.load(),
      throwsA(
        isA<ClientConfigStoreException>().having(
          (error) => error.message,
          'message',
          contains('JSON 已损坏'),
        ),
      ),
    );
  });

  test('save and load round-trip preserves remembered server history', () async {
    final tempDir = await Directory.systemTemp.createTemp('client-config-');
    addTearDown(() => tempDir.delete(recursive: true));

    final store = FileClientConfigStore(
      directoryProvider: () async => tempDir,
      fileName: 'config.json',
    );

    await store.save(
      const ClientConfig(
        lastBaseUrl: 'http://server-a:8080',
        serverHistory: <String>[
          'http://server-a:8080',
          'http://server-b:8080',
        ],
      ),
    );

    final config = await store.load();

    expect(config.lastBaseUrl, 'http://server-a:8080');
    expect(config.serverHistory, <String>[
      'http://server-a:8080',
      'http://server-b:8080',
    ]);
  });
}