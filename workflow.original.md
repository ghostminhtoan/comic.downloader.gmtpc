# Workflow hiện tại - Comic Downloader GMTPC

Tài liệu này là chuẩn làm việc cho repo hiện tại. Mục tiêu: sửa đúng chỗ, ít file, build sạch, không phá lane khác.

## 1. Quy tắc nền
- Luôn trả lời tiếng Việt.
- Luôn dùng emotion phong phú (không lặp đi lặp lại) để tôi không bị nhàm chán.
- Luôn dùng skill `ponyman` khi code, dù người dùng không tag skill.
- Ưu tiên sửa tối thiểu. Không thêm abstraction nếu chưa cần.
- Khi code phải chuẩn UTF-8, không được phép lỗi mobijake.
- WPF/UI đi qua `Dispatcher` khi đụng control từ luồng nền.
- Tác vụ dài phải giữ `async/await` và tôn trọng `CancellationToken`.
- Lỗi lẻ trong download/scan không được kéo sập cả app; log crash vào `.tmp\crash` và đánh dấu item/row lỗi rồi cho batch tiếp tục nếu còn an toàn.
- Sửa xong phải build sạch `0 error, 0 warning`.
- Sau build phải commit, push `main`, và trả mã commit.
- Khi đụng UI song ngữ, kiểm tra cả ENG/VI và trace vào `MainWindow.ENG-VI.md`.
- Không revert thay đổi người dùng nếu không được yêu cầu.
- Sau khi code xong luôn đánh giá và gợi ý các file cần sửa nếu cần sửa và rà soát lại các file cần kiểm tra nếu có, luôn kiểm tra EN/VI nếu có. 
- Luôn luôn đánh giá workflow.md và cập nhật nếu cần.
- Luôn luôn đánh giá prompt vừa làm và gợi ý các tính năng có thể có hoặc tính năng nên thêm vào và các file cần thiết kế thêm, hoặc các file nên thêm mới. Đừng ngại thay đổi workflow.md hoặc prompt.md và cập nhật lại nếu cần thiết.

## 2. Snapshot kiến trúc hiện tại

### Nền tảng
- App WPF .NET Framework `4.8`.
- Project file: `Comic-GMTPC.csproj`.
- App chạy kiểu portable:
  - root download mặc định: `PortablePaths.DefaultDownloadRoot`
  - data portable: `.portable`
  - temp portable: `.tmp`
  - danh sách autosave: `save gallery.md`
- Single-instance hiện tại:
  - cùng folder app: không được mở nhiều instance
  - khác folder app: được mở song song
  - logic nằm ở `App.xaml.cs`, mutex name sinh theo `PortablePaths.AppRoot`

### Dependency đang dùng
- `Microsoft.Web.WebView2`
- `Selenium.WebDriver`
- `System.Data.SQLite.Core`
- Có tích hợp Firecrawl qua API hoặc CLI fallback trong `MainWindow.SystemFirecrawl.cs`

## 3. Bản đồ file quan trọng

### Khởi động và portable
- `App.xaml.cs`
  - bootstrap runtime
  - single-instance theo folder
  - long path
  - hardware acceleration
- `PortablePaths.cs`
  - toàn bộ path chuẩn của app portable
- `PortableRuntimeBootstrap.cs`
- `PortableArchiveBootstrap.cs`

### Main window
- `MainWindow.xaml`
  - layout chính
  - toggle ENG/VI
  - combo `Single comic` / `Multi-comic`
  - toggle download/retry/copy/focus/global key
- `MainWindow.xaml.cs`
  - constructor và nối các phần partial
- `MainWindow.SystemProgress-Preview.cs`
  - hover preview dùng chung cho download queue và duplicate names
  - badge `preview`, delay `500ms`, cache ảnh, prefetch bitmap
