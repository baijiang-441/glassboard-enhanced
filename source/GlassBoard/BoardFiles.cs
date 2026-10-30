using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GlassBoard;

public static class BoardFiles
{
	public static readonly string[] Extensions = new string[4] { ".txt", ".md", ".docx", ".xlsx" };

	private static void Part(ZipArchive zip, string path, string xml)
	{
		using StreamWriter streamWriter = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.Write(xml);
	}

	public static void Create(string path)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		string text = Path.GetExtension(path).ToLowerInvariant();
		if (Array.IndexOf(Extensions, text) < 0)
		{
			throw new ArgumentException("请选择文本、Markdown、Word 或 Excel 文件格式。");
		}
		bool flag = false;
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
			flag = true;
			if (text == ".docx" || text == ".xlsx")
			{
				ZipArchive val = new ZipArchive((Stream)fileStream, (ZipArchiveMode)1, true);
				try
				{
					string text2 = ((text == ".docx") ? "word/document.xml" : "xl/workbook.xml");
					Part(val, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/" + text2 + "\" ContentType=\"application/vnd.openxmlformats-officedocument." + ((text == ".docx") ? "wordprocessingml.document" : "spreadsheetml.sheet") + ".main+xml\"/>" + ((text == ".xlsx") ? "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" : "") + "</Types>");
					Part(val, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"" + text2 + "\"/></Relationships>");
					if (text == ".docx")
					{
						Part(val, text2, "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/><w:sectPr/></w:body></w:document>");
					}
					else
					{
						Part(val, text2, "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"工作表1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
						Part(val, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
						Part(val, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData/></worksheet>");
					}
				}
				finally
				{
					((IDisposable)val)?.Dispose();
				}
			}
			fileStream.Flush(flushToDisk: true);
		}
		catch
		{
			if (flag)
			{
				try
				{
					File.Delete(path);
				}
				catch
				{
				}
			}
			throw;
		}
	}

	public static string CreateFolder(string parent, string name)
	{
		name = (name ?? "").Trim();
		if (name.Length == 0 || name == "." || name == ".." || name.IndexOfAny(new char[9] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || name.EndsWith(".") || Array.Exists(name.ToCharArray(), (char c) => c < ' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new ArgumentException("请输入有效的文件夹名称。");
		}
		string text = name.Split('.')[0].ToUpperInvariant();
		switch (text)
		{
		default:
			if (text.Length != 4 || (!text.StartsWith("COM") && !text.StartsWith("LPT")) || text[3] < '1' || text[3] > '9')
			{
				break;
			}
			goto case "CON";
		case "CON":
		case "PRN":
		case "AUX":
		case "NUL":
			throw new ArgumentException("此名称由 Windows 保留，请换一个名称。");
		}
		string text2 = Path.Combine(parent, name);
		if (File.Exists(text2) || Directory.Exists(text2))
		{
			throw new IOException("同名文件或文件夹已经存在，请换一个名称。");
		}
		Directory.CreateDirectory(text2);
		return text2;
	}
}
