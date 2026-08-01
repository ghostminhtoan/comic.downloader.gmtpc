@echo off
setlocal EnableExtensions

cd /d "%~dp0\.."

if "%~1"=="" (
    git restore --worktree --staged -- .
) else (
    git restore --worktree --staged -- %*
)

exit /b %ERRORLEVEL%
