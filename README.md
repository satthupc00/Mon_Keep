# Mondiro GlowBoard

Board ghi chú dạng widget nằm trên desktop Windows, chạy ở khay hệ thống (system tray).

![preview](release/GlowBoard_Preview.png)

## Tải về & chạy
1. Tải `release/GlowBoard_v1.0_Windows_x64.zip`, giải nén ra một thư mục (ví dụ `D:\Apps\GlowBoard`).
2. Chạy `GlowBoard.exe`. App cần **Microsoft Edge WebView2 Runtime**: Windows 11 có sẵn; nếu máy bạn thiếu, app sẽ hỏi và mở trang tải về.
3. Muốn GlowBoard tự bật cùng Windows: chuột phải icon ở khay hệ thống → **Khởi động cùng Windows**.

`release/GlowBoard_Preview.html` mở thẳng bằng trình duyệt để xem thử giao diện (không cần cài).

## Cách dùng
| Thao tác | Cách làm |
|---|---|
| Bật / tắt đầu mục | Click vào chấm tròn (xanh lá = bật, trắng xám = tắt) |
| Đổi thứ tự | Giữ chuột lên chữ ~0.3 giây rồi kéo lên/xuống |
| Thêm / xoá dòng | Rê chuột vào dòng → nút **+** / **−** ở cuối dòng (xoá sẽ hỏi lại) |
| Thêm mục con | Nút **↳** trên dòng, hoặc **Mục con ▾** trên thanh công cụ → chọn loại có ô check hoặc thường |
| In đậm | Bôi đen chữ → `Ctrl+B` |
| Chèn link | Bôi đen chữ → `Ctrl+K` (web hoặc đường dẫn thư mục trên máy). `Ctrl+Click` để mở |
| Dòng mới | `Enter` · xuống dòng trong cùng mục: `Shift+Enter` |
| Đổi cấp | `Tab`: đầu mục → mục con · `Shift+Tab`: mục con → đầu mục |
| Lưu / Mở / Mới | `Ctrl+S` (`Ctrl+Shift+S` = Lưu thành…) · `Ctrl+O` · `Ctrl+N` |
| Di chuyển / mở rộng board | Kéo thanh tiêu đề · kéo các cạnh viền |
| Hiện board lên trên | Click icon ở khay hệ thống (board tự lùi xuống dưới khi bạn click ra chỗ khác) |

Board **tự lưu** liên tục: vào file `.gboard` đang mở, hoặc vào `%APPDATA%\Mondiro\GlowBoard\board.gboard` nếu chưa lưu thành file.

## Build từ source
Cần .NET SDK 8 (build được trên Windows hoặc Linux):
```
cd src/GlowBoard
dotnet build -c Release
```
Kết quả nằm ở `src/GlowBoard/bin/Release/net48/`. App chạy trên .NET Framework 4.8 (có sẵn trong Windows 10/11) nên không cần cài thêm gì.

- `Web/index.html`: toàn bộ giao diện board (HTML/CSS/JS), được nhúng vào exe
- `MainForm.cs`: cửa sổ widget, khay hệ thống, lưu/mở file
- `Assets/`: icon app

Created by Mondiro