- extracted gallery list có 2 mode:
  - `details list`: `dgResults`
  - `details list` có toggle `Compact row` / `Dòng gọn`; mặc định bật để show nhiều hàng, tắt thì row tự cao để xem đủ link/progress phụ
  - compact row vẫn phải hiện mini progress bar trong cột trạng thái khi đang tải/pause; chỉ ẩn text phần trăm để giữ row thấp
  - `thumbnail list`: grid 7 cột thường, 9 cột khi compact, tile hẹp cho ảnh đứng, dùng chung `_scrapedItems`, có selection/keyboard/context menu/drag cơ bản giống `dgResults`
  - khi `Dòng gọn` bật trong `thumbnail list`, thumbnail dùng 9 cột và tự fit chiều cao tile để thấy đúng 2 hàng trong khung; metadata phụ ẩn, hover/popup vẫn xem đủ chi tiết
  - thumbnail preview phải cache file ở `.tmp\preview-cache`, lưu file gốc theo đúng đuôi ảnh và sinh thêm `.thumb.jpg` nhỏ cho grid
  - thumbnail list vẫn phải auto tải ảnh thumbnail nhỏ như cũ để hiện trực tiếp trong grid, không phụ thuộc hover
  - nút `popup preview` nằm giữa `details list` và `thumbnail list`, chỉ bật/tắt popup hover preview phóng to cho `details list`, `thumbnail list`, và `duplicate names`; thumbnail grid phải auto load độc lập, không phụ thuộc trạng thái nút này
  - sau khi tắt hoặc bật `popup preview`, `thumbnail list` vẫn phải tự refresh và auto load lại thumbnail ngay, không bắt người dùng hover lại để kick preview
  - thumbnail item phải có selected/focus highlight nhìn thấy rõ khi click hoặc chọn bằng bàn phím
  - checkbox trong thumbnail list chỉ dùng ô vuông, không kèm chữ; đặt chồng ở góc trên ảnh thumbnail
  - khi chọn thumbnail bằng chuột hoặc bàn phím, item đang chọn phải đổi nền/viền sang màu vàng rõ ràng
  - title trong thumbnail list phải đủ cao để hiện 3 dòng
  - popup preview khi hover phải hiện ảnh, title, và nếu đã có dữ liệu quét thiếu chap số nguyên thì hiện thêm `latest chapter: <chapter mới nhất>`
  - popup preview khi đã có dữ liệu scan phải hiện cả trạng thái `missing integer chapter: complete/thiếu chapter số nguyên`
  - auto scan missing integer chapter phải chạy theo `_scrapedItems` chung, không phụ thuộc đang ở `details list` hay `thumbnail list`; collection reset/bulk add vẫn phải trigger scan các item chưa quét
  - tab `Scan missing integer chapter` / `Scan chap số nguyên thiếu` có combo `multiple check`/`check song song` từ 1 đến 16, mặc định 8; giá trị combo phải điều khiển số tác vụ scan missing integer chapter chạy song song thật, không chỉ đổi text UI
  - scan missing integer chapter phải tự chạy ngay sau import/get link/load list khi có item mới, không chờ download
  - row sync của tab scan nếu tạo row trống phải tự kick scan ngay; không chỉ chờ user bấm tab hoặc chờ download
  - scan phải luôn lấy thứ tự từ list truyện hiện tại, bắt đầu từ book trên cùng; không lấy thứ tự từ grid scan/cached row cũ
  - khi list truyện đổi trong lúc đang scan, phải hủy scan cũ và tự scan lại list mới; không để task cũ âm thầm ghi kết quả vào list mới
  - scan/rescan missing integer chapter không phụ thuộc checkbox book; checkbox chỉ dùng cho thao tác chọn/copy/toggle
  - right click trong tab scan phải có copy book link, copy missing integer chapter, copy decimal chapter; Ctrl+C trong tab scan chỉ copy link truyện
  - mọi domain khi scan missing integer chapter phải tôn trọng `multiple check`; không đặt semaphore/lock bao toàn domain khiến scan bị single check
  - `nettruyen.tech`/`nettruyenviet10.com` khi cần WebView bấm `Xem thêm` phải cho mở ngầm song song theo số scan task đang chạy; không khóa WatchMore WebView còn 1 cửa
  - WatchMore WebView phải đóng và dispose sau khi lấy HTML/cookie; giảm `multiple check` thì không mở thêm WebView vượt limit mới
  - `nettruyen.tech`/`nettruyenviet10.com` phải ưu tiên API/AJAX chapter list trước; chỉ mở WebView bấm `Xem thêm` khi AJAX thiếu hoặc thất bại để tránh scan quá chậm
  - `nettruyen.tech`/`nettruyenviet10.com` nguồn chapter đầy đủ ưu tiên `/Comic/Services/ComicService.asmx/ChapterList?slug=<book-slug>`; đây là API của nút `Xem thêm`, dùng để tránh hụt chap đầu như `thuong-hoang-tro-ve`
  - nếu scan ra thiếu chap số nguyên 1-3 thì tự quét lại tối đa 3 lần trước khi lưu kết quả
  - mọi domain: label chap dạng `số:số`, `số-số`, `số - số` phải được tính là range phủ đủ các số trong khoảng đó, không báo thiếu các số nằm trong range
  - khi domain trả chapter label riêng với link (ví dụ Nettruyen API `chapter_name`), cache `ReaderChapterItem.Name` phải giữ label thật; không tự build lại từ link nếu làm mất range như `Chapter 58: 59`
  - cột missing integer chapter phải word wrap
  - progress scan song song không được báo như đang xử lý tuần tự một hàng; phải hiển thị số đã xong thật và số hàng vừa hoàn tất, cột `#` phải có checkbox kèm số thứ tự hàng
  - scan missing integer chapter phải phân biệt truyện bằng link/domain, không chỉ tên; cùng tên khác domain vẫn quét riêng, nhưng các task split/merge cùng link chỉ quét một lần
  - nếu nhiều domain có cùng tên truyện, từng token missing integer chapter trùng giữa domain phải màu vàng; token chỉ thiếu ở một domain phải màu trắng
  - cột chap thập phân có toggle `WRAP`; mặc định tắt để tránh hàng quá cao, bật thì màu xanh và chỉ cột chap thập phân xuống dòng, tắt thì màu đỏ và không wrap
  - không giữ bitmap RAM toàn cục cho preview
 
