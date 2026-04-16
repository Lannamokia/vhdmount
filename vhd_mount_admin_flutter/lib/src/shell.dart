part of '../app.dart';

class AdminApp extends StatelessWidget {
  const AdminApp({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final baseTheme = ThemeData(
      useMaterial3: true,
      brightness: Brightness.light,
      fontFamily: 'MiSans',
    );
    final colorScheme =
        ColorScheme.fromSeed(
          seedColor: AppPalette.mint,
          brightness: Brightness.light,
        ).copyWith(
          primary: AppPalette.mintDeep,
          onPrimary: Colors.white,
          secondary: AppPalette.coralDeep,
          onSecondary: Colors.white,
          tertiary: AppPalette.sky,
          surface: AppPalette.surfaceStrong,
          onSurface: AppPalette.ink,
          outline: AppPalette.border,
          outlineVariant: AppPalette.border.withValues(alpha: 0.58),
          error: AppPalette.danger,
        );
    final textTheme = baseTheme.textTheme.copyWith(
      displaySmall: miSansTextStyle(
        fontSize: 42,
        fontWeight: FontWeight.w700,
        height: 1.04,
        color: AppPalette.ink,
      ),
      headlineMedium: miSansTextStyle(
        fontSize: 30,
        fontWeight: FontWeight.w700,
        height: 1.12,
        color: AppPalette.ink,
      ),
      headlineSmall: miSansTextStyle(
        fontSize: 24,
        fontWeight: FontWeight.w700,
        height: 1.18,
        color: AppPalette.ink,
      ),
      titleLarge: miSansTextStyle(
        fontSize: 20,
        fontWeight: FontWeight.w700,
        height: 1.2,
        color: AppPalette.ink,
      ),
      titleMedium: miSansTextStyle(
        fontSize: 16,
        fontWeight: FontWeight.w600,
        height: 1.24,
        color: AppPalette.ink,
      ),
      bodyLarge: miSansTextStyle(
        fontSize: 16,
        fontWeight: FontWeight.w500,
        height: 1.55,
        color: AppPalette.ink,
      ),
      bodyMedium: miSansTextStyle(
        fontSize: 14,
        fontWeight: FontWeight.w500,
        height: 1.55,
        color: AppPalette.ink,
      ),
      bodySmall: miSansTextStyle(
        fontSize: 12,
        fontWeight: FontWeight.w500,
        height: 1.45,
        color: AppPalette.muted,
      ),
      labelLarge: miSansTextStyle(
        fontSize: 14,
        fontWeight: FontWeight.w600,
        height: 1.2,
        color: AppPalette.ink,
      ),
    );

    return MaterialApp(
      title: 'VHD Mount Admin',
      debugShowCheckedModeBanner: false,
      theme: baseTheme.copyWith(
        colorScheme: colorScheme,
        scaffoldBackgroundColor: AppPalette.canvasWarm,
        textTheme: textTheme,
        appBarTheme: AppBarTheme(
          backgroundColor: AppPalette.surfaceStrong.withValues(alpha: 0.78),
          foregroundColor: AppPalette.ink,
          surfaceTintColor: Colors.transparent,
          elevation: 0,
          scrolledUnderElevation: 0,
          titleTextStyle: textTheme.titleLarge,
          toolbarTextStyle: textTheme.bodyMedium,
        ),
        cardTheme: CardThemeData(
          color: Colors.white.withValues(alpha: 0.92),
          elevation: 0,
          surfaceTintColor: Colors.transparent,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(28),
          ),
          shadowColor: AppPalette.ink.withValues(alpha: 0.08),
          margin: EdgeInsets.zero,
        ),
        dividerTheme: DividerThemeData(
          color: AppPalette.border.withValues(alpha: 0.72),
          thickness: 1,
        ),
        snackBarTheme: SnackBarThemeData(
          backgroundColor: AppPalette.ink,
          contentTextStyle: textTheme.bodyMedium?.copyWith(color: Colors.white),
          behavior: SnackBarBehavior.floating,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(18),
          ),
        ),
        popupMenuTheme: PopupMenuThemeData(
          color: AppPalette.surfaceStrong,
          surfaceTintColor: Colors.transparent,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(18),
          ),
          textStyle: textTheme.bodyMedium,
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: Colors.white.withValues(alpha: 0.78),
          contentPadding: const EdgeInsets.symmetric(
            horizontal: 18,
            vertical: 18,
          ),
          labelStyle: textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
          hintStyle: textTheme.bodyMedium?.copyWith(
            color: AppPalette.muted.withValues(alpha: 0.7),
          ),
          prefixIconColor: AppPalette.mintDeep,
          suffixIconColor: AppPalette.muted,
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(20),
            borderSide: BorderSide(
              color: AppPalette.border.withValues(alpha: 0.2),
            ),
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(20),
            borderSide: BorderSide(
              color: AppPalette.border.withValues(alpha: 0.7),
            ),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(20),
            borderSide: const BorderSide(
              color: AppPalette.mintDeep,
              width: 1.4,
            ),
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            backgroundColor: AppPalette.mintDeep,
            foregroundColor: Colors.white,
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 18),
            textStyle: textTheme.labelLarge?.copyWith(
              color: Colors.white,
              fontWeight: FontWeight.w600,
            ),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(18),
            ),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            foregroundColor: AppPalette.ink,
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 18),
            side: BorderSide(color: AppPalette.border.withValues(alpha: 0.92)),
            textStyle: textTheme.labelLarge,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(18),
            ),
            backgroundColor: Colors.white.withValues(alpha: 0.54),
          ),
        ),
        textButtonTheme: TextButtonThemeData(
          style: TextButton.styleFrom(
            foregroundColor: AppPalette.coralDeep,
            textStyle: textTheme.labelLarge,
          ),
        ),
        navigationRailTheme: NavigationRailThemeData(
          backgroundColor: Colors.transparent,
          useIndicator: true,
          indicatorColor: AppPalette.coral.withValues(alpha: 0.18),
          selectedIconTheme: const IconThemeData(color: AppPalette.coralDeep),
          unselectedIconTheme: const IconThemeData(color: AppPalette.muted),
          selectedLabelTextStyle: textTheme.labelLarge?.copyWith(
            color: AppPalette.coralDeep,
          ),
          unselectedLabelTextStyle: textTheme.bodySmall?.copyWith(
            color: AppPalette.muted,
          ),
          minWidth: 84,
          minExtendedWidth: 220,
          groupAlignment: -1,
        ),
        navigationBarTheme: NavigationBarThemeData(
          backgroundColor: AppPalette.surfaceStrong.withValues(alpha: 0.96),
          surfaceTintColor: Colors.transparent,
          indicatorColor: AppPalette.mint.withValues(alpha: 0.16),
          height: 76,
          elevation: 0,
          labelTextStyle: WidgetStateProperty.resolveWith<TextStyle?>((states) {
            final selected = states.contains(WidgetState.selected);
            return miSansTextStyle(
              fontSize: 13,
              fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
              height: 1.2,
              color: selected ? AppPalette.ink : AppPalette.muted,
            );
          }),
        ),
      ),
      home: AdminRoot(controller: controller),
    );
  }
}

