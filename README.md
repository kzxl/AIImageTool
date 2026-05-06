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

### 3. Color Lab (Hiệu chỉnh màu sắc)
- Cung cấp giao diện trực quan hỗ trợ xem, chọn và tinh chỉnh màu sắc cho ảnh.
- **Selective Color Grading:** Thay thế có chọn lọc một dải màu cụ thể sang màu đích với chuyển đổi mềm mại dựa trên không gian màu HSL.
- **Phân tích Palette tự động (K-Means):** Tự động trích xuất 5 màu chủ đạo và gợi ý bảng phối màu (Analogous, Complementary, Split-Complementary, Triadic).
- **LUT Processor (.cube):** Tải và áp dụng file 3D LUT chuẩn `.cube` với điều chỉnh cường độ hiệu ứng (intensity slider).
- **White Balance:**
  - **Auto (Gray World):** Tự động cân bằng trắng theo thuật toán Gray World.
  - **Manual (White Point Pick):** Cân bằng trắng thủ công bằng cách chọn màu điểm tham chiếu trên ảnh.
- **Color Unification:** Đồng nhất tone màu toàn ảnh về một gam màu chỉ định với điều chỉnh cường độ.
- **Noise Reduction:** Khử nhiễu màu sắc (Color Noise) cho ảnh bằng bộ lọc thích ứng.
- Cơ chế khóa an toàn, ngăn xung đột khi đang load dữ liệu hình ảnh lớn.

### 4. Meta Editor (Chỉnh sửa siêu dữ liệu)
- Đọc, hiển thị và cho phép điều chỉnh trực tiếp thông tin Metadata/EXIF đính kèm của hình ảnh.
- Tích hợp tính năng khoá luồng (Locking Mechanism) đảm bảo ổn định và an toàn dữ liệu trong suốt quá trình người dùng tinh chỉnh.

### 5. Vision Tagger — AI Auto Tagger (Phân tích & gán nhãn ảnh)
- Tự động phân tích nội dung hình ảnh và sinh ra mô tả văn bản (Caption) cùng danh sách Tag từ khóa bằng AI Vision.
- Giao diện xem trước ảnh tích hợp, hỗ trợ định dạng JPG, JPEG, PNG, WebP.
- Chức năng **Copy Description** và **Copy Tags** (định dạng `tag1, tag2, tag3`) tiện lợi cho các workflow AI prompt engineering và quản lý thư viện ảnh.
- Kiến trúc mở: Backend AI có thể tích hợp ONNX Local Model, Python Vision Worker hoặc Cloud API.

### Core & Host Architecture
- Giao diện người dùng Windows Presentation Foundation (WPF) hiện đại, phản hồi kết quả AI theo tiến trình thời gian thực.
- Khả năng **Hot-load Plugins:** Module lõi tự động dò tìm và nạp thư viện `IImagePlugin` từ các tệp `.dll` đặt trong thư mục `Plugins`.
- **Kiến trúc Multi-Thread (In-Process Parallel):** Đảm bảo xử lý AI và render hoàn toàn In-Process bằng OnnxRuntime tối ưu, giải quyết triệt để lỗi thắt cổ chai, chia sẻ Native DLLs ngay lập tức và tránh các lỗi LoadLibrary từ hệ điều hành.

## Yêu cầu hệ thống

- Hệ điều hành: Windows 10/11 (64-bit).
- Nếu sử dụng bản *Lite*: Cần cài đặt Microsoft .NET 8 Desktop Runtime.
- **Bắt buộc:** [Visual C++ 2015-2022 Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe) để thư viện AI Native khởi tạo thành công.
- Khuyên dùng Card đồ hoạ bất kỳ tương thích thư viện DirectML (NVIDIA seri 10 trở lên, Intel UHD Graphics, hoặc AMD Radeon).

## Cài đặt từ Release
Đi tới mục [Releases](../../releases) để tải:
1. `ImageTool_Lite_Win_x64.zip`: Dành cho máy đã cài sẵn .NET 8.
2. `ImageTool_Full_Win_x64.zip`: Bản đóng gói trọn bộ (Click Run directly, không cần cài đặt), thích hợp đem chạy thử trên mọi máy tính.

## License

Dự án này được cấp phép theo giấy phép **Apache License 2.0**. Xem tệp [LICENSE](LICENSE) để biết danh sách các quyền và giới hạn sử dụng chính. Tóm bộ khung kiến trúc và chia sẻ tự do cho mọi nhu cầu nội bộ, thương mại hoá.
