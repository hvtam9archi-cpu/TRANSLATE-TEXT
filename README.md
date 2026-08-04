# TranslateText — AutoCAD Plugin

Plugin dịch thuật và chuyển đổi mã font tiếng Việt trực tiếp trong bản vẽ AutoCAD.

---

## Tính năng

### 🌐 TRANSLATETEXT — Dịch thuật Text
- Dịch tự động **Text, MText, MLeader, Block Attributes** và nội dung tĩnh trong Block Definition
- Hỗ trợ **20+ ngôn ngữ** (Việt, Anh, Hàn, Nhật, Trung, Pháp, Đức, Nga…)
- **Từ điển AEC** tích hợp — dịch chính xác thuật ngữ Kiến trúc/Kỹ thuật/Xây dựng (Mặt bằng, Mặt cắt, Chi tiết…)
- **Bảo vệ mã MText** — Giữ nguyên định dạng font, màu, gạch chân, xuống dòng (`\P`, `\F`, `%%c`…) trong quá trình dịch
- **Smart Block Processing** — Mỗi Block Definition chỉ xử lý 1 lần, tối ưu cho bản vẽ có hàng trăm block trùng
- **In-memory Cache** — Các chuỗi giống nhau chỉ gọi API 1 lần; kết quả lỗi không được cache

### 🔤 CHANGETEXTSTYLE — Chuyển mã Font tiếng Việt
- Chuyển đổi qua lại giữa **Unicode ↔ VNI Windows ↔ TCVN3 (ABC)**
- **Auto Detect** — Tự nhận diện encoding dựa trên nội dung text và tên font
- Đổi Text Style đồng thời khi chuyển mã
- Hỗ trợ xử lý **MText, DBText, MLeader, Dimension, Block Attributes, AttributeDefinition**
- Loại bỏ tự động font override (`\F`) trong MText khi đổi style

### 🔎 FINDFONT — Tìm và khôi phục font thiếu
- Kiểm tra font chính và Big Font của các Text Style đang được đối tượng lựa chọn sử dụng
- Hỗ trợ **Text, MText, MLeader, Dimension, Block Attributes** và text trong Block Definition
- Kiểm tra cả font override cục bộ (`\F...;`/`\f...;`) trong MText, MLeader và Dimension Text
- Tìm font theo tên file SHX/TTF và theo tên family của TrueType
- Nạp TTF riêng cho tiến trình AutoCAD và cập nhật Text Style tới file font đi kèm plugin
- Báo chi tiết font đã khôi phục, chưa tìm thấy hoặc không thể sửa trong Xref

---

## Kiến trúc

```
TRANSLATE TEXT/
├── Commands.cs                  ← Entry point ([CommandMethod])
├── RibbonSetup.cs               ← IExtensionApplication — Ribbon "TH Tools"
├── PackageContents.xml          ← Bundle metadata
├── Text Font/                   ← Font SHX/TTF được deploy cùng plugin
│
├── AutoCad/
│   ├── TranslationEntityRepository.cs ← Đọc/ghi text entity
│   ├── TextSelectionInteraction.cs ← Prompt và SelectionFilter dùng chung
│   └── FontRepairService.cs     ← Snapshot → resolve → apply font thiếu
│
├── Core/
│   └── TextProcessors.cs        ← FormatProtector, VnCharset, TextCaseHelper
│
├── Models/
│   ├── TextDataModels.cs        ← MaskResult, TextEntityData, LanguageItem
│   └── LanguageList.cs          ← Danh sách ngôn ngữ
│
├── Services/
│   ├── TranslationService.cs    ← Google Translate API + Cache + Semaphore
│   ├── TranslationBatchProcessor.cs ← Gom chuỗi trùng và điều phối dịch
│   ├── EmbeddedFontCatalog.cs   ← Tra cứu tên file và family của font
│   ├── AecGlossary.cs           ← Từ điển chuyên ngành AEC
│   └── AppSettings.cs           ← Lưu cài đặt Registry
│
└── UI/
    ├── PluginImageLoader.cs      ← Load icon embed bằng WPF Pack URI
    ├── TranslateWindow.xaml/.cs  ← WPF Dialog dịch thuật
    └── ChangeStyleWindow.xaml/.cs ← WPF Dialog đổi style
```