### System/UI
- `MainWindow.SystemBootstrap.cs`
  - init app
  - hotkey global/project
  - clipboard auto paste
  - http client/cookie state
- `MainWindow.SourceSearch.cs`
  - tab `Search` cạnh `Password` trong section source
  - ô `Search book` + combo checkbox domain dùng style `CyberpunkComboBox`
  - nút `Search` mở Google dạng `https://www.google.com/search?q=site:<domain không http/https>+<tên truyện encode>` để khóa đúng domain
  - domain search phải lấy theo home/redirect thật của tab, ví dụ `truyenqq` -> `truyenqqko.com`; tab có redirect thì dùng redirect hiện tại
- `MainWindow.SystemActions.cs`
- `MainWindow.SystemBuild.cs`
- `MainWindow.SystemUpdate.cs`
- `MainWindow.SystemFolders.cs`
- `MainWindow.SystemExplorer.cs`
- `MainWindow.SystemComboBox.cs`
- `MainWindow.SystemMessageBox.cs`
- `MainWindow.SystemFloatingControlWindow.cs`
- `MainWindow.WorkspaceLayout.cs`
- `MainWindow.Theme.cs`
- `MainWindow.UIBootstrap.cs`
- `MainWindow.UIResponsive.cs`
- `MainWindow.UIResultsGrid.cs`
- `MainWindow.UILogs.cs`
- `MainWindow.UIEnglish.cs`
- `MainWindow.UIVietnamese.cs`
- `MainWindow.UIFold.cs`
- `MainWindow.UINewFeatures.cs`

### Download
- `MainWindow.Download.cs`
  - flow tải chính
  - path/file naming
  - pause/resume/stop state
- `MainWindow.DownloadPipeline.cs`
  - profile theo domain
  - retry/rate-limit/browser session
  - manifest tải trang
- `MainWindow.DownloadState.cs`
  - state/cancel/token/phối hợp queue
- `MainWindow.singlemulticomic.cs`
  - nguồn sự thật cho mode folder type
  - `GetDownloadChapterFolderName()` chỉ trả tên folder chapter
  - caller tự ghép book/chapter cho mode multi-comic

### Routing và scraper theo domain
- `MainWindow.TabRouting.cs`
  - nhận URL, chọn lane, điều hướng tab, import direct link
- `MainWindow.Tab*.cs`
  - mỗi domain một partial riêng

### Novel / Reader / Watch
- `MainWindow.LightNovelDesk.cs`
- `MainWindow.Reader.cs`
- `MainWindow.TabWatch.cs`
- `HakoChapterCaptureWindow.cs`
- `ChapterRangeParser.cs`

### Window phụ
- `CaptchaWindow.xaml.cs`
- `DuplicateWindow.xaml.cs`
- `DirectDownloadWindow.xaml.cs`
- `BookmarkHistoryWindow.xaml.cs`
- `ErrorLogWindow.xaml.cs`
- `ErrorReportWindow.xaml.cs`

## 4. Domain đang support

