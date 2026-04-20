part of '../app.dart';

String describeError(Object error) {
  if (error is AdminApiException) {
    return error.message;
  }
  if (error is ClientConfigStoreException) {
    return error.message;
  }
  if (error is TimeoutException) {
    return '连接服务端超时，请稍后重试。';
  }
  if (error is SocketException) {
    final host = error.address?.host ?? '';
    if (host == 'localhost' || host == '127.0.0.1' || host == '::1') {
      return '无法连接到本机服务端。Android 模拟器请改用 10.0.2.2，真机请填写服务端的局域网 IP 或域名。';
    }
    return '无法连接到服务端，请检查服务器地址、端口和服务状态。';
  }
  if (error is HandshakeException || error is TlsException) {
    return '无法建立安全连接，请检查 HTTPS 地址或服务端证书配置。';
  }
  if (error is HttpException) {
    return '服务端响应异常，请稍后重试。';
  }
  if (error is FormatException) {
    return '服务端返回了无法识别的数据，请确认客户端与服务端版本匹配。';
  }

  final message = error.toString();
  if (message.contains('Connection refused') ||
      message.contains('Failed host lookup') ||
      message.contains('SocketException')) {
    return '无法连接到服务端，请检查服务器地址、端口和网络连接。';
  }
  if (message.contains('HandshakeException') || message.contains('CERTIFICATE')) {
    return '无法建立安全连接，请检查 HTTPS 地址或服务端证书配置。';
  }
  return '操作失败，请稍后重试。';
}

String generateSessionSecret([int length = 48]) {
  const alphabet =
      'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#\$%^&*()-_=+';
  final random = Random.secure();
  return List.generate(
    length,
    (_) => alphabet[random.nextInt(alphabet.length)],
  ).join();
}

String normalizeOtpauthUrl(String value) {
  final trimmed = value.trim();
  if (trimmed.isEmpty) {
    return '';
  }
  if (trimmed.startsWith('otpauth://')) {
    return trimmed;
  }
  if (trimmed.startsWith('otpauth:')) {
    return trimmed.replaceFirst(RegExp(r'^otpauth:/+'), 'otpauth://');
  }
  return trimmed;
}

String normalizeBaseUrl(String value) {
  final trimmed = value.trim();
  if (trimmed.isEmpty) {
    return '';
  }
  return trimmed.replaceAll(RegExp(r'/+$'), '');
}

String encodePathSegment(String value) {
  final trimmed = value.trim();
  if (trimmed.isEmpty) {
    return '';
  }
  return Uri.encodeComponent(trimmed);
}

const MethodChannel _windowsSecureInputChannel = MethodChannel(
  'vhd_mount_admin_flutter/secure_input',
);

void _syncWindowsSecureInput(bool enabled) {
  if (!Platform.isWindows) {
    return;
  }

  unawaited(() async {
    try {
      await _windowsSecureInputChannel.invokeMethod<void>(
        'setSecureInputEnabled',
        <String, bool>{'enabled': enabled},
      );
    } catch (_) {
      // 平台侧不可用时保持 Flutter 默认行为，不阻塞输入。
    }
  }());
}

String serverAddressInputHint() {
  if (Platform.isAndroid) {
    return '例如 http://10.0.2.2:8080 或 http://192.168.1.10:8080';
  }
  if (Platform.isIOS) {
    return '例如 http://localhost:8080 或 http://192.168.1.10:8080';
  }
  return '例如 http://localhost:8080';
}

String serverAddressConnectionTip() {
  if (Platform.isAndroid) {
    return '提示：Android 模拟器访问开发机服务通常使用 http://10.0.2.2:8080；真机请填写服务端的局域网 IP 或域名。';
  }
  if (Platform.isIOS) {
    return '提示：iOS 模拟器通常可直接访问开发机上的 http://localhost:8080；真机请填写服务端的局域网 IP 或域名。';
  }
  return '提示：如果你在本机联调，默认地址通常是 http://localhost:8080。';
}

