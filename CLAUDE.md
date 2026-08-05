# CLAUDE.md - Central Guidelines Redirect

## Build Commands
- **Restore NuGet Packages:** use shared cache at `%UserProfile%\.nuget\packages`
- **Build Solution (Release):** run `.\build.bat`
- **Run Application:** execute `bin\Release\Comic-GMTPC.exe`

## Critical Rule for Claude Code
Before writing, modifying, or analyzing any code in this repository, you MUST read the central workflow guidelines file in Vietnamese:
👉 [workflow.md](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/workflow.md)

LƯU Ý QUAN TRỌNG:
Trước khi viết code, chỉnh sửa hoặc phân tích dự án này, bạn BẮT BUỘC phải đọc kỹ toàn bộ quy tắc code, tiêu chuẩn giao diện và luồng xử lý tại file:
👉 [workflow.md](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/workflow.md)

### Code Splitting Rule
DO NOT write or modify code in [MainWindow.xaml.cs](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/MainWindow.xaml.cs) directly.
All logic is split into:
- [MainWindow.SystemActions.cs](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/MainWindow.SystemActions.cs) (Actions, Save/Load, Clipboard)
- [MainWindow.TabHentaiforce.cs](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/MainWindow.TabHentaiforce.cs) (Crawling logic)
- [MainWindow.UIResponsive.cs](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/MainWindow.UIResponsive.cs) (Responsive layout sizing)
- [MainWindow.UIResultsGrid.cs](file:///c:/Users/Admin/source/repos/ghostminhtoan/Comic%20Downloader%20GMTPC/MainWindow.UIResultsGrid.cs) (Results list event handlers)
