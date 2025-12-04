using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net;

// AutoCAD References
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

// --- PHẦN SỬA LỖI XUNG ĐỘT TÊN (QUAN TRỌNG) ---
// Tạo tên riêng (Alias) cho các thư viện để không bị nhầm lẫn
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: CommandClass(typeof(AutoCADTranslatePlugin.TranslateCommands))]

namespace AutoCADTranslatePlugin
{
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
		public List<string> Codes { get; set; }
	}

	// Class Ngụy trang User-Agent
	public static class StealthWebClient
	{
		private static readonly Random _rnd = new Random();

		private static readonly string[] _userAgents = new string[]
		{
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36",
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36",
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36",
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:109.0) Gecko/20100101 Firefox/115.0",
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36 Edg/115.0.1901.188"
		};

		public static string GetRandomUserAgent()
		{
			return _userAgents[_rnd.Next(_userAgents.Length)];
		}

		public static async Task RandomSleep()
		{
			await Task.Delay(_rnd.Next(200, 800));
		}
	}

	public static class FormatProtector
	{
		private static readonly Regex _regexCodes = new Regex(
			@"(%%[UuOoCcDdPp%])|" +
			@"(\\P)|" +
			@"(\\[LloOkK])|" +
			@"(\\[\\{}])|" +
			@"(\\[A-Za-z0-9]+;)|" +
			@"({|})",
			RegexOptions.Compiled);

		public static MaskResult MaskText(string input)
		{
			var result = new MaskResult();
			result.Codes = new List<string>();

			if (string.IsNullOrEmpty(input))
			{
				result.MaskedText = input;
				return result;
			}

			int index = 0;
			result.MaskedText = _regexCodes.Replace(input, m =>
			{
				result.Codes.Add(m.Value);
				return $"__TAG{index++}__";
			});

			return result;
		}

		public static string UnmaskText(string translated, List<string> codes)
		{
			if (string.IsNullOrEmpty(translated) || codes == null || codes.Count == 0) return translated;

			for (int i = 0; i < codes.Count; i++)
			{
				string pattern = $@"__\s*TAG{i}\s*__";
				translated = Regex.Replace(translated, pattern, codes[i], RegexOptions.IgnoreCase);
			}
			return translated;
		}
	}

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

			// 0. Lấy danh sách Text Style
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

			// 1. Hiển thị Form (Sử dụng WinForms.DialogResult để tránh lỗi)
			string selectedStyleName = "Keep Original";
			using (var form = new LanguageSelectionForm(_lastSourceLang, _lastTargetLang, styleNames, _lastTextStyle))
			{
				var result = AcApp.ShowModalDialog(form);
				if (result != WinForms.DialogResult.OK) return; // Sửa lỗi DialogResult

				_lastSourceLang = form.SelectedSourceCode;
				_lastTargetLang = form.SelectedTargetCode;
				_lastTextStyle = form.SelectedTextStyle;
				selectedStyleName = form.SelectedTextStyle;
			}

			// 2. Chọn đối tượng
			TypedValue[] filterList = new TypedValue[] {
				new TypedValue((int)DxfCode.Operator, "<OR"),
				new TypedValue((int)DxfCode.Start, "TEXT"),
				new TypedValue((int)DxfCode.Start, "MTEXT"),
				new TypedValue((int)DxfCode.Start, "MULTILEADER"),
				new TypedValue((int)DxfCode.Start, "INSERT"),
				new TypedValue((int)DxfCode.Operator, "OR>")
			};
			SelectionFilter filter = new SelectionFilter(filterList);
			PromptSelectionResult selRes = ed.GetSelection(filter);

			if (selRes.Status != PromptStatus.OK) return;

			ObjectId[] objectIds = selRes.Value.GetObjectIds();
			List<TextDataObj> dataList = new List<TextDataObj>();

			// --- BƯỚC 1: ĐỌC DỮ LIỆU ---
			using (Transaction tr = doc.TransactionManager.StartTransaction())
			{
				foreach (ObjectId objId in objectIds)
				{
					Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
					if (ent == null) continue;

					if (ent is DBText dbText)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = dbText.TextString, ObjectType = "TEXT" });
					else if (ent is MText mText)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = mText.Contents, ObjectType = "MTEXT" });
					else if (ent is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
						dataList.Add(new TextDataObj { ObjId = objId, OriginalText = mLeader.MText.Contents, ObjectType = "MLEADER" });
					else if (ent is BlockReference blkRef)
					{
						foreach (ObjectId attId in blkRef.AttributeCollection)
						{
							AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
							if (attRef != null && !attRef.IsConstant)
								dataList.Add(new TextDataObj { ObjId = attId, OriginalText = attRef.TextString, ObjectType = "ATTRIB" });
						}
					}
				}
				tr.Commit();
			}

			ed.WriteMessage($"\nStealth Mode: Translating {dataList.Count} objects safely...");

			// --- BƯỚC 2: DỊCH ---
			try
			{
				using (var client = new HttpClient())
				{
					client.Timeout = TimeSpan.FromSeconds(60);

					using (SemaphoreSlim semaphore = new SemaphoreSlim(8))
					{
						var tasks = dataList.Select(async item =>
						{
							await semaphore.WaitAsync();
							try
							{
								MaskResult maskResult = FormatProtector.MaskText(item.OriginalText);

								if (maskResult.MaskedText.Trim().StartsWith("__TAG") && maskResult.MaskedText.Trim().EndsWith("__") && !maskResult.MaskedText.Contains(" "))
								{
									item.TranslatedText = item.OriginalText;
								}
								else
								{
									string trans = await SafeGoogleTranslateApi(client, maskResult.MaskedText, _lastSourceLang, _lastTargetLang);
									item.TranslatedText = FormatProtector.UnmaskText(trans, maskResult.Codes);
								}
							}
							finally
							{
								semaphore.Release();
							}
						});

						await Task.WhenAll(tasks);
					}
				}
			}
			catch (System.Exception ex)
			{
				ed.WriteMessage($"\nConnection Error: {ex.Message}");
				return;
			}

			// --- BƯỚC 3: GHI DỮ LIỆU ---
			try
			{
				using (DocumentLock docLock = doc.LockDocument())
				{
					using (Transaction tr = doc.TransactionManager.StartTransaction())
					{
						ObjectId targetStyleId = ObjectId.Null;
						if (selectedStyleName != "Keep Original")
						{
							TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
							if (tst.Has(selectedStyleName))
								targetStyleId = tst[selectedStyleName];
						}

						int count = 0;
						foreach (var item in dataList)
						{
							Entity ent = tr.GetObject(item.ObjId, OpenMode.ForWrite) as Entity;
							if (ent == null) continue;

							if (!string.IsNullOrEmpty(item.TranslatedText) && item.OriginalText != item.TranslatedText)
							{
								if (ent is DBText dbText) dbText.TextString = item.TranslatedText;
								else if (ent is MText mText) mText.Contents = item.TranslatedText;
								else if (ent is MLeader mLeader)
								{
									MText mt = mLeader.MText;
									mt.Contents = item.TranslatedText;
									mLeader.MText = mt;
								}
								else if (ent is AttributeReference attRef) attRef.TextString = item.TranslatedText;
								count++;
							}

							if (targetStyleId != ObjectId.Null)
							{
								if (ent is DBText dbText) dbText.TextStyleId = targetStyleId;
								else if (ent is MText mText) mText.TextStyleId = targetStyleId;
								else if (ent is MLeader mLeader) mLeader.TextStyleId = targetStyleId;
								else if (ent is AttributeReference attRef) attRef.TextStyleId = targetStyleId;
							}
						}
						tr.Commit();
						ed.WriteMessage($"\nDone! Translated {count} objects.");
					}
				}
				ed.Regen();
			}
			catch (System.Exception ex)
			{
				ed.WriteMessage($"\nWrite Error: {ex.Message}");
			}
		}

		private async Task<string> SafeGoogleTranslateApi(HttpClient client, string input, string sl, string tl)
		{
			if (string.IsNullOrWhiteSpace(input)) return input;
			if (Regex.IsMatch(input, @"^[\d\s\.,-]+$")) return input;

			int maxRetries = 10;
			int retryDelay = 2000;

			await StealthWebClient.RandomSleep();

			for (int i = 0; i < maxRetries; i++)
			{
				try
				{
					var request = new HttpRequestMessage(HttpMethod.Get, $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={System.Web.HttpUtility.UrlEncode(input)}");
					request.Headers.Add("User-Agent", StealthWebClient.GetRandomUserAgent());

					HttpResponseMessage response = await client.SendAsync(request);

					if (response.IsSuccessStatusCode)
					{
						string jsonResponse = await response.Content.ReadAsStringAsync();
						return ParseResult(jsonResponse, input);
					}
					else if ((int)response.StatusCode == 429 || (int)response.StatusCode == 503)
					{
						await Task.Delay(retryDelay + new Random().Next(0, 1000));
						retryDelay *= 2;
						continue;
					}
					else
					{
						return input;
					}
				}
				catch
				{
					if (i == maxRetries - 1) return input;
					await Task.Delay(retryDelay);
				}
			}
			return input;
		}

		private string ParseResult(string jsonResponse, string original)
		{
			try
			{
				int firstQuote = jsonResponse.IndexOf('"');
				if (firstQuote == -1) return original;

				int secondQuote = jsonResponse.IndexOf('"', firstQuote + 1);
				while (secondQuote > 0 && jsonResponse[secondQuote - 1] == '\\')
					secondQuote = jsonResponse.IndexOf('"', secondQuote + 1);

				if (secondQuote > firstQuote)
				{
					string result = jsonResponse.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
					return Regex.Unescape(result);
				}
				return original;
			}
			catch { return original; }
		}
	}

	// --- FORM GIAO DIỆN ĐÃ SỬA LỖI ---
	public class LanguageSelectionForm : WinForms.Form // Dùng Alias WinForms
	{
		public string SelectedSourceCode { get; private set; }
		public string SelectedTargetCode { get; private set; }
		public string SelectedTextStyle { get; private set; }

		private WinForms.ComboBox cbSource;
		private WinForms.ComboBox cbTarget;
		private WinForms.ComboBox cbStyle;

		private Dictionary<string, string> languages = new Dictionary<string, string>()
		{
			{"auto", "Auto detect"}, {"en", "English"}, {"vi", "Vietnamese"}, {"ja", "Japanese"},
			{"ko", "Korean"}, {"zh-CN", "Chinese (Simplified)"}, {"fr", "French"}, {"ru", "Russian"},
			{"de", "German"}, {"es", "Spanish"}
		};

		public LanguageSelectionForm(string defaultSource, string defaultTarget, List<string> styleList, string defaultStyle)
		{
			this.Text = "Translate Text (Stealth Mode)";
			this.Size = new Drawing.Size(350, 260); // Sửa lỗi Size
			this.StartPosition = WinForms.FormStartPosition.CenterScreen;
			this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;

			int padding = 20;
			int lblWidth = 100;
			int comboWidth = 180;
			int top = padding;

			WinForms.Label lblSource = new WinForms.Label() { Text = "Source Language:", Left = padding, Top = top, Width = lblWidth };
			this.Controls.Add(lblSource);

			cbSource = new WinForms.ComboBox() { Left = padding + lblWidth, Top = top - 3, Width = comboWidth, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (var kvp in languages) cbSource.Items.Add(new LangItem(kvp.Key, kvp.Value));
			SetComboValue(cbSource, defaultSource);
			this.Controls.Add(cbSource);

			top += 40;

			WinForms.Label lblTarget = new WinForms.Label() { Text = "Target Language:", Left = padding, Top = top, Width = lblWidth };
			this.Controls.Add(lblTarget);

			cbTarget = new WinForms.ComboBox() { Left = padding + lblWidth, Top = top - 3, Width = comboWidth, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (var kvp in languages)
			{
				if (kvp.Key != "auto") cbTarget.Items.Add(new LangItem(kvp.Key, kvp.Value));
			}
			SetComboValue(cbTarget, defaultTarget);
			this.Controls.Add(cbTarget);

			top += 40;

			WinForms.Label lblStyle = new WinForms.Label() { Text = "Result Text Style:", Left = padding, Top = top, Width = lblWidth };
			this.Controls.Add(lblStyle);

			cbStyle = new WinForms.ComboBox() { Left = padding + lblWidth, Top = top - 3, Width = comboWidth, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
			foreach (string st in styleList) cbStyle.Items.Add(st);

			if (cbStyle.Items.Contains(defaultStyle)) cbStyle.SelectedItem = defaultStyle;
			else cbStyle.SelectedIndex = 0;

			this.Controls.Add(cbStyle);

			top += 50;

			WinForms.Button btnOk = new WinForms.Button() { Text = "Translate", Left = 120, Top = top, DialogResult = WinForms.DialogResult.OK, Width = 90, Height = 30 };
			btnOk.Click += (s, e) => {
				SelectedSourceCode = ((LangItem)cbSource.SelectedItem).Code;
				SelectedTargetCode = ((LangItem)cbTarget.SelectedItem).Code;
				SelectedTextStyle = cbStyle.SelectedItem.ToString();
			};
			this.Controls.Add(btnOk);

			WinForms.Button btnCancel = new WinForms.Button() { Text = "Cancel", Left = 220, Top = top, DialogResult = WinForms.DialogResult.Cancel, Width = 90, Height = 30 };
			this.Controls.Add(btnCancel);
		}

		private void SetComboValue(WinForms.ComboBox cb, string code)
		{
			foreach (LangItem item in cb.Items)
			{
				if (item.Code == code) { cb.SelectedItem = item; break; }
			}
			if (cb.SelectedIndex == -1 && cb.Items.Count > 0) cb.SelectedIndex = 0;
		}

		private class LangItem
		{
			public string Code { get; set; }
			public string Name { get; set; }
			public LangItem(string code, string name) { Code = code; Name = name; }
			public override string ToString() { return Name; }
		}
	}
}