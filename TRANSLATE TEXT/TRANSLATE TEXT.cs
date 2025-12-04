using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Net;

// AutoCAD References
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

// Alias
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(AutoCADTranslatePlugin.TranslateCommands))]

namespace AutoCADTranslatePlugin
{
	// --- 1. DATA OBJECTS ---
	public class TextDataObj
	{
		public ObjectId ObjId { get; set; }
		public string OriginalText { get; set; }
		public string TranslatedText { get; set; }
		public string ObjectType { get; set; }
	}

	public class MaskResult
	{
		public string MaskedText { get; set; }
		public List<string> Codes { get; set; } = new List<string>();
	}

	// --- 2. UTILITIES ---
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

	// --- 3. FORMAT PROTECTOR (HANDLES CODES & WHITESPACE) ---
	public static class FormatProtector
	{
		private static readonly Regex _regexCodes = new Regex(
			// Hex Code 32 chars (Data Link)
			@"(\{[^}]*[0-9a-fA-F]{32}[^}]*\})|" +
			@"(\\[A-Za-z0-9]*[0-9a-fA-F]{32})|" +
			@"\b[0-9a-fA-F]{32}\b|" +
			// Field Code
			@"(%<[^>]+>%)|" +
			// MText Codes (\P, \L, \W...)
			@"(\\P)|" +
			@"(\\[LloOkK])|" +
			@"(\\[\\{}])|" +
			@"(\\[A-Za-z0-9]+[^;]*;)|" +
			// Brackets
			@"({|})",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static MaskResult MaskText(string input)
		{
			var result = new MaskResult();
			if (string.IsNullOrEmpty(input)) { result.MaskedText = input; return result; }

			int index = 0;
			// Add padding [ID:n] so Google translates context better
			result.MaskedText = _regexCodes.Replace(input, m =>
			{
				result.Codes.Add(m.Value);
				return $" [ID:{index++}] ";
			});

			return result;
		}

		public static string UnmaskText(string translated, List<string> codes)
		{
			if (string.IsNullOrEmpty(translated) || codes == null || codes.Count == 0) return translated.Trim();

			// 1. Restore codes to ID positions
			for (int i = 0; i < codes.Count; i++)
			{
				string pattern = $@"\[\s*ID\s*:\s*{i}\s*\]";
				translated = Regex.Replace(translated, pattern, match => codes[i], RegexOptions.IgnoreCase);
			}

			// --- CLEAN UP STEPS ---

			// 2. Handle Newline \P: Remove surrounding whitespace
			// FIXED: Changed to RegexOptions.None to distinguish \P (Newline) from \p (Paragraph)
			translated = Regex.Replace(translated, @"\s*(\\P)\s*", @"\P", RegexOptions.None);

			// 3. Handle whitespace AFTER formatting codes
			// E.g., "\A1;  TEXT" -> "\A1;TEXT"
			translated = Regex.Replace(translated, @"(\\[A-Za-z0-9]+[^;]*;)\s+", @"$1", RegexOptions.IgnoreCase);

			// 4. Handle whitespace after single switch codes like \L, \O
			translated = Regex.Replace(translated, @"(\\[LloOkK])\s+", @"$1", RegexOptions.IgnoreCase);

			// 5. Split lines and Trim each line
			// FIXED: Changed to RegexOptions.None to avoid splitting on \p
			string[] lines = Regex.Split(translated, @"\\P", RegexOptions.None);
			for (int j = 0; j < lines.Length; j++)
			{
				lines[j] = lines[j].Trim();
			}

			// 6. Join back
			return string.Join("\\P", lines);
		}
	}

	// --- 4. LANGUAGE HELPER ---
	public class LanguageItem
	{
		public string Code { get; set; }
		public string Name { get; set; }
		public override string ToString() => Name;
	}

	public static class LanguageList
	{
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

	// --- 5. MAIN COMMANDS ---
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
			List<string> styleNames = new List<string>() { "Keep Original" };
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
			string selectedStyleName = "Keep Original";
			using (var form = new LanguageSelectionForm(_lastSourceLang, _lastTargetLang, styleNames, _lastTextStyle))
			{
				if (AcApp.ShowModalDialog(form) != DialogResult.OK) return;
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

			// 3. Read Data
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
					else if (ent is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = mLeader.MText.Contents });
					else if (ent is BlockReference blkRef)
					{
						foreach (ObjectId attId in blkRef.AttributeCollection)
						{
							AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
							if (attRef != null && !attRef.IsConstant)
								dataList.Add(new TextDataObj { ObjId = attId, OriginalText = attRef.TextString });
						}
					}
				}
				tr.Commit();
			}

			ed.WriteMessage($"\nProcessing {dataList.Count} objects (Full Trim & Languages)...");

			// 4. Translate Process
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
							if (IsAllTags(maskResult.MaskedText))
							{
								item.TranslatedText = item.OriginalText;
							}
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
			catch (System.Exception ex)
			{
				ed.WriteMessage($"\nError: {ex.Message}");
				return;
			}

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

