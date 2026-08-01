# Nhật ký Git Commit

## 2026-06-22
- fix(build): restore clean csproj to resolve MarkupCompilePass1 ChecksumAlgorithm build error
- fix(build): update Verify-GitRestorePoint.ps1 to ignore Compile items inside Target elements

## 2026-06-21
- feat(archive): delete incomplete archive file if compression is cancelled or fails
- feat(captcha): delete the entire runtimes folder during cookie reset instead of selectively deleting directories
- fix(captcha): move locked runtimes files/folders to tmp folder to allow full deletion during reset
- feat(captcha): refactor captcha into General, Special, and WatchMore types
- fix(captcha): restore Ctrl+F keys search sequence in CaptchaWindow to guarantee precise Turnstile checkbox focus
- fix(captcha): preserve cookies during Nettruyen chapter expansion by setting autoDeleteCookiesOnLoad to false to prevent captcha spam

## 2026-06-20
- fix(ui): prevent selection bubbling from inner controls and allow infinite window dimensions when maximized
- feat(ui): linkify chapter and page columns in error windows & eliminate rootLayout margin when maximized
- feat(download): filter out nettruyenviet.webp image URLs from Nettruyen downloads
- feat(download): add adaptive renaming based on original image sequential naming consistency
- feat(download): parallelize daomeoden page downloads and remove admin auto-elevation from build.bat to allow standard execution and drag-and-drop

## 2026-06-19
- docs(workflow): update workflow.md to enforce direct commits to default branch main
