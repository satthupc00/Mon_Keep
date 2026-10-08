# Mondiro GlowBoard

Board ghi chú dạng widget nằm trên desktop Windows, chạy ở khay hệ thống (system tray).

![preview](release/GlowBoard_Preview.png)

## Tải về & chạy
1. Vào trang **Releases** của repo, tải `GlowBoard_Windows_x64.zip` của bản mới nhất, giải nén ra một thư mục không cần quyền admin (ví dụ `D:\Apps\GlowBoard`, **không** để trong `Program Files`).
2. Chạy `GlowBoard.exe`. App cần **Microsoft Edge WebView2 Runtime**: Windows 11 có sẵn; nếu máy bạn thiếu, app sẽ hỏi và mở trang tải về.
3. Muốn GlowBoard tự bật cùng Windows: chuột phải icon ở khay hệ thống → **Khởi động cùng Windows**.


## Board của team (đồng bộ online)
- Lần đầu mở app: nhập **mã team** (do quản lý gửi) và **tên hiển thị**. Từ đó app tự vào lại team mỗi lần mở.
- Mọi người trong team thấy thay đổi của nhau gần như ngay lập tức. Mất mạng vẫn ghi được, có mạng lại tự đồng bộ.
- Rê chuột vào một dòng ~0.7 giây để xem **"Sửa bởi … · … trước"**.
- Chấm nhỏ cạnh tên team: xanh = đã đồng bộ, xám = đang offline.
- Khay hệ thống → **Rời team / nhập mã khác**.

### Admin (quản lý)
- Khay hệ thống → **Chế độ Admin…** (hoặc link "Đăng nhập Admin" ở màn hình nhập mã) → đăng nhập email + mật khẩu Admin.
- **＋ Tạo team**: đặt tên, app tự sinh mã (VD `DRAGON-K6EEAB`) và copy sẵn để gửi cho team.
- **Mở board**: xem / sửa board của team đó (nút ← để quay lại danh sách).
- **Đổi mã**: mã cũ hết hiệu lực ngay, máy đang dùng mã cũ phải nhập mã mới.
- **Khóa team / Mở khóa**: tạm ngưng cả team truy cập board, dữ liệu vẫn giữ nguyên.
- **Nhập board cũ**: đưa board cá nhân của bản trước (trên máy Admin) vào board của team.
- Luật bảo mật nằm ở `firestore.rules` – dán vào Firebase Console → Firestore → **Rules** → **Publish** mỗi khi file này thay đổi.

## Cách dùng board
| Thao tác | Cách làm |
|---|---|
| Bật / tắt đầu mục | Click vào chấm tròn (xanh lá nhấp nháy = bật, xám = tắt, chữ cũng chuyển xám) |
| Đổi thứ tự | Nhấn giữ vào dòng rồi kéo lên/xuống (dòng đang gõ chữ: giữ ~0.3 giây rồi kéo) |
| Gõ chữ | Click vào dòng. Đầu mục in đậm màu xanh dạ quang, mục con chữ trắng thường |
| Thêm / xoá dòng | Rê chuột vào dòng → nút **+** / **−** ở cuối dòng (xoá sẽ hỏi lại). Board trống: nút **＋ Thêm đầu mục** |
| Thêm mục con | Nút **↳** ở cuối dòng đầu mục |
| Thu gọn / mở mục con | Mũi tên **⌄ / ›** ở sau các nút link của đầu mục (lưu riêng trên máy bạn; khi thu gọn sẽ hiện số mục con) |
| Nút link sau chữ đầu mục | **click** = mở link, **double click** = chèn / sửa link (nút sáng khi đã có link) |
| Config các nút link | Nút ⚙ trên thanh tiêu đề (cạnh nút ghim): thêm nút mới với icon PNG, sửa tooltip, ẩn / hiện, xoá. 4 nút mặc định (Reference, List task, Feedback GD, Miro) chỉ ẩn / hiện được. Áp dụng chung cả team |
| Dòng mới | `Enter` · xuống dòng trong cùng mục: `Shift+Enter` |
| Đổi cấp | `Tab`: đầu mục → mục con · `Shift+Tab`: mục con → đầu mục |
| Di chuyển / mở rộng board | Kéo thanh tiêu đề · kéo các cạnh viền |
| Luôn nằm trên cùng | Nút ghim 📌 trên thanh tiêu đề, hoặc khay hệ thống → **Luôn nằm trên cùng** |
| Hiện board lên trên | Click icon ở khay hệ thống (board tự lùi xuống dưới khi bạn click ra chỗ khác) |

## Tự cập nhật
Mỗi lần push thay đổi trong `src/` lên GitHub, GitHub Actions (`.github/workflows/release.yml`) tự build và tạo một **Release** mới (`v1.0.<số lần build>`).
App kiểm tra bản mới 30 giây sau khi mở và mỗi 2 giờ. Khi có bản mới, app tải sẵn và tự khởi động lại lúc bạn không thao tác trên board.
Chuột phải icon ở khay hệ thống → **Kiểm tra cập nhật** để cập nhật ngay.

Repo cần để **public** để app tải được bản mới.

## Build từ source
Cần .NET SDK 8 (build được trên Windows hoặc Linux):
```
cd src/GlowBoard
dotnet build -c Release
```
Kết quả nằm ở `src/GlowBoard/bin/Release/net48/`. App chạy trên .NET Framework 4.8 (có sẵn trong Windows 10/11) nên không cần cài thêm gì.

- `Web/index.html`: toàn bộ giao diện board (HTML/CSS/JS), được nhúng vào exe
- `Web/fb.js`: thư viện Firebase đã gói sẵn (tạo lại: `cd src/GlowBoard/WebSrc && npm install && npm run build`)
- `firestore.rules`: luật bảo mật Firestore
- `MainForm.cs`: cửa sổ widget, khay hệ thống, lưu/mở file
- `Assets/`: icon app

Created by Mondiro
