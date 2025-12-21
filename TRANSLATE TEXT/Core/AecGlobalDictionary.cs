using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HoangTam.AutoCAD.Tools.Core
{
    /// <summary>
    /// CORE ENGINE: Từ điển thuật ngữ chuyên ngành AEC đa ngôn ngữ.
    /// Hỗ trợ tra cứu đa chiều (Any-to-Any) thông qua cầu nối Tiếng Anh.
    /// </summary>
    public static class AecGlobalDictionary
    {
        // =========================================================================
        // 1. KHAI BÁO BIẾN (FIELD DECLARATIONS) - BẮT BUỘC PHẢI CÓ
        // =========================================================================

        // Master Data: [LangCode] -> [English Term] -> [Translated Term]
        private static readonly Dictionary<string, Dictionary<string, string>> MasterData =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // Cache Tra Xuôi: [LangCode] -> List<Key: English, Value: Local>
        // Dùng khi nguồn là Tiếng Anh
        private static readonly Dictionary<string, List<KeyValuePair<string, string>>> ForwardCache =
            new Dictionary<string, List<KeyValuePair<string, string>>>();

        // Cache Tra Ngược: [LangCode] -> List<Key: Local, Value: English>
        // Dùng khi nguồn KHÔNG phải Tiếng Anh (Local -> English)
        private static readonly Dictionary<string, List<KeyValuePair<string, string>>> ReverseCache =
            new Dictionary<string, List<KeyValuePair<string, string>>>();

        // =========================================================================
        // 2. STATIC CONSTRUCTOR (KHỞI TẠO DỮ LIỆU)
        // =========================================================================
        static AecGlobalDictionary()
        {
            InitializeVietnamese();
            InitializeJapanese();
            InitializeKorean();
            InitializeChinese();

            // Khởi tạo Cache cho cả 2 chiều để tối ưu hiệu năng
            foreach (var langKey in MasterData.Keys)
            {
                var dict = MasterData[langKey];

                // 1. Xuôi: English -> Local (Sắp xếp từ dài đến ngắn để Regex match đúng nhất)
                ForwardCache[langKey] = dict
                    .OrderByDescending(x => x.Key.Length)
                    .ToList();

                // 2. Ngược: Local -> English
                // GroupBy để loại bỏ trùng lặp nếu có nhiều từ địa phương trỏ về cùng 1 từ Anh
                ReverseCache[langKey] = dict
                    .GroupBy(x => x.Value)
                    .Select(g => new KeyValuePair<string, string>(g.Key, g.First().Key))
                    .OrderByDescending(x => x.Key.Length)
                    .ToList();
            }
        }

        // =========================================================================
        // 3. LOGIC XỬ LÝ CHÍNH (CORE LOGIC)
        // =========================================================================

        /// <summary>
        /// Áp dụng thuật ngữ đa chiều.
        /// Logic: Source Term -> [English Pivot] -> Target Term
        /// </summary>
        public static string ApplyTerminology(string text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string sCode = NormalizeCode(sourceLang);
            string tCode = NormalizeCode(targetLang);

            // --- XỬ LÝ AUTO DETECT THÔNG MINH ---
            if (sCode == "auto")
            {
                // Chiến thuật: Thử rà soát qua các ngôn ngữ ưu tiên (Việt -> Nhật -> Hàn -> Trung)
                // Nếu văn bản khớp với từ điển của ngôn ngữ nào thì áp dụng ngay.
                string[] priorityLangs = { "vi", "ja", "ko", "zh-CN" };

                foreach (var lang in priorityLangs)
                {
                    // Gọi đệ quy với ngôn ngữ cụ thể (sẽ rơi vào Case 3 bên dưới)
                    string tempResult = ApplyTerminology(text, lang, targetLang);

                    // Nếu kết quả khác văn bản gốc => Đã tìm thấy và thay thế được từ khóa
                    if (tempResult != text) return tempResult;
                }

                // Nếu không khớp ngôn ngữ nào -> Coi như tiếng Anh (Tra xuôi - Case 2)
                return ApplyTerminology(text, "en", targetLang);
            }
            // -------------------------------------

            if (sCode == tCode) return text;

            // CASE 2: Nguồn là Tiếng Anh (Tra xuôi trực tiếp: En -> Target)
            if (sCode == "en")
            {
                if (!ForwardCache.ContainsKey(tCode)) return text;
                return ReplaceText(text, ForwardCache[tCode]);
            }

            // CASE 3: Nguồn là ngôn ngữ khác (Cần tra ngược về Key tiếng Anh trước)
            if (!ReverseCache.ContainsKey(sCode)) return text; // Không hỗ trợ nguồn này

            var sourceTerms = ReverseCache[sCode];

            // Nếu đích là Tiếng Anh -> Chỉ cần tra ngược và thay thế bằng English Key
            if (tCode == "en")
            {
                return ReplaceText(text, sourceTerms);
            }

            // CASE 4: Cross-Lingual (VD: Nhật -> Việt)
            // Logic: Tìm từ Nhật -> Lấy Key Anh -> Tìm từ Việt -> Thay thế
            if (!MasterData.ContainsKey(tCode)) return text; // Đích không hỗ trợ

            var targetDict = MasterData[tCode];
            string processedText = text;

            foreach (var item in sourceTerms)
            {
                string localTerm = item.Key;   // Từ gốc (VD: Mặt cắt)
                string engKey = item.Value;    // Key tiếng Anh (VD: Section)

                // Nếu từ tiếng Anh này có tồn tại trong từ điển Đích (Việt/Anh/Trung...)
                if (targetDict.ContainsKey(engKey))
                {
                    string finalTerm = targetDict[engKey]; // Từ đích (VD: Cutting Edge -> Section 1-1 nếu target là En)

                    // Thay thế
                    string pattern = BuildRegexPattern(localTerm);
                    processedText = Regex.Replace(processedText, pattern, finalTerm, RegexOptions.IgnoreCase);
                }
            }

            return processedText;
        }

        // =========================================================================
        // 4. HELPER METHODS
        // =========================================================================

        private static string NormalizeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "en";
            string c = code.ToLower();
            if (c == "auto") return "auto";
            if (c.StartsWith("zh")) return "zh-CN";
            if (c.Contains("-")) return c.Split('-')[0];
            return c;
        }

        private static string ReplaceText(string input, List<KeyValuePair<string, string>> replacements)
        {
            string output = input;
            foreach (var term in replacements)
            {
                string pattern = BuildRegexPattern(term.Key);
                output = Regex.Replace(output, pattern, term.Value, RegexOptions.IgnoreCase);
            }
            return output;
        }

        private static string BuildRegexPattern(string term)
        {
            // Nếu là ký tự CJK (Trung/Nhật/Hàn) -> Không cần boundary \b vì chúng không dùng dấu cách
            if (Regex.IsMatch(term, @"[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uac00-\ud7af]"))
            {
                return Regex.Escape(term);
            }

            // Xử lý từ viết tắt có dấu chấm (N.T.S.) hoặc ký tự đặc biệt -> Không dùng \b ở cuối
            if (!char.IsLetterOrDigit(term.Last()))
            {
                return $@"\b{Regex.Escape(term)}";
            }

            // Mặc định: Whole word match
            return $@"\b{Regex.Escape(term)}\b";
        }

        // =========================================================================
        // DATA INITIALIZATION
        // =========================================================================
        private static void InitializeVietnamese()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Polyline", "Đường đa tuyến" }, { "Spline", "Đường cong Spline" },
                { "Hatch", "Mẫu tô" }, { "Block", "Khối (Block)" },
                { "Xref", "Tham chiếu ngoại" }, { "Viewport", "Cổng nhìn" },
                { "Layout", "Layout" }, { "Model Space", "Không gian mô hình" },
                { "Dimension", "Kích thước" }, { "Leader", "Đường dẫn ghi chú" },
                { "Scale", "Tỷ lệ" }, { "Section", "Mặt cắt" }, { "Elevation", "Mặt đứng" },
                { "Detail", "Chi tiết" },

                // --- TITLES ---
                { "List of Drawings", "Danh mục bản vẽ" }, { "Symbol Legend", "Bảng ký hiệu" },
                { "General Notes", "Ghi chú chung" }, { "Key Plan", "Sơ đồ vị trí" },
                { "Site Plan", "Tổng mặt bằng" }, { "Master Plan", "Quy hoạch tổng thể" },
                { "Floor Plan", "Mặt bằng" }, { "Reflected Ceiling Plan", "Mặt bằng trần đèn" },
                { "Roof Plan", "Mặt bằng mái" }, { "Structural Plan", "Mặt bằng kết cấu" },
                { "Section A-A", "Mặt cắt A-A" }, { "Front Elevation", "Mặt đứng chính" },
                { "Door Schedule", "Bảng thống kê cửa" }, { "Finish Schedule", "Bảng thống kê hoàn thiện" },

                // --- ABBREVIATIONS ---
                { "N.T.S", "Không theo tỷ lệ" }, { "Typ.", "Điển hình" },
                { "F.F.L", "Cốt hoàn thiện" }, { "S.F.L", "Cốt kết cấu" },
                { "C/C", "Khoảng cách tâm" }, { "Thk.", "Dày" },
                { "Dia.", "Đường kính" }, { "Hgt.", "Cao" }, { "Wdt.", "Rộng" },
                { "Qty.", "Số lượng" }, { "Dwg.", "Bản vẽ" },

                // --- ANNOTATIONS ---
                { "Verify in field", "Kiểm tra thực tế" }, { "To be removed", "Cần phá dỡ" },
                { "To remain", "Giữ nguyên" }, { "Existing", "Hiện trạng" },
                { "New partition", "Vách ngăn mới" }, { "See Detail", "Xem chi tiết" },
                { "Match existing", "Khớp với hiện trạng" },

                // --- ROOMS ---
                { "Living Room", "Phòng khách" }, { "Kitchen", "Bếp" },
                { "Dining Room", "Phòng ăn" }, { "Bedroom", "Phòng ngủ" },
                { "Master Bedroom", "Phòng ngủ chính" }, { "Bathroom", "Phòng tắm" },
                { "WC", "Vệ sinh" }, { "Corridor", "Hành lang" },
                { "Balcony", "Ban công" }, { "Lobby", "Sảnh" },
                { "Storage", "Kho" }, { "Parking", "Bãi đậu xe" },

                // --- MATERIALS ---
                { "Reinforced Concrete", "Bê tông cốt thép" },
                { "Precast Concrete", "Bê tông đúc sẵn" },
                { "Lean Concrete", "Bê tông lót" },
                { "Concrete", "Bê tông" },
                { "Masonry", "Tường xây" },
                { "Brick", "Gạch" },
                { "Mortar", "Vữa" },
                { "Stainless Steel", "Inox" },
                { "Galvanized Steel", "Thép mạ kẽm" },
                { "Aluminum", "Nhôm" },
                { "Wrought Iron", "Sắt mỹ thuật" },
                { "Tempered Glass", "Kính cường lực" },
                { "Laminated Glass", "Kính dán an toàn" },
                { "Frosted Glass", "Kính mờ" },
                { "Marble", "Đá cẩm thạch" },
                { "Granite", "Đá hoa cương" },
                { "Ceramic Tile", "Gạch Ceramic" },
                { "Porcelain Tile", "Gạch bán sứ" },
                { "Hardwood", "Gỗ tự nhiên" },
                { "Plywood", "Gỗ dán" },
                { "Gypsum Board", "Tấm thạch cao" },
                { "Waterproofing", "Chống thấm" }
            };
            MasterData["vi"] = dict;
        }

        // =========================================================================
        // 2. JAPANESE (TIẾNG NHẬT - Full Parity)
        // =========================================================================
        private static void InitializeJapanese()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // --- GENERAL COMMANDS ---
                { "Polyline", "ポリライン" }, { "Spline", "スプライン" },
                { "Hatch", "ハッチング" }, { "Block", "ブロック" },
                { "Xref", "外部参照" }, { "Viewport", "ビューポート" },
                { "Layout", "レイアウト" }, { "Model Space", "モデル空間" },
                { "Dimension", "寸法" }, { "Leader", "引出線" },
                { "Scale", "尺度" }, { "Section", "断面" }, { "Elevation", "立面" },
                { "Detail", "詳細" },

                // --- TITLES ---
                { "List of Drawings", "図面リスト" }, { "Symbol Legend", "凡例" },
                { "General Notes", "特記仕様書" }, { "Key Plan", "キープラン" },
                { "Site Plan", "配置図" }, { "Master Plan", "全体計画図" },
                { "Floor Plan", "平面図" }, { "Reflected Ceiling Plan", "天井伏図" },
                { "Roof Plan", "屋根伏図" }, { "Structural Plan", "構造図" },
                { "Section A-A", "A-A 断面図" }, { "Front Elevation", "正立面図" },
                { "Door Schedule", "建具表" }, { "Finish Schedule", "仕上表" },

                // --- ABBREVIATIONS ---
                { "N.T.S", "縮尺なし" }, { "Typ.", "一般/共通" },
                { "F.F.L", "仕上レベル" }, { "S.F.L", "躯体レベル" },
                { "C/C", "ピッチ" }, { "Thk.", "厚" },
                { "Dia.", "径" }, { "Hgt.", "高" }, { "Wdt.", "幅" },
                { "Qty.", "数量" }, { "Dwg.", "図面" },

                // --- ANNOTATIONS ---
                { "Verify in field", "現調確認" }, { "To be removed", "撤去" },
                { "To remain", "既存利用" }, { "Existing", "既存" },
                { "New partition", "新規間仕切" }, { "See Detail", "詳細図参照" },
                { "Match existing", "既存合わせ" },

                // --- ROOMS ---
                { "Living Room", "居間 (リビング)" }, { "Kitchen", "厨房 (キッチン)" },
                { "Dining Room", "食堂 (ダイニング)" }, { "Bedroom", "寝室" },
                { "Master Bedroom", "主寝室" }, { "Bathroom", "浴室" },
                { "WC", "便所/トイレ" }, { "Corridor", "廊下" },
                { "Balcony", "バルコニー" }, { "Lobby", "ロビー" },
                { "Storage", "倉庫" }, { "Parking", "駐車場" },

                // --- MATERIALS ---
                { "Reinforced Concrete", "鉄筋コンクリート (RC)" }, { "Precast Concrete", "プレキャストコンクリート (PC)" },
                { "Lean Concrete", "捨てコンクリート" }, { "Concrete", "コンクリート" },
                { "Masonry", "組積造" }, { "Brick", "煉瓦" }, { "Mortar", "モルタル" },
                { "Stainless Steel", "ステンレス (SUS)" }, { "Galvanized Steel", "亜鉛メッキ鋼" },
                { "Aluminum", "アルミニウム" }, { "Wrought Iron", "錬鉄" },
                { "Tempered Glass", "強化ガラス" }, { "Laminated Glass", "合わせガラス" },
                { "Frosted Glass", "曇りガラス" },
                { "Marble", "大理石" }, { "Granite", "御影石" },
                { "Ceramic Tile", "磁器質タイル" }, { "Porcelain Tile", "ポーセリンタイル" },
                { "Hardwood", "無垢材" }, { "Plywood", "合板" },
                { "Gypsum Board", "石膏ボード" }, { "Waterproofing", "防水" }
            };
            MasterData["ja"] = dict;
        }

        // =========================================================================
        // 3. KOREAN (TIẾNG HÀN - Full Parity)
        // =========================================================================
        private static void InitializeKorean()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // --- GENERAL COMMANDS ---
                { "Polyline", "폴리선" }, { "Spline", "스플라인" },
                { "Hatch", "해치" }, { "Block", "블록" },
                { "Xref", "외부 참조" }, { "Viewport", "뷰포트" },
                { "Layout", "레이아웃" }, { "Model Space", "모형 공간" },
                { "Dimension", "치수" }, { "Leader", "지시선" },
                { "Scale", "축척" }, { "Section", "단면" }, { "Elevation", "입면" },
                { "Detail", "상세" },

                // --- TITLES ---
                { "List of Drawings", "도면 목록" }, { "Symbol Legend", "범례" },
                { "General Notes", "일반 사항" }, { "Key Plan", "키 플랜" },
                { "Site Plan", "배치도" }, { "Master Plan", "종합 계획도" },
                { "Floor Plan", "평면도" }, { "Reflected Ceiling Plan", "천장도" },
                { "Roof Plan", "지붕 평면도" }, { "Structural Plan", "구조 평면도" },
                { "Section A-A", "A-A 단면도" }, { "Front Elevation", "정면도" },
                { "Door Schedule", "창호 일람표" }, { "Finish Schedule", "실내 마감표" },

                // --- ABBREVIATIONS ---
                { "N.T.S", "Non-Scale" }, { "Typ.", "대표" },
                { "F.F.L", "마감 레벨" }, { "S.F.L", "구조 레벨" },
                { "C/C", "중심 간격" }, { "Thk.", "두께" },
                { "Dia.", "직경" }, { "Hgt.", "높이" }, { "Wdt.", "폭" },
                { "Qty.", "수량" }, { "Dwg.", "도면" },

                // --- ANNOTATIONS ---
                { "Verify in field", "현장 확인" }, { "To be removed", "철거" },
                { "To remain", "존치" }, { "Existing", "기존" },
                { "New partition", "신설 벽체" }, { "See Detail", "상세 참조" },
                { "Match existing", "기존 마감 유지" },

                // --- ROOMS ---
                { "Living Room", "거실" }, { "Kitchen", "주방" },
                { "Dining Room", "식당" }, { "Bedroom", "침실" },
                { "Master Bedroom", "안방" }, { "Bathroom", "욕실" },
                { "WC", "화장실" }, { "Corridor", "복도" },
                { "Balcony", "발코니" }, { "Lobby", "로비" },
                { "Storage", "창고" }, { "Parking", "주차장" },

                // --- MATERIALS ---
                { "Reinforced Concrete", "철근 콘크리트" }, { "Precast Concrete", "PC 콘크리트" },
                { "Lean Concrete", "버림 콘크리트" }, { "Concrete", "콘크리트" },
                { "Masonry", "조적" }, { "Brick", "벽돌" }, { "Mortar", "모르타르" },
                { "Stainless Steel", "스테인리스" }, { "Galvanized Steel", "아연 도금 강" },
                { "Aluminum", "알루미늄" }, { "Wrought Iron", "연철" },
                { "Tempered Glass", "강화 유리" }, { "Laminated Glass", "접합 유리" },
                { "Frosted Glass", "불투명 유리" },
                { "Marble", "대리석" }, { "Granite", "화강석" },
                { "Ceramic Tile", "세라믹 타일" }, { "Porcelain Tile", "포세린 타일" },
                { "Hardwood", "원목" }, { "Plywood", "합판" },
                { "Gypsum Board", "석고 보드" }, { "Waterproofing", "방수" }
            };
            MasterData["ko"] = dict;
        }

        // =========================================================================
        // 4. CHINESE (TIẾNG TRUNG - Full Parity)
        // =========================================================================
        private static void InitializeChinese()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // --- GENERAL COMMANDS ---
                { "Polyline", "多段线" }, { "Spline", "样条曲线" },
                { "Hatch", "图案填充" }, { "Block", "块" },
                { "Xref", "外部参照" }, { "Viewport", "视口" },
                { "Layout", "布局" }, { "Model Space", "模型空间" },
                { "Dimension", "标注" }, { "Leader", "引线" },
                { "Scale", "比例" }, { "Section", "剖面" }, { "Elevation", "立面" },
                { "Detail", "详图" },

                // --- TITLES ---
                { "List of Drawings", "图纸目录" }, { "Symbol Legend", "符号图例" },
                { "General Notes", "设计总说明" }, { "Key Plan", "索引图" },
                { "Site Plan", "总平面图" }, { "Master Plan", "总体规划图" },
                { "Floor Plan", "平面图" }, { "Reflected Ceiling Plan", "吊顶平面图" },
                { "Roof Plan", "屋顶平面图" }, { "Structural Plan", "结构平面图" },
                { "Section A-A", "A-A 剖面图" }, { "Front Elevation", "正立面图" },
                { "Door Schedule", "门窗表" }, { "Finish Schedule", "装修做法表" },

                // --- ABBREVIATIONS ---
                { "N.T.S", "不按比例" }, { "Typ.", "典型" },
                { "F.F.L", "建筑标高" }, { "S.F.L", "结构标高" },
                { "C/C", "中心间距" }, { "Thk.", "厚" },
                { "Dia.", "直径" }, { "Hgt.", "高" }, { "Wdt.", "宽" },
                { "Qty.", "数量" }, { "Dwg.", "图纸" },

                // --- ANNOTATIONS ---
                { "Verify in field", "现场核实" }, { "To be removed", "拆除" },
                { "To remain", "保留" }, { "Existing", "现状" },
                { "New partition", "新建隔墙" }, { "See Detail", "详见大样" },
                { "Match existing", "由现场确定" },

                // --- ROOMS ---
                { "Living Room", "起居室" }, { "Kitchen", "厨房" },
                { "Dining Room", "餐厅" }, { "Bedroom", "卧室" },
                { "Master Bedroom", "主卧" }, { "Bathroom", "浴室" },
                { "WC", "卫生间" }, { "Corridor", "走廊" },
                { "Balcony", "阳台" }, { "Lobby", "大堂" },
                { "Storage", "储藏室" }, { "Parking", "停车场" },

                // --- MATERIALS ---
                { "Reinforced Concrete", "钢筋混凝土" }, { "Precast Concrete", "预制混凝土" },
                { "Lean Concrete", "素混凝土/垫层" }, { "Concrete", "混凝土" },
                { "Masonry", "砌体" }, { "Brick", "砖" }, { "Mortar", "砂浆" },
                { "Stainless Steel", "不锈钢" }, { "Galvanized Steel", "镀锌钢" },
                { "Aluminum", "铝合金" }, { "Wrought Iron", "熟铁" },
                { "Tempered Glass", "钢化玻璃" }, { "Laminated Glass", "夹胶玻璃" },
                { "Frosted Glass", "磨砂玻璃" },
                { "Marble", "大理石" }, { "Granite", "花岗岩" },
                { "Ceramic Tile", "釉面砖" }, { "Porcelain Tile", "玻化砖" },
                { "Hardwood", "硬木/实木" }, { "Plywood", "胶合板" },
                { "Gypsum Board", "石膏板" }, { "Waterproofing", "防水层" }
            };
            MasterData["zh-CN"] = dict;
        }
    }
}