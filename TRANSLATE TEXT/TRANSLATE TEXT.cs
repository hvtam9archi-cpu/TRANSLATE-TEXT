using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WinForms = System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System.Drawing;
// Yêu cầu tham chiếu System.Drawing

// Đăng ký lệnh cho AutoCAD
[assembly: CommandClass(typeof(HoangTam.AutoCAD.Tools.TranslateCommands))]
[assembly: CommandClass(typeof(HoangTam.AutoCAD.Tools.StyleCommands))]

namespace HoangTam.AutoCAD.Tools
{
	// ========================================================================================
	// CÁC CLASS HỖ TRỢ DỮ LIỆU
	// ========================================================================================

	public class TextDataObj
	{
		public ObjectId ObjId { get; set; }
		public string OriginalText { get; set; }
		public string TranslatedText { get; set; }
	}

	public class MaskResult
	{
		public string MaskedText { get; set; }
		public List<string> Codes { get; set; } = new List<string>();
	}

	public static class StealthWebClient
	{
		private static readonly Random _rnd = new Random();
		private static readonly string[] _userAgents = new string[]
		{
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0"
		};
		public static string GetRandomUserAgent() => _userAgents[_rnd.Next(_userAgents.Length)];
		public static async Task RandomSleep() => await Task.Delay(_rnd.Next(300, 1000));
	}

	public static class FormatProtector
	{
		// Regex bảo vệ các mã đặc biệt của AutoCAD (MText codes, Unicode escapes, Standard codes)
		private static readonly Regex _regexCodes = new Regex(
			@"(\\U\+[0-9a-fA-F]{4})|" +            // Unicode Escapes
			@"(\\[ACFHQTWacfhqtw][^;]*;)|" +       // MText Formatting (Font, Color...)
			@"(\\S[^;]*;)|" +                      // Stacking (Phân số)
			@"(%%[cdpCDPuoUO])|" +                 // Standard Codes (%%c, %%d...)
			@"(%%[0-9]{3})|" +                     // ASCII codes
			@"(\\P)|" +                            // Xuống dòng
			@"(\\[LloOkK])|" +                     // Gạch chân/ngang
			@"(\\[\\{}])|" +                       // Ký tự escape
			@"({|})",                              // Ngoặc nhọn
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static MaskResult MaskText(string input)
		{
			var result = new MaskResult();
			if (string.IsNullOrEmpty(input)) { result.MaskedText = input; return result; }
			int index = 0;
			result.MaskedText = _regexCodes.Replace(input, m =>
			{
				result.Codes.Add(m.Value);
				return $" [ID:{index++}] ";
			});
			return result;
		}

		public static string UnmaskText(string translated, List<string> codes)
		{
			if (string.IsNullOrEmpty(translated) || codes == null || codes.Count == 0) return translated?.Trim();
			// Khôi phục mã từ placeholder
			for (int i = 0; i < codes.Count; i++)
			{
				translated = Regex.Replace(translated, $@"\[\s*ID\s*:\s*{i}\s*\]", match => codes[i], RegexOptions.IgnoreCase);
			}

			// Xử lý khoảng trắng thừa do quá trình dịch sinh ra quanh các mã
			translated = Regex.Replace(translated, @"\s*(\\P)\s*", @"\P", RegexOptions.None);
			translated = Regex.Replace(translated, @"(\\[A-Za-z0-9]+[^;]*;)\s+", @"$1", RegexOptions.IgnoreCase);
			translated = Regex.Replace(translated, @"(\\[LloOkK])\s+", @"$1", RegexOptions.IgnoreCase);
			translated = Regex.Replace(translated, @"(%%[cdpCDPuoUO])\s+", @"$1", RegexOptions.IgnoreCase);
			string[] lines = Regex.Split(translated, @"\\P", RegexOptions.None);
			for (int j = 0; j < lines.Length; j++) lines[j] = lines[j].Trim();
			return string.Join("\\P", lines);
		}
	}

	public class LanguageItem
	{
		public string Code { get; set; }
		public string Name { get; set; }
		public override string ToString() => Name;
	}

	public static class LanguageList
	{
		// Danh sách đầy đủ các ngôn ngữ hỗ trợ
		public static List<LanguageItem> GetSupportedLanguages()
		{
			return new List<LanguageItem>
			{
				new LanguageItem { Code = "auto", Name = "Auto Detect" },
				new LanguageItem { Code = "vi", Name = "Vietnamese (Tiếng Việt)" },
				new LanguageItem { Code = "en", Name = "English" },
				new LanguageItem { Code = "ko", Name = "Korean (Hàn Quốc)" },
				new LanguageItem { Code = "ja", Name = "Japanese (Nhật Bản)" },
				new LanguageItem { Code = "zh-CN", Name = "Chinese Simplified (Trung Giản thể)" },
				new LanguageItem { Code = "zh-TW", Name = "Chinese Traditional (Trung Phồn thể)" },
				new LanguageItem { Code = "fr", Name = "French (Pháp)" },
				new LanguageItem { Code = "de", Name = "German (Đức)" },
				new LanguageItem { Code = "ru", Name = "Russian (Nga)" },
				new LanguageItem { Code = "es", Name = "Spanish (Tây Ban Nha)" },
				new LanguageItem { Code = "th", Name = "Thai (Thái Lan)" },
				new LanguageItem { Code = "lo", Name = "Lao (Lào)" },
				new LanguageItem { Code = "km", Name = "Khmer (Campuchia)" },
				new LanguageItem { Code = "id", Name = "Indonesian (Indonesia)" },
				new LanguageItem { Code = "ms", Name = "Malay (Malaysia)" },
				new LanguageItem { Code = "it", Name = "Italian (Ý)" },
				new LanguageItem { Code = "pt", Name = "Portuguese (Bồ Đào Nha)" },
				new LanguageItem { Code = "hi", Name = "Hindi (Ấn Độ)" },
				new LanguageItem { Code = "ar", Name = "Arabic (Ả Rập)" }
			};
		}
	}

