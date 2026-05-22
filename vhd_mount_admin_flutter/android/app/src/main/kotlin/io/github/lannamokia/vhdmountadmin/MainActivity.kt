package io.github.lannamokia.vhdmountadmin

import io.flutter.embedding.android.FlutterFragmentActivity

// local_auth 在 Android 上要求宿主 Activity 是 FragmentActivity，
// 否则 BiometricPrompt 无法挂载，canCheckBiometrics 返回 false。
class MainActivity : FlutterFragmentActivity()