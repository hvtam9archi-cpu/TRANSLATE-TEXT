using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TranslateText.Services
{
    public class AecTerm
    {
        public string En { get; set; } // English
        public string Vi { get; set; } // Vietnamese
        public string Zh { get; set; } // Chinese Simplified (zh-CN)
        public string Ja { get; set; } // Japanese
        public string Ko { get; set; } // Korean
    }

    /// <summary>
    /// Bản chuẩn hóa của một thuật ngữ để đối chiếu nhanh:
    /// bỏ dấu tiếng Việt, bỏ khoảng trắng thừa, chữ hoa thống nhất.
    /// </summary>
    internal sealed class AecTermIndex
    {
        public AecTerm Term { get; set; }
        public string EnN { get; set; }
        public string ViN { get; set; }
        public string ZhN { get; set; }
        public string JaN { get; set; }
        public string KoN { get; set; }
    }

    public class AecPattern
    {
        public string SourceLang { get; set; }
        public string TargetLang { get; set; }
        public string RegexStr { get; set; }
        public string Replacement { get; set; }
        public MatchEvaluator Evaluator { get; set; }
    }

    /// <summary>
    /// Từ điển chuyên ngành AEC (Kiến trúc - Kỹ thuật - Xây dựng).
    /// Tra cứu trước khi gọi dịch thuật để giữ đúng thuật ngữ ngành.
    /// Đối chiếu không phân biệt hoa/thường, dấu tiếng Việt và khoảng trắng thừa,
    /// phù hợp với văn bản bản vẽ thường bị mất dấu hoặc viết toàn chữ hoa.
    /// </summary>
    public static class AecGlossary
    {
        private static readonly List<AecTerm> _terms = BuildTerms();
        private static readonly List<AecTermIndex> _termIndexes =
            _terms.Select(BuildIndex).ToList();

        private static readonly List<AecPattern> _patterns = BuildPatterns();

        private static List<AecTerm> BuildTerms()
        {
            return new List<AecTerm>
            {
                // ==================== BẢN VẼ & TÀI LIỆU ====================
                new AecTerm { En = "plan", Vi = "Mặt bằng", Zh = "平面图", Ja = "平面図", Ko = "평면도" },
                new AecTerm { En = "ground floor plan", Vi = "Mặt bằng tầng trệt", Zh = "首层平面图", Ja = "1階平面図", Ko = "1층 평면도" },
                new AecTerm { En = "roof plan", Vi = "Mặt bằng mái", Zh = "屋顶平面图", Ja = "屋根伏図", Ko = "지붕 평면도" },
                new AecTerm { En = "ceiling plan", Vi = "Mặt bằng trần", Zh = "天花板平面图", Ja = "天井伏図", Ko = "천장 평면도" },
                new AecTerm { En = "section", Vi = "Mặt cắt", Zh = "剖面图", Ja = "断面図", Ko = "단면도" },
                new AecTerm { En = "cross section", Vi = "Mặt cắt ngang", Zh = "横剖面图", Ja = "横断面図", Ko = "횡단면도" },
                new AecTerm { En = "longitudinal section", Vi = "Mặt cắt dọc", Zh = "纵剖面图", Ja = "縦断面図", Ko = "종단면도" },
                new AecTerm { En = "elevation", Vi = "Mặt đứng", Zh = "立面图", Ja = "立面図", Ko = "입면도" },
                new AecTerm { En = "front elevation", Vi = "Mặt đứng chính", Zh = "正立面图", Ja = "正面図", Ko = "정면도" },
                new AecTerm { En = "side elevation", Vi = "Mặt đứng bên", Zh = "侧立面图", Ja = "側面図", Ko = "측면도" },
                new AecTerm { En = "detail", Vi = "Chi tiết", Zh = "详图", Ja = "詳細図", Ko = "상세도" },
                new AecTerm { En = "scale", Vi = "Tỷ lệ", Zh = "比例", Ja = "縮尺", Ko = "축척" },
                new AecTerm { En = "note", Vi = "Ghi chú", Zh = "注", Ja = "注記", Ko = "주기" },
                new AecTerm { En = "general note", Vi = "Ghi chú chung", Zh = "总说明", Ja = "特記仕様", Ko = "일반 사항" },
                new AecTerm { En = "legend", Vi = "Chú giải", Zh = "图例", Ja = "凡例", Ko = "범례" },
                new AecTerm { En = "revision", Vi = "Bản sửa đổi", Zh = "修改", Ja = "改訂", Ko = "수정" },
                new AecTerm { En = "drawing", Vi = "Bản vẽ", Zh = "图纸", Ja = "図面", Ko = "도면" },
                new AecTerm { En = "shop drawing", Vi = "Bản vẽ thi công", Zh = "施工图", Ja = "施工図", Ko = "시공도면" },
                new AecTerm { En = "as-built drawing", Vi = "Bản vẽ hoàn công", Zh = "竣工图", Ja = "竣工図", Ko = "준공도면" },
                new AecTerm { En = "layout", Vi = "Bố trí", Zh = "布局", Ja = "配置図", Ko = "배치도" },
                new AecTerm { En = "title block", Vi = "Khung tên", Zh = "图框", Ja = "タイトルブロック", Ko = "제목란" },
                new AecTerm { En = "drawing number", Vi = "Số bản vẽ", Zh = "图号", Ja = "図番", Ko = "도면번호" },
                new AecTerm { En = "drawn by", Vi = "Người vẽ", Zh = "绘图者", Ja = "作図者", Ko = "작도자" },
                new AecTerm { En = "checked by", Vi = "Người kiểm tra", Zh = "校对者", Ja = "検図者", Ko = "검도자" },
                new AecTerm { En = "approved by", Vi = "Người duyệt", Zh = "批准者", Ja = "承認者", Ko = "승인자" },
                new AecTerm { En = "date", Vi = "Ngày", Zh = "日期", Ja = "日付", Ko = "날짜" },
                new AecTerm { En = "project", Vi = "Dự án", Zh = "项目", Ja = "プロジェクト", Ko = "프로젝트" },
                new AecTerm { En = "client", Vi = "Chủ đầu tư", Zh = "业主", Ja = "発注者", Ko = "발주처" },
                new AecTerm { En = "consultant", Vi = "Tư vấn", Zh = "顾问", Ja = "コンサルタント", Ko = "컨설턴트" },
                new AecTerm { En = "contractor", Vi = "Nhà thầu", Zh = "承包商", Ja = "施工会社", Ko = "시공사" },
                new AecTerm { En = "typical detail", Vi = "Chi tiết tiêu chuẩn", Zh = "标准详图", Ja = "標準詳細", Ko = "표준 상세" },
                new AecTerm { En = "location plan", Vi = "Sơ đồ vị trí", Zh = "位置图", Ja = "位置図", Ko = "위치도" },
                new AecTerm { En = "key plan", Vi = "Sơ đồ chỉ dẫn", Zh = "索引图", Ja = "キープラン", Ko = "키 플랜" },

                // ==================== KIẾN TRÚC & PHÒNG ====================
                new AecTerm { En = "lobby", Vi = "Sảnh chờ", Zh = "大堂", Ja = "ロビー", Ko = "로비" },
                new AecTerm { En = "corridor", Vi = "Hành lang", Zh = "走廊", Ja = "廊下", Ko = "복도" },
                new AecTerm { En = "staircase", Vi = "Cầu thang", Zh = "楼梯", Ja = "階段", Ko = "계단" },
                new AecTerm { En = "stair", Vi = "Cầu thang", Zh = "楼梯", Ja = "階段", Ko = "계단" },
                new AecTerm { En = "ramp", Vi = "Dốc", Zh = "坡道", Ja = "スロープ", Ko = "경사로" },
                new AecTerm { En = "balcony", Vi = "Ban công", Zh = "阳台", Ja = "バルコニー", Ko = "발코니" },
                new AecTerm { En = "terrace", Vi = "Sân thượng", Zh = "露台", Ja = "テラス", Ko = "테라스" },
                new AecTerm { En = "toilet", Vi = "Nhà vệ sinh", Zh = "卫生间", Ja = "トイレ", Ko = "화장실" },
                new AecTerm { En = "bathroom", Vi = "Phòng tắm", Zh = "浴室", Ja = "浴室", Ko = "욕실" },
                new AecTerm { En = "bedroom", Vi = "Phòng ngủ", Zh = "卧室", Ja = "寝室", Ko = "침실" },
                new AecTerm { En = "living room", Vi = "Phòng khách", Zh = "客厅", Ja = "リビング", Ko = "거실" },
                new AecTerm { En = "kitchen", Vi = "Bếp", Zh = "厨房", Ja = "キッチン", Ko = "주방" },
                new AecTerm { En = "pantry", Vi = "Pantry", Zh = "食品储藏室", Ja = "パントリー", Ko = "팬트리" },
                new AecTerm { En = "parking", Vi = "Bãi đỗ xe", Zh = "停车场", Ja = "駐車場", Ko = "주차장" },
                new AecTerm { En = "garage", Vi = "Nhà để xe", Zh = "车库", Ja = "ガレージ", Ko = "차고" },
                new AecTerm { En = "elevator", Vi = "Thang máy", Zh = "电梯", Ja = "エレベーター", Ko = "엘리베이터" },
                new AecTerm { En = "lift", Vi = "Thang máy", Zh = "电梯", Ja = "エレベーター", Ko = "엘리베이터" },
                new AecTerm { En = "escalator", Vi = "Thang cuốn", Zh = "自动扶梯", Ja = "エスカレーター", Ko = "에스컬레이터" },
                new AecTerm { En = "entrance", Vi = "Lối vào", Zh = "入口", Ja = "入口", Ko = "입구" },
                new AecTerm { En = "exit", Vi = "Lối thoát hiểm", Zh = "出口", Ja = "非常口", Ko = "출구" },
                new AecTerm { En = "shaft", Vi = "Giếng kỹ thuật", Zh = "竖井", Ja = "シャフト", Ko = "샤프트" },
                new AecTerm { En = "parapet", Vi = "Tường chắn mái", Zh = "女儿墙", Ja = "パラペット", Ko = "파라펫" },
                new AecTerm { En = "canopy", Vi = "Mái che", Zh = "雨棚", Ja = "キャノピー", Ko = "캐노피" },
                new AecTerm { En = "facade", Vi = "Mặt tiền", Zh = "外立面", Ja = "ファサード", Ko = "파사드" },
                new AecTerm { En = "curtain wall", Vi = "Tường kính", Zh = "幕墙", Ja = "カーテンウォール", Ko = "커튼월" },
                new AecTerm { En = "partition", Vi = "Vách ngăn", Zh = "隔断", Ja = "間仕切り", Ko = "파티션" },
                new AecTerm { En = "suspended ceiling", Vi = "Trần thả", Zh = "吊顶", Ja = "吊り天井", Ko = "천장" },
                new AecTerm { En = "handrail", Vi = "Tay vịn", Zh = "扶手", Ja = "手すり", Ko = "난간" },
                new AecTerm { En = "railing", Vi = "Lan can", Zh = "栏杆", Ja = "手すり", Ko = "난간" },
                new AecTerm { En = "balustrade", Vi = "Lan can", Zh = "栏杆", Ja = "欄干", Ko = "난간대" },

                // ==================== KẾT CẤU ====================
                new AecTerm { En = "beam", Vi = "Dầm", Zh = "梁", Ja = "梁", Ko = "보" },
                new AecTerm { En = "column", Vi = "Cột", Zh = "柱", Ja = "柱", Ko = "기둥" },
                new AecTerm { En = "slab", Vi = "Sàn", Zh = "板", Ja = "床", Ko = "슬래브" },
                new AecTerm { En = "flat slab", Vi = "Sàn phẳng", Zh = "平板", Ja = "フラットスラブ", Ko = "플랫 슬래브" },
                new AecTerm { En = "wall", Vi = "Tường", Zh = "墙", Ja = "壁", Ko = "벽" },
                new AecTerm { En = "shear wall", Vi = "Tường chịu lực", Zh = "剪力墙", Ja = "耐震壁", Ko = "전단벽" },
                new AecTerm { En = "core wall", Vi = "Lõi tường", Zh = "核心筒", Ja = "コア壁", Ko = "코어월" },
                new AecTerm { En = "retaining wall", Vi = "Tường chắn", Zh = "挡土墙", Ja = "擁壁", Ko = "옹벽" },
                new AecTerm { En = "foundation", Vi = "Móng", Zh = "基础", Ja = "基礎", Ko = "기초" },
                new AecTerm { En = "footing", Vi = "Móng bàn", Zh = "独立基础", Ja = "独立基礎", Ko = "독립기초" },
                new AecTerm { En = "pile", Vi = "Cọc", Zh = "桩", Ja = "杭", Ko = "말뚝" },
                new AecTerm { En = "pile cap", Vi = "Móng cọc", Zh = "承台", Ja = "パイルキャップ", Ko = "필캡" },
                new AecTerm { En = "ground beam", Vi = "Dầm móng", Zh = "地梁", Ja = "地中梁", Ko = "지중보" },
                new AecTerm { En = "tie beam", Vi = "Dầm liên kết", Zh = "拉梁", Ja = "繋梁", Ko = "연결보" },
                new AecTerm { En = "landing", Vi = "Chiếu nghỉ", Zh = "休息平台", Ja = "中間踊場", Ko = "계단참" },
                new AecTerm { En = "rebar", Vi = "Thép cốt", Zh = "钢筋", Ja = "鉄筋", Ko = "철근" },
                new AecTerm { En = "reinforcement", Vi = "Cốt thép", Zh = "配筋", Ja = "配筋", Ko = "배근" },
                new AecTerm { En = "stirrup", Vi = "Cốt đai", Zh = "箍筋", Ja = "帯筋", Ko = "스터럽" },
                new AecTerm { En = "expansion joint", Vi = "Khe giãn nở", Zh = "伸缩缝", Ja = "伸縮目地", Ko = "신축줄눈" },
                new AecTerm { En = "construction joint", Vi = "Khe ngừng thi công", Zh = "施工缝", Ja = "打ち継ぎ目地", Ko = "시공줄눈" },
                new AecTerm { En = "anchor bolt", Vi = "Bu lông neo", Zh = "锚栓", Ja = "アンカーボルト", Ko = "앵커볼트" },
                new AecTerm { En = "precast", Vi = "Đúc sẵn", Zh = "预制", Ja = "プレキャスト", Ko = "프리캐스트" },
                new AecTerm { En = "formwork", Vi = "Cốp pha", Zh = "模板", Ja = "型枠", Ko = "거푸집" },
                new AecTerm { En = "scaffolding", Vi = "Giàn giáo", Zh = "脚手架", Ja = "足場", Ko = "비계" },
                new AecTerm { En = "concrete", Vi = "Bê tông", Zh = "混凝土", Ja = "コンクリート", Ko = "콘크리트" },
                new AecTerm { En = "reinforced concrete", Vi = "Bê tông cốt thép", Zh = "钢筋混凝土", Ja = "鉄筋コンクリート", Ko = "철근콘크리트" },

                // ==================== CƠ ĐIỆN (MEP) ====================
                new AecTerm { En = "hvac", Vi = "Điều hòa thông gió", Zh = "暖通空调", Ja = "空調設備", Ko = "냉난방" },
                new AecTerm { En = "air conditioning", Vi = "Điều hòa không khí", Zh = "空调", Ja = "空調", Ko = "에어컨" },
                new AecTerm { En = "duct", Vi = "Ống gió", Zh = "风管", Ja = "ダクト", Ko = "덕트" },
                new AecTerm { En = "diffuser", Vi = "Miệng gió", Zh = "散流器", Ja = "吹出口", Ko = "디퓨저" },
                new AecTerm { En = "grille", Vi = "Cửa gió", Zh = "格栅", Ja = "グリル", Ko = "그릴" },
                new AecTerm { En = "chiller", Vi = "Máy lạnh trung tâm", Zh = "冷水机组", Ja = "チラー", Ko = "칠러" },
                new AecTerm { En = "cooling tower", Vi = "Tháp giải nhiệt", Zh = "冷却塔", Ja = "冷却塔", Ko = "냉각탑" },
                new AecTerm { En = "exhaust fan", Vi = "Quạt hút", Zh = "排风扇", Ja = "換気扇", Ko = "환기팬" },
                new AecTerm { En = "plumbing", Vi = "Hệ thống cấp thoát nước", Zh = "给排水", Ja = "衛生設備", Ko = "위생설비" },
                new AecTerm { En = "water supply", Vi = "Cấp nước", Zh = "给水", Ja = "上水", Ko = "급수" },
                new AecTerm { En = "drainage", Vi = "Thoát nước", Zh = "排水", Ja = "排水", Ko = "배수" },
                new AecTerm { En = "floor drain", Vi = "Ga thoát sàn", Zh = "地漏", Ja = "排水口", Ko = "바닥 배수구" },
                new AecTerm { En = "septic tank", Vi = "Bể tự hoại", Zh = "化粪池", Ja = "浄化槽", Ko = "정화조" },
                new AecTerm { En = "fire fighting", Vi = "Phòng cháy chữa cháy", Zh = "消防", Ja = "消防", Ko = "소방" },
                new AecTerm { En = "hydrant", Vi = "Vòi chữa cháy", Zh = "消火栓", Ja = "消火栓", Ko = "소화전" },
                new AecTerm { En = "sprinkler", Vi = "Đầu phun sprinkler", Zh = "洒水喷头", Ja = "スプリンクラー", Ko = "스프링클러" },
                new AecTerm { En = "smoke detector", Vi = "Đầu báo khói", Zh = "烟雾探测器", Ja = "煙感知器", Ko = "연기감지기" },
                new AecTerm { En = "fire alarm", Vi = "Báo cháy", Zh = "火灾报警", Ja = "火災報知器", Ko = "화재경보" },
                new AecTerm { En = "electrical", Vi = "Điện", Zh = "电气", Ja = "電気", Ko = "전기" },
                new AecTerm { En = "switchboard", Vi = "Tủ điện tổng", Zh = "配电盘", Ja = "配電盤", Ko = "배전반" },
                new AecTerm { En = "distribution board", Vi = "Tủ điện phân phối", Zh = "配电箱", Ja = "分電盤", Ko = "분전반" },
                new AecTerm { En = "cable tray", Vi = "Khay cáp", Zh = "电缆桥架", Ja = "ケーブルトレイ", Ko = "케이블 트레이" },
                new AecTerm { En = "conduit", Vi = "Ống luồn dây", Zh = "导线管", Ja = "電線管", Ko = "전선관" },
                new AecTerm { En = "socket outlet", Vi = "Ổ cắm", Zh = "插座", Ja = "コンセント", Ko = "콘센트" },
                new AecTerm { En = "light fixture", Vi = "Đèn", Zh = "灯具", Ja = "照明器具", Ko = "조명기구" },
                new AecTerm { En = "lighting", Vi = "Chiếu sáng", Zh = "照明", Ja = "照明", Ko = "조명" },
                new AecTerm { En = "earthing", Vi = "Tiếp địa", Zh = "接地", Ja = "接地", Ko = "접지" },
                new AecTerm { En = "lightning protection", Vi = "Chống sét", Zh = "防雷", Ja = "避雷", Ko = "피뢰" },
                new AecTerm { En = "generator", Vi = "Máy phát điện", Zh = "发电机", Ja = "発電機", Ko = "발전기" },
                new AecTerm { En = "transformer", Vi = "Máy biến áp", Zh = "变压器", Ja = "変圧器", Ko = "변압기" },
                new AecTerm { En = "pump", Vi = "Máy bơm", Zh = "泵", Ja = "ポンプ", Ko = "펌프" },
                new AecTerm { En = "valve", Vi = "Van", Zh = "阀门", Ja = "弁", Ko = "밸브" },
                new AecTerm { En = "pipe", Vi = "Ống", Zh = "管道", Ja = "配管", Ko = "배관" },

                // ==================== QUY HOẠCH & MÔI TRƯỜNG ====================
                new AecTerm { En = "site plan", Vi = "Mặt bằng tổng thể", Zh = "总平面图", Ja = "敷地計画図", Ko = "부지 평면도" },
                new AecTerm { En = "road", Vi = "Đường", Zh = "道路", Ja = "道路", Ko = "도로" },
                new AecTerm { En = "pavement", Vi = "Mặt đường", Zh = "路面", Ja = "鋪装", Ko = "포장" },
                new AecTerm { En = "curb", Vi = "Lề đường", Zh = "路缘石", Ja = "縁石", Ko = "연석" },
                new AecTerm { En = "culvert", Vi = "Cống", Zh = "涵洞", Ja = "暗渠", Ko = "암거" },
                new AecTerm { En = "slope", Vi = "Dốc", Zh = "边坡", Ja = "法面", Ko = "사면" },
                new AecTerm { En = "fence", Vi = "Hàng rào", Zh = "围栏", Ja = "塀", Ko = "울타리" },
                new AecTerm { En = "gate", Vi = "Cổng", Zh = "大门", Ja = "門", Ko = "대문" },
                new AecTerm { En = "landscape", Vi = "Cảnh quan", Zh = "景观", Ja = "造園", Ko = "조경" },
                new AecTerm { En = "boundary", Vi = "Ranh giới", Zh = "边界", Ja = "境界", Ko = "경계" },

                // ==================== HÌNH HỌC & VẬT LIỆU ====================
                new AecTerm { En = "level", Vi = "Cao độ", Zh = "标高", Ja = "レベル", Ko = "레벨" },
                new AecTerm { En = "grid", Vi = "Lưới trục", Zh = "轴网", Ja = "通り芯", Ko = "그리드" },
                new AecTerm { En = "axis", Vi = "Trục", Zh = "轴线", Ja = "通り芯", Ko = "축선" },
                new AecTerm { En = "centerline", Vi = "Đường tâm", Zh = "中心线", Ja = "中心線", Ko = "중심선" },
                new AecTerm { En = "dimension", Vi = "Kích thước", Zh = "尺寸", Ja = "寸法", Ko = "치수" },
                new AecTerm { En = "thickness", Vi = "Bề dày", Zh = "厚度", Ja = "厚さ", Ko = "두께" },
                new AecTerm { En = "width", Vi = "Chiều rộng", Zh = "宽度", Ja = "幅", Ko = "폭" },
                new AecTerm { En = "height", Vi = "Chiều cao", Zh = "高度", Ja = "高さ", Ko = "높이" },
                new AecTerm { En = "length", Vi = "Chiều dài", Zh = "长度", Ja = "長さ", Ko = "길이" },
                new AecTerm { En = "diameter", Vi = "Đường kính", Zh = "直径", Ja = "直径", Ko = "직경" },
                new AecTerm { En = "area", Vi = "Diện tích", Zh = "面积", Ja = "面積", Ko = "면적" },
                new AecTerm { En = "volume", Vi = "Thể tích", Zh = "体积", Ja = "体積", Ko = "체적" },
                new AecTerm { En = "tolerance", Vi = "Dung sai", Zh = "公差", Ja = "公差", Ko = "공차" },
                new AecTerm { En = "door", Vi = "Cửa đi", Zh = "门", Ja = "ドア", Ko = "문" },
                new AecTerm { En = "window", Vi = "Cửa sổ", Zh = "窗", Ja = "窓", Ko = "창문" },
                new AecTerm { En = "basement", Vi = "Tầng hầm", Zh = "地下室", Ja = "地下室", Ko = "지하실" },
                new AecTerm { En = "materials", Vi = "Vật liệu", Zh = "材料", Ja = "材料", Ko = "재료" },
                new AecTerm { En = "specification", Vi = "Chỉ dẫn kỹ thuật", Zh = "规范", Ja = "仕様書", Ko = "시방서" },
                new AecTerm { En = "insulation", Vi = "Lớp cách nhiệt", Zh = "保温", Ja = "断熱", Ko = "단열" },
                new AecTerm { En = "waterproofing", Vi = "Chống thấm", Zh = "防水", Ja = "防水", Ko = "방수" },
                new AecTerm { En = "screed", Vi = "Lớp láng", Zh = "找平层", Ja = "モルタル", Ko = "스크리드" },
                new AecTerm { En = "tile", Vi = "Gạch ốp", Zh = "瓷砖", Ja = "タイル", Ko = "타일" },
                new AecTerm { En = "paint", Vi = "Sơn", Zh = "油漆", Ja = "塗装", Ko = "도장" },
                new AecTerm { En = "plaster", Vi = "Trát tường", Zh = "抹灰", Ja = "左官", Ko = "미장" },
                new AecTerm { En = "brick", Vi = "Gạch", Zh = "砖", Ja = "レンガ", Ko = "벽돌" },
                new AecTerm { En = "steel", Vi = "Thép", Zh = "钢材", Ja = "鋼材", Ko = "강재" },
                new AecTerm { En = "timber", Vi = "Gỗ", Zh = "木材", Ja = "木材", Ko = "목재" },
                new AecTerm { En = "glass", Vi = "Kính", Zh = "玻璃", Ja = "ガラス", Ko = "유리" },
                new AecTerm { En = "aluminum", Vi = "Nhôm", Zh = "铝", Ja = "アルミ", Ko = "알루미늄" }
            };
        }

        private static List<AecPattern> BuildPatterns()
        {
            var patterns = new List<AecPattern>();

            // ---------- Việt → Anh ----------
            patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt bằng(?:\s+công(?:\s+năng)?)?(?:\s+tầng|\s+lầu)\s*([A-Za-z0-9]+)",
                Evaluator = m => $"{ToOrdinalNumber(m.Groups[1].Value)} Floor Functional Plan"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt bằng(?:\s+tầng|\s+lầu)\s*([A-Za-z0-9]+)",
                Evaluator = m => $"{ToOrdinalNumber(m.Groups[1].Value)} Floor Plan"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Chi tiết\s+(.+)",
                Replacement = "$1 Detail"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt cắt\s+(.+)",
                Replacement = "Section $1"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt đứng\s+(.+)",
                Replacement = "Elevation $1"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Trục\s+(.+)",
                Replacement = "Grid $1"
            });

            // ---------- Anh → Việt (tiêu đề bản vẽ phổ biến) ----------
            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = @"^Ground\s+Floor\s+Plan$",
                Replacement = "Mặt bằng tầng trệt"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = @"^(?:Roof|Top)\s+Floor\s+Plan$",
                Replacement = "Mặt bằng mái"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = @"^([A-Za-z]+)\s+Floor\s+Plan$",
                Evaluator = m => $"Mặt bằng tầng {ToVietnameseFloor(m.Groups[1].Value)}"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = @"^Section\s+([A-Za-z0-9\-]+)$",
                Replacement = "Mặt cắt $1"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = @"^Detail\s+(.+)$",
                Replacement = "Chi tiết $1"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = @"^Elevation\s+(.+)$",
                Replacement = "Mặt đứng $1"
            });

            patterns.Add(new AecPattern
            {
                SourceLang = "en", TargetLang = "vi",
                RegexStr = "^Grid\\s+(.+)$",
                Replacement = "Trục $1"
            });

            return patterns;
        }

        public static string Lookup(string input, string sl, string tl)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string keyword = StripTrailingPunctuation(CollapseWhitespace(input.Trim()));
            string keywordNormalized = Normalize(keyword);

            // Giai đoạn 1: Tra từ điển chính xác (không phân biệt hoa/thường,
            // dấu tiếng Việt và khoảng trắng thừa)
            foreach (AecTermIndex index in _termIndexes)
            {
                bool isMatch = sl == "auto"
                    ? MatchesAnyLanguage(index, keywordNormalized)
                    : MatchesLanguage(index, sl, keywordNormalized);

                if (isMatch)
                {
                    string targetVal = GetVal(index.Term, tl);
                    if (!string.IsNullOrEmpty(targetVal)) return targetVal;
                }
            }

            // Giai đoạn 2: Quét bằng Regex Patterns (Cấu trúc mẫu)
            foreach (AecPattern rule in _patterns)
            {
                if ((sl == "auto" || sl == rule.SourceLang) && tl == rule.TargetLang)
                {
                    if (!Regex.IsMatch(keyword, rule.RegexStr, RegexOptions.IgnoreCase))
                        continue;

                    return rule.Evaluator != null
                        ? Regex.Replace(keyword, rule.RegexStr, rule.Evaluator, RegexOptions.IgnoreCase)
                        : Regex.Replace(keyword, rule.RegexStr, rule.Replacement, RegexOptions.IgnoreCase);
                }
            }

            return null;
        }

        private static AecTermIndex BuildIndex(AecTerm term)
        {
            return new AecTermIndex
            {
                Term = term,
                EnN = Normalize(term.En),
                ViN = Normalize(term.Vi),
                ZhN = Normalize(term.Zh),
                JaN = Normalize(term.Ja),
                KoN = Normalize(term.Ko)
            };
        }

        private static bool MatchesAnyLanguage(AecTermIndex index, string keywordNormalized)
        {
            return EqualsN(index.EnN, keywordNormalized) ||
                EqualsN(index.ViN, keywordNormalized) ||
                EqualsN(index.ZhN, keywordNormalized) ||
                EqualsN(index.JaN, keywordNormalized) ||
                EqualsN(index.KoN, keywordNormalized);
        }

        private static bool MatchesLanguage(AecTermIndex index, string langCode, string keywordNormalized)
        {
            if (langCode == null) return false;
            if (langCode == "en") return EqualsN(index.EnN, keywordNormalized);
            if (langCode == "vi") return EqualsN(index.ViN, keywordNormalized);
            if (langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return EqualsN(index.ZhN, keywordNormalized);
            if (langCode == "ja") return EqualsN(index.JaN, keywordNormalized);
            if (langCode == "ko") return EqualsN(index.KoN, keywordNormalized);
            return false;
        }

        private static bool EqualsN(string normalizedValue, string keywordNormalized)
        {
            return !string.IsNullOrEmpty(normalizedValue) &&
                normalizedValue.Equals(keywordNormalized, StringComparison.Ordinal);
        }

        /// <summary>
        /// Chuẩn hóa để đối chiếu: bỏ dấu tiếng Việt (FormD + loại combining marks),
        /// chữ hoa invariant, thu gọn khoảng trắng. Cho phép khớp văn bản bản vẽ
        /// viết hoa hoặc mất dấu như "MAT CAT NGANG".
        /// </summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            string collapsed = CollapseWhitespace(value);
            string formD = collapsed.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(formD.Length);
            foreach (char character in formD)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }
            return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }

        private static string CollapseWhitespace(string value)
        {
            return Regex.Replace(value, @"\s+", " ");
        }

        private static string StripTrailingPunctuation(string value)
        {
            return value.TrimEnd(':', ';', '.', ',', '-', '–', '=');
        }

        private static string GetVal(AecTerm term, string langCode)
        {
            if (langCode == null) return null;
            if (langCode == "en") return term.En;
            if (langCode == "vi") return term.Vi;
            if (langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return term.Zh;
            if (langCode == "ja") return term.Ja;
            if (langCode == "ko") return term.Ko;
            return null;
        }

        private static string ToOrdinalNumber(string val)
        {
            if (int.TryParse(val, out int number))
            {
                int suffix = number % 100;
                if (suffix >= 11 && suffix <= 13) return number + "th";
                switch (number % 10)
                {
                    case 1: return number + "st";
                    case 2: return number + "nd";
                    case 3: return number + "rd";
                    default: return number + "th";
                }
            }

            if (val.Equals("trệt", StringComparison.OrdinalIgnoreCase) || val.Equals("tret", StringComparison.OrdinalIgnoreCase)) return "Ground";
            if (val.Equals("mái", StringComparison.OrdinalIgnoreCase) || val.Equals("mai", StringComparison.OrdinalIgnoreCase)) return "Roof";
            if (val.Equals("lửng", StringComparison.OrdinalIgnoreCase) || val.Equals("lung", StringComparison.OrdinalIgnoreCase)) return "Mezzanine";

            return val.ToUpperInvariant();
        }

        private static string ToVietnameseFloor(string val)
        {
            switch (val.ToLowerInvariant())
            {
                case "ground": return "trệt";
                case "roof": return "mái";
                case "mezzanine": return "lửng";
                case "basement": return "hầm";
                case "first": return "1";
                case "second": return "2";
                case "third": return "3";
                case "fourth": return "4";
                case "fifth": return "5";
                case "sixth": return "6";
                case "seventh": return "7";
                case "eighth": return "8";
                case "ninth": return "9";
                case "tenth": return "10";
                default: return val.ToUpperInvariant();
            }
        }
    }
}