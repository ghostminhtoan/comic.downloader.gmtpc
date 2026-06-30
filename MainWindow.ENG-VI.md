# MainWindow ENG-VI trace

Mục tiêu: tìm chuỗi UI còn lệch giữa ENG và VI trong `MainWindow.*`.

| EN | VI | File | Status |
| --- | --- | --- | --- |
| `Missing chapters` | `Chương thiếu` | `MainWindow.Reader.cs` | OK |
| `Copy missing chapter` | `Sao chép chap thiếu` | `MainWindow.Reader.cs` | MISSING |
| `Copy all book's missing chapter` | `Sao chép chap thiếu của mọi truyện` | `MainWindow.Reader.cs` | MISSING |
| `google all book's missing chapter` | `Google chap thiếu của mọi truyện` | `MainWindow.Reader.cs` | MISSING |
| `Chapter / Image` | `Chapter / Ảnh` | `MainWindow.Reader.cs` | MISSING |

Ghi chú:
- Bảng này chỉ trace chỗ đang lộ trên UI.
- Khi thấy `MISSING`, sửa ngay trong file sinh control, không chỉ thêm vào map.