class AdminRoot extends StatefulWidget {
  const AdminRoot({super.key, required this.controller});

  final AppController controller;

  @override
  State<AdminRoot> createState() => _AdminRootState();
}

class _AdminRootState extends State<AdminRoot> {
  int _selectedIndex = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      widget.controller.bootstrap();
    });
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.controller,
      builder: (context, _) {
        final controller = widget.controller;

        Widget content;
        if (controller.isLoading) {
          content = const SplashScreen();
        } else if (controller.serverStatus == null) {
          content = ConnectionScreen(controller: controller);
        } else if (!controller.serverStatus!.initialized) {
          content = InitializationScreen(controller: controller);
        } else if (!controller.isAuthenticated) {
          content = LoginScreen(controller: controller);
        } else {
          content = DashboardScreen(
            controller: controller,
            selectedIndex: _selectedIndex,
            onDestinationSelected: (index) async {
              setState(() {
                _selectedIndex = index;
              });
              if (index == 0) {
                await controller.loadMachines();
              } else if (index == 1 && controller.otpVerified) {
                await controller.loadCertificates();
              } else if (index == 2) {
                await controller.loadAudit(
                  machineId: controller.auditFilterMachineId,
                );
              }
            },
          );
        }

        return Stack(
          children: <Widget>[
            content,
            if (controller.isWorking)
              Positioned.fill(
                child: ColoredBox(
                  color: AppPalette.ink.withValues(alpha: 0.08),
                  child: const Center(child: CircularProgressIndicator()),
                ),
              ),
          ],
        );
      },
    );
  }
}

