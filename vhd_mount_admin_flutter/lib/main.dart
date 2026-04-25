import 'package:flutter/material.dart';
import 'package:vhd_mount_admin_flutter/app.dart' as app;

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(
    app.AdminApp(
      controller: app.AppController(
        api: app.HttpAdminApi(cookieStore: app.FileCookieStore()),
      ),
    ),
  );
}