### Manga
- `truyenqq`
  - preview cover ưu tiên lấy từ `div.book_avatar img`
  - giữ nguyên query string của URL ảnh nếu site trả về cùng file `.jpg?...`, không tự cắt phần sau dấu `?`
  - nếu `book_avatar` có cả `src` và `data-ni` hay nhiều host ảnh, preview phải thử tuần tự từng URL thay vì fail ngay ở URL đầu
- `haibabamanga.somee.com`
  - preview cover ưu tiên lấy từ `div.manga-cover-container img.manga-cover`, chấp nhận cả `.jpg` và `.png`
- `mangadex.org`
  - ưu tiên route theo `tag / title / chapter`
  - lane hiện tại dùng API chính chủ để lấy chapter list, cover preview, và ảnh chapter
- `nettruyen`
- `nettruyen.tech`
  - preview cover lấy từ `div.col-image img`, chấp nhận `.jpg`, `.png`, `.webp`; khi hover book phải hiện badge trắng `preview` nếu tích hợp thành công
  - download folder/process phải tách riêng `nettruyen.tech`, không dùng chung folder `nettruyen` hoặc `nettruyenviet10.com`
- `nettruyenviet10.com`
  - download folder/process phải tách riêng `nettruyenviet10.com`, không dùng chung folder `nettruyen` hoặc `nettruyen.tech`
  - khi AJAX `ProcessChapterList`/`GetListChapter` trả danh sách chương đầy đủ, phải gán lại `chapterLinks` bằng kết quả AJAX, không chỉ thay HTML trung gian
- `dilib.vn / thuviensach.vn`
  - book slug có thể chứa số; khi nhận dạng book từ chapter URL chỉ cắt phần sau marker chapter (`-chap-...` hoặc `/chuong...`), không xóa số cuối book slug
  - book URL hợp lệ gồm cả `/{book-slug}.html` và `/truyen-tranh/{book-slug}`; `/truyen-tranh/{book-slug}` không được route nhầm thành category
  - khi scan missing integer chapter, tên `ReaderChapterItem` phải lấy từ chapter URL/label (`chap 469`), không dùng title gồm tên book vì book có số như `7 Viên...` sẽ bị parse thành chapter 7
- `doctruyen.us`

### Hentai / ảnh
- `daomeoden`
- `damconuong.shop`
- `vi-hentai`
- `truyengg` / `sayhentai`
- `hentaiforce`
- `nhentai`
- `hentai2read`
- `hentaiera`

### Light novel
- `hako.vn`
- `hako.re`
- `docln.net`

Nguồn sự thật cho routing: `MainWindow.TabRouting.cs`.

## 5. Folder type hiện tại
- `Single comic`
  - `root\book name\chapter name\page files`
- `Multi-comic`
  - vẫn tạo thư mục theo book, nhưng phần tên chapter/path phải lấy qua flow chung đang dùng trong downloader hiện tại
  - khi sửa logic folder, phải sửa theo toàn flow đang gọi `GetDownloadChapterFolderName()`, không sửa riêng từng domain
- Combo UI nằm ở `MainWindow.xaml`
- State và event xử lý nằm ở `MainWindow.singlemulticomic.cs`
- Float window phải sync lại folder type khi đổi mode

## 6. Quy tắc queue/download
- Chỉ dừng đúng item/book được thao tác. Không làm rơi các book khác.
- Nếu untick checkbox:
  - status có thể về `Stopped`
  - request đang chạy phải thật sự honor cancel token
  - tick lại thì book đó mới được tải tiếp
- `Completed` chỉ set khi book thực sự tải xong.
- Không set completed chỉ vì queue/error vừa được clear.
- Không remove book giữa chừng nếu chưa hoàn tất flow.
- Resume phải tôn trọng file sẵn có và manifest trong `.tmp\.manifest`.
- File ảnh nhỏ/hỏng phải bị retry, không tính là xong.
- Domain có profile throttle/retry riêng trong `MainWindow.DownloadPipeline.cs`.

## 7. Browser session / captcha / anti-bot
- Site bị challenge có thể đi qua WebView2 session hoặc Chrome fallback.
- Cookie + user-agent lấy về phải được bơm lại vào `_httpClient`.
- `MainWindow.SystemCaptcha.cs`, `MainWindow.CaptchaGeneral.cs`, `MainWindow.CaptchaSpecial.cs`, `MainWindow.Captchawatchmore.cs` là lane captcha chính.
- Focus off thì không được ép minimize main window sau captcha/webview nếu flow hiện tại không yêu cầu.

