using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace GlassBoard;

public static class NoteFiles
{
	public static NoteBook Read(string path, Action<Exception> log = null)
	{
		string[] array = new string[2]
		{
			path,
			path + ".bak"
		};
		foreach (string text in array)
		{
			try
			{
				if (File.Exists(text))
				{
					using (FileStream stream = File.OpenRead(text))
					{
						NoteBook obj = ((NoteBook)new DataContractJsonSerializer(typeof(NoteBook)).ReadObject(stream)) ?? throw new InvalidDataException("备忘录文件为空");
						obj.Normalize();
						return obj;
					}
				}
			}
			catch (Exception obj2)
			{
				log?.Invoke(obj2);
				try
				{
					File.Copy(text, text + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), overwrite: false);
				}
				catch
				{
				}
			}
		}
		NoteBook noteBook = new NoteBook();
		noteBook.Normalize();
		return noteBook;
	}

	public static void Write(string path, NoteBook book)
	{
		book.Normalize();
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		string text = path + ".tmp";
		using (FileStream stream = File.Create(text))
		{
			new DataContractJsonSerializer(typeof(NoteBook)).WriteObject(stream, book);
		}
		if (File.Exists(path))
		{
			File.Replace(text, path, path + ".bak");
		}
		else
		{
			File.Move(text, path);
		}
	}
}
