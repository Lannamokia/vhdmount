import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:path_provider/path_provider.dart';
import 'package:qr_flutter/qr_flutter.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(AdminApp(controller: AppController(api: HttpAdminApi())));
}

String describeError(Object error) {
  if (error is AdminApiException) {
    return error.message;
  }
  return error.toString();
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
        final narrow = constraints.maxWidth < 430;
        final ultraNarrow = constraints.maxWidth < 320;
        final short =
            constraints.maxHeight.isFinite && constraints.maxHeight < 620;
        final panelPadding = ultraNarrow
            ? 18.0
            : narrow
            ? 22.0
            : 30.0;
        final titleFontSize = ultraNarrow
            ? 28.0
            : narrow
            ? 32.0
            : 38.0;

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
                      size: ultraNarrow ? 48 : 56,
                    ),
                    const SizedBox(height: 16),
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
                      size: 56,
                    ),
                    const SizedBox(width: 16),
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
                SizedBox(height: short ? 10 : 14),
                Text(
                  subtitle,
                  style: theme.textTheme.bodyLarge?.copyWith(
                    color: AppPalette.muted,
                    height: 1.65,
                  ),
                ),
              ],
              if (spotlight != null) ...<Widget>[
                SizedBox(height: short ? 16 : 24),
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
        final dense = constraints.maxHeight < 140 || constraints.maxWidth < 236;
        final panelPadding = dense ? 14.0 : 18.0;
        final iconSize = dense ? 44.0 : 52.0;
        final contentGap = dense ? 10.0 : 14.0;
        final lineGap = dense ? 2.0 : 4.0;

        return AppPanel(
          borderRadius: 26,
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
                      style: theme.textTheme.labelLarge?.copyWith(
                        color: AppPalette.muted,
                      ),
                    ),
                    SizedBox(height: lineGap),
                    Text(
                      value,
                      maxLines: dense ? 1 : 2,
                      overflow: TextOverflow.ellipsis,
                      style:
                          (dense
                                  ? theme.textTheme.titleMedium
                                  : theme.textTheme.titleLarge)
                              ?.copyWith(
                                color: AppPalette.ink,
                                fontWeight: FontWeight.w700,
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
  });

  final List<Widget> cards;
  final bool singleRow;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        const spacing = 14.0;
        final availableWidth = constraints.maxWidth;

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
              separatorBuilder: (_, _) => const SizedBox(width: spacing),
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

const Set<String> _auditReservedKeys = <String>{
  'timestamp',
  'type',
  'actor',
  'result',
  'path',
  'ip',
};

class AuditEventPresentation {
  const AuditEventPresentation({
    required this.title,
    required this.description,
  });

  final String title;
  final String description;
}

String formatAuditTimestamp(String value) {
  final parsed = DateTime.tryParse(value);
  if (parsed == null) {
    return value.isEmpty ? '未知时间' : value;
  }

  final local = parsed.toLocal();
  final offset = local.timeZoneOffset;
  final sign = offset.isNegative ? '-' : '+';
  final offsetHours = offset.inHours.abs().toString().padLeft(2, '0');
  final offsetMinutes = (offset.inMinutes.abs() % 60).toString().padLeft(
    2,
    '0',
  );

  return '${local.year.toString().padLeft(4, '0')}-'
      '${local.month.toString().padLeft(2, '0')}-'
      '${local.day.toString().padLeft(2, '0')} '
      '${local.hour.toString().padLeft(2, '0')}:'
      '${local.minute.toString().padLeft(2, '0')}:'
      '${local.second.toString().padLeft(2, '0')} '
      'UTC$sign$offsetHours:$offsetMinutes';
}

String describeAuditActor(String actor) {
  switch (actor) {
    case 'admin':
      return '管理员';
    case 'machine':
      return '机台';
    case 'bootstrap':
      return '初始化流程';
    default:
      return actor.isEmpty ? '未知主体' : actor;
  }
}

String describeAuditResult(String result) {
  switch (result) {
    case 'success':
      return '成功';
    case 'failure':
      return '失败';
    default:
      return result.isEmpty ? '未知结果' : result;
  }
}

String normalizeAuditIp(String ip) {
  final trimmed = ip.trim();
  if (trimmed.isEmpty) {
    return '未知地址';
  }
  if (trimmed.startsWith('::ffff:')) {
    return trimmed.substring('::ffff:'.length);
  }
  return trimmed;
}

String _auditMetadataText(Map<String, dynamic> metadata, String key) {
  final value = metadata[key];
  if (value == null) {
    return '';
  }
  return value.toString().trim();
}

bool? _auditMetadataBool(Map<String, dynamic> metadata, String key) {
  final value = metadata[key];
  if (value is bool) {
    return value;
  }
  if (value is String) {
    if (value == 'true') {
      return true;
    }
    if (value == 'false') {
      return false;
    }
  }
  return null;
}

String _shortAuditValue(String value, {int edge = 8}) {
  if (value.length <= edge * 2 + 3) {
    return value;
  }
  return '${value.substring(0, edge)}...${value.substring(value.length - edge)}';
}

AuditEventPresentation describeAuditEvent(AuditEntry entry) {
  final machineId = _auditMetadataText(entry.metadata, 'machineId');
  final keyword = _auditMetadataText(entry.metadata, 'vhdKeyword').isNotEmpty
      ? _auditMetadataText(entry.metadata, 'vhdKeyword')
      : _auditMetadataText(entry.metadata, 'defaultVhdKeyword');
  final reason = _auditMetadataText(entry.metadata, 'reason');
  final fingerprint =
      _auditMetadataText(entry.metadata, 'fingerprint256').isNotEmpty
      ? _auditMetadataText(entry.metadata, 'fingerprint256')
      : _auditMetadataText(entry.metadata, 'registrationCertFingerprint');
  final protectedState = _auditMetadataBool(entry.metadata, 'protected');
  final approved = _auditMetadataBool(entry.metadata, 'approved');

  switch (entry.type) {
    case 'init.prepare':
      return const AuditEventPresentation(
        title: '准备初始化服务',
        description: '已生成首次初始化所需的 OTP 信息与安全配置草稿。',
      );
    case 'init.complete':
      final certCount = _auditMetadataText(
        entry.metadata,
        'trustedRegistrationCertificateCount',
      );
      return AuditEventPresentation(
        title: '完成服务初始化',
        description: certCount.isEmpty
            ? '服务已完成初始化并写入安全配置。'
            : '服务已完成初始化，并载入 $certCount 个可信注册证书。',
      );
    case 'auth.login':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '管理员登录成功' : '管理员登录失败',
        description: entry.result == 'success'
            ? '管理员凭证校验通过，已建立登录会话。'
            : '管理员凭证校验失败，登录被拒绝。',
      );
    case 'auth.change-password':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '管理员密码已修改' : '管理员密码修改失败',
        description: entry.result == 'success'
            ? '管理员密码已更新，旧会话的高敏感验证状态已清除。'
            : '密码变更时当前密码校验失败。',
      );
    case 'auth.otp.verify':
      return AuditEventPresentation(
        title: entry.result == 'success' ? 'OTP 二次验证通过' : 'OTP 二次验证失败',
        description: entry.result == 'success'
            ? '高敏感操作窗口已开启。'
            : '提供的 OTP 验证码未通过校验。',
      );
    case 'auth.otp.rotate.prepare':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '开始更换 OTP 绑定密钥' : '旧 OTP 校验失败',
        description: entry.result == 'success'
            ? '已通过旧 OTP 校验，并生成新的 OTP 绑定密钥等待确认。'
            : '提供的旧 OTP 验证码未通过校验，无法开始更换流程。',
      );
    case 'auth.otp.rotate.complete':
      return AuditEventPresentation(
        title: entry.result == 'success' ? 'OTP 绑定密钥已更换' : '新 OTP 校验失败',
        description: entry.result == 'success'
            ? '新的 OTP 绑定密钥已验证通过并替换原有绑定。'
            : '使用新绑定密钥生成的 OTP 验证码未通过校验，原绑定保持不变。',
      );
    case 'settings.default-vhd.update':
      return AuditEventPresentation(
        title: '默认启动关键词已更新',
        description: keyword.isEmpty
            ? '服务默认使用的启动关键词已被修改。'
            : '服务默认使用的启动关键词已更新为 $keyword。',
      );
    case 'machine.protection.update':
      return AuditEventPresentation(
        title: '机台保护状态已更新',
        description: machineId.isEmpty
            ? '机台保护状态已被修改。'
            : '机台 $machineId 的保护状态已切换为${protectedState == true ? '开启' : '关闭'}。',
      );
    case 'machine.create':
      return AuditEventPresentation(
        title: '已新增机台',
        description: machineId.isEmpty
            ? '已创建新的机台记录。'
            : keyword.isEmpty
            ? '已创建机台 $machineId。'
            : '已创建机台 $machineId，默认启动关键词为 $keyword。',
      );
    case 'machine.registration.submit':
      if (entry.result != 'success') {
        return AuditEventPresentation(
          title: '机台公钥注册失败',
          description: machineId.isEmpty
              ? (reason.isEmpty ? '机台提交公钥注册失败。' : '机台提交公钥注册失败，原因：$reason。')
              : (reason.isEmpty
                    ? '机台 $machineId 提交公钥注册失败。'
                    : '机台 $machineId 提交公钥注册失败，原因：$reason。'),
        );
      }
      return AuditEventPresentation(
        title: '机台提交公钥注册',
        description: machineId.isEmpty
            ? '收到新的机台公钥注册请求，等待管理员审批。'
            : fingerprint.isEmpty
            ? '机台 $machineId 已提交公钥注册，等待管理员审批。'
            : '机台 $machineId 已提交公钥注册，请求由证书 ${_shortAuditValue(fingerprint)} 签名。',
      );
    case 'machine.approval.update':
      return AuditEventPresentation(
        title: approved == false ? '已取消机台审批' : '已审批机台公钥',
        description: machineId.isEmpty
            ? '机台公钥审批状态已更新。'
            : approved == false
            ? '机台 $machineId 的公钥审批已取消。'
            : '机台 $machineId 的公钥审批已通过。',
      );
    case 'machine.registration.reset':
      return AuditEventPresentation(
        title: '已重置机台注册状态',
        description: machineId.isEmpty
            ? '机台的注册密钥与审批状态已被清空。'
            : '机台 $machineId 的注册密钥与审批状态已被清空。',
      );
    case 'machine.vhd.update':
      return AuditEventPresentation(
        title: '机台启动关键词已更新',
        description: machineId.isEmpty
            ? '机台启动关键词已被修改。'
            : keyword.isEmpty
            ? '机台 $machineId 的启动关键词已更新。'
            : '机台 $machineId 的启动关键词已更新为 $keyword。',
      );
    case 'machine.evhd-password.update':
      return AuditEventPresentation(
        title: '机台 EVHD 密码已更新',
        description: machineId.isEmpty
            ? '机台 EVHD 密码已被修改。'
            : '机台 $machineId 的 EVHD 密码已更新。',
      );
    case 'machine.evhd-password.read':
      return AuditEventPresentation(
        title: '读取机台 EVHD 明文密码',
        description: machineId.isEmpty
            ? (reason.isEmpty
                  ? '管理员读取了 EVHD 明文密码。'
                  : '管理员读取了 EVHD 明文密码，原因：$reason。')
            : (reason.isEmpty
                  ? '管理员读取了机台 $machineId 的 EVHD 明文密码。'
                  : '管理员读取了机台 $machineId 的 EVHD 明文密码，原因：$reason。'),
      );
    case 'machine.evhd-envelope.read':
      return AuditEventPresentation(
        title: entry.result == 'success' ? '机台获取 EVHD 密文成功' : '机台获取 EVHD 密文失败',
        description: machineId.isEmpty
            ? (entry.result == 'success'
                  ? '机台已通过鉴权并获取 EVHD 密文信封。'
                  : (reason.isEmpty
                        ? '机台获取 EVHD 密文失败。'
                        : '机台获取 EVHD 密文失败，原因：$reason。'))
            : (entry.result == 'success'
                  ? '机台 $machineId 已通过鉴权并获取 EVHD 密文信封。'
                  : (reason.isEmpty
                        ? '机台 $machineId 获取 EVHD 密文失败。'
                        : '机台 $machineId 获取 EVHD 密文失败，原因：$reason。')),
      );
    case 'machine.delete':
      return AuditEventPresentation(
        title: '已删除机台',
        description: machineId.isEmpty ? '机台记录已被删除。' : '机台 $machineId 的记录已被删除。',
      );
    case 'security.trusted-certificate.add':
      return AuditEventPresentation(
        title: '已添加可信注册证书',
        description: fingerprint.isEmpty
            ? '可信注册证书列表已新增一项。'
            : '已添加可信注册证书 ${_shortAuditValue(fingerprint)}。',
      );
    case 'security.trusted-certificate.remove':
      return AuditEventPresentation(
        title: '已移除可信注册证书',
        description: fingerprint.isEmpty
            ? '可信注册证书列表已移除一项。'
            : '已移除可信注册证书 ${_shortAuditValue(fingerprint)}。',
      );
    default:
      return AuditEventPresentation(
        title: entry.type.isEmpty ? '未命名审计事件' : entry.type,
        description: '暂未配置该事件的中文说明。',
      );
  }
}