class AppPalette {
  static const Color canvasWarm = Color(0xFFFFF7EA);
  static const Color canvasCool = Color(0xFFF2FFF8);
  static const Color canvasRose = Color(0xFFFFF1E8);
  static const Color surface = Color(0xFFFFFCF8);
  static const Color surfaceStrong = Color(0xFFFFFFFF);
  static const Color border = Color(0xFFE7DDCF);
  static const Color ink = Color(0xFF193239);
  static const Color muted = Color(0xFF6A7F85);
  static const Color mint = Color(0xFF26B38D);
  static const Color mintDeep = Color(0xFF0F7762);
  static const Color coral = Color(0xFFFF8F6C);
  static const Color coralDeep = Color(0xFFE06A47);
  static const Color sky = Color(0xFF66B7FF);
  static const Color sun = Color(0xFFF2C861);
  static const Color success = Color(0xFF1C9C75);
  static const Color warning = Color(0xFFCC8A17);
  static const Color danger = Color(0xFFC5574E);
  static const Color info = Color(0xFF3E80C9);
}

const String _miSansFamily100 = 'MiSans100';
const String _miSansFamily200 = 'MiSans200';
const String _miSansFamily300 = 'MiSans300';
const String _miSansFamily400 = 'MiSans400';
const String _miSansFamily500 = 'MiSans500';
const String _miSansFamily600 = 'MiSans600';
const String _miSansFamily700 = 'MiSans700';
const String _miSansFamily800 = 'MiSans800';

FontWeight _normalizeMiSansWeight(FontWeight weight) {
  if (weight == FontWeight.w100) {
    return FontWeight.w100;
  }
  if (weight == FontWeight.w200) {
    return FontWeight.w200;
  }
  if (weight == FontWeight.w300) {
    return FontWeight.w300;
  }
  if (weight == FontWeight.w400) {
    return FontWeight.w400;
  }
  if (weight == FontWeight.w500) {
    return FontWeight.w500;
  }
  if (weight == FontWeight.w600) {
    return FontWeight.w600;
  }
  if (weight == FontWeight.w700) {
    return FontWeight.w700;
  }
  return FontWeight.w800;
}

String _miSansFamilyFor(FontWeight weight) {
  switch (_normalizeMiSansWeight(weight)) {
    case FontWeight.w100:
      return _miSansFamily100;
    case FontWeight.w200:
      return _miSansFamily200;
    case FontWeight.w300:
      return _miSansFamily300;
    case FontWeight.w400:
      return _miSansFamily400;
    case FontWeight.w500:
      return _miSansFamily500;
    case FontWeight.w600:
      return _miSansFamily600;
    case FontWeight.w700:
      return _miSansFamily700;
    case FontWeight.w800:
    case FontWeight.w900:
      return _miSansFamily800;
  }

  return _miSansFamily400;
}

TextStyle miSansTextStyle({
  double? fontSize,
  FontWeight fontWeight = FontWeight.w400,
  double? height,
  Color? color,
  double? letterSpacing,
}) {
  final normalizedWeight = _normalizeMiSansWeight(fontWeight);
  return TextStyle(
    inherit: true,
    fontFamily: _miSansFamilyFor(normalizedWeight),
    fontWeight: normalizedWeight,
    fontSize: fontSize,
    height: height,
    color: color,
    letterSpacing: letterSpacing,
  );
}

class AdminBackdrop extends StatelessWidget {
  const AdminBackdrop({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            AppPalette.canvasWarm,
            AppPalette.canvasCool,
            AppPalette.canvasRose,
          ],
        ),
      ),
      child: Stack(
        children: <Widget>[
          const Positioned(
            top: -130,
            left: -70,
            child: _GlowOrb(size: 320, color: AppPalette.coral),
          ),
          const Positioned(
            top: 120,
            right: -90,
            child: _GlowOrb(size: 280, color: AppPalette.sky),
          ),
          const Positioned(
            bottom: -110,
            left: 180,
            child: _GlowOrb(size: 300, color: AppPalette.mint),
          ),
          child,
        ],
      ),
    );
  }
}

class _GlowOrb extends StatelessWidget {
  const _GlowOrb({required this.size, required this.color});

  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: <Color>[
              color.withValues(alpha: 0.28),
              color.withValues(alpha: 0.0),
            ],
          ),
        ),
      ),
    );
  }
}

