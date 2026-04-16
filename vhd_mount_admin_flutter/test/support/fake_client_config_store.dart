import 'package:vhd_mount_admin_flutter/app.dart';

class FakeClientConfigStore implements ClientConfigStore {
  FakeClientConfigStore([
    this.config = const ClientConfig(),
    this.loadError,
    this.saveError,
  ]);

  ClientConfig config;
  Object? loadError;
  Object? saveError;
  int saveCalls = 0;

  @override
  Future<ClientConfig> load() async {
    if (loadError != null) {
      throw loadError!;
    }
    return config;
  }

  @override
  Future<void> save(ClientConfig nextConfig) async {
    if (saveError != null) {
      throw saveError!;
    }
    saveCalls += 1;
    config = nextConfig;
  }
}