List<String> mergeRememberedBaseUrls(
  Iterable<String> existing, {
  String? preferredFirst,
  int maxItems = 8,
}) {
  final merged = <String>[];

  void addUrl(String value) {
    final normalized = normalizeBaseUrl(value);
    if (normalized.isEmpty || merged.contains(normalized)) {
      return;
    }
    merged.add(normalized);
  }

  if (preferredFirst != null) {
    addUrl(preferredFirst);
  }

  for (final value in existing) {
    addUrl(value);
    if (merged.length >= maxItems) {
      break;
    }
  }

  return merged;
}

class AdminApiException implements Exception {
  AdminApiException(
    this.message, {
    this.statusCode,
    this.requireAuth = false,
    this.requireOtp = false,
    this.initializeRequired = false,
  });

  final String message;
  final int? statusCode;
  final bool requireAuth;
  final bool requireOtp;
  final bool initializeRequired;

  @override
  String toString() => message;
}

class ServerStatus {
  const ServerStatus({
    required this.initialized,
    required this.pendingInitialization,
    required this.databaseReady,
    required this.defaultVhdKeyword,
    required this.trustedRegistrationCertificateCount,
  });

  final bool initialized;
  final bool pendingInitialization;
  final bool databaseReady;
  final String defaultVhdKeyword;
  final int trustedRegistrationCertificateCount;

  factory ServerStatus.fromJson(Map<String, dynamic> json) {
    return ServerStatus(
      initialized: json['initialized'] == true,
      pendingInitialization: json['pendingInitialization'] == true,
      databaseReady: json['databaseReady'] == true,
      defaultVhdKeyword: (json['defaultVhdKeyword'] as String?) ?? 'SDEZ',
      trustedRegistrationCertificateCount:
          (json['trustedRegistrationCertificateCount'] as num?)?.toInt() ?? 0,
    );
  }
}

class AuthStatus {
  const AuthStatus({
    required this.initialized,
    required this.isAuthenticated,
    required this.otpVerified,
  });

  final bool initialized;
  final bool isAuthenticated;
  final bool otpVerified;

  factory AuthStatus.fromJson(Map<String, dynamic> json) {
    return AuthStatus(
      initialized: json['initialized'] == true,
      isAuthenticated: json['isAuthenticated'] == true,
      otpVerified: json['otpVerified'] == true,
    );
  }
}

class InitializationPreparation {
  const InitializationPreparation({
    required this.issuer,
    required this.accountName,
    required this.totpSecret,
    required this.otpauthUrl,
  });

  final String issuer;
  final String accountName;
  final String totpSecret;
  final String otpauthUrl;

  factory InitializationPreparation.fromJson(Map<String, dynamic> json) {
    return InitializationPreparation(
      issuer: (json['issuer'] as String?) ?? 'VHDMountServer',
      accountName: (json['accountName'] as String?) ?? 'admin',
      totpSecret: (json['totpSecret'] as String?) ?? '',
      otpauthUrl: (json['otpauthUrl'] as String?) ?? '',
    );
  }
}

class OtpStatus {
  const OtpStatus({required this.verified, required this.verifiedUntil});

  final bool verified;
  final int verifiedUntil;

  factory OtpStatus.fromJson(Map<String, dynamic> json) {
    final verifiedUntil = (json['otpVerifiedUntil'] as num?)?.toInt() ?? 0;
    return OtpStatus(
      verified:
          json['otpVerified'] == true ||
          verifiedUntil > DateTime.now().millisecondsSinceEpoch,
      verifiedUntil: verifiedUntil,
    );
  }
}

class ClientConfig {
  const ClientConfig({
    this.lastBaseUrl = '',
    this.serverHistory = const <String>[],
  });

  final String lastBaseUrl;
  final List<String> serverHistory;

  factory ClientConfig.fromJson(Map<String, dynamic> json) {
    final rawHistory =
        (json['serverHistory'] as List<dynamic>? ?? const <dynamic>[])
            .map((value) => value.toString())
            .toList();
    final lastBaseUrl = normalizeBaseUrl(
      (json['lastBaseUrl'] as String?) ??
          (rawHistory.isNotEmpty ? rawHistory.first : ''),
    );
    return ClientConfig(
      lastBaseUrl: lastBaseUrl,
      serverHistory: mergeRememberedBaseUrls(
        rawHistory,
        preferredFirst: lastBaseUrl,
      ),
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'lastBaseUrl': lastBaseUrl,
      'serverHistory': serverHistory,
    };
  }
}

abstract class ClientConfigStore {
  Future<ClientConfig> load();

  Future<void> save(ClientConfig config);
}

class FileClientConfigStore implements ClientConfigStore {
  FileClientConfigStore({
    Future<Directory> Function()? directoryProvider,
    this.fileName = 'vhd_mount_admin_client.json',
  }) : _directoryProvider = directoryProvider ?? getApplicationSupportDirectory;

  final Future<Directory> Function() _directoryProvider;
  final String fileName;

  Future<File> _getConfigFile() async {
    final directory = await _directoryProvider();
    return File('${directory.path}${Platform.pathSeparator}$fileName');
  }

  @override
  Future<ClientConfig> load() async {
    try {
      final file = await _getConfigFile();
      if (!await file.exists()) {
        return const ClientConfig();
      }

      final content = await file.readAsString();
      if (content.trim().isEmpty) {
        return const ClientConfig();
      }

      final decoded = jsonDecode(content);
      if (decoded is Map<String, dynamic>) {
        return ClientConfig.fromJson(decoded);
      }
    } catch (_) {
      return const ClientConfig();
    }

    return const ClientConfig();
  }

  @override
  Future<void> save(ClientConfig config) async {
    final file = await _getConfigFile();
    await file.parent.create(recursive: true);
    await file.writeAsString(
      '${const JsonEncoder.withIndent('  ').convert(config.toJson())}\n',
    );
  }
}

class MachineRecord {
  const MachineRecord({
    required this.machineId,
    required this.protectedState,
    required this.vhdKeyword,
    required this.evhdPasswordConfigured,
    required this.approved,
    required this.revoked,
    required this.keyId,
    required this.keyType,
    required this.registrationCertFingerprint,
    required this.lastSeen,
  });

  final String machineId;
  final bool protectedState;
  final String vhdKeyword;
  final bool evhdPasswordConfigured;
  final bool approved;
  final bool revoked;
  final String? keyId;
  final String? keyType;
  final String? registrationCertFingerprint;
  final String? lastSeen;