class AppPanel extends StatelessWidget {
  const AppPanel({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(24),
    this.borderRadius = 30,
    this.gradient,
    this.fillColor,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double borderRadius;
  final Gradient? gradient;
  final Color? fillColor;

  @override
  Widget build(BuildContext context) {
    final baseColor = fillColor ?? AppPalette.surfaceStrong;
    return Container(
      decoration: BoxDecoration(
        gradient:
            gradient ??
            LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: <Color>[
                baseColor.withValues(alpha: 0.94),
                AppPalette.surface.withValues(alpha: 0.9),
              ],
            ),
        borderRadius: BorderRadius.circular(borderRadius),
        border: Border.all(
          color: Colors.white.withValues(alpha: 0.8),
          width: 1.1,
        ),
        boxShadow: <BoxShadow>[
          BoxShadow(
            color: AppPalette.ink.withValues(alpha: 0.08),
            blurRadius: 28,
            offset: const Offset(0, 18),
          ),
          BoxShadow(
            color: AppPalette.coral.withValues(alpha: 0.08),
            blurRadius: 40,
            offset: const Offset(-10, 16),
          ),
        ],
      ),
      child: Padding(padding: padding, child: child),
    );
  }
}

class AccentIconBadge extends StatelessWidget {
  const AccentIconBadge({
    super.key,
    required this.icon,
    required this.color,
    this.size = 48,
  });

  final IconData icon;
  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            color.withValues(alpha: 0.2),
            color.withValues(alpha: 0.08),
          ],
        ),
        borderRadius: BorderRadius.circular(size * 0.34),
        border: Border.all(color: color.withValues(alpha: 0.22)),
      ),
      child: Icon(icon, color: color),
    );
  }
}

class AuthShell extends StatelessWidget {
  const AuthShell({
    super.key,
    required this.eyebrow,
    required this.title,
    required this.subtitle,
    required this.child,
    this.heroIcon = Icons.auto_awesome_rounded,
    this.spotlight,
    this.maxWidth = 1280,
  });

