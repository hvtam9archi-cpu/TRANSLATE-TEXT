using System;
using System.Collections.Generic;
using System.Linq;
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

    public class AecPattern
    {
        public string SourceLang { get; set; }
        public string TargetLang { get; set; }
        public string RegexStr { get; set; }
        public string Replacement { get; set; }
        public MatchEvaluator Evaluator { get; set; }
    }

    /// <summary>
    /// Từ điển chuyên ngành AEC (Kiến trúc - Kỹ thuật - Xây dựng)
    /// Tra cứu chính xác thuật ngữ trước khi gọi Google Translate.
    /// </summary>
    public static class AecGlossary
    {
        private static readonly List<AecTerm> _terms = new List<AecTerm>
        {
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
            new AecTerm { En = "beam", Vi = "Dầm", Zh = "梁", Ja = "梁", Ko = "보" },
            new AecTerm { En = "column", Vi = "Cột", Zh = "柱", Ja = "柱", Ko = "기둥" },
            new AecTerm { En = "slab", Vi = "Sàn", Zh = "板", Ja = "床", Ko = "슬래브" },
            new AecTerm { En = "wall", Vi = "Tường", Zh = "墙", Ja = "壁", Ko = "벽" },
            new AecTerm { En = "retaining wall", Vi = "Tường chắn", Zh = "挡土墙", Ja = "擁壁", Ko = "옹벽" },
            new AecTerm { En = "foundation", Vi = "Móng", Zh = "基础", Ja = "基礎", Ko = "기초" },
            new AecTerm { En = "pile", Vi = "Cọc", Zh = "桩", Ja = "杭", Ko = "파일" },
            new AecTerm { En = "roof", Vi = "Mái", Zh = "屋顶", Ja = "屋根", Ko = "지붕" },
            new AecTerm { En = "ceiling", Vi = "Trần", Zh = "天花板", Ja = "天井", Ko = "천장" },
            new AecTerm { En = "grid", Vi = "Lưới trục", Zh = "轴网", Ja = "通り芯", Ko = "그리드" },
            new AecTerm { En = "dimension", Vi = "Kích thước", Zh = "尺寸", Ja = "寸法", Ko = "치수" },
            new AecTerm { En = "door", Vi = "Cửa đi", Zh = "门", Ja = "ドア", Ko = "문" },
            new AecTerm { En = "window", Vi = "Cửa sổ", Zh = "窗", Ja = "窓", Ko = "창문" },
            new AecTerm { En = "basement", Vi = "Tầng hầm", Zh = "地下室", Ja = "地下室", Ko = "지하실" },
            new AecTerm { En = "level", Vi = "Cao độ", Zh = "标高", Ja = "レベル", Ko = "레벨" },
            new AecTerm { En = "materials", Vi = "Vật liệu", Zh = "材料", Ja = "材料", Ko = "재료" },
            new AecTerm { En = "specification", Vi = "Chỉ dẫn kỹ thuật", Zh = "规范", Ja = "仕様書", Ko = "시방서" }
        };

        private static readonly List<AecPattern> _patterns = new List<AecPattern>();

        static AecGlossary()
        {
            // Regex Pattern tiếng Việt → Tiếng Anh
            _patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt bằng(?:\s+công(?:\s+năng)?)?(?:\s+tầng|\s+lầu)\s*([A-Za-z0-9]+)",
                Evaluator = m => $"{ToOrdinalNumber(m.Groups[1].Value)} Floor Functional Plan"
            });

            _patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt bằng(?:\s+tầng|\s+lầu)\s*([A-Za-z0-9]+)",
                Evaluator = m => $"{ToOrdinalNumber(m.Groups[1].Value)} Floor Plan"
            });

            _patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Chi tiết\s+(.+)",
                Replacement = "$1 Detail"
            });

            _patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Mặt cắt\s+(.+)",
                Replacement = "Section $1"
            });

            _patterns.Add(new AecPattern
            {
                SourceLang = "vi", TargetLang = "en",
                RegexStr = @"Trục\s+(.+)",
                Replacement = "Grid $1"
            });
        }

        public static string Lookup(string input, string sl, string tl)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string keyword = input.Trim();

            // Giai đoạn 1: Tra từ điển chính xác
            foreach (var term in _terms)
            {
                bool isMatch = false;

                if (sl == "auto")
                {
                    if (IsEqual(term.En, keyword) || IsEqual(term.Vi, keyword) ||
                        IsEqual(term.Zh, keyword) || IsEqual(term.Ja, keyword) || IsEqual(term.Ko, keyword))
                    {
                        isMatch = true;
                    }
                }
                else
                {
                    isMatch = IsEqual(GetVal(term, sl), keyword);
                }

                if (isMatch)
                {
                    string targetVal = GetVal(term, tl);
                    if (!string.IsNullOrEmpty(targetVal)) return targetVal;
                }
            }

            // Giai đoạn 2: Quét bằng Regex Patterns (Cấu trúc mẫu)
            foreach (var rule in _patterns)
            {
                if ((sl == "auto" || sl == rule.SourceLang) && tl == rule.TargetLang)
                {
                    if (Regex.IsMatch(keyword, rule.RegexStr, RegexOptions.IgnoreCase))
                    {
                        if (rule.Evaluator != null)
                        {
                            return Regex.Replace(keyword, rule.RegexStr, rule.Evaluator, RegexOptions.IgnoreCase);
                        }
                        else
                        {
                            return Regex.Replace(keyword, rule.RegexStr, rule.Replacement, RegexOptions.IgnoreCase);
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsEqual(string dictVal, string input)
        {
            if (string.IsNullOrEmpty(dictVal)) return false;
            return dictVal.Equals(input, StringComparison.OrdinalIgnoreCase);
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

            return val.ToUpper();
        }
    }
}
