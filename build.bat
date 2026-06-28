@echo off
:: Đảm bảo code hiển thị đúng font tiếng Việt nếu cần
chcp 65001 > nul

:: Di chuyển đến thư mục chứa script
cd /d "%~dp0"

:: Tắt tiến trình nếu đang chạy (bỏ qua lỗi nếu không tìm thấy)
:: Enable long paths for app + Explorer if running elevated
reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f >nul 2>&1
taskkill /im "Comic-GMTPC.exe" /f >nul 2>&1

:: Khai báo biến thư mục đích để dùng lại cho gọn
set "TARGET_DIR=bin\release\"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

:: ------------------------------------------
:: BƯỚC 1: DỌN DẸP THƯ MỤC
:: Luôn luôn xóa các folder/file ngoài 2 file exe yêu cầu
:: ------------------------------------------
echo [1/4] Dang don dep cac file va folder thua...
:: Xóa file
for %%F in ("%TARGET_DIR%\*") do (
    if /I not "%%~nxF"=="Comic-GMTPC.exe" (
        if /I not "%%~nxF"=="Comic-GMTPC-old.exe" (
            del /q "%%F"
        )
    )
)
:: Xóa folder con
for /d %%D in ("%TARGET_DIR%\*") do (
    rd /s /q "%%D"
)

:: ------------------------------------------
:: BƯỚC 2: WORKFLOW XỬ LÝ FILE BACKUP (-old.exe)
:: ------------------------------------------
echo [2/4] Dang xu ly backup file...
if exist "%TARGET_DIR%\Comic-GMTPC-old.exe" (
    if exist "%TARGET_DIR%\Comic-GMTPC.exe" (
        :: Trường hợp: Có cả 2 file -> Xóa old, đổi tên exe thành old
        del /q "%TARGET_DIR%\Comic-GMTPC-old.exe"
        ren "%TARGET_DIR%\Comic-GMTPC.exe" "Comic-GMTPC-old.exe"
    ) else (
        :: Trường hợp: Đã có sẵn old (nhưng không có exe)
        :: Không làm gì thêm, sẵn sàng build
        echo Da co san ban backup old.
    )
) else (
    if exist "%TARGET_DIR%\Comic-GMTPC.exe" (
        :: Trường hợp: Chỉ có exe (chưa có old) -> Copy exe và rename thành old
        copy /y "%TARGET_DIR%\Comic-GMTPC.exe" "%TARGET_DIR%\Comic-GMTPC-old.exe" > nul
        :: Mẹo an toàn: Xóa luôn exe cũ đi sau khi đã copy ra bản old.
        :: Việc này đảm bảo MSBuild bắt buộc phải sinh ra file mới tinh và dễ check lỗi.
        del /q "%TARGET_DIR%\Comic-GMTPC.exe"
    )
)

:: ------------------------------------------
:: BƯỚC 3: CHẠY LỆNH MSBUILD
:: ------------------------------------------
echo [3/4] Dang tien hanh Build du an...
"C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" "r:\HDD R\ZC SYMLINK\USERS\source\repos\ghostminhtoan\get link manga\Comic-GMTPC.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:AutoStampBuildInfo=true /p:AutoPublishRelease=true
:: Bắt mã lỗi (Error Level) của MSBuild ngay lập tức
set BUILD_STATUS=%ERRORLEVEL%

:: Chờ 2 giây cho chắc chắn hệ thống file đã cập nhật xong
ping 127.0.0.1 -n 3 > nul

:: ------------------------------------------
:: BƯỚC 4: XỬ LÝ KẾT QUẢ BUILD
:: ------------------------------------------
echo [4/4] Kiem tra ket qua build...
if %BUILD_STATUS% NEQ 0 (
    echo [!] LOI: Build that bai! Dang phuc hoi file cu...
    :: Xóa file exe lỗi (nếu MSBuild lỡ tạo ra dở dang)
    if exist "%TARGET_DIR%\Comic-GMTPC.exe" del /q "%TARGET_DIR%\Comic-GMTPC.exe"
    :: Đổi tên old về lại exe
    if exist "%TARGET_DIR%\Comic-GMTPC-old.exe" ren "%TARGET_DIR%\Comic-GMTPC-old.exe" "Comic-GMTPC.exe"
    pause
    exit
)

:: Nếu MSBuild không báo lỗi, check xem file có thực sự được tạo ra không
if exist "%TARGET_DIR%\Comic-GMTPC.exe" (
    echo [V] Build thanh cong! Dang khoi dong chuong trinh...
    start "" "%TARGET_DIR%\Comic-GMTPC.exe"
) else (
    echo [!] LOI: Khong bao loi nhung khong tim thay file Comic-GMTPC.exe moi!
    echo Dang phuc hoi file cu...
    if exist "%TARGET_DIR%\Comic-GMTPC-old.exe" ren "%TARGET_DIR%\Comic-GMTPC-old.exe" "Comic-GMTPC.exe"
    pause
)

exit