## 8. Hotkey / toggle / UI state
- `Ctrl+Shift+F`: bật/tắt float button.
- `Alt+Shift+G`: bật/tắt global hotkey mode.
- Các toggle download/retry/copy/focus/global key phải phản ánh đúng state thật, không chỉ đổi label.
- Khi sửa toggle:
  - check cả click UI
  - check hotkey
  - check sync với floating control
  - check ENG/VI nếu có text lộ ra UI

## 9. Cách chọn file trước khi sửa

### Nếu bug thuộc domain
1. Xem `MainWindow.TabRouting.cs` route vào tab nào.
2. Mở `MainWindow.Tab<Domain>.cs`.
3. Nếu lỗi phát sinh lúc tải file/chapter/path, đọc thêm:
   - `MainWindow.Download.cs`
   - `MainWindow.DownloadPipeline.cs`
   - `MainWindow.singlemulticomic.cs`

### Nếu bug thuộc queue, checkbox, stop/resume
1. Đọc `MainWindow.DownloadState.cs`.
2. Đọc `MainWindow.Download.cs`.
3. Chỉ khi cần mới xuống `MainWindow.DownloadPipeline.cs`.

### Nếu bug thuộc toggle/layout/ngôn ngữ
1. `MainWindow.xaml`
2. partial UI tương ứng (`UI*`, `Theme`, `WorkspaceLayout`, `SystemFloatingControlWindow`)
3. `MainWindow.ENG-VI.md`

### Nếu bug hoặc feature thuộc hover preview ở extracted gallery list
1. `MainWindow.xaml`
2. `MainWindow.SystemProgress-Preview.cs`
3. partial UI hoặc window đang gắn host hover (`MainWindow.UIResultsGrid.cs`, `DuplicateWindow.xaml`, `DuplicateWindow.xaml.cs`)
4. partial domain đang cấp dữ liệu preview (`MainWindow.Tab*.cs`)

### Nếu bug hoặc feature thuộc thumbnail list của extracted gallery links
1. `MainWindow.xaml`
2. `MainWindow.UIResultsGrid.cs`
3. `MainWindow.SystemProgress-Preview.cs`
4. partial domain đang cấp `HoverPreviewThumbnailUrl` hoặc data preview (`MainWindow.Tab*.cs`)

### Nếu bug thuộc app portable / startup / multi-instance
1. `App.xaml.cs`
2. `PortablePaths.cs`
3. `PortableRuntimeBootstrap.cs`
4. `PortableArchiveBootstrap.cs`

## 10. Workflow sửa đúng
1. Đọc `workflow.md`.
2. Xác định lane:
   - startup/portable
   - system/ui
   - queue/download
   - domain scraper
   - novel/reader/watch
3. Tìm đúng partial/file nguồn sự thật.
4. Sửa ít file nhất.
5. Nếu đụng text UI, cập nhật `MainWindow.ENG-VI.md`.
6. Build bằng `.\build.bat`.
7. Nếu còn error/warning, sửa tiếp đến sạch.
8. Kiểm tra `BuildInfo.cs` vì build release sẽ auto stamp.
9. Commit đúng scope.
10. Push `origin main`.

## 11. Quy tắc build/release
- Luôn dùng `.\build.bat`.
- Script sẽ:
  - kill `Comic-GMTPC.exe`
  - rebuild Release qua MSBuild
  - auto stamp `BuildInfo.cs`
  - publish artifact sang `release\Comic-GMTPC`
  - auto mở exe mới nếu build thành công
- Vì `BuildInfo.cs` bị đổi sau mỗi build release, file này thường phải vào commit cuối cùng.
- Không chấp nhận warning mới.

## 12. Quy tắc git
- Commit theo thay đổi thật, scope nhỏ.
- Không kéo file test tạm, dump, html debug, log rác vào commit nếu không cần.
- Branch làm việc mặc định hiện tại: `main`.
- Sau khi sửa xong: commit rồi push `origin main`.

## 13. Ghi nhớ thực chiến
- Nhiều bug trong repo này không nằm ở parser mà nằm ở state sync giữa:
  - queue item
  - checkbox
  - toggle UI
  - cancellation token
  - folder type
- Khi thấy lỗi "status đúng nhưng hành vi sai", ưu tiên đọc flow state/cancel trước khi sửa parser.
- Khi thấy lỗi "đúng domain này nhưng sai mọi domain khác", ưu tiên đọc flow chung thay vì vá từng tab.