class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: AdminBackdrop(
        child: Center(
          child: TweenAnimationBuilder<double>(
            tween: Tween<double>(begin: 0.92, end: 1),
            duration: const Duration(milliseconds: 900),
            curve: Curves.easeOutCubic,
            builder: (context, value, child) {
              return Opacity(
                opacity: value,
                child: Transform.scale(scale: value, child: child),
              );
            },
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: AppPanel(
                padding: const EdgeInsets.all(32),
                gradient: const LinearGradient(
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                  colors: <Color>[
                    Color(0xFFFFF7EF),
                    Color(0xFFF6FFF9),
                    Color(0xFFEAF6FF),
                  ],
                ),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    const AccentIconBadge(
                      icon: Icons.auto_awesome_rounded,
                      color: AppPalette.coral,
                      size: 72,
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'VHD Mount Admin',
                      style: Theme.of(context).textTheme.headlineMedium,
                    ),
                    const SizedBox(height: 8),
                    Text(
                      '正在整理安全配置、会话状态和管理入口。',
                      textAlign: TextAlign.center,
                      style: Theme.of(
                        context,
                      ).textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
                    ),
                    const SizedBox(height: 18),
                    const CircularProgressIndicator(),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class ServerAddressField extends StatelessWidget {
  const ServerAddressField({
    super.key,
    required this.controller,
    required this.rememberedBaseUrls,
  });

  final TextEditingController controller;
  final List<String> rememberedBaseUrls;

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: controller,
      decoration: InputDecoration(
        labelText: '服务器地址',
        hintText: '例如 http://localhost:8080',
        prefixIcon: const Icon(Icons.link_rounded),
        helperText: rememberedBaseUrls.isEmpty ? null : '可从右侧历史中选择已登录过的服务端地址',
        suffixIcon: rememberedBaseUrls.isEmpty
            ? null
            : PopupMenuButton<String>(
                tooltip: '已保存的服务器地址',
                icon: const Icon(Icons.history_rounded),
                onSelected: (value) {
                  controller.value = TextEditingValue(
                    text: value,
                    selection: TextSelection.collapsed(offset: value.length),
                  );
                },
                itemBuilder: (context) {
                  return rememberedBaseUrls
                      .map(
                        (value) => PopupMenuItem<String>(
                          value: value,
                          child: SizedBox(
                            width: 260,
                            child: Text(value, overflow: TextOverflow.ellipsis),
                          ),
                        ),
                      )
                      .toList();
                },
              ),
      ),
    );
  }
}

class ConnectionScreen extends StatefulWidget {
  const ConnectionScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<ConnectionScreen> createState() => _ConnectionScreenState();
}

class _ConnectionScreenState extends State<ConnectionScreen> {
  late final TextEditingController _baseUrlController;

  @override
  void initState() {
    super.initState();
    _baseUrlController = TextEditingController(text: widget.controller.baseUrl);
  }

  @override
  void dispose() {
    _baseUrlController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AuthShell(
      eyebrow: 'Flutter 桌面入口',
      title: '连接你的 VHD 服务',
      subtitle: '旧 Web 管理页已经下线，现在用更轻快的桌面控制台来完成初始化、登录与运维操作。',
      heroIcon: Icons.hub_rounded,
      spotlight: OverviewStatCard(
        label: '最近地址',
        value: widget.controller.rememberedBaseUrls.isEmpty
            ? '尚未保存'
            : '${widget.controller.rememberedBaseUrls.length} 个',
        icon: Icons.history_rounded,
        color: AppPalette.sky,
        caption: '可以从输入框右侧历史菜单快速切换。',
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text('连接服务器', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            '输入管理服务地址后检查服务状态。常用地址会自动保存在本地。',
            style: Theme.of(
              context,
            ).textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
          ),
          if (widget.controller.errorMessage != null) ...<Widget>[
            const SizedBox(height: 16),
            ErrorBanner(message: widget.controller.errorMessage!),
          ],
          const SizedBox(height: 16),
          ServerAddressField(
            controller: _baseUrlController,
            rememberedBaseUrls: widget.controller.rememberedBaseUrls,
          ),
          const SizedBox(height: 18),
          FilledButton.icon(
            onPressed: () async {
              widget.controller.updateBaseUrl(_baseUrlController.text);
              await widget.controller.bootstrap();
            },
            icon: const Icon(Icons.sync_rounded),
            label: const Text('检查服务状态'),
          ),
          const SizedBox(height: 14),
          Text(
            '提示：如果你在本机联调，默认地址通常是 http://localhost:8080。',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class InitializationScreen extends StatefulWidget {
  const InitializationScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<InitializationScreen> createState() => _InitializationScreenState();
}

class _InitializationScreenState extends State<InitializationScreen> {
  late final TextEditingController _baseUrlController;
  late final TextEditingController _issuerController;
  late final TextEditingController _accountNameController;
  late final TextEditingController _adminPasswordController;
  late final TextEditingController _confirmPasswordController;
  late final TextEditingController _sessionSecretController;
  late final TextEditingController _dbHostController;
  late final TextEditingController _dbPortController;
  late final TextEditingController _dbNameController;
  late final TextEditingController _dbUserController;
  late final TextEditingController _dbPasswordController;
  late final TextEditingController _defaultVhdController;
  late final TextEditingController _trustedCertificateNameController;
  late final TextEditingController _trustedCertificatePemController;
  late final TextEditingController _totpCodeController;

  @override
  void initState() {
    super.initState();
    _baseUrlController = TextEditingController(text: widget.controller.baseUrl);
    _issuerController = TextEditingController(text: 'VHDMountServer');
    _accountNameController = TextEditingController(text: 'admin');
    _adminPasswordController = TextEditingController();
    _confirmPasswordController = TextEditingController();
    _sessionSecretController = TextEditingController(
      text: generateSessionSecret(),
    );
    _dbHostController = TextEditingController(text: 'localhost');
    _dbPortController = TextEditingController(text: '5432');
    _dbNameController = TextEditingController(text: 'vhd_select');
    _dbUserController = TextEditingController(text: 'postgres');
    _dbPasswordController = TextEditingController();
    _defaultVhdController = TextEditingController(text: 'SDEZ');
    _trustedCertificateNameController = TextEditingController(
      text: 'machine-registration',
    );
    _trustedCertificatePemController = TextEditingController();
    _totpCodeController = TextEditingController();
  }

  @override
  void dispose() {
    _baseUrlController.dispose();
    _issuerController.dispose();
    _accountNameController.dispose();
    _adminPasswordController.dispose();
    _confirmPasswordController.dispose();
    _sessionSecretController.dispose();
    _dbHostController.dispose();
    _dbPortController.dispose();
    _dbNameController.dispose();
    _dbUserController.dispose();
    _dbPasswordController.dispose();
    _defaultVhdController.dispose();
    _trustedCertificateNameController.dispose();
    _trustedCertificatePemController.dispose();
    _totpCodeController.dispose();
    super.dispose();
  }

  Widget _buildOtpImportPanel(InitializationPreparation preparation) {
    final otpauthUrl = normalizeOtpauthUrl(preparation.otpauthUrl);

    return InfoPanel(
      title: 'OTP 导入信息',
      icon: Icons.qr_code_2_rounded,
      color: AppPalette.mint,
      body: Wrap(
        spacing: 20,
        runSpacing: 20,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: <Widget>[
          Container(
            width: 232,
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: <Color>[
                  Colors.white.withValues(alpha: 0.94),
                  AppPalette.mint.withValues(alpha: 0.08),
                ],
              ),
              borderRadius: BorderRadius.circular(22),
              border: Border.all(
                color: AppPalette.mint.withValues(alpha: 0.18),
              ),
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                const Text(
                  '扫描二维码导入',
                  style: TextStyle(
                    fontFamily: _miSansFamily700,
                    fontSize: 16,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 12),
                if (otpauthUrl.isNotEmpty)
                  QrImageView(
                    data: otpauthUrl,
                    version: QrVersions.auto,
                    size: 180,
                    backgroundColor: Colors.white,
                    eyeStyle: const QrEyeStyle(
                      eyeShape: QrEyeShape.square,
                      color: AppPalette.ink,
                    ),
                    dataModuleStyle: const QrDataModuleStyle(
                      dataModuleShape: QrDataModuleShape.square,
                      color: AppPalette.mintDeep,
                    ),
                  )
                else
                  Container(
                    width: 180,
                    height: 180,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.circular(18),
                      border: Border.all(color: AppPalette.border),
                    ),
                    child: const Text('未返回 otpauth URI'),
                  ),
                const SizedBox(height: 12),
                Text(
                  '使用手机验证器扫描此二维码即可添加 TOTP。',
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: AppPalette.muted,
                    height: 1.45,
                  ),
                ),
              ],
            ),
          ),
          ConstrainedBox(
            constraints: const BoxConstraints(minWidth: 280, maxWidth: 520),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  '优先扫码导入；如果验证器不支持扫码，再使用下方密钥或 URI 手动添加。',
                  style: Theme.of(
                    context,
                  ).textTheme.bodyMedium?.copyWith(height: 1.5),
                ),
                const SizedBox(height: 12),
                SelectableText('Issuer: ${preparation.issuer}'),
                const SizedBox(height: 6),
                SelectableText('Account: ${preparation.accountName}'),
                const SizedBox(height: 6),
                SelectableText('Secret: ${preparation.totpSecret}'),
                const SizedBox(height: 6),
                SelectableText('URI: $otpauthUrl'),
              ],
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final preparation = widget.controller.initializationPreparation;
    return AuthShell(
      eyebrow: '服务未初始化',
      title: '把管理服务安全地启动起来',
      subtitle: '先准备 OTP，再一次性写入管理员口令、Session Secret、数据库连接和可信注册证书。',
      heroIcon: Icons.shield_rounded,
      spotlight: OverviewStatCard(
        label: 'OTP 准备状态',
        value: preparation == null ? '待生成' : '已就绪',
        icon: Icons.qr_code_2_rounded,
        color: preparation == null ? AppPalette.sun : AppPalette.mint,
        caption: preparation == null
            ? '先点击“准备 OTP”生成二维码。'
            : '导入验证器后填写验证码并完成初始化。',
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text('初始化向导', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            '配置管理员凭据、数据库连接与可信证书，完成服务首次安全落地。',
            style: Theme.of(
              context,
            ).textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
          ),
          if (widget.controller.errorMessage != null) ...<Widget>[
            const SizedBox(height: 16),
            ErrorBanner(message: widget.controller.errorMessage!),
          ],
          const SizedBox(height: 16),
          const InfoPanel(
            title: '推荐顺序',
            body: Text('1. 准备 OTP 并导入验证器。2. 填写管理员与数据库信息。3. 输入 TOTP 验证码后完成初始化。'),
            icon: Icons.flag_rounded,
            color: AppPalette.sky,
          ),
          const SizedBox(height: 16),
          _buildFieldRow(
            child: ServerAddressField(
              controller: _baseUrlController,
              rememberedBaseUrls: widget.controller.rememberedBaseUrls,
            ),
          ),
          _buildFieldRow(
            left: TextField(
              controller: _issuerController,
              decoration: const InputDecoration(labelText: 'OTP issuer'),
            ),
            right: TextField(
              controller: _accountNameController,
              decoration: const InputDecoration(labelText: 'OTP account'),
            ),
          ),
          _buildFieldRow(
            left: buildSecureTextField(
              controller: _adminPasswordController,
              autofillHints: const <String>[AutofillHints.newPassword],
              decoration: const InputDecoration(labelText: '管理员密码'),
            ),
            right: buildSecureTextField(
              controller: _confirmPasswordController,
              autofillHints: const <String>[AutofillHints.newPassword],
              decoration: const InputDecoration(labelText: '确认管理员密码'),
            ),
          ),
          _buildFieldRow(
            child: buildSecureTextField(
              controller: _sessionSecretController,
              decoration: InputDecoration(
                labelText: 'Session Secret',
                suffixIcon: IconButton(
                  onPressed: () {
                    _sessionSecretController.text = generateSessionSecret();
                  },
                  icon: const Icon(Icons.casino_rounded),
                ),
              ),
            ),
          ),
          _buildFieldRow(
            left: TextField(
              controller: _dbHostController,
              decoration: const InputDecoration(labelText: 'DB Host'),
            ),
            right: TextField(
              controller: _dbPortController,
              decoration: const InputDecoration(labelText: 'DB Port'),
            ),
          ),
          _buildFieldRow(
            left: TextField(
              controller: _dbNameController,
              decoration: const InputDecoration(labelText: 'DB Name'),
            ),
            right: TextField(
              controller: _dbUserController,
              decoration: const InputDecoration(labelText: 'DB User'),
            ),
          ),
          _buildFieldRow(
            left: buildSecureTextField(
              controller: _dbPasswordController,
              decoration: const InputDecoration(labelText: 'DB Password'),
            ),
            right: TextField(
              controller: _defaultVhdController,
              decoration: const InputDecoration(labelText: '默认启动关键词'),
            ),
          ),
          _buildFieldRow(
            left: TextField(
              controller: _trustedCertificateNameController,
              decoration: const InputDecoration(labelText: '可信注册证书名称'),
            ),
            right: TextField(
              controller: _totpCodeController,
              decoration: const InputDecoration(labelText: 'TOTP 验证码'),
            ),
          ),
          TextField(
            controller: _trustedCertificatePemController,
            minLines: 8,
            maxLines: 12,
            decoration: const InputDecoration(
              labelText: '初始可信注册证书 PEM（可选）',
              alignLabelWithHint: true,
            ),
          ),
          const SizedBox(height: 18),
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: <Widget>[
              FilledButton.icon(
                onPressed: () async {
                  widget.controller.updateBaseUrl(_baseUrlController.text);
                  try {
                    await widget.controller.prepareInitialization(
                      issuer: _issuerController.text.trim(),
                      accountName: _accountNameController.text.trim(),
                    );
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(
                        content: Text('OTP 准备完成，请在验证器中导入密钥后填写验证码。'),
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
                icon: const Icon(Icons.qr_code_2_rounded),
                label: const Text('准备 OTP'),
              ),
              OutlinedButton.icon(
                onPressed: () async {
                  widget.controller.updateBaseUrl(_baseUrlController.text);
                  await widget.controller.bootstrap();
                },
                icon: const Icon(Icons.sync_rounded),
                label: const Text('刷新状态'),
              ),
            ],
          ),
          if (preparation != null) ...<Widget>[
            const SizedBox(height: 20),
            _buildOtpImportPanel(preparation),
          ],
          const SizedBox(height: 20),
          FilledButton.tonalIcon(
            onPressed: () async {
              if (_adminPasswordController.text !=
                  _confirmPasswordController.text) {
                ScaffoldMessenger.of(
                  context,
                ).showSnackBar(const SnackBar(content: Text('管理员密码与确认密码不一致。')));
                return;
              }

              final trustedCertificates = <Map<String, String>>[];
              if (_trustedCertificatePemController.text.trim().isNotEmpty) {
                trustedCertificates.add(<String, String>{
                  'name': _trustedCertificateNameController.text.trim().isEmpty
                      ? 'machine-registration'
                      : _trustedCertificateNameController.text.trim(),
                  'certificatePem': _trustedCertificatePemController.text
                      .trim(),
                });
              }

              widget.controller.updateBaseUrl(_baseUrlController.text);
              try {
                await widget.controller.completeInitialization(
                  adminPassword: _adminPasswordController.text,
                  sessionSecret: _sessionSecretController.text,
                  totpCode: _totpCodeController.text.trim(),
                  defaultVhdKeyword: _defaultVhdController.text.trim(),
                  dbHost: _dbHostController.text.trim(),
                  dbPort: int.tryParse(_dbPortController.text.trim()) ?? 5432,
                  dbName: _dbNameController.text.trim(),
                  dbUser: _dbUserController.text.trim(),
                  dbPassword: _dbPasswordController.text,
                  trustedCertificates: trustedCertificates,
                );
                if (!context.mounted) {
                  return;
                }
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(content: Text('初始化完成，请使用管理员密码登录。')),
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
            icon: const Icon(Icons.lock_open_rounded),
            label: const Text('完成初始化'),
          ),
        ],
      ),
    );
  }

  Widget _buildFieldRow({Widget? left, Widget? right, Widget? child}) {
    if (child != null) {
      return Padding(padding: const EdgeInsets.only(bottom: 12), child: child);
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 720) {
            return Column(
              children: <Widget>[
                left ?? const SizedBox.shrink(),
                const SizedBox(height: 12),
                right ?? const SizedBox.shrink(),
              ],
            );
          }

          return Row(
            children: <Widget>[
              Expanded(child: left ?? const SizedBox.shrink()),
              const SizedBox(width: 12),
              Expanded(child: right ?? const SizedBox.shrink()),
            ],
          );
        },
      ),
    );
  }
}

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  late final TextEditingController _baseUrlController;
  late final TextEditingController _passwordController;

  @override
  void initState() {
    super.initState();
    _baseUrlController = TextEditingController(text: widget.controller.baseUrl);
    _passwordController = TextEditingController();
  }

  @override
  void dispose() {
    _baseUrlController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final serverStatus = widget.controller.serverStatus;
    return AuthShell(
      eyebrow: '安全登录',
      title: '回到管理台继续工作',
      subtitle: '',
      heroIcon: Icons.admin_panel_settings_rounded,
      spotlight: OverviewStatCard(
        label: '连接状态',
        value: serverStatus?.databaseReady == true ? '数据库已连接' : '等待检查',
        icon: Icons.storage_rounded,
        color: serverStatus?.databaseReady == true
            ? AppPalette.mint
            : AppPalette.sun,
        caption: serverStatus == null
            ? '先确认服务可达，再输入管理员密码。'
            : '默认关键词 ${serverStatus.defaultVhdKeyword} · 可信证书 ${serverStatus.trustedRegistrationCertificateCount}',
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text('管理员登录', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            '当前服务已初始化，管理操作通过 Session + OTP 二次验证保护。',
            style: Theme.of(
              context,
            ).textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
          ),
          if (widget.controller.errorMessage != null) ...<Widget>[
            const SizedBox(height: 16),
            ErrorBanner(message: widget.controller.errorMessage!),
          ],
          const SizedBox(height: 16),
          ServerAddressField(
            controller: _baseUrlController,
            rememberedBaseUrls: widget.controller.rememberedBaseUrls,
          ),
          const SizedBox(height: 12),
          buildSecureTextField(
            controller: _passwordController,
            autofillHints: const <String>[AutofillHints.password],
            decoration: const InputDecoration(labelText: '管理员密码'),
          ),
          const SizedBox(height: 18),
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: <Widget>[
              FilledButton.icon(
                onPressed: () async {
                  widget.controller.updateBaseUrl(_baseUrlController.text);
                  try {
                    await widget.controller.login(_passwordController.text);
                  } catch (error) {
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(content: Text(describeError(error))),
                    );
                  }
                },
                icon: const Icon(Icons.login_rounded),
                label: const Text('登录'),
              ),
              OutlinedButton.icon(
                onPressed: () async {
                  widget.controller.updateBaseUrl(_baseUrlController.text);
                  await widget.controller.bootstrap();
                },
                icon: const Icon(Icons.sync_rounded),
                label: const Text('刷新状态'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}