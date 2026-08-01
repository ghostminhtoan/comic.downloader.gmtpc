# AGENTS.md - Central Guidelines Redirect

## Build Commands
- **Restore NuGet Packages:** use shared cache at `%UserProfile%\.nuget\packages`
- **Build Solution (Release):** run `.\build.bat`
- **Run Application:** execute `bin\Release\Comic-GMTPC.exe`

## Critical Rule for Codex
Before writing, modifying, or analyzing any code in this repository, you MUST read the central workflow guidelines file in Vietnamese:
👉 [workflow.md](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/workflow.md)

LƯU Ý QUAN TRỌNG:
Trước khi viết code, chỉnh sửa hoặc phân tích dự án này, bạn BẮT BUỘC phải đọc kỹ toàn bộ quy tắc code, tiêu chuẩn giao diện và luồng xử lý tại file:
👉 [workflow.md](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/workflow.md)

### Code Splitting Rule
DO NOT write or modify code in [MainWindow.xaml.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.xaml.cs) directly.
All logic is split into:
- [MainWindow.SystemActions.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.SystemActions.cs) (Actions, Save/Load, Clipboard)
- [MainWindow.TabHentaiforce.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.TabHentaiforce.cs) (Crawling logic)
- [MainWindow.UIResponsive.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.UIResponsive.cs) (Responsive layout sizing)
- [MainWindow.UIResultsGrid.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.UIResultsGrid.cs) (Results list event handlers)