  factory MachineRecord.fromJson(Map<String, dynamic> json) {
    return MachineRecord(
      machineId: (json['machine_id'] as String?) ?? '',
      protectedState: json['protected'] == true,
      vhdKeyword: (json['vhd_keyword'] as String?) ?? 'SDEZ',
      evhdPasswordConfigured: json['evhd_password_configured'] == true,
      approved: json['approved'] == true,
      revoked: json['revoked'] == true,
      keyId: json['key_id'] as String?,
      keyType: json['key_type'] as String?,
      registrationCertFingerprint:
          json['registration_cert_fingerprint'] as String?,
      lastSeen: json['last_seen'] as String?,
    );
  }
}

class MachineDraft {
  const MachineDraft({
    required this.machineId,
    required this.vhdKeyword,
    required this.protectedState,
    this.evhdPassword,
  });

  final String machineId;
  final String vhdKeyword;
  final bool protectedState;
  final String? evhdPassword;
}

class TrustedCertificateRecord {
  const TrustedCertificateRecord({
    required this.name,
    required this.fingerprint256,
    required this.subject,
    required this.validFrom,
    required this.validTo,
    required this.certificatePem,
  });

  final String name;
  final String fingerprint256;
  final String subject;
  final String validFrom;
  final String validTo;
  final String certificatePem;

  factory TrustedCertificateRecord.fromJson(Map<String, dynamic> json) {
    return TrustedCertificateRecord(
      name: (json['name'] as String?) ?? 'unnamed-certificate',
      fingerprint256: (json['fingerprint256'] as String?) ?? '',
      subject: (json['subject'] as String?) ?? '',
      validFrom: (json['validFrom'] as String?) ?? '',
      validTo: (json['validTo'] as String?) ?? '',
      certificatePem: (json['certificatePem'] as String?) ?? '',
    );
  }
}

class AuditEntry {
  const AuditEntry({
    required this.timestamp,
    required this.type,
    required this.actor,
    required this.result,
    required this.path,
    required this.ip,
    required this.metadata,
  });

  final String timestamp;
  final String type;
  final String actor;
  final String result;
  final String path;
  final String ip;
  final Map<String, dynamic> metadata;

  factory AuditEntry.fromJson(Map<String, dynamic> json) {
    return AuditEntry(
      timestamp: (json['timestamp'] as String?) ?? '',
      type: (json['type'] as String?) ?? '',
      actor: (json['actor'] as String?) ?? '',
      result: (json['result'] as String?) ?? '',
      path: (json['path'] as String?) ?? '',
      ip: (json['ip'] as String?) ?? '',
      metadata: Map<String, dynamic>.fromEntries(
        json.entries.where((entry) => !_auditReservedKeys.contains(entry.key)),
      ),
    );
  }

  String get localizedTimestamp => formatAuditTimestamp(timestamp);

  String get localizedActor => describeAuditActor(actor);

  String get localizedResult => describeAuditResult(result);

  String? get machineId {
    final value = _auditMetadataText(metadata, 'machineId');
    return value.isEmpty ? null : value;
  }

  String get normalizedIp => normalizeAuditIp(ip);

  String get displayPath => path.isEmpty ? '未知接口' : path;

  AuditEventPresentation get presentation => describeAuditEvent(this);

  String get searchableText {
    final details = presentation;
    return <String>[
      timestamp,
      type,
      actor,
      result,
      path,
      ip,
      machineId ?? '',
      details.title,
      details.description,
      ...metadata.entries.map((entry) => '${entry.key} ${entry.value}'),
    ].join(' ').toLowerCase();
  }
}

List<String> buildAuditMachineOptions(
  Iterable<MachineRecord> machines,
  Iterable<AuditEntry> entries,
) {
  final values = <String>{
    ...machines
        .map((machine) => machine.machineId)
        .where((value) => value.trim().isNotEmpty),
    ...entries
        .map((entry) => entry.machineId ?? '')
        .where((value) => value.trim().isNotEmpty),
  }.toList();

  values.sort(
    (left, right) => left.toLowerCase().compareTo(right.toLowerCase()),
  );
  return values;
}

abstract class AdminApi {
  String get baseUrl;

  void updateBaseUrl(String baseUrl);

  Future<ServerStatus> getServerStatus();

  Future<AuthStatus> getAuthStatus();

  Future<InitializationPreparation> prepareInitialization({
    required String issuer,
    required String accountName,
  });

  Future<void> completeInitialization({
    required String adminPassword,
    required String sessionSecret,
    required String totpCode,
    required String defaultVhdKeyword,
    required String dbHost,
    required int dbPort,
    required String dbName,
    required String dbUser,
    required String dbPassword,
    List<Map<String, String>> trustedCertificates =
        const <Map<String, String>>[],
  });

  Future<void> login(String password);

  Future<void> logout();

  Future<OtpStatus> verifyOtp(String code);

  Future<OtpStatus> getOtpStatus();

  Future<InitializationPreparation> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  });

  Future<OtpStatus> completeOtpRotation(String code);

  Future<List<MachineRecord>> getMachines();

  Future<void> addMachine(MachineDraft draft);

  Future<void> setMachineApproval(String machineId, bool approved);

  Future<void> setMachineProtection(String machineId, bool protectedState);

  Future<void> resetMachineRegistration(String machineId);

  Future<void> setMachineVhd(String machineId, String vhdKeyword);

  Future<void> setMachineEvhdPassword(String machineId, String evhdPassword);

  Future<String> getPlainEvhdPassword(String machineId, String reason);

  Future<void> deleteMachine(String machineId);

  Future<List<TrustedCertificateRecord>> getTrustedCertificates();

  Future<void> addTrustedCertificate(String name, String certificatePem);

  Future<void> removeTrustedCertificate(String fingerprint);

  Future<List<AuditEntry>> getAuditEntries({String? machineId});

  Future<void> updateDefaultVhd(String vhdKeyword);

  Future<void> changePassword(String currentPassword, String newPassword);
}

class HttpAdminApi implements AdminApi {
  HttpAdminApi({String baseUrl = 'http://localhost:8080'}) : _baseUrl = baseUrl;

  final HttpClient _client = HttpClient();
  final Map<String, Cookie> _cookies = <String, Cookie>{};
  String _baseUrl;

  @override
  String get baseUrl => _baseUrl;

  @override
  void updateBaseUrl(String baseUrl) {
    _baseUrl = normalizeBaseUrl(baseUrl);
  }

  Uri _resolve(String path) {
    final normalizedBase = _baseUrl.isEmpty
        ? 'http://localhost:8080'
        : _baseUrl;
    return Uri.parse('$normalizedBase$path');
  }

  void _applyCookies(HttpClientRequest request) {
    if (_cookies.isEmpty) {
      return;
    }
    request.headers.set(
      HttpHeaders.cookieHeader,
      _cookies.values
          .map((cookie) => '${cookie.name}=${cookie.value}')
          .join('; '),
    );
  }

  void _storeCookies(HttpClientResponse response) {
    for (final cookie in response.cookies) {
      _cookies[cookie.name] = cookie;
    }
  }

  Future<Map<String, dynamic>> _requestJson(
    String method,
    String path, {
    Object? body,
  }) async {
    final request = await _client.openUrl(method, _resolve(path));
    request.headers.set(HttpHeaders.acceptHeader, 'application/json');
    _applyCookies(request);

    if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(jsonEncode(body));
    }