	// ========================================================================================
	// PHẦN 1: TRANSLATE TEXT (CÔNG CỤ DỊCH THUẬT - OPTIMIZED BLOCKS)
	// ========================================================================================

	public class TranslateCommands
	{
		private static string _lastSourceLang = "auto";
		private static string _lastTargetLang = "vi";
		private static string _lastTextStyle = "Keep Original";

		[CommandMethod("TRANSLATETEXT", CommandFlags.UsePickSet)]
		public async void TranslateTextCmd()
		{
			ServicePointManager.DefaultConnectionLimit = 50;
			ServicePointManager.Expect100Continue = false;
			Document doc = AcApp.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;
			// 0. Get Styles
			List<string> styleNames = new List<string> { "Keep Original" };
			using (Transaction tr = doc.TransactionManager.StartTransaction())
			{
				TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
				foreach (ObjectId id in tst)
				{
					TextStyleTableRecord tsr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
					styleNames.Add(tsr.Name);
				}
				tr.Commit();
			}

			// 1. Show Form
			string selectedStyleName;
			using (var form = new LanguageSelectionForm(_lastSourceLang, _lastTargetLang, styleNames, _lastTextStyle))
			{
				if (AcApp.ShowModalDialog(form) != WinForms.DialogResult.OK) return;
				_lastSourceLang = form.SelectedSourceCode;
				_lastTargetLang = form.SelectedTargetCode;
				_lastTextStyle = form.SelectedTextStyle;
				selectedStyleName = form.SelectedTextStyle;
			}

			// 2. Select Objects
			var filter = new SelectionFilter(new TypedValue[] {
				new TypedValue((int)DxfCode.Operator, "<OR"),
				new TypedValue((int)DxfCode.Start, "TEXT"),
				new TypedValue((int)DxfCode.Start, "MTEXT"),
				new TypedValue((int)DxfCode.Start, "MULTILEADER"),
				new TypedValue((int)DxfCode.Start, "INSERT"),
				new TypedValue((int)DxfCode.Operator, "OR>")
			});
			PromptSelectionResult selRes = ed.GetSelection(filter);
			if (selRes.Status != PromptStatus.OK) return;

			List<TextDataObj> dataList = new List<TextDataObj>();
			HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();
			// Để tránh xử lý lặp lại Definition

			// 3. Read Data (Smart Block Processing)
			using (Transaction tr = doc.TransactionManager.StartTransaction())
			{
				foreach (ObjectId objId in selRes.Value.GetObjectIds())
				{
					Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
					if (ent == null) continue;

					if (ent is DBText dbText)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = dbText.TextString });
					else if (ent is MText mText)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = mText.Contents });
					else if (ent is MLeader mLeader && mLeader.ContentType == Autodesk.AutoCAD.DatabaseServices.ContentType.MTextContent)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = mLeader.MText.Contents });
					else if (ent is BlockReference blkRef)
					{
						// A. Xử lý Attribute (Luôn phải làm từng cái vì giá trị khác nhau)
						foreach (ObjectId attId in blkRef.AttributeCollection)
						{
							AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
							if (attRef != null && !attRef.IsConstant)
								dataList.Add(new TextDataObj { ObjId = attId, OriginalText = attRef.TextString });
						}

						// B. Xử lý Block Definition (Chỉ làm 1 lần cho mỗi loại Block để tối ưu)
						ObjectId btrId = blkRef.BlockTableRecord;
						if (!processedBlockDefs.Contains(btrId))
						{
							processedBlockDefs.Add(btrId);
							// Đánh dấu đã xử lý loại block này
							BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
							// Quét tất cả đối tượng tĩnh bên trong Block Definition
							foreach (ObjectId subId in btr)
							{
								Entity subEnt = tr.GetObject(subId, OpenMode.ForRead) as Entity;
								if (subEnt is DBText subTxt)
									dataList.Add(new TextDataObj { ObjId = subId, OriginalText = subTxt.TextString });
								else if (subEnt is MText subMtext)
									dataList.Add(new TextDataObj { ObjId = subId, OriginalText = subMtext.Contents });
							}
						}
					}
				}
				tr.Commit();
			}

			ed.WriteMessage($"\nProcessing {dataList.Count} objects (Optimized Blocks & Languages)...");
			// 4. Translate Process (Async)
			try
			{
				using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
				using (SemaphoreSlim semaphore = new SemaphoreSlim(8))
				{
					var tasks = dataList.Select(async item =>
					{
						await semaphore.WaitAsync();
						try
						{
							MaskResult maskResult = FormatProtector.MaskText(item.OriginalText);
							if (IsAllTags(maskResult.MaskedText)) item.TranslatedText = item.OriginalText;
							else
							{
								string trans = await SafeGoogleTranslateApi(client, maskResult.MaskedText, _lastSourceLang, _lastTargetLang);
								item.TranslatedText = FormatProtector.UnmaskText(trans, maskResult.Codes);
							}
						}
						catch { }
						finally { semaphore.Release(); }
					});
					await Task.WhenAll(tasks);
				}
			}
			catch (System.Exception ex) { ed.WriteMessage($"\nError: {ex.Message}"); return; }

			// 5. Write Data
			try
			{
				using (DocumentLock docLock = doc.LockDocument())
				using (Transaction tr = doc.TransactionManager.StartTransaction())
				{
					ObjectId targetStyleId = ObjectId.Null;
					if (selectedStyleName != "Keep Original")
					{
						TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
						if (tst.Has(selectedStyleName)) targetStyleId = tst[selectedStyleName];
					}

					int count = 0;
					foreach (var item in dataList)
					{
						if (string.IsNullOrEmpty(item.TranslatedText) || item.OriginalText == item.TranslatedText) continue;
						Entity ent = tr.GetObject(item.ObjId, OpenMode.ForWrite) as Entity;
						if (ent == null) continue;
						if (ent is DBText dbText)
						{
							dbText.TextString = item.TranslatedText;
							if (targetStyleId != ObjectId.Null) dbText.TextStyleId = targetStyleId;
						}
						else if (ent is MText mText)
						{
							mText.Contents = item.TranslatedText;
							if (targetStyleId != ObjectId.Null) mText.TextStyleId = targetStyleId;
						}
						else if (ent is MLeader mLeader)
						{
							MText mt = mLeader.MText;
							mt.Contents = item.TranslatedText;
							if (targetStyleId != ObjectId.Null) mt.TextStyleId = targetStyleId;
							mLeader.MText = mt;
						}
						else if (ent is AttributeReference attRef)
						{
							attRef.TextString = item.TranslatedText;
							if (targetStyleId != ObjectId.Null) attRef.TextStyleId = targetStyleId;
						}
						// Support cập nhật đối tượng gốc trong Block Definition
						else if (ent is AttributeDefinition attDef)
						{
							attDef.TextString = item.TranslatedText;
							if (targetStyleId != ObjectId.Null) attDef.TextStyleId = targetStyleId;
						}

						count++;
					}
					tr.Commit();
					ed.WriteMessage($"\nDone! Translated {count} items.");
				}
				ed.Regen();
				// Quan trọng để cập nhật hiển thị Block
			}
			catch (System.Exception ex)
			{
				ed.WriteMessage($"\nWrite Error: {ex.Message}");
			}
		}

		private bool IsAllTags(string text)
		{
			string clean = Regex.Replace(text, @"\[ID:\d+\]", "");
			return string.IsNullOrWhiteSpace(clean) || Regex.IsMatch(clean, @"^[\d\W]+$");
		}

		private async Task<string> SafeGoogleTranslateApi(HttpClient client, string input, string sl, string tl)
		{
			if (string.IsNullOrWhiteSpace(input)) return input;
			int retryDelay = 2000;
			await StealthWebClient.RandomSleep();

			for (int i = 0; i < 5; i++)
			{
				try
				{
					var request = new HttpRequestMessage(HttpMethod.Get, $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={System.Web.HttpUtility.UrlEncode(input)}");
					request.Headers.Add("User-Agent", StealthWebClient.GetRandomUserAgent());

					HttpResponseMessage response = await client.SendAsync(request);
					if (response.IsSuccessStatusCode) return ParseResultStrict(await response.Content.ReadAsStringAsync(), input);

					if ((int)response.StatusCode == 429) { await Task.Delay(retryDelay); retryDelay *= 2; } else break;
				}
				catch { await Task.Delay(retryDelay); }
			}
			return input;
		}

		private string ParseResultStrict(string json, string original)
		{
			try
			{
				StringBuilder sb = new StringBuilder();
				int depth = 0; bool inString = false; bool isTransSegment = false;
				for (int i = 0; i < json.Length; i++)
				{
					char c = json[i];
					if (inString)
					{
						if (c == '\\' && i + 1 < json.Length) { i++; continue; }
						if (c == '"')
						{
							inString = false; if (depth == 3 && isTransSegment)
							{
								int end = i, start = i - 1; while (start >= 0) { if (json[start] == '"' && (start == 0 || json[start - 1] != '\\')) break; start--; }
								if (start >= 0) { sb.Append(Regex.Unescape(json.Substring(start + 1, end - start - 1))); isTransSegment = false; }
							}
						}
						continue;
					}
					if (c == '[') { depth++; if (depth == 3) isTransSegment = true; } else if (c == ']') { if (depth == 2) return sb.ToString(); depth--; } else if (c == '"') inString = true;
				}
				return string.IsNullOrWhiteSpace(sb.ToString()) ? original : sb.ToString();
			}
			catch { return original; }
		}
	}

	public class LanguageSelectionForm : WinForms.Form
	{
		public string SelectedSourceCode { get; private set; }
		public string SelectedTargetCode { get; private set; }
		public string SelectedTextStyle { get; private set; }

		private readonly WinForms.ComboBox cbSource, cbTarget, cbStyle;

		public LanguageSelectionForm(string defaultSource, string defaultTarget, List<string> styleList, string defaultStyle)
		{
			this.Text = "Translate Tool (Pro)";
			this.Size = new Size(400, 260);
			this.StartPosition = WinForms.FormStartPosition.CenterScreen;
			this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			int pad = 20, lblW = 100, cbW = 230, top = 20;
			this.Controls.Add(new WinForms.Label { Text = "Source Lang:", Left = pad, Top = top, Width = lblW });
			cbSource = new WinForms.ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (var lang in LanguageList.GetSupportedLanguages()) cbSource.Items.Add(lang);
			SetComboValue(cbSource, defaultSource);
			this.Controls.Add(cbSource);

			top += 40;
			this.Controls.Add(new WinForms.Label { Text = "Target Lang:", Left = pad, Top = top, Width = lblW });
			cbTarget = new WinForms.ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (var lang in LanguageList.GetSupportedLanguages()) { if (lang.Code != "auto") cbTarget.Items.Add(lang); }
			SetComboValue(cbTarget, defaultTarget);
			this.Controls.Add(cbTarget);

			top += 40;
			this.Controls.Add(new WinForms.Label { Text = "Text Style:", Left = pad, Top = top, Width = lblW });
			cbStyle = new WinForms.ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (string s in styleList) cbStyle.Items.Add(s);
			if (cbStyle.Items.Contains(defaultStyle)) cbStyle.SelectedItem = defaultStyle; else cbStyle.SelectedIndex = 0;
			this.Controls.Add(cbStyle);

			top += 50;
			WinForms.Button btnOk = new WinForms.Button { Text = "Translate", Left = 130, Top = top, DialogResult = WinForms.DialogResult.OK, Width = 100 };
			btnOk.Click += (s, e) => {
				SelectedSourceCode = ((LanguageItem)cbSource.SelectedItem).Code;
				SelectedTargetCode = ((LanguageItem)cbTarget.SelectedItem).Code;
				SelectedTextStyle = cbStyle.SelectedItem.ToString();
			};
			this.Controls.Add(btnOk);

			WinForms.Button btnCancel = new WinForms.Button { Text = "Cancel", Left = 240, Top = top, DialogResult = WinForms.DialogResult.Cancel, Width = 100 };
			this.Controls.Add(btnCancel);
		}
		private void SetComboValue(WinForms.ComboBox cb, string code)
		{
			foreach (LanguageItem item in cb.Items) if (item.Code == code) { cb.SelectedItem = item; return; }
			if (cb.Items.Count > 0) cb.SelectedIndex = 0;
		}
	}

	// ========================================================================================
	// PHẦN 2: CHANGE TEXT STYLE (CHUYỂN MÃ FONT - OPTIMIZED BLOCKS & SMART DETECT)
	// ========================================================================================

	public enum EncodingType { Auto, Unicode, VNI, TCVN3 }

	public static class VnCharset
	{
		private static readonly Dictionary<string, string> VniToUni = new Dictionary<string, string>();
		private static readonly Dictionary<string, string> UniToVni = new Dictionary<string, string>();
		private static readonly Dictionary<char, char> TcvnToUni = new Dictionary<char, char>();
		private static readonly Dictionary<char, char> UniToTcvn = new Dictionary<char, char>();
		private static readonly HashSet<char> UnicodeVietnameseChars = new HashSet<char>();
		static VnCharset() { InitializeMaps(); }

		private static void InitializeMaps()
		{
			// Init Unicode Chars (Khôi phục đầy đủ)
			string[] uni = {
				"á", "à", "ả", "ã", "ạ", "ă", "ắ", "ằ", "ẳ", "ẵ", "ặ", "â", "ấ", "ầ", "ẩ", "ẫ", "ậ",
				"é", "è", "ẻ", "ẽ", "ẹ", "ê", "ế", "ề", "ể", "ễ", "ệ",
				"í", "ì", "ỉ", "ĩ", "ị",
				"ó", "ò", "ỏ", "õ", "ọ", "ô", "ố", "ồ", "ổ", "ỗ", "ộ", "ơ", "ớ", "ờ", "ở", "ỡ", "ợ",
				"ú", "ù", "ủ", "ũ", "ụ", "ư", "ứ", "ừ", "ử", "ữ", "ự",
				"ý", "ỳ", "ỷ", "ỹ", "ỵ", "đ",
				"Á", "À", "Ả", "Ã", "Ạ", "Ă", "Ắ", "Ằ", "Ẳ", "Ẵ", "Ặ", "Â", "Ấ", "Ầ", "Ẩ", "Ẫ", "Ậ",
				"É", "È", "Ẻ", "Ẽ", "Ẹ", "Ê", "Ế", "Ề", "Ể", "Ễ", "Ệ",
				"Í", "Ì", "Ỉ", "Ĩ", "Ị",
				"Ó", "Ò", "Ỏ", "Õ", "Ọ", "Ô", "Ố", "Ồ", "Ổ", "Ỗ", "Ộ", "Ơ", "Ớ", "Ờ", "Ở", "Ỡ", "Ợ",
				"Ú", "Ù", "Ủ", "Ũ", "Ụ", "Ư", "Ứ", "Ừ", "Ử", "Ữ", "Ự",
				"Ý", "Ỳ", "Ỷ", "Ỹ", "Ỵ", "Đ"
			};
			// Init VNI Hex (Khôi phục đầy đủ)
			string[] vni = {
				"a\u00D9", "a\u00D8", "a\u00DB", "a\u00D5", "a\u00CF",
				"\u00E6", "\u00E6\u00D9", "\u00E6\u00D8", "\u00E6\u00DB", "\u00E6\u00D5", "\u00E6\u00CF",
				"\u00E2", "\u00E2\u00D9", "\u00E2\u00D8", "\u00E2\u00DB", "\u00E2\u00D5", "\u00E2\u00CF",
				"e\u00D9", "e\u00D8", "e\u00DB", "e\u00D5", "e\u00CF",
				"\u00EA", "\u00EA\u00D9", "\u00EA\u00D8", "\u00EA\u00DB", "\u00EA\u00D5", "\u00EA\u00CF",
				"i\u00D9", "i\u00D8", "i\u00DB", "i\u00D5", "i\u00CF",
				"o\u00D9", "o\u00D8", "o\u00DB", "o\u00D5", "o\u00CF",
				"\u00F4", "\u00F4\u00D9", "\u00F4\u00D8", "\u00F4\u00DB", "\u00F4\u00D5", "\u00F4\u00CF",
				"\u00F6", "\u00F6\u00D9", "\u00F6\u00D8", "\u00F6\u00DB", "\u00F6\u00D5", "\u00F6\u00CF",
				"u\u00D9", "u\u00D8", "u\u00DB", "u\u00D5", "u\u00CF",
				"\u00F9", "\u00F9\u00D9", "\u00F9\u00D8", "\u00F9\u00DB", "\u00F9\u00D5", "\u00F9\u00CF",
				"y\u00D9", "y\u00D8", "y\u00DB", "y\u00D5", "\u00EE", "d\u00F1",
				"A\u00D9", "A\u00D8", "A\u00DB", "A\u00D5", "A\u00CF",
				"\u00A1", "\u00A1\u00D9", "\u00A1\u00D8", "\u00A1\u00DB", "\u00A1\u00D5", "\u00A1\u00CF",
				"\u00A2", "\u00A2\u00D9", "\u00A2\u00D8", "\u00A2\u00DB", "\u00A2\u00D5", "\u00A2\u00CF",
				"E\u00D9", "E\u00D8", "E\u00DB", "E\u00D5", "E\u00CF",
				"\u00A3", "\u00A3\u00D9", "\u00A3\u00D8", "\u00A3\u00DB", "\u00A3\u00D5", "\u00A3\u00CF",
				"I\u00D9", "I\u00D8", "I\u00DB", "I\u00D5", "I\u00CF",
				"O\u00D9", "O\u00D8", "O\u00DB", "O\u00D5", "O\u00CF",
				"\u00A4", "\u00A4\u00D9", "\u00A4\u00D8", "\u00A4\u00DB", "\u00A4\u00D5", "\u00A4\u00CF",
				"\u00A5", "\u00A5\u00D9", "\u00A5\u00D8", "\u00A5\u00DB", "\u00A5\u00D5", "\u00A5\u00CF",
				"U\u00D9", "U\u00D8", "U\u00DB", "U\u00D5", "U\u00CF",
				"\u00A6", "\u00A6\u00D9", "\u00A6\u00D8", "\u00A6\u00DB", "\u00A6\u00D5", "\u00A6\u00CF",
				"Y\u00D9", "Y\u00D8", "Y\u00DB", "Y\u00D5", "\u00A7", "D\u00D1"
			};
			// Init TCVN3 (ABC) Full (Khôi phục đầy đủ)
			char[] tcvn = {
				'\u00B8', '\u00B5', '\u00B6', '\u00B7', '\u00B9',
				'\u00A8', '\u00BE', '\u00BB', '\u00BC', '\u00BD', '\u00C6',
				'\u00A9', '\u00CA', '\u00C7', '\u00C8', '\u00C9', '\u00CB',
				'\u00D0', '\u00CC', '\u00CE', '\u00CF', '\u00D1',
				'\u00AA', '\u00D5', '\u00D2', '\u00D3', '\u00D4', '\u00D6',
				'\u00DD', '\u00D7', '\u00D8', '\u00DC', '\u00DE',
				'\u00E3', '\u00DF', '\u00E1', '\u00E2', '\u00E4',
				'\u00AB', '\u00E8', '\u00E5', '\u00E6', '\u00E7', '\u00E9',
				'\u00AC', '\u00ED', '\u00EA', '\u00EB', '\u00EC', '\u00EE',
				'\u00F3', '\u00EF', '\u00F1', '\u00F2', '\u00F4',
				'\u00AD', '\u00F8', '\u00F5', '\u00F6', '\u00F7', '\u00F9',
				'\u00FD', '\u00FA', '\u00FB', '\u00FC', '\u00FE', '\u00AE',
				'\u00B8', '\u00B5', '\u00B6', '\u00B7', '\u00B9',
				'\u00A1', '\u00BE', '\u00BB', '\u00BC', '\u00BD', '\u00C6',
				'\u00A2', '\u00CA', '\u00C7', '\u00C8', '\u00C9', '\u00CB',
				'\u00D0', '\u00CC', '\u00CE', '\u00CF', '\u00D1',
				'\u00A3', '\u00D5', '\u00D2', '\u00D3', '\u00D4', '\u00D6',
				'\u00DD', '\u00D7', '\u00D8', '\u00DC', '\u00DE',
				'\u00E3', '\u00DF', '\u00E1', '\u00E2', '\u00E4',
				'\u00A4', '\u00E8', '\u00E5', '\u00E6', '\u00E7', '\u00E9',
				'\u00A5', '\u00ED', '\u00EA', '\u00EB', '\u00EC', '\u00EE',
				'\u00F3', '\u00EF', '\u00F1', '\u00F2', '\u00F4',
				'\u00A6', '\u00F8', '\u00F5', '\u00F6', '\u00F7', '\u00F9',
				'\u00FD', '\u00FA', '\u00FB', '\u00FC', '\u00FE', '\u00A7'
			};
			for (int i = 0; i < uni.Length && i < vni.Length; i++)
			{
				if (!VniToUni.ContainsKey(vni[i])) VniToUni.Add(vni[i], uni[i]);
				if (!UniToVni.ContainsKey(uni[i])) UniToVni.Add(uni[i], vni[i]);
				foreach (char c in uni[i]) UnicodeVietnameseChars.Add(c);
			}

			for (int i = 0; i < uni.Length && i < tcvn.Length; i++)
			{
				char u = uni[i][0], t = tcvn[i];
				if (!TcvnToUni.ContainsKey(t)) TcvnToUni.Add(t, u);
				if (!UniToTcvn.ContainsKey(u)) UniToTcvn.Add(u, t);
				UnicodeVietnameseChars.Add(u);
			}
		}

		public static string Convert(string input, EncodingType source, EncodingType target)
		{
			if (string.IsNullOrEmpty(input) || source == target) return input;
			string unicodeTemp = input;

			if (source == EncodingType.Auto) source = DetectEncoding(input);

			if (source == EncodingType.TCVN3) unicodeTemp = TcvnToUnicode(input);
			else if (source == EncodingType.VNI) unicodeTemp = VniToUnicode(input);

			if (target == EncodingType.Unicode) return unicodeTemp;
			if (target == EncodingType.TCVN3) return UnicodeToTcvn(unicodeTemp);
			if (target == EncodingType.VNI) return UnicodeToVni(unicodeTemp);

			return unicodeTemp;
		}

		private static string VniToUnicode(string input)
		{
			var keys = VniToUni.Keys.OrderByDescending(k => k.Length);
			StringBuilder sb = new StringBuilder(input);
			foreach (var k in keys) sb.Replace(k, VniToUni[k]);
			return sb.ToString();
		}
		private static string TcvnToUnicode(string input)
		{
			StringBuilder sb = new StringBuilder();
			foreach (char c in input) sb.Append(TcvnToUni.ContainsKey(c) ? TcvnToUni[c] : c);
			return sb.ToString();
		}
		private static string UnicodeToVni(string input)
		{
			StringBuilder sb = new StringBuilder();
			foreach (char c in input) sb.Append(UniToVni.ContainsKey(c.ToString()) ? UniToVni[c.ToString()] : c.ToString());
			return sb.ToString();
		}
		private static string UnicodeToTcvn(string input)
		{
			StringBuilder sb = new StringBuilder();
			foreach (char c in input) sb.Append(UniToTcvn.ContainsKey(c) ? UniToTcvn[c] : c);
			return sb.ToString();
		}

		// Smart Encoding Detection: Chấm điểm dựa trên toàn bộ nội dung
		public static EncodingType DetectEncoding(string text)
		{
			if (string.IsNullOrEmpty(text)) return EncodingType.Unicode;
			// 1. Clean MText Formatting Codes
			string clean = Regex.Replace(text, @"\\[ACFHQTWacfhqtw][^;]*;", "");
			clean = Regex.Replace(clean, @"\\P", "");
			clean = Regex.Replace(clean, @"[{}]", "");
			clean = Regex.Replace(clean, @"\\[LloOkK]", "");
			int sTCVN = 0, sVNI = 0, sUni = 0;

			foreach (char c in clean) if (TcvnToUni.ContainsKey(c)) sTCVN++;
			foreach (string k in VniToUni.Keys)
			{
				if (k.Length < 2) continue;
				sVNI += (clean.Length - clean.Replace(k, "").Length) / k.Length;
			}

			foreach (char c in clean) if (UnicodeVietnameseChars.Contains(c)) sUni++;
			if (sVNI > sTCVN && sVNI >= sUni) return EncodingType.VNI;

			// [FIXED LOGIC]: Ưu tiên Unicode nếu điểm bằng TCVN (vì TCVN dùng trùng ký tự Unicode chuẩn)
			if (sTCVN > sVNI && sTCVN > sUni) return EncodingType.TCVN3;

			return EncodingType.Unicode;
		}
	}

	public static class AppSettings
	{
		private const string REG_PATH = @"Software\HoangTamAutoCADTools";
		public static void Save(string style, int tEnc, int sEnc)
		{
			try
			{
				using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_PATH))
				{
					key.SetValue("TargetStyle", style);
					key.SetValue("TargetEncodingIndex", tEnc); key.SetValue("SourceEncodingIndex", sEnc);
				}
			}
			catch { }
		}
		public static void Load(out string style, out int tEnc, out int sEnc)
		{
			style = "";
			tEnc = 0; sEnc = 0;
			try
			{
				using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_PATH))
				{
					if (key != null)
					{
						style = key.GetValue("TargetStyle", "").ToString();
						tEnc = System.Convert.ToInt32(key.GetValue("TargetEncodingIndex", 0)); sEnc = System.Convert.ToInt32(key.GetValue("SourceEncodingIndex", 0));
					}
				}
			}
			catch { }
		}
	}

	public class TextStyleForm : WinForms.Form
	{
		public string TargetStyle { get; private set; }
		public EncodingType TargetEncoding { get; private set; }
		public EncodingType SourceEncoding { get; private set; }

		public int SelectedTargetIndex => cbTargetEncoding.SelectedIndex;
		public int SelectedSourceIndex => cbSourceEncoding.SelectedIndex;

		private WinForms.ComboBox cbTargetStyle, cbTargetEncoding, cbSourceEncoding;
		public TextStyleForm(List<string> styleNames, string savedStyle, int savedTgtIdx, int savedSrcIdx)
		{
			this.Text = "Change Text Style (Combined)";
			this.Size = new System.Drawing.Size(380, 260);
			this.StartPosition = WinForms.FormStartPosition.CenterScreen;
			this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			int lblW = 110, cbW = 200, left = 130, gap = 40, top = 20;
			this.Controls.Add(new WinForms.Label { Text = "Target Style:", Left = 20, Top = top, Width = lblW });
			cbTargetStyle = new WinForms.ComboBox { Left = left, Top = top - 2, Width = cbW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (var s in styleNames) cbTargetStyle.Items.Add(s);
			if (cbTargetStyle.Items.Count > 0)
			{
				int idx = -1; if (!string.IsNullOrEmpty(savedStyle)) idx = cbTargetStyle.FindStringExact(savedStyle);
				cbTargetStyle.SelectedIndex = idx != -1 ? idx : 0;
			}
			this.Controls.Add(cbTargetStyle);
			top += gap;
			this.Controls.Add(new WinForms.Label { Text = "Target Encoding:", Left = 20, Top = top, Width = lblW });
			cbTargetEncoding = new WinForms.ComboBox { Left = left, Top = top - 2, Width = cbW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			// [FIXED ORDER]: Match EncodingType logic
			cbTargetEncoding.Items.AddRange(new object[] { "Unicode (Default)", "VNI Windows", "TCVN3 (ABC)" });
			cbTargetEncoding.SelectedIndex = (savedTgtIdx >= 0 && savedTgtIdx < cbTargetEncoding.Items.Count) ? savedTgtIdx : 0;
			this.Controls.Add(cbTargetEncoding);

			top += gap;
			this.Controls.Add(new WinForms.Label { Text = "Source Encoding:", Left = 20, Top = top, Width = lblW });
			cbSourceEncoding = new WinForms.ComboBox { Left = left, Top = top - 2, Width = cbW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			// [FIXED ORDER]: Match Enum EncodingType (Auto=0, Unicode=1, VNI=2, TCVN3=3)
			cbSourceEncoding.Items.AddRange(new object[] { "Auto Detect", "Unicode", "VNI Windows", "TCVN3 (ABC)" });
			cbSourceEncoding.SelectedIndex = (savedSrcIdx >= 0 && savedSrcIdx < cbSourceEncoding.Items.Count) ? savedSrcIdx : 0;
			this.Controls.Add(cbSourceEncoding);

			top += gap + 15;
			WinForms.Button btnOk = new WinForms.Button { Text = "OK", Left = left, Top = top, Width = 90, DialogResult = WinForms.DialogResult.OK };
			btnOk.Click += (s, e) =>
			{
				if (cbTargetStyle.SelectedItem != null) TargetStyle = cbTargetStyle.SelectedItem.ToString();

				// [FIXED MAPPING]: Map Dropdown Index to Enum correctly
				TargetEncoding = (EncodingType)(cbTargetEncoding.SelectedIndex + 1); // Index 0(Uni)->Enum 1, Index 1(VNI)->Enum 2...
				if (TargetEncoding == (EncodingType)4) TargetEncoding = EncodingType.TCVN3; // Just in case, though logic above matches 0,1,2 -> 1,2,3
				if (cbTargetEncoding.SelectedIndex == 0) TargetEncoding = EncodingType.Unicode; // Explicit safety

				SourceEncoding = (EncodingType)cbSourceEncoding.SelectedIndex; // Matches Enum directly now
			};
			WinForms.Button btnCancel = new WinForms.Button { Text = "Cancel", Left = left + 110, Top = top, Width = 90, DialogResult = WinForms.DialogResult.Cancel };
			this.Controls.AddRange(new WinForms.Control[] { btnOk, btnCancel });
			this.AcceptButton = btnOk;
			this.CancelButton = btnCancel;
		}
	}

	public class StyleCommands
	{
		private HashSet<ObjectId> _processedIds;
		[CommandMethod("CHANGETEXTSTYLE")]
		public void ChangeTextStyle()
		{
			Document doc = AcApp.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;
			_processedIds = new HashSet<ObjectId>();
			try
			{
				List<string> styleList = new List<string>();
				using (Transaction tr = db.TransactionManager.StartTransaction())
				{
					TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
					foreach (ObjectId id in tst)
					{
						TextStyleTableRecord tsr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead); if (!tsr.Name.Contains("|")) styleList.Add(tsr.Name);
					}
					styleList.Sort();
					tr.Commit();
				}
				if (styleList.Count == 0) { ed.WriteMessage("\nNo Text Style found!"); return; }

				AppSettings.Load(out string savedStyle, out int savedTgt, out int savedSrc);
				string targetStyleName; EncodingType srcEnc, tgtEnc;

				using (var form = new TextStyleForm(styleList, savedStyle, savedTgt, savedSrc))
				{
					if (AcApp.ShowModalDialog(form) != WinForms.DialogResult.OK) return;
					targetStyleName = form.TargetStyle; srcEnc = form.SourceEncoding; tgtEnc = form.TargetEncoding;
					AppSettings.Save(targetStyleName, form.SelectedTargetIndex, form.SelectedSourceIndex);
				}

				PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\nSelect Text/Block to Change Style:" };
				PromptSelectionResult psr = ed.GetSelection(pso);
				if (psr.Status != PromptStatus.OK) return;

				HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();
				// Smart Block Processing

				using (Transaction tr = db.TransactionManager.StartTransaction())
				{
					TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
					if (!string.IsNullOrEmpty(targetStyleName) && tst.Has(targetStyleName))
					{
						ObjectId targetStyleId = tst[targetStyleName];
						int count = 0;
						foreach (SelectedObject so in psr.Value)
						{
							Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
							if (ent == null) continue;

							// 1. Process normal entities & Attributes (Instance level)
							if (ProcessEntity(ent, targetStyleId, srcEnc, tgtEnc, tr)) count++;
							// 2. Process Block Definition (Once per block type)
							if (ent is BlockReference blkRef)
							{
								ObjectId btrId = blkRef.BlockTableRecord;
								if (!processedBlockDefs.Contains(btrId))
								{
									processedBlockDefs.Add(btrId);
									BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
									foreach (ObjectId subId in btr)
									{
										Entity subEnt = tr.GetObject(subId, OpenMode.ForWrite) as Entity;
										if (subEnt != null) ProcessEntity(subEnt, targetStyleId, srcEnc, tgtEnc, tr);
									}
								}
							}
						}
						tr.Commit();
						ed.WriteMessage($"\nDone. Processed {count} items (plus unique block definitions).");
						ed.Regen();
					}
				}
			}
			catch (System.Exception ex)
			{
				ed.WriteMessage("\nError: " + ex.Message);
			}
		}

		private bool ProcessEntity(Entity ent, ObjectId styleId, EncodingType src, EncodingType tgt, Transaction tr)
		{
			if (_processedIds.Contains(ent.ObjectId)) return false;
			try
			{
				if (!ent.IsWriteEnabled) ent.UpgradeOpen();
				if (ent is DBText txt)
				{
					txt.TextStyleId = styleId;
					txt.TextString = VnCharset.Convert(txt.TextString, src, tgt);
					_processedIds.Add(ent.ObjectId); return true;
				}
				else if (ent is MText mtxt)
				{
					mtxt.TextStyleId = styleId;
					// Remove font override to apply new style
					string content = Regex.Replace(mtxt.Contents, @"\\[Ff][^;]*;", "");
					content = Regex.Replace(content, @"\\?U\+([0-9A-Fa-f]{4})", m => ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());
					mtxt.Contents = VnCharset.Convert(content, src, tgt);
					_processedIds.Add(ent.ObjectId); return true;
				}
				else if (ent is MLeader mld && mld.ContentType == Autodesk.AutoCAD.DatabaseServices.ContentType.MTextContent)
				{
					MText mt = mld.MText;
					mt.TextStyleId = styleId;
					string content = Regex.Replace(mt.Contents, @"\\[Ff][^;]*;", "");
					content = Regex.Replace(content, @"\\?U\+([0-9A-Fa-f]{4})", m => ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());
					mt.Contents = VnCharset.Convert(content, src, tgt);
					mld.MText = mt; mld.TextStyleId = styleId;
					_processedIds.Add(ent.ObjectId); return true;
				}
				else if (ent is BlockReference blk)
				{
					bool hasAtt = false;
					foreach (ObjectId attId in blk.AttributeCollection)
					{
						AttributeReference att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
						if (att != null && !_processedIds.Contains(attId))
						{
							att.TextStyleId = styleId;
							att.TextString = VnCharset.Convert(att.TextString, src, tgt);
							_processedIds.Add(attId); hasAtt = true;
						}
					}
					_processedIds.Add(ent.ObjectId);
					return hasAtt;
				}
				else if (ent is Dimension dim)
				{
					dim.DimensionStyle = styleId;
					if (!string.IsNullOrEmpty(dim.DimensionText)) dim.DimensionText = VnCharset.Convert(dim.DimensionText, src, tgt);
					_processedIds.Add(ent.ObjectId); return true;
				}
				else if (ent is AttributeDefinition attDef) // Support for Block Definition processing
				{
					attDef.TextStyleId = styleId;
					attDef.TextString = VnCharset.Convert(attDef.TextString, src, tgt);
					_processedIds.Add(ent.ObjectId); return true;
				}
			}
			catch { }
			return false;
		}
	}
}