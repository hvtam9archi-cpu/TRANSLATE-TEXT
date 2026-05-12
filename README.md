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
- **In-memory Cache** — Các chuỗi giống nhau chỉ gọi API 1 lần

### 🔤 CHANGETEXTSTYLE — Chuyển mã Font tiếng Việt
- Chuyển đổi qua lại giữa **Unicode ↔ VNI Windows ↔ TCVN3 (ABC)**
- **Auto Detect** — Tự nhận diện encoding dựa trên nội dung text và tên font
- Đổi Text Style đồng thời khi chuyển mã
- Hỗ trợ xử lý **MText, DBText, MLeader, Dimension, Block Attributes, AttributeDefinition**
- Loại bỏ tự động font override (`\F`) trong MText khi đổi style

---

## Kiến trúc

```
TRANSLATE TEXT/
├── Commands.cs                  ← Entry point ([CommandMethod])
├── RibbonSetup.cs               ← IExtensionApplication — Ribbon "TH Tools"
├── PackageContents.xml          ← Bundle metadata
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
│   ├── AecGlossary.cs           ← Từ điển chuyên ngành AEC
│   └── AppSettings.cs           ← Lưu cài đặt Registry
│
└── UI/
    ├── TranslateWindow.xaml/.cs  ← WPF Dialog dịch thuật
    └── ChangeStyleWindow.xaml/.cs ← WPF Dialog đổi style
```

| Tầng | Vai trò |
|------|---------|
| **Commands.cs** | Chỉ chứa `[CommandMethod]`, gọi đến Services/UI |
| **RibbonSetup.cs** | Tạo Tab/Panel/Button trên Ribbon AutoCAD |
| **Core/** | Logic thuần C# — không phụ thuộc AutoCAD API |
| **Services/** | Tích hợp API bên ngoài, Registry, từ điển |
| **Models/** | POCO data class — decouple khỏi Transaction |
| **UI/** | WPF Windows — dark theme thống nhất |

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
    └── TranslateText.dll
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
