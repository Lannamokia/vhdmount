#include "flutter_window.h"

#include <optional>

#include "flutter/generated_plugin_registrant.h"

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

  RECT frame = GetClientArea();

  // The size here must match the window dimensions to avoid unnecessary surface
  // creation / destruction in the startup path.
  flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
      frame.right - frame.left, frame.bottom - frame.top, project_);
  // Ensure that basic setup of the controller was successful.
  if (!flutter_controller_->engine() || !flutter_controller_->view()) {
    return false;
  }
  RegisterPlugins(flutter_controller_->engine());
  flutter_content_window_ = flutter_controller_->view()->GetNativeWindow();
  SetChildContent(flutter_content_window_);
  RegisterSecureInputChannel();

  flutter_controller_->engine()->SetNextFrameCallback([&]() {
    this->Show();
  });

  // Flutter can complete the first frame before the "show window" callback is
  // registered. The following call ensures a frame is pending to ensure the
  // window is shown. It is a no-op if the first frame hasn't completed yet.
  flutter_controller_->ForceRedraw();

  return true;
}

void FlutterWindow::OnDestroy() {
  RestoreSecureInputContext();
  secure_input_channel_.reset();

  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  flutter_content_window_ = nullptr;

  Win32Window::OnDestroy();
}

void FlutterWindow::RegisterSecureInputChannel() {
  secure_input_channel_ =
      std::make_unique<flutter::MethodChannel<flutter::EncodableValue>>(
          flutter_controller_->engine()->messenger(),
          "vhd_mount_admin_flutter/secure_input",
          &flutter::StandardMethodCodec::GetInstance());

  secure_input_channel_->SetMethodCallHandler(
      [this](const flutter::MethodCall<flutter::EncodableValue>& call,
             std::unique_ptr<flutter::MethodResult<flutter::EncodableValue>>
                 result) {
        if (call.method_name() != "setSecureInputEnabled") {
          result->NotImplemented();
          return;
        }

        const auto* arguments = std::get_if<flutter::EncodableMap>(call.arguments());
        if (arguments == nullptr) {
          result->Error("bad-args", "Expected a map payload.");
          return;
        }

        const auto enabled_it = arguments->find(flutter::EncodableValue("enabled"));
        if (enabled_it == arguments->end()) {
          result->Error("bad-args", "Missing enabled flag.");
          return;
        }

        const auto* enabled = std::get_if<bool>(&enabled_it->second);
        if (enabled == nullptr) {
          result->Error("bad-args", "Enabled flag must be a bool.");
          return;
        }

        SetSecureInputEnabled(*enabled);
        result->Success(flutter::EncodableValue(true));
      });
}

void FlutterWindow::SetSecureInputEnabled(bool enabled) {
  if (flutter_content_window_ == nullptr || enabled == secure_input_enabled_) {
    return;
  }

  if (enabled) {
    if (HIMC active_context = ImmGetContext(flutter_content_window_)) {
      ImmSetOpenStatus(active_context, FALSE);
      ImmReleaseContext(flutter_content_window_, active_context);
    }

    saved_ime_context_ = ImmAssociateContext(flutter_content_window_, nullptr);
    secure_input_enabled_ = true;
    return;
  }

  RestoreSecureInputContext();
}

void FlutterWindow::RestoreSecureInputContext() {
  if (flutter_content_window_ == nullptr) {
    saved_ime_context_ = nullptr;
    secure_input_enabled_ = false;
    return;
  }

  if (secure_input_enabled_) {
    if (saved_ime_context_ != nullptr) {
      ImmAssociateContext(flutter_content_window_, saved_ime_context_);
      saved_ime_context_ = nullptr;
    } else {
      ImmAssociateContextEx(flutter_content_window_, nullptr, IACE_DEFAULT);
    }
  }

  secure_input_enabled_ = false;
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
  // Give Flutter, including plugins, an opportunity to handle window messages.
  if (flutter_controller_) {
    std::optional<LRESULT> result =
        flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                      lparam);
    if (result) {
      return *result;
    }
  }

  switch (message) {
    case WM_FONTCHANGE:
      flutter_controller_->engine()->ReloadSystemFonts();
      break;
  }

  return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}
