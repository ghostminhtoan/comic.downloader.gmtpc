---
vi: Kết nối & Luồng tải Domain
en: Domain Connections & Concurrency
---

<!-- VI -->
## 🚀 CẤU HÌNH KẾT NỐI (CONNECTION) & TẢI NHIỀU TRUYỆN (MULTI-BOOK) THEO DOMAIN

Để tối ưu hóa tốc độ tải và tránh bị máy chủ website giới hạn IP/Block tạm thời (429 Too Many Requests, Cloudflare 403/503), ứng dụng đề xuất cấu hình Connection (kết nối trang/chương) và Multi Download (số truyện tải cùng lúc) chuẩn xác cho từng domain:

### 📖 1. Manga
• **truyenqq** (`truyenqqko.com`): 2 connection, 4 truyện
• **nettruyenviet10.com**: 2 connection, 2 truyện
• **nettruyen.tech**: 2 connection, 2 truyện
• **thuviensach.vn** (`dilib.vn`): 2 connection, 4 truyện
• **loppytoonn.com**: 4 connection, 4 truyện
• **mangadex.org**: 2 connection, 2 truyện

### 🔞 2. Hentai
• **hentaivn** (`vi-hentai.pro`): 2 connection, 2 truyện
• **damconuong.shop**: 2 connection, 2 truyện
• **sayhentai** (`sayhentai.cx` / `truyengg`): 2 connection, 2 truyện
• **hentaiforce** (`hentaiforce.net`): 4 connection, 4 truyện
• **hitomi.la**: 2 connection, 1 truyện
• **hentai2read** (`hentai2read.com`): 2 connection, 4 truyện
• **hentaiera** (`hentaiera.com`): 2 connection, 2 truyện
• **daomeoden** (`daomeoden.net`): 4 connection, 2 truyện
• **e-hentai** (`e-hentai.org` / `exhentai.org`): 2 connection, 2 truyện

### 📚 3. Novel
• **hako** (`ln.hako.vn` / `docln.net`): 1 connection, 2 truyện

---
⚠️ **GHI CHÚ:**
• **CONNECTION (Kết nối)**: Điều chỉnh số trang/chương tải song song trong cùng 1 cuốn truyện.
• **DOWNLOAD MULTIPLE BOOK (Tải nhiều truyện)**: Điều chỉnh số lượng truyện được tải song song cùng lúc trong hàng chờ.

<!-- EN -->
## 🚀 RECOMMENDED CONNECTIONS & MULTI-BOOK CONCURRENCY BY DOMAIN

To optimize download speed and avoid server rate-limits (HTTP 429 / Cloudflare 403/503 blocks), please configure Connections (page threads per book) and Multi-Book concurrency according to each domain:

### 📖 1. Manga
• **truyenqq** (`truyenqqko.com`): 2 connections, 4 books
• **nettruyenviet10.com**: 2 connections, 2 books
• **nettruyen.tech**: 2 connections, 2 books
• **thuviensach.vn** (`dilib.vn`): 2 connections, 4 books
• **loppytoonn.com**: 4 connections, 4 books
• **mangadex.org**: 2 connections, 2 books

### 🔞 2. Hentai
• **hentaivn** (`vi-hentai.pro`): 2 connections, 2 books
• **damconuong.shop**: 2 connections, 2 books
• **sayhentai** (`sayhentai.cx` / `truyengg`): 2 connections, 2 books
• **hentaiforce** (`hentaiforce.net`): 4 connections, 4 books
• **hitomi.la**: 2 connections, 1 book
• **hentai2read** (`hentai2read.com`): 2 connections, 4 books
• **hentaiera** (`hentaiera.com`): 2 connections, 2 books
• **daomeoden** (`daomeoden.net`): 4 connections, 2 books
• **e-hentai** (`e-hentai.org` / `exhentai.org`): 2 connections, 2 books

### 📚 3. Novel
• **hako** (`ln.hako.vn` / `docln.net`): 1 connection, 2 books

---
⚠️ **NOTES:**
• **CONNECTION**: Adjusts parallel page/chapter download threads within a single book.
• **DOWNLOAD MULTIPLE BOOK**: Adjusts how many books download simultaneously in queue.
