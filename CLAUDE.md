# CLAUDE.md - Central Guidelines Redirect

## Build Commands
- **Restore NuGet Packages:** use shared cache at `%UserProfile%\.nuget\packages`
- **Build Solution (Release):** run `.\build.bat`
- **Run Application:** execute `bin\Release\Comic-GMTPC.exe`

## Critical Rule for Claude Code
Before writing, modifying, or analyzing any code in this repository, you MUST read the central workflow guidelines file in Vietnamese:
👉 [workflow.md](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/workflow.md)

LƯU Ý QUAN TRỌNG:
Tru?c khi vi?t code, ch?nh s?a ho?c ph�n t�ch d? �n n�y, b?n B?T BU?C ph?i d?c k? to�n b? quy t?c code, ti�u chu?n giao di?n v� lu?ng x? l� t?i file:
👉 [workflow.md](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/workflow.md)

### Code Splitting Rule
DO NOT write or modify code in [MainWindow.xaml.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.xaml.cs) directly.
All logic is split into:
- [MainWindow.SystemActions.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.SystemActions.cs) (Actions, Save/Load, Clipboard)
- [MainWindow.TabHentaiforce.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.TabHentaiforce.cs) (Crawling logic)
- [MainWindow.UIResponsive.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.UIResponsive.cs) (Responsive layout sizing)
- [MainWindow.UIResultsGrid.cs](file:///r:/HDD%20R/ZC%20SYMLINK/USERS/source/repos/ghostminhtoan/get%20link%20manga/MainWindow.UIResultsGrid.cs) (Results list event handlers)