						if (ent is DBText dbText) { dbText.TextString = item.TranslatedText; if (targetStyleId != ObjectId.Null) dbText.TextStyleId = targetStyleId; }
						else if (ent is MText mText) { mText.Contents = item.TranslatedText; if (targetStyleId != ObjectId.Null) mText.TextStyleId = targetStyleId; }
						else if (ent is MLeader mLeader)
						{
							MText mt = mLeader.MText;
							mt.Contents = item.TranslatedText;
							if (targetStyleId != ObjectId.Null) mt.TextStyleId = targetStyleId;
							mLeader.MText = mt;
						}
						else if (ent is AttributeReference attRef) { attRef.TextString = item.TranslatedText; if (targetStyleId != ObjectId.Null) attRef.TextStyleId = targetStyleId; }

						count++;
					}
					tr.Commit();
					ed.WriteMessage($"\nDone! Translated {count} objects.");
				}
				ed.Regen();
			}
			catch (System.Exception ex) { ed.WriteMessage($"\nWrite Error: {ex.Message}"); }
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
					var request = new HttpRequestMessage(HttpMethod.Get,
						$"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={System.Web.HttpUtility.UrlEncode(input)}");
					request.Headers.Add("User-Agent", StealthWebClient.GetRandomUserAgent());

					HttpResponseMessage response = await client.SendAsync(request);
					if (response.IsSuccessStatusCode)
					{
						string json = await response.Content.ReadAsStringAsync();
						// STRICT PARSER (Prevents vivi errors)
						return ParseResultStrict(json, input);
					}

					if ((int)response.StatusCode == 429)
					{
						await Task.Delay(retryDelay);
						retryDelay *= 2;
					}
					else break;
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
				int depth = 0;
				bool inString = false;
				bool isTransSegment = false;

				for (int i = 0; i < json.Length; i++)
				{
					char c = json[i];
					if (inString)
					{
						if (c == '\\' && i + 1 < json.Length) { i++; continue; }
						if (c == '"')
						{
							inString = false;
							if (depth == 3 && isTransSegment)
							{
								int endContent = i;
								int startContent = i - 1;
								while (startContent >= 0)
								{
									if (json[startContent] == '"' && (startContent == 0 || json[startContent - 1] != '\\')) break;
									startContent--;
								}
								if (startContent >= 0)
								{
									string segment = json.Substring(startContent + 1, endContent - startContent - 1);
									sb.Append(Regex.Unescape(segment));
									isTransSegment = false;
								}
							}
						}
						continue;
					}

					if (c == '[')
					{
						depth++;
						if (depth == 3) isTransSegment = true;
					}
					else if (c == ']')
					{
						if (depth == 2) return sb.ToString();
						depth--;
					}
					else if (c == '"')
					{
						inString = true;
					}
				}

				string res = sb.ToString();
				return string.IsNullOrWhiteSpace(res) ? original : res;
			}
			catch { return original; }
		}
	}

	// --- 6. UI FORM ---
	public class LanguageSelectionForm : Form
	{
		public string SelectedSourceCode { get; private set; }
		public string SelectedTargetCode { get; private set; }
		public string SelectedTextStyle { get; private set; }

		private readonly ComboBox cbSource;
		private readonly ComboBox cbTarget;
		private readonly ComboBox cbStyle;

		public LanguageSelectionForm(string defaultSource, string defaultTarget, List<string> styleList, string defaultStyle)
		{
			this.Text = "Translate Tool (Pro)"; this.Size = new Size(400, 260);
			this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false; this.MinimizeBox = false;

			int pad = 20, lblW = 100, cbW = 230, top = 20;

			this.Controls.Add(new Label { Text = "Source Lang:", Left = pad, Top = top, Width = lblW });
			cbSource = new ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
			// Load Languages
			foreach (var lang in LanguageList.GetSupportedLanguages()) cbSource.Items.Add(lang);
			SetComboValue(cbSource, defaultSource);
			this.Controls.Add(cbSource);

			top += 40;
			this.Controls.Add(new Label { Text = "Target Lang:", Left = pad, Top = top, Width = lblW });
			cbTarget = new ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
			foreach (var lang in LanguageList.GetSupportedLanguages())
			{
				if (lang.Code != "auto") cbTarget.Items.Add(lang);
			}
			SetComboValue(cbTarget, defaultTarget);
			this.Controls.Add(cbTarget);

			top += 40;
			this.Controls.Add(new Label { Text = "Text Style:", Left = pad, Top = top, Width = lblW });
			cbStyle = new ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
			foreach (string s in styleList) cbStyle.Items.Add(s);
			if (cbStyle.Items.Contains(defaultStyle)) cbStyle.SelectedItem = defaultStyle; else cbStyle.SelectedIndex = 0;
			this.Controls.Add(cbStyle);

			top += 50;
			Button btnOk = new Button { Text = "Translate", Left = 130, Top = top, DialogResult = DialogResult.OK, Width = 100 };
			btnOk.Click += (s, e) => {
				SelectedSourceCode = ((LanguageItem)cbSource.SelectedItem).Code;
				SelectedTargetCode = ((LanguageItem)cbTarget.SelectedItem).Code;
				SelectedTextStyle = cbStyle.SelectedItem.ToString();
			};
			this.Controls.Add(btnOk);

			Button btnCancel = new Button { Text = "Cancel", Left = 240, Top = top, DialogResult = DialogResult.Cancel, Width = 100 };
			this.Controls.Add(btnCancel);
		}

		private void SetComboValue(ComboBox cb, string code)
		{
			foreach (LanguageItem item in cb.Items) if (item.Code == code) { cb.SelectedItem = item; return; }
			if (cb.Items.Count > 0) cb.SelectedIndex = 0;
		}
	}
}