| Tầng | Vai trò |
|------|---------|
| **Commands.cs** | Chỉ chứa `[CommandMethod]`, gọi đến Services/UI |
| **RibbonSetup.cs** | Tạo Tab/Panel/Button trên Ribbon AutoCAD |
| **AutoCad/** | Tương tác Transaction, entity và Text Style của AutoCAD |
| **Core/** | Logic thuần C# — không phụ thuộc AutoCAD API |
| **Services/** | Tích hợp API bên ngoài, Registry, từ điển |
| **Models/** | POCO data class — decouple khỏi Transaction |
| **UI/** | WPF Windows — dark theme thống nhất |

### Translation pipeline

```text
AutoCAD selection
    -> AutoCad/TranslationEntityRepository (read transaction)
    -> Services/TranslationBatchProcessor (deduplicate + bounded concurrency)
    -> Services/TranslationService (glossary + bounded cache + HTTP)
    -> AutoCad/TranslationEntityRepository (write transaction)
```

The command layer only coordinates UI and transaction lifetimes. Identical strings in a
selection share one translation task, and concurrent callers share the same in-flight HTTP
request. The completed-result cache is capped at 4,096 entries for long AutoCAD sessions.
Nếu API lỗi sau ba lần thử, text gốc được giữ nguyên, lỗi được báo theo từng chuỗi duy
nhất và không được ghi vào cache như một bản dịch thành công.

### FINDFONT pipeline

```text
AutoCAD selection
    -> read transaction (snapshot ObjectId, Text Style và formatted text)
    -> resolve ngoài transaction (quét catalog, đọc metadata TTF, nạp private font)
    -> write transaction (áp dụng Text Style và inline SHX path)
```

Không có I/O font chậm hoặc đăng ký font Windows trong lúc transaction AutoCAD đang mở.

---

## Ribbon

Plugin tự động tạo panel **Text Tools** trong tab **TH Tools** trên Ribbon:

| Button | Lệnh | Mô tả |
|--------|-------|-------|
| **Translate Text** | `TRANSLATETEXT` | Dịch thuật text trong bản vẽ |
| **Change Style** | `CHANGETEXTSTYLE` | Chuyển đổi encoding font tiếng Việt |

> Tab `TH Tools` (ID: `TH_TOOLS_TAB`) được chia sẻ giữa các plugin: **TPL**, **Block Utilities**, **TranslateText**. Nếu tab đã tồn tại, plugin chỉ thêm panel mới.

---

## Yêu cầu

- **AutoCAD** 2021–2024 (R24.0–R24.3)
- **.NET Framework** 4.8
- **Platform:** x64

## Cài đặt

### Tự động (Build)
Build project → DLL tự động deploy vào:
```
%APPDATA%\Autodesk\ApplicationPlugins\TranslateText.bundle\
├── PackageContents.xml
└── Contents/
    ├── TranslateText.dll
    └── Text Font/
        ├── Font SHX/
        ├── Font TCVN3-ABC/
        ├── Font UTM/
        └── Font VNI/
```

### Thủ công
Copy thư mục `TranslateText.bundle` vào `%APPDATA%\Autodesk\ApplicationPlugins\`.

---

## Tech Stack

| Thành phần | Công nghệ |
|------------|-----------|
| Framework | .NET Framework 4.8 |
| Project Format | SDK-style |
| AutoCAD API | NuGet `AutoCAD.NET` 23.1.0 |
| UI | WPF (Dark Theme) |
| Translation | Google Translate API (gtx endpoint) |
| Deployment | ApplicationPlugins Bundle |
