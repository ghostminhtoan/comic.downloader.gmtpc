# MainWindow ENG-VI trace

Mục tiêu: tìm chuỗi UI còn lệch giữa ENG và VI trong `MainWindow.*`.

| EN | VI | File | Status |
| --- | --- | --- | --- |
| `Missing chapters` | `Chương thiếu` | `MainWindow.Reader.cs` | OK |
| `Copy missing chapter` | `Sao chép chap thiếu` | `MainWindow.Reader.cs` | MISSING |
| `Copy all book's missing chapter` | `Sao chép chap thiếu của mọi truyện` | `MainWindow.Reader.cs` | MISSING |
| `google all book's missing chapter` | `Google chap thiếu của mọi truyện` | `MainWindow.Reader.cs` | MISSING |
| `Chapter / Image` | `Chapter / Ảnh` | `MainWindow.Reader.cs` | MISSING |
| `Copy file gif` | `Copy file gif` | `MainWindow.Reader.cs` | OK |
| `Copy GIF Files` | `Copy file GIF` | `MainWindow.Reader.cs` | OK |
| `Root folder path:` | `Đường dẫn folder tổng:` | `MainWindow.Reader.cs` | OK |
| `Converted folder path:` | `Đường dẫn folder đã convert:` | `MainWindow.Reader.cs` | OK |
| `SCAN GIF` | `QUÉT GIF` | `MainWindow.Reader.cs` | OK |
| `COPY PATH` | `COPY PATH` | `MainWindow.Reader.cs` | OK |
| `Scan missing integer chapter` | `Scan chap số nguyên thiếu` | `MainWindow.TabGalleryLinks.cs` | OK |
| `Rescan missing integer chapter` | `Scan lại chap số nguyên thiếu` | `MainWindow.TabGalleryLinks.cs` | OK |
| `Copy missing integer chapter` | `Copy chap số nguyên thiếu` | `MainWindow.TabGalleryLinks.cs` | OK |
| `Copy decimal chapter` | `Copy chap thập phân` | `MainWindow.TabGalleryLinks.cs` | OK |
| `Copy book link` | `Copy link truyện` | `MainWindow.TabGalleryLinks.cs` | OK |
| `Copy selected missing integer chapter` | `Copy selected chap số nguyên thiếu` | `MainWindow.TabGalleryLinks.cs` | OK |
| `complete` | `đủ chapter` | `MainWindow.TabGalleryLinks.cs` | OK - normalized after toggle |
| `Compact row` | `Nén dòng` | `MainWindow.xaml` / `MainWindow.UIEnglish.cs` / `MainWindow.UIVietnamese.cs` | OK |
| `Hide settings` | `Ẩn thiết lập` | `MainWindow.xaml` / `MainWindow.UIEnglish.cs` / `MainWindow.UIVietnamese.cs` | OK |
| `POPUP PREVIEW` | `XEM TRƯỚC POPUP` | `MainWindow.xaml` / `MainWindow.UIEnglish.cs` / `MainWindow.UIVietnamese.cs` | OK |
| `Search book` | `Tìm truyện` | `MainWindow.SourceSearch.cs` / `MainWindow.xaml` | OK |
| `Please enter book name.` | `Vui lòng nhập tên truyện.` | `MainWindow.SourceSearch.cs` | OK |
| `Please check at least one domain.` | `Vui lòng tick ít nhất một domain.` | `MainWindow.SourceSearch.cs` | OK |
| `Open Google search failed:` | `Mở Google search lỗi:` | `MainWindow.SourceSearch.cs` | OK |
| `Stop` | `Stop` | `MainWindow.TabGalleryLinks.cs` | OK |
| `Float button` | `Nút nổi` | `MainWindow.WorkspaceLayout.cs` | OK |
| `Ctrl+Shift+F` | `Ctrl+Shift+F` | `MainWindow.SystemBootstrap.cs` | HOTKEY |
| `Focus` | `Focus` / `Tự focus` | `MainWindow.SystemFloatingControlWindow.cs` | POLICY |
| `damconuong.shop` | `damconuong.shop` | `MainWindow.xaml` | OK |
| `dilib.vn / thuviensach.vn` | `dilib.vn / thuviensach.vn` | `MainWindow.xaml` | OK |
| `doctruyen.us` | `doctruyen.us` | `MainWindow.xaml` | OK |
| `loppytoonn.com` | `loppytoonn.com` | `MainWindow.xaml` | OK |
| `mangadex.org` | `mangadex.org` | `MainWindow.xaml` | OK |
| `Split Chapters to Parallel Tasks` | `Split Chapters to Parallel Tasks` | `MainWindow.xaml` | OK |
| `Gaming-style comic scraper: paste source links, group chapters, check queue, then bulk download.` | `Gaming-style comic scraper: dán link nguồn, gom chapter, kiểm tra queue, rồi tải hàng loạt.` | `MainWindow.xaml` / `MainWindow.WorkspaceLayout.cs` | OK |
| `Shutdown options` | `Tùy chọn tắt máy` | `MainWindow.xaml` / `MainWindow.UIVietnamese.cs` | OK |
| `Search book name, link, or chapter in queue...` | `Tìm kiếm tên truyện, link hoặc chapter trong hàng chờ...` | `MainWindow.xaml` | OK |
| `Shutdown after done` | `Tắt máy sau khi tải xong toàn bộ và không còn công việc chờ.` | `MainWindow.xaml` | OK |
| `RESTORE DEFAULT TAG` | `RESTORE DEFAULT TAG` | `MainWindow.xaml` | OK |
| `Tutorial` | `Hướng dẫn` | `MainWindow.WorkspaceLayout.cs` | OK |
| `Tutorial & Config` | `Hướng dẫn & Cấu hình` | `MainWindow.WorkspaceLayout.cs` | OK |

Ghi chú:
- Bảng này chỉ trace chỗ đang lộ trên UI.
- Khi thấy `MISSING`, sửa ngay trong file sinh control, không chỉ thêm vào map.
- Float button phải là global hotkey.
- Webview xong, focus off thì không ép minimize main window.