    final response = await request.close();
    _storeCookies(response);
    final text = await response.transform(utf8.decoder).join();

    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (text.trim().isEmpty) {
        return <String, dynamic>{};
      }
      final decoded = jsonDecode(text);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
      return <String, dynamic>{'data': decoded};
    }

    Map<String, dynamic> errorJson = <String, dynamic>{};
    if (text.trim().isNotEmpty) {
      try {
        final decoded = jsonDecode(text);
        if (decoded is Map<String, dynamic>) {
          errorJson = decoded;
        }
      } catch (_) {
        errorJson = <String, dynamic>{'error': text};
      }
    }

    throw AdminApiException(
      (errorJson['error'] as String?) ??
          (errorJson['message'] as String?) ??
          '请求失败: ${response.statusCode}',
      statusCode: response.statusCode,
      requireAuth: errorJson['requireAuth'] == true,
      requireOtp: errorJson['requireOtp'] == true,
      initializeRequired: errorJson['initializeRequired'] == true,
    );
  }

  @override
  Future<ServerStatus> getServerStatus() async {
    final json = await _requestJson('GET', '/api/init/status');
    return ServerStatus.fromJson(json);
  }

  @override
  Future<AuthStatus> getAuthStatus() async {
    final json = await _requestJson('GET', '/api/auth/check');
    return AuthStatus.fromJson(json);
  }

  @override
  Future<InitializationPreparation> prepareInitialization({
    required String issuer,
    required String accountName,
  }) async {
    final json = await _requestJson(
      'POST',
      '/api/init/prepare',
      body: <String, dynamic>{'issuer': issuer, 'accountName': accountName},
    );
    return InitializationPreparation.fromJson(json);
  }

  @override
  Future<void> completeInitialization({
    required String adminPassword,
    required String sessionSecret,
    required String totpCode,
    required String defaultVhdKeyword,
    required String dbHost,
    required int dbPort,
    required String dbName,
    required String dbUser,
    required String dbPassword,
    List<Map<String, String>> trustedCertificates =
        const <Map<String, String>>[],
  }) async {
    await _requestJson(
      'POST',
      '/api/init/complete',
      body: <String, dynamic>{
        'adminPassword': adminPassword,
        'sessionSecret': sessionSecret,
        'totpCode': totpCode,
        'defaultVhdKeyword': defaultVhdKeyword,
        'dbConfig': <String, dynamic>{
          'host': dbHost,
          'port': dbPort,
          'database': dbName,
          'user': dbUser,
          'password': dbPassword,
        },
        'trustedRegistrationCertificates': trustedCertificates,
      },
    );
  }

  @override
  Future<void> login(String password) async {
    await _requestJson(
      'POST',
      '/api/auth/login',
      body: <String, dynamic>{'password': password},
    );
  }

  @override
  Future<void> logout() async {
    await _requestJson('POST', '/api/auth/logout');
  }

  @override
  Future<OtpStatus> verifyOtp(String code) async {
    final json = await _requestJson(
      'POST',
      '/api/auth/otp/verify',
      body: <String, dynamic>{'code': code},
    );
    return OtpStatus.fromJson(json);
  }

  @override
  Future<OtpStatus> getOtpStatus() async {
    final json = await _requestJson('GET', '/api/auth/otp/status');
    return OtpStatus.fromJson(json);
  }

  @override
  Future<InitializationPreparation> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  }) async {
    final body = <String, dynamic>{'currentCode': currentCode};
    if (issuer != null && issuer.trim().isNotEmpty) {
      body['issuer'] = issuer.trim();
    }
    if (accountName != null && accountName.trim().isNotEmpty) {
      body['accountName'] = accountName.trim();
    }

    final json = await _requestJson(
      'POST',
      '/api/auth/otp/rotate/prepare',
      body: body,
    );
    return InitializationPreparation.fromJson(json);
  }

  @override
  Future<OtpStatus> completeOtpRotation(String code) async {
    final json = await _requestJson(
      'POST',
      '/api/auth/otp/rotate/complete',
      body: <String, dynamic>{'code': code},
    );
    return OtpStatus.fromJson(json);
  }

  @override
  Future<List<MachineRecord>> getMachines() async {
    final json = await _requestJson('GET', '/api/machines');
    final rows = (json['machines'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(MachineRecord.fromJson)
        .toList();
    return rows;
  }

  @override
  Future<void> addMachine(MachineDraft draft) async {
    await _requestJson(
      'POST',
      '/api/machines',
      body: <String, dynamic>{
        'machineId': draft.machineId,
        'vhdKeyword': draft.vhdKeyword,
        'protected': draft.protectedState,
        if (draft.evhdPassword != null && draft.evhdPassword!.isNotEmpty)
          'evhdPassword': draft.evhdPassword,
      },
    );
  }

  @override
  Future<void> setMachineApproval(String machineId, bool approved) async {
    await _requestJson(
      'POST',
      '/api/machines/$machineId/approve',
      body: <String, dynamic>{'approved': approved},
    );
  }

  @override
  Future<void> setMachineProtection(
    String machineId,
    bool protectedState,
  ) async {
    await _requestJson(
      'POST',
      '/api/protect',
      body: <String, dynamic>{
        'machineId': machineId,
        'protected': protectedState,
      },
    );
  }

  @override
  Future<void> resetMachineRegistration(String machineId) async {
    await _requestJson('POST', '/api/machines/$machineId/revoke');
  }

  @override
  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    await _requestJson(
      'POST',
      '/api/machines/$machineId/vhd',
      body: <String, dynamic>{'vhdKeyword': vhdKeyword},
    );
  }

  @override
  Future<void> setMachineEvhdPassword(
    String machineId,
    String evhdPassword,
  ) async {
    await _requestJson(
      'POST',
      '/api/machines/$machineId/evhd-password',
      body: <String, dynamic>{'evhdPassword': evhdPassword},
    );
  }

  @override
  Future<String> getPlainEvhdPassword(String machineId, String reason) async {
    final encodedMachineId = Uri.encodeQueryComponent(machineId);
    final encodedReason = Uri.encodeQueryComponent(reason);
    final json = await _requestJson(
      'GET',
      '/api/evhd-password/plain?machineId=$encodedMachineId&reason=$encodedReason',
    );
    return (json['evhdPassword'] as String?) ?? '';
  }

  @override
  Future<void> deleteMachine(String machineId) async {
    await _requestJson('DELETE', '/api/machines/$machineId');
  }

  @override
  Future<List<TrustedCertificateRecord>> getTrustedCertificates() async {
    final json = await _requestJson(
      'GET',
      '/api/security/trusted-certificates',
    );
    return (json['certificates'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(TrustedCertificateRecord.fromJson)
        .toList();
  }

  @override
  Future<void> addTrustedCertificate(String name, String certificatePem) async {
    await _requestJson(
      'POST',
      '/api/security/trusted-certificates',
      body: <String, dynamic>{'name': name, 'certificatePem': certificatePem},
    );
  }

  @override
  Future<void> removeTrustedCertificate(String fingerprint) async {
    await _requestJson(
      'DELETE',
      '/api/security/trusted-certificates/$fingerprint',
    );
  }

  @override
  Future<List<AuditEntry>> getAuditEntries({String? machineId}) async {
    final queryParameters = <String, String>{'limit': '100'};
    if (machineId != null && machineId.trim().isNotEmpty) {
      queryParameters['machineId'] = machineId.trim();
    }

    final query = Uri(queryParameters: queryParameters).query;
    final json = await _requestJson('GET', '/api/audit?$query');
    return (json['entries'] as List<dynamic>? ?? <dynamic>[])
        .whereType<Map<String, dynamic>>()
        .map(AuditEntry.fromJson)
        .toList();
  }

  @override
  Future<void> updateDefaultVhd(String vhdKeyword) async {
    await _requestJson(
      'POST',
      '/api/settings/default-vhd',
      body: <String, dynamic>{'vhdKeyword': vhdKeyword},
    );
  }

  @override
  Future<void> changePassword(
    String currentPassword,
    String newPassword,
  ) async {
    await _requestJson(
      'POST',
      '/api/auth/change-password',
      body: <String, dynamic>{
        'currentPassword': currentPassword,
        'newPassword': newPassword,
        'confirmPassword': newPassword,
      },
    );
  }
}

class AppController extends ChangeNotifier {
  AppController({required this.api, ClientConfigStore? clientConfigStore})
    : clientConfigStore = clientConfigStore ?? FileClientConfigStore();

  final AdminApi api;
  final ClientConfigStore clientConfigStore;

  Timer? _otpExpiryTimer;
  bool _clientConfigLoaded = false;

  bool isLoading = true;
  bool isWorking = false;
  String? errorMessage;
  ServerStatus? serverStatus;
  bool isAuthenticated = false;
  bool otpVerified = false;
  int otpVerifiedUntil = 0;
  InitializationPreparation? initializationPreparation;
  InitializationPreparation? otpRotationPreparation;
  List<MachineRecord> machines = <MachineRecord>[];
  List<TrustedCertificateRecord> certificates = <TrustedCertificateRecord>[];
  List<AuditEntry> auditEntries = <AuditEntry>[];
  List<String> rememberedBaseUrls = <String>[];
  String? auditFilterMachineId;

  String get baseUrl => api.baseUrl;

  void updateBaseUrl(String value) {
    api.updateBaseUrl(value);
    notifyListeners();
  }

  void setAuditMachineFilter(String? machineId, {bool notify = true}) {
    final normalized = machineId?.trim();
    auditFilterMachineId = normalized == null || normalized.isEmpty
        ? null
        : normalized;
    if (notify) {
      notifyListeners();
    }
  }

  Future<void> _ensureClientConfigLoaded() async {
    if (_clientConfigLoaded) {
      return;
    }

    final config = await clientConfigStore.load();
    rememberedBaseUrls = config.serverHistory;
    if (config.lastBaseUrl.isNotEmpty) {
      api.updateBaseUrl(config.lastBaseUrl);
    }
    _clientConfigLoaded = true;
  }

  Future<void> _rememberCurrentBaseUrl() async {
    final normalizedBaseUrl = normalizeBaseUrl(baseUrl);
    if (normalizedBaseUrl.isEmpty) {
      return;
    }

    rememberedBaseUrls = mergeRememberedBaseUrls(
      rememberedBaseUrls,
      preferredFirst: normalizedBaseUrl,
    );

    await clientConfigStore.save(
      ClientConfig(
        lastBaseUrl: normalizedBaseUrl,
        serverHistory: rememberedBaseUrls,
      ),
    );
  }

  Future<void> _rememberCurrentBaseUrlSilently() async {
    try {
      await _rememberCurrentBaseUrl();
    } catch (_) {
      return;
    }
  }

  void _clearOtpVerification({bool notify = true}) {
    _otpExpiryTimer?.cancel();
    _otpExpiryTimer = null;
    otpVerified = false;
    otpVerifiedUntil = 0;
    certificates = <TrustedCertificateRecord>[];
    if (notify) {
      notifyListeners();
    }
  }

  void _scheduleOtpExpiryTimer() {
    _otpExpiryTimer?.cancel();
    _otpExpiryTimer = null;

    if (!otpVerified || otpVerifiedUntil <= 0) {
      return;
    }

    final remainingMilliseconds =
        otpVerifiedUntil - DateTime.now().millisecondsSinceEpoch;
    if (remainingMilliseconds <= 0) {
      otpVerified = false;
      otpVerifiedUntil = 0;
      certificates = <TrustedCertificateRecord>[];
      return;
    }

    _otpExpiryTimer = Timer(
      Duration(milliseconds: remainingMilliseconds + 1),
      _clearOtpVerification,
    );
  }

  void _applyOtpStatus(OtpStatus otpStatus, {bool notify = true}) {
    otpVerified = otpStatus.verified;
    otpVerifiedUntil = otpStatus.verified ? otpStatus.verifiedUntil : 0;
    if (!otpVerified) {
      certificates = <TrustedCertificateRecord>[];
    }
    _scheduleOtpExpiryTimer();
    if (notify) {
      notifyListeners();
    }
  }

  Future<T> _runAction<T>(Future<T> Function() action) async {
    isWorking = true;
    errorMessage = null;
    notifyListeners();
    try {
      return await action();
    } catch (error) {
      errorMessage = describeError(error);
      notifyListeners();
      rethrow;
    } finally {
      isWorking = false;
      notifyListeners();
    }
  }

  Future<void> bootstrap() async {
    isLoading = true;
    errorMessage = null;
    notifyListeners();
    try {
      await _ensureClientConfigLoaded();
      serverStatus = await api.getServerStatus();
      final authStatus = await api.getAuthStatus();
      isAuthenticated = authStatus.isAuthenticated;

      if (isAuthenticated) {
        _applyOtpStatus(await api.getOtpStatus(), notify: false);
        machines = await api.getMachines();
        auditEntries = await api.getAuditEntries(
          machineId: auditFilterMachineId,
        );
      } else {
        _clearOtpVerification(notify: false);
        otpRotationPreparation = null;
        machines = <MachineRecord>[];
        certificates = <TrustedCertificateRecord>[];
        auditEntries = <AuditEntry>[];
      }
    } catch (error) {
      errorMessage = describeError(error);
    } finally {
      isLoading = false;
      notifyListeners();
    }
  }

  Future<void> prepareInitialization({
    required String issuer,
    required String accountName,
  }) async {
    initializationPreparation = await _runAction(
      () => api.prepareInitialization(issuer: issuer, accountName: accountName),
    );
  }

  Future<void> completeInitialization({
    required String adminPassword,
    required String sessionSecret,
    required String totpCode,
    required String defaultVhdKeyword,
    required String dbHost,
    required int dbPort,
    required String dbName,
    required String dbUser,
    required String dbPassword,
    List<Map<String, String>> trustedCertificates =
        const <Map<String, String>>[],
  }) async {
    await _runAction(
      () => api.completeInitialization(
        adminPassword: adminPassword,
        sessionSecret: sessionSecret,
        totpCode: totpCode,
        defaultVhdKeyword: defaultVhdKeyword,
        dbHost: dbHost,
        dbPort: dbPort,
        dbName: dbName,
        dbUser: dbUser,
        dbPassword: dbPassword,
        trustedCertificates: trustedCertificates,
      ),
    );
    initializationPreparation = null;
    await _rememberCurrentBaseUrlSilently();
    await bootstrap();
  }

  Future<void> login(String password) async {
    await _runAction(() => api.login(password));
    await _rememberCurrentBaseUrlSilently();
    await bootstrap();
  }

  Future<void> logout() async {
    await _runAction(api.logout);
    _clearOtpVerification(notify: false);
    otpRotationPreparation = null;
    await bootstrap();
  }

  Future<void> verifyOtp(String code) async {
    final otpStatus = await _runAction(() => api.verifyOtp(code));
    _applyOtpStatus(otpStatus);

    if (!otpVerified) {
      return;
    }

    try {
      await loadCertificates();
    } catch (_) {
      // OTP 已成功，只把证书刷新失败作为界面错误保留，不中断成功状态。
    }
  }

  Future<void> prepareOtpRotation({
    required String currentCode,
    String? issuer,
    String? accountName,
  }) async {
    otpRotationPreparation = await _runAction(
      () => api.prepareOtpRotation(
        currentCode: currentCode,
        issuer: issuer,
        accountName: accountName,
      ),
    );
    notifyListeners();
  }

  Future<void> completeOtpRotation(String code) async {
    final otpStatus = await _runAction(() => api.completeOtpRotation(code));
    otpRotationPreparation = null;
    _applyOtpStatus(otpStatus);
  }

  void clearOtpRotationPreparation() {
    otpRotationPreparation = null;
    notifyListeners();
  }

  Future<void> refreshOtpStatus() async {
    try {
      _applyOtpStatus(await api.getOtpStatus());
    } catch (_) {
      _clearOtpVerification();
    }
  }

  Future<void> loadMachines() async {
    machines = await _runAction(api.getMachines);
    notifyListeners();
  }

  Future<void> loadCertificates() async {
    certificates = await _runAction(api.getTrustedCertificates);
    notifyListeners();
  }

  Future<void> loadAudit({String? machineId}) async {
    setAuditMachineFilter(machineId, notify: false);
    auditEntries = await _runAction(
      () => api.getAuditEntries(machineId: auditFilterMachineId),
    );
    notifyListeners();
  }

  Future<void> addMachine(MachineDraft draft) async {
    await _runAction(() => api.addMachine(draft));
    await loadMachines();
  }

  Future<void> setMachineApproval(String machineId, bool approved) async {
    await _runAction(() => api.setMachineApproval(machineId, approved));
    await loadMachines();
  }

  Future<void> setMachineProtection(
    String machineId,
    bool protectedState,
  ) async {
    await _runAction(() => api.setMachineProtection(machineId, protectedState));
    await loadMachines();
  }

  Future<void> resetMachineRegistration(String machineId) async {
    await _runAction(() => api.resetMachineRegistration(machineId));
    await loadMachines();
  }

  Future<void> setMachineVhd(String machineId, String vhdKeyword) async {
    await _runAction(() => api.setMachineVhd(machineId, vhdKeyword));
    await loadMachines();
  }

  Future<void> setMachineEvhdPassword(
    String machineId,
    String evhdPassword,
  ) async {
    await _runAction(() => api.setMachineEvhdPassword(machineId, evhdPassword));
    await loadMachines();
  }

  Future<String> readPlainEvhdPassword(String machineId, String reason) async {
    return _runAction(() => api.getPlainEvhdPassword(machineId, reason));
  }

  Future<void> deleteMachine(String machineId) async {
    await _runAction(() => api.deleteMachine(machineId));
    await loadMachines();
  }

  Future<void> addTrustedCertificate(String name, String certificatePem) async {
    await _runAction(() => api.addTrustedCertificate(name, certificatePem));
    await loadCertificates();
  }

  Future<void> removeTrustedCertificate(String fingerprint) async {
    await _runAction(() => api.removeTrustedCertificate(fingerprint));
    await loadCertificates();
  }

  Future<void> updateDefaultVhd(String vhdKeyword) async {
    await _runAction(() => api.updateDefaultVhd(vhdKeyword));
    serverStatus = await api.getServerStatus();
    notifyListeners();
  }

  Future<void> changePassword(
    String currentPassword,
    String newPassword,
  ) async {
    await _runAction(() => api.changePassword(currentPassword, newPassword));
    otpRotationPreparation = null;
    notifyListeners();
  }

  @override
  void dispose() {
    _otpExpiryTimer?.cancel();
    super.dispose();
  }
}

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
                await controller.loadAudit();
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

class DashboardScreen extends StatelessWidget {
  const DashboardScreen({
    super.key,
    required this.controller,
    required this.selectedIndex,
    required this.onDestinationSelected,
  });

  final AppController controller;
  final int selectedIndex;
  final ValueChanged<int> onDestinationSelected;

  @override
  Widget build(BuildContext context) {
    Future<void> openAuditForMachine(String machineId) async {
      await controller.loadAudit(machineId: machineId);
      onDestinationSelected(2);
    }

    final destinations = <DashboardDestinationSpec>[
      const DashboardDestinationSpec(
        label: '机器管理',
        subtitle: '审批、保护、EVHD',
        icon: Icons.dns_rounded,
        color: AppPalette.coral,
      ),
      const DashboardDestinationSpec(
        label: '证书',
        subtitle: '信任链、PEM、移除',
        icon: Icons.verified_user_rounded,
        color: AppPalette.sky,
      ),
      const DashboardDestinationSpec(
        label: '审计',
        subtitle: '过滤、搜索、回溯',
        icon: Icons.history_rounded,
        color: AppPalette.mint,
      ),
      const DashboardDestinationSpec(
        label: '设置',
        subtitle: 'OTP、密码、默认值',
        icon: Icons.tune_rounded,
        color: AppPalette.sun,
      ),
    ];

    final pages = <Widget>[
      MachinesView(
        controller: controller,
        onOpenAuditForMachine: openAuditForMachine,
      ),
      CertificatesView(controller: controller),
      AuditView(controller: controller),
      SettingsView(controller: controller),
    ];

    Future<void> openOtpDialog() async {
      final code = await showSingleInputDialog(
        context,
        title: 'OTP 二次验证',
        label: '验证码',
        obscureText: false,
      );
      if (code == null || code.trim().isEmpty) {
        return;
      }
      try {
        await controller.verifyOtp(code.trim());
        if (!context.mounted) {
          return;
        }
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('OTP 验证成功。')));
      } catch (error) {
        if (!context.mounted) {
          return;
        }
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(describeError(error))));
      }
    }

    final overviewCards = <Widget>[
      OverviewStatCard(
        label: '数据库',
        value: controller.serverStatus?.databaseReady == true ? '已连接' : '异常',
        icon: Icons.storage_rounded,
        color: controller.serverStatus?.databaseReady == true
            ? AppPalette.mint
            : AppPalette.coral,
        caption: '服务端数据库链路健康状态。',
      ),
      OverviewStatCard(
        label: '默认关键词',
        value: controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
        icon: Icons.bolt_rounded,
        color: AppPalette.coral,
        caption: '所有机台的默认启动关键字。',
      ),
      OverviewStatCard(
        label: 'OTP',
        value: controller.otpVerified ? '已验证' : '待验证',
        icon: Icons.verified_user_rounded,
        color: controller.otpVerified ? AppPalette.mint : AppPalette.sun,
        caption: controller.otpVerified ? '高敏操作已解锁。' : '证书与敏感操作仍受保护。',
      ),
      OverviewStatCard(
        label: '当前入口',
        value: controller.baseUrl.replaceFirst(RegExp(r'^https?://'), ''),
        icon: Icons.link_rounded,
        color: AppPalette.sky,
        caption: '当前连接的管理服务地址。',
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        final compact =
            constraints.maxWidth < 1100 || constraints.maxHeight < 720;
        final overviewSection = OverviewStatsGrid(
          cards: overviewCards,
          singleRow: compact,
        );

        final pageBody = AnimatedSwitcher(
          duration: const Duration(milliseconds: 280),
          switchInCurve: Curves.easeOutCubic,
          switchOutCurve: Curves.easeInCubic,
          transitionBuilder: (child, animation) {
            final offset = Tween<Offset>(
              begin: const Offset(0.03, 0),
              end: Offset.zero,
            ).animate(animation);
            return FadeTransition(
              opacity: animation,
              child: SlideTransition(position: offset, child: child),
            );
          },
          child: KeyedSubtree(
            key: ValueKey<int>(selectedIndex),
            child: pages[selectedIndex],
          ),
        );

        final contentColumn = Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            PageHeader(
              eyebrow: 'Control Room',
              title: 'VHD Mount Admin',
              subtitle: '',
              actions: <Widget>[
                FilledButton.tonalIcon(
                  onPressed: openOtpDialog,
                  icon: const Icon(Icons.key_rounded),
                  label: Text(controller.otpVerified ? '重新验证 OTP' : '验证 OTP'),
                ),
                OutlinedButton.icon(
                  onPressed: () async {
                    await controller.bootstrap();
                  },
                  icon: const Icon(Icons.sync_rounded),
                  label: const Text('刷新'),
                ),
                TextButton.icon(
                  onPressed: () async {
                    await controller.logout();
                  },
                  icon: const Icon(Icons.logout_rounded),
                  label: const Text('登出'),
                ),
              ],
            ),
            const SizedBox(height: 18),
            overviewSection,
            if (controller.errorMessage != null) ...<Widget>[
              const SizedBox(height: 16),
              ErrorBanner(message: controller.errorMessage!),
            ],
            const SizedBox(height: 20),
            Expanded(child: pageBody),
          ],
        );

        return Scaffold(
          backgroundColor: Colors.transparent,
          bottomNavigationBar: compact
              ? NavigationBar(
                  selectedIndex: selectedIndex,
                  onDestinationSelected: onDestinationSelected,
                  destinations: destinations
                      .map(
                        (item) => NavigationDestination(
                          icon: Icon(item.icon),
                          label: item.label,
                        ),
                      )
                      .toList(),
                )
              : null,
          body: AdminBackdrop(
            child: SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(20),
                child: compact
                    ? contentColumn
                    : Row(
                        children: <Widget>[
                          SizedBox(
                            width: 260,
                            child: AppPanel(
                              padding: const EdgeInsets.fromLTRB(
                                16,
                                18,
                                16,
                                18,
                              ),
                              gradient: const LinearGradient(
                                begin: Alignment.topLeft,
                                end: Alignment.bottomRight,
                                colors: <Color>[
                                  Color(0xFFFFF5EA),
                                  Color(0xFFF6FFF9),
                                ],
                              ),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  const AccentIconBadge(
                                    icon: Icons.auto_awesome_rounded,
                                    color: AppPalette.coral,
                                    size: 54,
                                  ),
                                  const SizedBox(height: 16),
                                  Text(
                                    '工作区',
                                    style: Theme.of(context)
                                        .textTheme
                                        .labelLarge
                                        ?.copyWith(color: AppPalette.coralDeep),
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    '安全控制面板',
                                    style: Theme.of(
                                      context,
                                    ).textTheme.titleLarge,
                                  ),
                                  const SizedBox(height: 20),
                                  Expanded(
                                    child: SingleChildScrollView(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.stretch,
                                        children: <Widget>[
                                          for (
                                            int index = 0;
                                            index < destinations.length;
                                            index++
                                          ) ...<Widget>[
                                            DashboardSidebarButton(
                                              label: destinations[index].label,
                                              subtitle:
                                                  destinations[index].subtitle,
                                              icon: destinations[index].icon,
                                              color: destinations[index].color,
                                              selected: selectedIndex == index,
                                              onTap: () =>
                                                  onDestinationSelected(index),
                                            ),
                                            if (index !=
                                                destinations.length - 1)
                                              const SizedBox(height: 12),
                                          ],
                                          const SizedBox(height: 16),
                                          InfoPanel(
                                            title: controller.otpVerified
                                                ? '高敏操作已解锁'
                                                : '等待 OTP 验证',
                                            icon: controller.otpVerified
                                                ? Icons.verified_rounded
                                                : Icons.lock_clock_rounded,
                                            color: controller.otpVerified
                                                ? AppPalette.mint
                                                : AppPalette.sun,
                                            body: Text(
                                              controller.otpVerified
                                                  ? '证书管理、密码读取等高敏操作当前可用。'
                                                  : '先完成 OTP 验证，再执行证书管理和敏感操作。',
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                          const SizedBox(width: 24),
                          Expanded(child: contentColumn),
                        ],
                      ),
              ),
            ),
          ),
        );
      },
    );
  }
}

class MachinesView extends StatelessWidget {
  const MachinesView({
    super.key,
    required this.controller,
    required this.onOpenAuditForMachine,
  });

  final AppController controller;
  final Future<void> Function(String machineId) onOpenAuditForMachine;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Machine Fleet',
          title: '机器管理',
          subtitle: '审批、保护、注册重置与 EVHD 配置都集中在这里。',
          actions: <Widget>[
            FilledButton.icon(
              onPressed: () async {
                final draft = await showAddMachineDialog(
                  context,
                  defaultVhdKeyword:
                      controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
                );
                if (draft == null) {
                  return;
                }
                try {
                  await controller.addMachine(draft);
                  if (!context.mounted) {
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text('机台 ${draft.machineId} 已添加。')),
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
              icon: const Icon(Icons.add_rounded),
              label: const Text('添加机台'),
            ),
            OutlinedButton.icon(
              onPressed: controller.loadMachines,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新列表'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (controller.machines.isEmpty)
          Expanded(
            child: Center(
              child: SizedBox(
                width: 420,
                child: const InfoPanel(
                  title: '当前还没有机器记录',
                  body: Text('可以先创建机台，再继续做审批、保护和 EVHD 配置。'),
                  icon: Icons.add_business_rounded,
                  color: AppPalette.sky,
                ),
              ),
            ),
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: controller.machines.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final machine = controller.machines[index];
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            AccentIconBadge(
                              icon: machine.approved
                                  ? Icons.memory_rounded
                                  : Icons.pending_actions_rounded,
                              color: machine.approved
                                  ? AppPalette.mint
                                  : AppPalette.sun,
                            ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  Text(
                                    machine.machineId,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.titleLarge,
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    machine.keyType == null
                                        ? '尚未提交注册密钥'
                                        : '密钥类型：${machine.keyType}',
                                    style: Theme.of(context)
                                        .textTheme
                                        .bodyMedium
                                        ?.copyWith(color: AppPalette.muted),
                                  ),
                                ],
                              ),
                            ),
                            StatusChip(
                              label: machine.approved ? '已审批' : '待审批',
                              color: machine.approved
                                  ? AppPalette.mint
                                  : AppPalette.sun,
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: <Widget>[
                            StatusChip(
                              label: '当前启动关键词 ${machine.vhdKeyword}',
                              color: AppPalette.sky,
                            ),
                            StatusChip(
                              label: machine.protectedState ? '保护开启' : '保护关闭',
                              color: machine.protectedState
                                  ? AppPalette.coral
                                  : AppPalette.mint,
                            ),
                            StatusChip(
                              label: machine.evhdPasswordConfigured
                                  ? 'EVHD 已配置'
                                  : 'EVHD 未配置',
                              color: machine.evhdPasswordConfigured
                                  ? AppPalette.mint
                                  : AppPalette.sun,
                            ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Text('Key ID: ${machine.keyId ?? '未注册'}'),
                        Text('最后在线: ${machine.lastSeen ?? '未知'}'),
                        if (machine.registrationCertFingerprint != null)
                          Text('注册证书: ${machine.registrationCertFingerprint}'),
                        const SizedBox(height: 16),
                        Wrap(
                          spacing: 10,
                          runSpacing: 10,
                          children: <Widget>[
                            FilledButton.tonal(
                              onPressed: () async {
                                try {
                                  await controller.setMachineApproval(
                                    machine.machineId,
                                    !machine.approved,
                                  );
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(
                                        machine.approved ? '已取消审批。' : '已审批通过。',
                                      ),
                                    ),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              child: Text(machine.approved ? '取消审批' : '审批通过'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                try {
                                  await controller.setMachineProtection(
                                    machine.machineId,
                                    !machine.protectedState,
                                  );
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(
                                        machine.protectedState
                                            ? '已关闭保护。'
                                            : '已开启保护。',
                                      ),
                                    ),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              child: Text(
                                machine.protectedState ? '关闭保护' : '开启保护',
                              ),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                try {
                                  await controller.resetMachineRegistration(
                                    machine.machineId,
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              child: const Text('重置注册'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                final value = await showSingleInputDialog(
                                  context,
                                  title: '设置启动关键词',
                                  label: '启动关键词',
                                  initialValue: machine.vhdKeyword,
                                );
                                if (value == null || value.trim().isEmpty) {
                                  return;
                                }
                                try {
                                  await controller.setMachineVhd(
                                    machine.machineId,
                                    value.trim().toUpperCase(),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              child: const Text('设置启动关键词'),
                            ),
                            OutlinedButton.icon(
                              onPressed: () async {
                                try {
                                  await onOpenAuditForMachine(
                                    machine.machineId,
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              icon: const Icon(Icons.history_rounded),
                              label: const Text('查阅审计日志'),
                            ),
                            OutlinedButton(
                              onPressed: () async {
                                final value = await showSingleInputDialog(
                                  context,
                                  title: '设置 EVHD 密码',
                                  label: 'EVHD 密码',
                                  obscureText: true,
                                );
                                if (value == null || value.isEmpty) {
                                  return;
                                }
                                try {
                                  await controller.setMachineEvhdPassword(
                                    machine.machineId,
                                    value,
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              child: const Text('设置 EVHD'),
                            ),
                            FilledButton(
                              onPressed: () async {
                                final reason = await showSingleInputDialog(
                                  context,
                                  title: '读取 EVHD 明文',
                                  label: '查询原因',
                                  initialValue: 'support investigation',
                                );
                                if (reason == null || reason.trim().isEmpty) {
                                  return;
                                }
                                try {
                                  final password = await controller
                                      .readPlainEvhdPassword(
                                        machine.machineId,
                                        reason.trim(),
                                      );
                                  if (!context.mounted) {
                                    return;
                                  }
                                  await showDialog<void>(
                                    context: context,
                                    builder: (context) => AlertDialog(
                                      title: const Text('EVHD 明文'),
                                      content: SelectableText(password),
                                      actions: <Widget>[
                                        TextButton(
                                          onPressed: () =>
                                              Navigator.of(context).pop(),
                                          child: const Text('关闭'),
                                        ),
                                      ],
                                    ),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              child: const Text('读取明文'),
                            ),
                            OutlinedButton.icon(
                              onPressed: () async {
                                final confirmed = await showConfirmDialog(
                                  context,
                                  title: '删除机台',
                                  message:
                                      '确认删除 ${machine.machineId} 吗？这会移除该机台的管理记录与已保存的 EVHD 密码。',
                                  confirmLabel: '删除',
                                );
                                if (confirmed != true) {
                                  return;
                                }
                                try {
                                  await controller.deleteMachine(
                                    machine.machineId,
                                  );
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(
                                        '机台 ${machine.machineId} 已删除。',
                                      ),
                                    ),
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              icon: const Icon(Icons.delete_outline_rounded),
                              label: const Text('删除机台'),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class CertificatesView extends StatelessWidget {
  const CertificatesView({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Trust Store',
          title: '可信注册证书',
          subtitle: '这里管理用于机台注册审批链路的可信证书。',
          actions: <Widget>[
            OutlinedButton.icon(
              onPressed: controller.otpVerified
                  ? controller.loadCertificates
                  : null,
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新证书'),
            ),
            FilledButton.icon(
              onPressed: () async {
                if (!controller.otpVerified) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('添加可信证书前请先完成 OTP 验证。')),
                  );
                  return;
                }

                final values = await showTwoFieldDialog(
                  context,
                  title: '添加可信证书',
                  firstLabel: '名称',
                  secondLabel: 'PEM 证书',
                  secondMinLines: 10,
                );
                if (values == null) {
                  return;
                }
                try {
                  await controller.addTrustedCertificate(
                    values.first.trim(),
                    values.last.trim(),
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
              icon: const Icon(Icons.add_rounded),
              label: const Text('导入证书'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (!controller.otpVerified)
          const InfoPanel(
            title: '需要 OTP',
            body: Text('证书管理属于高敏感操作。请先在右上角完成 OTP 验证，再刷新证书列表。'),
            icon: Icons.lock_clock_rounded,
            color: AppPalette.sun,
          )
        else if (controller.certificates.isEmpty)
          Expanded(
            child: Center(
              child: SizedBox(
                width: 420,
                child: const InfoPanel(
                  title: '当前没有可信注册证书',
                  body: Text('可以在完成 OTP 验证后导入第一张机台注册证书。'),
                  icon: Icons.verified_user_rounded,
                  color: AppPalette.sky,
                ),
              ),
            ),
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: controller.certificates.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final certificate = controller.certificates[index];
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            const AccentIconBadge(
                              icon: Icons.verified_user_rounded,
                              color: AppPalette.sky,
                            ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  Text(
                                    certificate.name,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.titleLarge,
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    certificate.subject,
                                    style: Theme.of(context)
                                        .textTheme
                                        .bodyMedium
                                        ?.copyWith(color: AppPalette.muted),
                                  ),
                                ],
                              ),
                            ),
                            IconButton(
                              onPressed: () async {
                                try {
                                  await controller.removeTrustedCertificate(
                                    certificate.fingerprint256,
                                  );
                                } catch (error) {
                                  if (!context.mounted) {
                                    return;
                                  }
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(
                                      content: Text(describeError(error)),
                                    ),
                                  );
                                }
                              },
                              icon: const Icon(Icons.delete_outline_rounded),
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        Text('Fingerprint: ${certificate.fingerprint256}'),
                        Text(
                          'Valid: ${certificate.validFrom} -> ${certificate.validTo}',
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class AuditView extends StatefulWidget {
  const AuditView({super.key, required this.controller});

  final AppController controller;

  @override
  State<AuditView> createState() => _AuditViewState();
}

class _AuditViewState extends State<AuditView> {
  final TextEditingController _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _searchController.addListener(_handleSearchChanged);
  }

  @override
  void dispose() {
    _searchController.removeListener(_handleSearchChanged);
    _searchController.dispose();
    super.dispose();
  }

  void _handleSearchChanged() {
    setState(() {});
  }

  Future<void> _reloadAudit({String? machineId}) async {
    try {
      await widget.controller.loadAudit(machineId: machineId);
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final machineOptions = buildAuditMachineOptions(
      controller.machines,
      controller.auditEntries,
    ).toList();
    final selectedMachineId = controller.auditFilterMachineId;
    if (selectedMachineId != null &&
        !machineOptions.contains(selectedMachineId)) {
      machineOptions.insert(0, selectedMachineId);
    }
    final searchQuery = _searchController.text.trim().toLowerCase();
    final visibleEntries = searchQuery.isEmpty
        ? controller.auditEntries
        : controller.auditEntries
              .where((entry) => entry.searchableText.contains(searchQuery))
              .toList();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Audit Trail',
          title: '审计日志',
          subtitle: '按机台过滤、全文搜索，并快速回溯初始化与敏感操作记录。',
          actions: <Widget>[
            OutlinedButton.icon(
              onPressed: () =>
                  _reloadAudit(machineId: controller.auditFilterMachineId),
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新审计'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: <Widget>[
            SizedBox(
              width: 320,
              child: DropdownMenu<String>(
                key: ValueKey<String>(
                  controller.auditFilterMachineId ?? '__all__',
                ),
                width: 320,
                enableFilter: true,
                enableSearch: true,
                label: const Text('按机台过滤'),
                hintText: '全部机台',
                initialSelection: controller.auditFilterMachineId,
                dropdownMenuEntries: machineOptions
                    .map(
                      (machineId) => DropdownMenuEntry<String>(
                        value: machineId,
                        label: machineId,
                      ),
                    )
                    .toList(),
                onSelected: (value) => _reloadAudit(machineId: value),
              ),
            ),
            SizedBox(
              width: 320,
              child: TextField(
                controller: _searchController,
                decoration: InputDecoration(
                  labelText: '搜索审计内容',
                  hintText: '输入机台 ID、事件键、原因等',
                  prefixIcon: const Icon(Icons.search_rounded),
                  suffixIcon: searchQuery.isEmpty
                      ? null
                      : IconButton(
                          onPressed: _searchController.clear,
                          icon: const Icon(Icons.close_rounded),
                        ),
                ),
              ),
            ),
            if (controller.auditFilterMachineId != null)
              TextButton.icon(
                onPressed: () => _reloadAudit(machineId: null),
                icon: const Icon(Icons.filter_alt_off_rounded),
                label: const Text('清除机台过滤'),
              ),
          ],
        ),
        const SizedBox(height: 12),
        if (controller.auditFilterMachineId != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: Text(
              '当前仅显示机台 ${controller.auditFilterMachineId} 的审计记录。',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ),
        if (controller.auditEntries.isEmpty)
          Expanded(
            child: Center(
              child: Text(
                controller.auditFilterMachineId == null
                    ? '暂时没有审计记录。'
                    : '所选机台暂时没有审计记录。',
              ),
            ),
          )
        else if (visibleEntries.isEmpty)
          const Expanded(child: Center(child: Text('没有匹配搜索条件的审计记录。')))
        else
          Expanded(
            child: ListView.separated(
              itemCount: visibleEntries.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final entry = visibleEntries[index];
                final presentation = entry.presentation;
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            AccentIconBadge(
                              icon: entry.result == 'success'
                                  ? Icons.task_alt_rounded
                                  : Icons.error_outline_rounded,
                              color: entry.result == 'success'
                                  ? AppPalette.mint
                                  : AppPalette.coral,
                            ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  Text(
                                    presentation.title,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.titleMedium,
                                  ),
                                  const SizedBox(height: 6),
                                  Text(
                                    presentation.description,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.bodyMedium,
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Text(
                          '时间：${entry.localizedTimestamp}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '操作主体：${entry.localizedActor} · 结果：${entry.localizedResult} · 来源：${entry.normalizedIp}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                        if (entry.machineId != null) ...<Widget>[
                          const SizedBox(height: 2),
                          Text(
                            '机台：${entry.machineId}',
                            style: Theme.of(context).textTheme.bodyMedium,
                          ),
                        ],
                        const SizedBox(height: 2),
                        Text(
                          '接口：${entry.displayPath}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '事件键：${entry.type.isEmpty ? 'unknown' : entry.type}',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class SettingsView extends StatefulWidget {
  const SettingsView({super.key, required this.controller});

  final AppController controller;

  @override
  State<SettingsView> createState() => _SettingsViewState();
}

class _SettingsViewState extends State<SettingsView> {
  late final TextEditingController _defaultVhdController;
  final TextEditingController _currentPasswordController =
      TextEditingController();
  final TextEditingController _newPasswordController = TextEditingController();
  final TextEditingController _confirmPasswordController =
      TextEditingController();
  final TextEditingController _otpRotateCurrentCodeController =
      TextEditingController();
  final TextEditingController _otpRotateIssuerController =
      TextEditingController();
  final TextEditingController _otpRotateAccountController =
      TextEditingController();
  final TextEditingController _otpRotateNewCodeController =
      TextEditingController();

  @override
  void initState() {
    super.initState();
    _defaultVhdController = TextEditingController(
      text: widget.controller.serverStatus?.defaultVhdKeyword ?? 'SDEZ',
    );
  }

  @override
  void dispose() {
    _defaultVhdController.dispose();
    _currentPasswordController.dispose();
    _newPasswordController.dispose();
    _confirmPasswordController.dispose();
    _otpRotateCurrentCodeController.dispose();
    _otpRotateIssuerController.dispose();
    _otpRotateAccountController.dispose();
    _otpRotateNewCodeController.dispose();
    super.dispose();
  }

  Widget _buildOtpRotationImportPanel(InitializationPreparation preparation) {
    final otpauthUrl = normalizeOtpauthUrl(preparation.otpauthUrl);

    return InfoPanel(
      title: '新的 OTP 绑定信息',
      icon: Icons.qr_code_scanner_rounded,
      color: AppPalette.coral,
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
                  AppPalette.coral.withValues(alpha: 0.08),
                ],
              ),
              borderRadius: BorderRadius.circular(22),
              border: Border.all(
                color: AppPalette.coral.withValues(alpha: 0.18),
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
                      color: AppPalette.coralDeep,
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
                  '旧绑定会一直保留，直到你使用新的绑定密钥生成验证码并验证通过。',
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
                  '如果验证器不支持扫码，可以使用下面的参数手动绑定。',
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
    final status = widget.controller.serverStatus;
    final rotationPreparation = widget.controller.otpRotationPreparation;
    return ListView(
      children: <Widget>[
        const PageHeader(
          eyebrow: 'Security Settings',
          title: '服务设置',
          subtitle: '调整默认启动关键词、OTP 绑定与管理员密码。',
        ),
        const SizedBox(height: 18),
        SectionPanel(
          title: '服务设置',
          subtitle: '更新默认启动关键词。',
          icon: Icons.tune_rounded,
          color: AppPalette.sky,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              TextField(
                controller: _defaultVhdController,
                decoration: const InputDecoration(labelText: '默认启动关键词'),
              ),
              const SizedBox(height: 12),
              FilledButton.icon(
                onPressed: () async {
                  try {
                    await widget.controller.updateDefaultVhd(
                      _defaultVhdController.text.trim().toUpperCase(),
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
                icon: const Icon(Icons.save_rounded),
                label: const Text('保存默认启动关键词'),
              ),
              if (status != null) ...<Widget>[
                const SizedBox(height: 12),
                Text('数据库状态: ${status.databaseReady ? '正常' : '异常'}'),
                Text('可信注册证书数量: ${status.trustedRegistrationCertificateCount}'),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),
        SectionPanel(
          title: '更换 OTP 绑定密钥',
          subtitle: '先验证旧 OTP，再导入并验证新的 OTP 绑定密钥。',
          icon: Icons.qr_code_2_rounded,
          color: AppPalette.coral,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              if (rotationPreparation == null)
                const InfoPanel(
                  title: '流程说明',
                  body: Text(
                    '提交旧 OTP 验证码后，系统会生成新的绑定密钥。只有在你使用新密钥生成的 OTP 验证成功后，旧绑定才会被替换。',
                  ),
                  icon: Icons.swap_horiz_rounded,
                  color: AppPalette.sun,
                )
              else
                _buildOtpRotationImportPanel(rotationPreparation),
              const SizedBox(height: 12),
              TextField(
                controller: _otpRotateCurrentCodeController,
                decoration: const InputDecoration(labelText: '旧 OTP 验证码'),
              ),
              const SizedBox(height: 12),
              Row(
                children: <Widget>[
                  Expanded(
                    child: TextField(
                      controller: _otpRotateIssuerController,
                      decoration: const InputDecoration(
                        labelText: '新的 OTP Issuer（可选）',
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: TextField(
                      controller: _otpRotateAccountController,
                      decoration: const InputDecoration(
                        labelText: '新的 OTP Account（可选）',
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 12,
                runSpacing: 12,
                children: <Widget>[
                  FilledButton.icon(
                    onPressed: () async {
                      final currentCode = _otpRotateCurrentCodeController.text
                          .trim();
                      if (currentCode.isEmpty) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('请输入旧 OTP 验证码。')),
                        );
                        return;
                      }
                      try {
                        await widget.controller.prepareOtpRotation(
                          currentCode: currentCode,
                          issuer: _otpRotateIssuerController.text.trim(),
                          accountName: _otpRotateAccountController.text.trim(),
                        );
                        _otpRotateNewCodeController.clear();
                        if (!context.mounted) {
                          return;
                        }
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(
                            content: Text('新的 OTP 绑定密钥已生成，请导入后验证新验证码。'),
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
                    label: Text(
                      rotationPreparation == null ? '生成新的绑定密钥' : '重新生成绑定密钥',
                    ),
                  ),
                  if (rotationPreparation != null)
                    TextButton.icon(
                      onPressed: () {
                        widget.controller.clearOtpRotationPreparation();
                        _otpRotateNewCodeController.clear();
                      },
                      icon: const Icon(Icons.close_rounded),
                      label: const Text('取消本次更换'),
                    ),
                ],
              ),
              if (rotationPreparation != null) ...<Widget>[
                const SizedBox(height: 16),
                TextField(
                  controller: _otpRotateNewCodeController,
                  decoration: const InputDecoration(labelText: '新 OTP 验证码'),
                ),
                const SizedBox(height: 12),
                FilledButton.tonalIcon(
                  onPressed: () async {
                    final newCode = _otpRotateNewCodeController.text.trim();
                    if (newCode.isEmpty) {
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('请输入新的 OTP 验证码。')),
                      );
                      return;
                    }
                    try {
                      await widget.controller.completeOtpRotation(newCode);
                      _otpRotateCurrentCodeController.clear();
                      _otpRotateNewCodeController.clear();
                      if (!context.mounted) {
                        return;
                      }
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('OTP 绑定密钥已更换。')),
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
                  icon: const Icon(Icons.verified_rounded),
                  label: const Text('验证新绑定并替换旧绑定'),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),
        SectionPanel(
          title: '修改管理员密码',
          subtitle: '服务端要求新密码长度至少 12 位。',
          icon: Icons.password_rounded,
          color: AppPalette.mint,
          child: Column(
            children: <Widget>[
              buildSecureTextField(
                controller: _currentPasswordController,
                autofillHints: const <String>[AutofillHints.password],
                decoration: const InputDecoration(labelText: '当前密码'),
              ),
              const SizedBox(height: 12),
              buildSecureTextField(
                controller: _newPasswordController,
                autofillHints: const <String>[AutofillHints.newPassword],
                decoration: const InputDecoration(labelText: '新密码'),
              ),
              const SizedBox(height: 12),
              buildSecureTextField(
                controller: _confirmPasswordController,
                autofillHints: const <String>[AutofillHints.newPassword],
                decoration: const InputDecoration(labelText: '确认新密码'),
              ),
              const SizedBox(height: 12),
              FilledButton.tonalIcon(
                onPressed: () async {
                  if (_newPasswordController.text !=
                      _confirmPasswordController.text) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('两次输入的新密码不一致。')),
                    );
                    return;
                  }
                  try {
                    await widget.controller.changePassword(
                      _currentPasswordController.text,
                      _newPasswordController.text,
                    );
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(
                      context,
                    ).showSnackBar(const SnackBar(content: Text('管理员密码已更新。')));
                  } catch (error) {
                    if (!context.mounted) {
                      return;
                    }
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(content: Text(describeError(error))),
                    );
                  }
                },
                icon: const Icon(Icons.password_rounded),
                label: const Text('更新密码'),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class SectionPanel extends StatelessWidget {
  const SectionPanel({
    super.key,
    required this.title,
    required this.subtitle,
    required this.child,
    this.icon = Icons.auto_awesome_rounded,
    this.color = AppPalette.coral,
  });

  final String title;
  final String subtitle;
  final Widget child;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return AppPanel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              AccentIconBadge(icon: icon, color: color),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      title,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 6),
                    Text(
                      subtitle,
                      style: Theme.of(
                        context,
                      ).textTheme.bodyMedium?.copyWith(color: AppPalette.muted),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 18),
          child,
        ],
      ),
    );
  }
}

class InfoPanel extends StatelessWidget {
  const InfoPanel({
    super.key,
    required this.title,
    required this.body,
    this.icon = Icons.tips_and_updates_rounded,
    this.color = AppPalette.sky,
  });

  final String title;
  final Widget body;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            color.withValues(alpha: 0.16),
            Colors.white.withValues(alpha: 0.84),
          ],
        ),
        borderRadius: BorderRadius.circular(22),
        border: Border.all(color: color.withValues(alpha: 0.2)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              AccentIconBadge(icon: icon, color: color, size: 42),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(title, style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 6),
                    body,
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class ErrorBanner extends StatelessWidget {
  const ErrorBanner({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[
            AppPalette.danger.withValues(alpha: 0.16),
            Colors.white.withValues(alpha: 0.84),
          ],
        ),
        borderRadius: BorderRadius.circular(22),
        border: Border.all(color: AppPalette.danger.withValues(alpha: 0.22)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          const AccentIconBadge(
            icon: Icons.error_outline_rounded,
            color: AppPalette.danger,
            size: 42,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              message,
              style: Theme.of(
                context,
              ).textTheme.bodyMedium?.copyWith(color: AppPalette.ink),
            ),
          ),
        ],
      ),
    );
  }
}

class StatusChip extends StatelessWidget {
  const StatusChip({super.key, required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        border: Border.all(color: color.withValues(alpha: 0.22)),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 8),
          Text(
            label,
            style: TextStyle(
              fontFamily: _miSansFamily700,
              color: color,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

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