  final String eyebrow;
  final String title;
  final String subtitle;
  final Widget child;
  final IconData heroIcon;
  final Widget? spotlight;
  final double maxWidth;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: AdminBackdrop(
        child: SafeArea(
          child: Center(
            child: ConstrainedBox(
              constraints: BoxConstraints(maxWidth: maxWidth),
              child: LayoutBuilder(
                builder: (context, viewportConstraints) {
                  final shellPadding = viewportConstraints.maxWidth < 460
                      ? 12.0
                      : viewportConstraints.maxWidth < 720
                      ? 16.0
                      : 24.0;

                  return Padding(
                    padding: EdgeInsets.all(shellPadding),
                    child: LayoutBuilder(
                      builder: (context, constraints) {
                        final compact =
                            constraints.maxWidth < 1040 ||
                            constraints.maxHeight < 780;
                        final panelPadding = compact
                            ? constraints.maxWidth < 460
                                  ? 18.0
                                  : 22.0
                            : 28.0;
                        final hero = _AuthHeroPanel(
                          eyebrow: eyebrow,
                          title: title,
                          subtitle: subtitle,
                          heroIcon: heroIcon,
                          spotlight: spotlight,
                        );
                        final content = AppPanel(
                          padding: EdgeInsets.all(panelPadding),
                          child: child,
                        );

                        return SingleChildScrollView(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: <Widget>[
                              hero,
                              SizedBox(height: compact ? 20 : 24),
                              content,
                            ],
                          ),
                        );
                      },
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _AuthHeroPanel extends StatelessWidget {
  const _AuthHeroPanel({
    required this.eyebrow,
    required this.title,
    required this.subtitle,
    required this.heroIcon,
    this.spotlight,
  });

  final String eyebrow;
  final String title;
  final String subtitle;
  final IconData heroIcon;
  final Widget? spotlight;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hasSubtitle = subtitle.trim().isNotEmpty;

    return LayoutBuilder(
      builder: (context, constraints) {
        final mobile = constraints.maxWidth < 720;
        final narrow = constraints.maxWidth < 430;
        final ultraNarrow = constraints.maxWidth < 320;
        final short =
            constraints.maxHeight.isFinite && constraints.maxHeight < 620;
        final compactHero = mobile || short;
        final panelPadding = ultraNarrow
          ? 16.0
          : narrow
          ? 18.0
          : mobile
          ? 22.0
          : 30.0;
        final titleFontSize = ultraNarrow
          ? 23.0
          : narrow
          ? 26.0
          : mobile
          ? 31.0
          : 38.0;
        final iconSize = ultraNarrow
          ? 42.0
          : compactHero
          ? 48.0
          : 56.0;

        return AppPanel(
          padding: EdgeInsets.all(panelPadding),
          gradient: const LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: <Color>[
              Color(0xFFFFF4E8),
              Color(0xFFF4FFF8),
              Color(0xFFEAF6FF),
            ],
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 8,
                ),
                decoration: BoxDecoration(
                  color: AppPalette.ink.withValues(alpha: 0.08),
                  borderRadius: BorderRadius.circular(999),
                ),
                child: Text(
                  eyebrow,
                  style: theme.textTheme.labelLarge?.copyWith(
                    color: AppPalette.ink,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 0.3,
                  ),
                ),
              ),
              SizedBox(height: short ? 16 : 24),
              if (narrow)
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    AccentIconBadge(
                      icon: heroIcon,
                      color: AppPalette.coral,
                      size: iconSize,
                    ),
                    SizedBox(height: compactHero ? 12 : 16),
                    Text(
                      title,
                      style: theme.textTheme.displaySmall?.copyWith(
                        fontSize: titleFontSize,
                      ),
                    ),
                  ],
                )
              else
                Row(
                  children: <Widget>[
                    AccentIconBadge(
                      icon: heroIcon,
                      color: AppPalette.coral,
                      size: iconSize,
                    ),
                    SizedBox(width: compactHero ? 12 : 16),
                    Expanded(
                      child: Text(
                        title,
                        style: theme.textTheme.displaySmall?.copyWith(
                          fontSize: titleFontSize,
                        ),
                      ),
                    ),
                  ],
                ),
              if (hasSubtitle) ...<Widget>[
                SizedBox(height: compactHero ? 10 : 14),
                Text(
                  subtitle,
                  style: (compactHero
                          ? theme.textTheme.bodyMedium
                          : theme.textTheme.bodyLarge)
                      ?.copyWith(
                    color: AppPalette.muted,
                    height: compactHero ? 1.55 : 1.65,
                  ),
                ),
              ],
              if (spotlight != null) ...<Widget>[
                SizedBox(height: compactHero ? 14 : 24),
                if (mobile)
                  SizedBox(height: 104, child: spotlight!)
                else
                  spotlight!,
              ],
            ],
          ),
        );
      },
    );
  }
}

class PageHeader extends StatelessWidget {
  const PageHeader({
    super.key,
    required this.eyebrow,
    required this.title,
    required this.subtitle,
    this.actions = const <Widget>[],
  });

  final String eyebrow;
  final String title;
  final String subtitle;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hasSubtitle = subtitle.trim().isNotEmpty;

    Widget heading = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Text(
          eyebrow,
          style: theme.textTheme.labelLarge?.copyWith(
            color: AppPalette.coralDeep,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.4,
          ),
        ),
        const SizedBox(height: 6),
        Text(title, style: theme.textTheme.headlineMedium),
        if (hasSubtitle) ...<Widget>[
          const SizedBox(height: 8),
          Text(
            subtitle,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: AppPalette.muted,
              height: 1.55,
            ),
          ),
        ],
      ],
    );

    if (actions.isEmpty) {
      return heading;
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < 760;
        final actionBar = OverflowBar(
          spacing: 10,
          overflowSpacing: 10,
          alignment: MainAxisAlignment.end,
          overflowAlignment: OverflowBarAlignment.end,
          children: actions,
        );

        if (compact) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              heading,
              const SizedBox(height: 16),
              Align(alignment: Alignment.centerRight, child: actionBar),
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Expanded(child: heading),
            const SizedBox(width: 16),
            Expanded(
              child: Align(alignment: Alignment.topRight, child: actionBar),
            ),
          ],
        );
      },
    );
  }
}

class OverviewStatCard extends StatelessWidget {
  const OverviewStatCard({
    super.key,
    required this.label,
    required this.value,
    required this.icon,
    required this.color,
    this.caption,
  });

