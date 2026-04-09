# AIImageTool

AIImageTool là một ứng dụng Desktop (WPF, .NET 8) mã nguồn mở phục vụ cho mục đích xử lý và nâng cấp hình ảnh sử dụng trí tuệ nhân tạo (AI). Dự án được thiết kế với kiến trúc **Plugin mở rộng (Extensible Plugin Architecture)**, mang lại khả năng tích hợp linh hoạt các luồng xử lý AI đa dạng mà không làm thay đổi hệ thống lõi.

## Các tính năng chính (Plugins)

### 1. AI Upscaler (Nâng cấp độ phân giải)
- **Model sử dụng:** 4x-UltraSharpV2 (ONNX FP32).
- **Phóng đại chi tiết:** Hỗ trợ Upscale gấp 4 lần (x4) độ phân giải ảnh gốc với độ sắc nét cực cao.
- **Tối ưu VRAM (Chunking):** Tích hợp công nghệ Tiled Inference lưới 64x64 kết hợp xử lý đa luồng vòng lặp, giúp máy tính có thể upscale ảnh dung lượng lớn mà không sợ tràn bộ nhớ RAM/VRAM.
- **Tính năng Hardware Acceleration:**
  - Hỗ trợ tăng tốc trên mọi loại card đồ hoạ phần cứng nhờ **DirectML** (Tương thích tốt với NVIDIA, AMD, cũng như GPU Intel Onboard).
  - Tự động dò tìm ID Card Onboard và Fallback an toàn về chế độ nội suy CPU nếu lỗi Driver GPU.

### 2. Face Restorer (Khôi phục chi tiết khuôn mặt)
- **Model sử dụng:** GFPGAN (ONNX).
- Phục hồi lại độ sắc nét tự nhiên cho chân dung và khuôn mặt bị vỡ, nhoè, mờ (thường bị mất chi tiết sau khi upscale bằng các model phong cảnh).
- Quy trình chạy song song và tương thích tốt với DirectML qua Hardware Acceleration.

### 3. Core & Host Architecture
- Giao diện người dùng Windows Presentation Foundation (WPF) hiện đại, phản hồi kết quả AI theo tiến trình thời gian thực.
- Khả năng **Hot-load Plugins:** Module lõi tự động dò tìm và nạp thư viện `IImagePlugin` từ các tệp `.dll` đặt trong thư mục `Plugins`.

## Yêu cầu hệ thống

- Hệ điều hành: Windows 10/11 (64-bit).
- Nếu sử dụng bản *Lite*: Microsoft .NET 8 Desktop Runtime.
- Khuyên dùng Card đồ hoạ bất kỳ tương thích thư viện DirectML (NVIDIA seri 10 trở lên, Intel UHD Graphics, hoặc AMD Radeon).

## Cài đặt từ Release
Đi tới mục [Releases](../../releases) để tải:
1. `ImageTool_Lite_Win_x64.zip`: Dành cho máy đã cài sẵn .NET 8.
2. `ImageTool_Full_Win_x64.zip`: Bản đóng gói trọn bộ (Click Run directly, không cần cài đặt), thích hợp đem chạy thử trên mọi máy tính.

## License

Dự án này được cấp phép theo giấy phép **Apache License 2.0**. Xem tệp [LICENSE](LICENSE) để biết danh sách các quyền và giới hạn sử dụng chính. Tóm bộ khung kiến trúc và chia sẻ tự do cho mọi nhu cầu nội bộ, thương mại hoá.
