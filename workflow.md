# Workflow hiện tại - get link manga

Tài liệu này là chuẩn làm việc cho repo hiện tại.

## 1. Quy tắc chung
- Luôn trả lời tiếng Việt.
- Ưu tiên xóa hơn thêm.
- Không tạo abstraction mới nếu chưa cần.
- UI/WPF phải đi qua `Dispatcher`.
- I/O dài phải dùng `async/await` và `CancellationToken`.
- Build xong phải sạch `0 error, 0 warning`.
- Khi đổi code xong: build, rồi commit, rồi push.

## 2. Cấu trúc code hiện tại

### File chính
- `MainWindow.xaml`
- `MainWindow.xaml.cs` chỉ giữ partial class rỗng.
- `MainWindow.SystemBootstrap.cs`
- `MainWindow.SystemCaptcha.cs`
- `MainWindow.SystemFolders.cs`
- `MainWindow.SystemExplorer.cs`
- `MainWindow.SystemMessageBox.cs`
- `MainWindow.SystemComboBox.cs`
- `MainWindow.UIBootstrap.cs`
- `MainWindow.UIResponsive.cs`
- `MainWindow.UIResultsGrid.cs`
- `MainWindow.UINewFeatures.cs`
- `MainWindow.Download.cs`
- `MainWindow.DownloadPipeline.cs`
- `MainWindow.DownloadState.cs`
- `MainWindow.Tab*.cs`

### File riêng cho folder type
- Có.
- File quản lý chính: `MainWindow.SystemFolders.cs`
- Helper đang dùng cho single/multi comic: `GetDownloadChapterFolderName()` trong `MainWindow.Download.cs`

## 3. Tính năng hiện tại

### Queue tải
- Danh sách truyện trong `Extracted gallery links`.
- Có sort theo:
  - tên
  - trạng thái
  - tiến trình
  - speed
- Speed luôn sort từ cao xuống thấp.
- `remove completed` là xóa truyện hoàn tất khỏi queue, không phải chỉ ẩn.

### Folder type
- `Single comic`
  - `root\book name\chapter name`
- `Multi-comic`
  - `root\book name - chapter name`

### Captcha
- Hỗ trợ captcha/bypass cho các domain chính.
- Nếu captcha xong thì phải trả cookie/user-agent về main window.
- Không làm main window tự minimize sau khi captcha xong.

### Error log
- Xóa error trong error log không được làm crash app.
- Không được đánh dấu completed nhầm khi chỉ vừa clear error.

### Download
- Có retry, pause, resume.
- Có xử lý `.tmp`.
- Có xử lý 429 cho từng book/domain.

## 4. Luồng làm việc đúng

1. Đọc `workflow.md` trước khi sửa.
2. Xác định lane:
   - Manga/Hentai
   - Novel
   - UI/System
3. Tìm file đúng partial trước khi sửa.
4. Sửa ít file nhất có thể.
5. Build.
6. Nếu còn lỗi/warning thì sửa tiếp.
7. Commit.
8. Push.

## 5. Quy tắc UI
- Theme phải giữ cyberpunk.
- Nút close/maximize/minimize phải khít mép khi maximize.
- Control góc phải phải dễ bấm.
- Không để margin/responsive tạo vùng click chết ở mép cửa sổ.

## 6. Quy tắc update queue
- `Completed` chỉ set khi book thật sự tải xong.
- Không được set `Completed` chỉ vì error list rỗng.
- Không được remove book giữa chừng.
- Nếu còn page/chapter đang tải thì trạng thái vẫn phải là `Downloading`.

## 7. Quy tắc build
- Dùng `.\build.bat`.
- Chỉ dừng khi build sạch.
- Không bỏ qua warning mới.

## 8. Quy tắc Git
- Commit theo thay đổi thật.
- Không kéo file rác debug/temp vào commit.
- Push lên `main`.

