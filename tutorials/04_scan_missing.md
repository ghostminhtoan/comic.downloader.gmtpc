---
vi: Scan missing chapter
en: Scan Missing Chapters
---

<!-- VI -->
## 🔍 SCAN MISSING INTEGER CHAPTER (QUÉT CHAP SỐ NGUYÊN THIẾU)
Tính năng thông minh tự động phát hiện các chapter số nguyên bị thiếu trong bộ truyện:

## 1. Tự động Quét & Phân Tích Range
• Ngay sau khi Get Link / Import link, app sẽ tự động quét danh sách chapter từ nguồn web.
• Hệ thống phân tích chính xác các label dạng `số:số`, `số-số` (ví dụ `Chapter 58: 59`) là dải phủ đủ chap, không báo thiếu nhầm.
• Phân biệt truyện chính xác theo Link/Domain chứ không chỉ theo tên truyện.

## 2. Cấu hình Quét Song Song (Multiple Check)
• Combobox 'check song song' (Multiple Check) cho phép tùy chỉnh từ 1 đến 16 task chạy cùng lúc (mặc định 8).
• Giá trị này thực sự điều khiển luồng scan đa nhiệm giúp tốc độ quét nhanh gấp 10 lần.
• Tự động rescan tối đa 3 lần nếu phát hiện thiếu chap 1-3 để đảm bảo chính xác tuyệt đối.

## 3. Menu Chuột Phải & Tùy Chọn Wrap
• Click chuột phải trong tab Scan: Copy link truyện, copy danh sách chap thiếu số nguyên, copy chap thập phân.
• Toggle WRAP: Bật/Tắt xuống dòng riêng cho cột chap thập phân để tránh dãn chiều cao bảng.

<!-- EN -->
## 🔍 SCAN MISSING INTEGER CHAPTERS
Smart engine automatically detecting missing integer chapters in a series:

## 1. Auto Scan & Range Analysis
• Auto-triggers right after Get Link or link import.
• Parses range labels like `58:59` or `58-59` accurately to prevent false missing reports.
• Distinguishes comics by URL/Domain, not just by title.

## 2. Parallel Task Combobox (Multiple Check)
• 'Multiple Check' combobox configures 1 to 16 parallel tasks (default 8).
• Truly controls parallel worker threads for up to 10x faster scans.
• Auto rescans up to 3 times if chapters 1-3 appear missing for total accuracy.

## 3. Context Menu & Wrap Toggle
• Right-click in Scan tab: Copy book link, copy missing integer chapters, copy decimal chapters.
• WRAP Toggle: Toggle line wrapping specifically for decimal chapters column.
