@echo off
chcp 65001 > nul

cd /d "%~dp0"
reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f >nul 2>&1
taskkill /im "Comic-GMTPC.exe" /f >nul 2>&1

set "TARGET_DIR=bin\release\"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo [1/4] Dang don dep cac file va folder thua...
for %%F in ("%TARGET_DIR%\*") do (
    if /I not "%%~nxF"=="Comic-GMTPC.exe" (
        if /I not "%%~nxF"=="Comic-GMTPC-old.exe" (
            del /q "%%F"
        )
    )
)
for /d %%D in ("%TARGET_DIR%\*") do (
    rd /s /q "%%D"
)

echo [2/4] Dang xu ly backup file...
if exist "%TARGET_DIR%\Comic-GMTPC-old.exe" (
    if exist "%TARGET_DIR%\Comic-GMTPC.exe" (
        del /q "%TARGET_DIR%\Comic-GMTPC-old.exe"
        ren "%TARGET_DIR%\Comic-GMTPC.exe" "Comic-GMTPC-old.exe"
    )
) else (
    if exist "%TARGET_DIR%\Comic-GMTPC.exe" (
        copy /y "%TARGET_DIR%\Comic-GMTPC.exe" "%TARGET_DIR%\Comic-GMTPC-old.exe" > nul
        del /q "%TARGET_DIR%\Comic-GMTPC.exe"
    )
)

echo [3/4] Dang tien hanh Build du an...
"C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" "%~dp0Comic-GMTPC.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:AutoStampBuildInfo=true /p:AutoPublishRelease=true
set BUILD_STATUS=%ERRORLEVEL%

ping 127.0.0.1 -n 3 > nul

echo [4/4] Kiem tra ket qua build...
if %BUILD_STATUS% NEQ 0 (
    echo [!] LOI: Build that bai! Dang phuc hoi file cu...
    if exist "%TARGET_DIR%\Comic-GMTPC.exe" del /q "%TARGET_DIR%\Comic-GMTPC.exe"
    if exist "%TARGET_DIR%\Comic-GMTPC-old.exe" ren "%TARGET_DIR%\Comic-GMTPC-old.exe" "Comic-GMTPC.exe"
    pause
    exit
)

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