  final String label;
  final String value;
  final IconData icon;
  final Color color;
  final String? caption;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return LayoutBuilder(
      builder: (context, constraints) {
        final compactCard =
        constraints.maxHeight < 108 || constraints.maxWidth < 180;
        final dense =
            compactCard ||
            constraints.maxHeight < 140 ||
            constraints.maxWidth < 236;
        final panelPadding = compactCard
        ? 10.0
            : (dense ? 14.0 : 18.0);
        final iconSize = compactCard
        ? 36.0
            : (dense ? 44.0 : 52.0);
        final contentGap = compactCard
        ? 6.0
            : (dense ? 10.0 : 14.0);
        final lineGap = compactCard ? 0.0 : (dense ? 2.0 : 4.0);

        return AppPanel(
        borderRadius: compactCard ? 20 : 26,
          padding: EdgeInsets.all(panelPadding),
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: <Color>[
              Colors.white.withValues(alpha: 0.96),
              color.withValues(alpha: 0.08),
            ],
          ),
          child: Row(
            children: <Widget>[
              AccentIconBadge(icon: icon, color: color, size: iconSize),
              SizedBox(width: contentGap),
              Expanded(
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: (compactCard
                              ? theme.textTheme.bodySmall
                              : theme.textTheme.labelLarge)
                          ?.copyWith(
                            color: AppPalette.muted,
                            fontWeight: compactCard
                                ? FontWeight.w600
                                : FontWeight.w500,
                          ),
                    ),
                    SizedBox(height: lineGap),
                    Text(
                      value,
                      maxLines: dense ? 1 : 2,
                      overflow: TextOverflow.ellipsis,
                      style:
                          (compactCard
                                  ? theme.textTheme.titleMedium
                                  : dense
                                  ? theme.textTheme.titleMedium
                                  : theme.textTheme.titleLarge)
                              ?.copyWith(
                                color: AppPalette.ink,
                                fontWeight: FontWeight.w700,
                                height: compactCard ? 1.1 : null,
                              ),
                    ),
                    if (caption != null) ...<Widget>[
                      SizedBox(height: lineGap),
                      Text(
                        caption!,
                        maxLines: dense ? 1 : 2,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: AppPalette.muted,
                          height: dense ? 1.25 : 1.45,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class OverviewStatsGrid extends StatelessWidget {
  const OverviewStatsGrid({
    super.key,
    required this.cards,
    this.singleRow = false,
    this.forceTwoColumnGrid = false,
  });

  final List<Widget> cards;
  final bool singleRow;
  final bool forceTwoColumnGrid;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final spacing = forceTwoColumnGrid ? 10.0 : 14.0;
        final availableWidth = constraints.maxWidth;

        if (forceTwoColumnGrid) {
          final columns = min(2, max(1, cards.length));
          final cardWidth = (availableWidth - spacing * (columns - 1)) / columns;
          final cardHeight = min(104.0, max(88.0, cardWidth * 0.54));
          final rows = (cards.length / columns).ceil();
          final totalHeight = rows * cardHeight + (rows - 1) * spacing;

          return SizedBox(
            height: totalHeight,
            child: GridView.builder(
              physics: const NeverScrollableScrollPhysics(),
              padding: EdgeInsets.zero,
              gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                crossAxisCount: columns,
                mainAxisSpacing: spacing,
                crossAxisSpacing: spacing,
                childAspectRatio: cardWidth / cardHeight,
              ),
              itemCount: cards.length,
              itemBuilder: (context, index) =>
                  SizedBox.expand(child: cards[index]),
            ),
          );
        }

        if (singleRow) {
          final visibleCards = min<double>(
            cards.length.toDouble(),
            max(1.45, availableWidth / 230.0),
          );
          final cardWidth = max(
            188.0,
            (availableWidth - spacing * (visibleCards - 1)) / visibleCards,
          );
          final cardHeight = min(132.0, max(116.0, cardWidth * 0.46));

          return SizedBox(
            height: cardHeight,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding: EdgeInsets.zero,
              itemCount: cards.length,
              separatorBuilder: (_, _) => SizedBox(width: spacing),
              itemBuilder: (context, index) =>
                  SizedBox(width: cardWidth, child: cards[index]),
            ),
          );
        }

        final estimatedColumns = ((availableWidth + spacing) / (220 + spacing))
            .floor();
        final columns = max(1, min(cards.length, estimatedColumns));
        final cardWidth = (availableWidth - spacing * (columns - 1)) / columns;
        final cardHeight = min(196.0, max(148.0, cardWidth * 0.56));
        final rows = (cards.length / columns).ceil();
        final totalHeight = rows * cardHeight + (rows - 1) * spacing;

        return SizedBox(
          height: totalHeight,
          child: GridView.builder(
            physics: const NeverScrollableScrollPhysics(),
            padding: EdgeInsets.zero,
            gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: columns,
              mainAxisSpacing: spacing,
              crossAxisSpacing: spacing,
              childAspectRatio: cardWidth / cardHeight,
            ),
            itemCount: cards.length,
            itemBuilder: (context, index) =>
                SizedBox.expand(child: cards[index]),
          ),
        );
      },
    );
  }
}

class DashboardDestinationSpec {
  const DashboardDestinationSpec({
    required this.label,
    required this.subtitle,
    required this.icon,
    required this.color,
  });

  final String label;
  final String subtitle;
  final IconData icon;
  final Color color;
}

class DashboardSidebarButton extends StatelessWidget {
  const DashboardSidebarButton({
    super.key,
    required this.label,
    required this.subtitle,
    required this.icon,
    required this.color,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final String subtitle;
  final IconData icon;
  final Color color;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 92),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(24),
          onTap: onTap,
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 220),
            curve: Curves.easeOutCubic,
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: selected
                    ? <Color>[
                        color.withValues(alpha: 0.18),
                        Colors.white.withValues(alpha: 0.96),
                      ]
                    : <Color>[
                        Colors.white.withValues(alpha: 0.76),
                        Colors.white.withValues(alpha: 0.48),
                      ],
              ),
              borderRadius: BorderRadius.circular(24),
              border: Border.all(
                color: selected
                    ? color.withValues(alpha: 0.28)
                    : AppPalette.border.withValues(alpha: 0.78),
              ),
              boxShadow: selected
                  ? <BoxShadow>[
                      BoxShadow(
                        color: color.withValues(alpha: 0.12),
                        blurRadius: 24,
                        offset: const Offset(0, 12),
                      ),
                    ]
                  : const <BoxShadow>[],
            ),
            child: Row(
              children: <Widget>[
                AccentIconBadge(icon: icon, color: color, size: 52),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        label,
                        style: theme.textTheme.titleMedium?.copyWith(
                          color: selected ? color : AppPalette.ink,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        subtitle,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: selected ? AppPalette.ink : AppPalette.muted,
                          height: 1.45,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                Icon(
                  selected
                      ? Icons.arrow_forward_rounded
                      : Icons.chevron_right_rounded,
                  color: selected ? color : AppPalette.muted,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class SecureTextField extends StatefulWidget {
  const SecureTextField({
    super.key,
    required this.controller,
    required this.decoration,
    this.autofocus = false,
    this.autofillHints,
  });

  final TextEditingController controller;
  final InputDecoration decoration;
  final bool autofocus;
  final Iterable<String>? autofillHints;

  @override
  State<SecureTextField> createState() => _SecureTextFieldState();
}

class _SecureTextFieldState extends State<SecureTextField> {
  late final FocusNode _focusNode;

  @override
  void initState() {
    super.initState();
    _focusNode = FocusNode();
    _focusNode.addListener(_handleFocusChanged);
  }

  void _handleFocusChanged() {
    _syncWindowsSecureInput(_focusNode.hasFocus);
  }

  @override
  void dispose() {
    if (_focusNode.hasFocus) {
      _syncWindowsSecureInput(false);
    }
    _focusNode.removeListener(_handleFocusChanged);
    _focusNode.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: widget.controller,
      focusNode: _focusNode,
      autofocus: widget.autofocus,
      obscureText: true,
      keyboardType: TextInputType.text,
      autocorrect: false,
      enableSuggestions: false,
      enableIMEPersonalizedLearning: false,
      smartDashesType: SmartDashesType.disabled,
      smartQuotesType: SmartQuotesType.disabled,
      autofillHints: widget.autofillHints,
      decoration: widget.decoration,
    );
  }
}

Widget buildSecureTextField({
  required TextEditingController controller,
  required InputDecoration decoration,
  bool autofocus = false,
  Iterable<String>? autofillHints,
}) {
  return SecureTextField(
    controller: controller,
    autofocus: autofocus,
    decoration: decoration,
    autofillHints: autofillHints,
  );
}