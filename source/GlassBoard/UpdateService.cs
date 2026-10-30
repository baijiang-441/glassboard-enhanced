using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GlassBoard;

public static class UpdateService
{
	public static string LastDiagnostics { get; private set; }

	private static byte[] Read(string url, long maximum, CancellationToken token, bool direct, Action<long, long> progress = null)
	{
		token.ThrowIfCancellationRequested();
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.UserAgent = "GlassBoard-updater/0.9.12";
		request.Accept = "application/vnd.github+json";
		request.Timeout = ((progress == null) ? 12000 : 120000);
		request.ReadWriteTimeout = ((progress == null) ? 12000 : 30000);
		request.MaximumAutomaticRedirections = 8;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		if (direct)
		{
			request.Proxy = null;
		}
		try
		{
			using (token.Register(delegate
			{
				request.Abort();
			}))
			{
				using HttpWebResponse httpWebResponse = (HttpWebResponse)request.GetResponse();
				if (httpWebResponse.ResponseUri.Scheme != "https" || httpWebResponse.ContentLength > maximum)
				{
					throw new InvalidDataException("更新响应不符合要求");
				}
				using Stream stream = httpWebResponse.GetResponseStream();
				using MemoryStream memoryStream = new MemoryStream();
				byte[] array = new byte[16384];
				long num = 0L;
				int num2;
				while ((num2 = stream.Read(array, 0, array.Length)) > 0)
				{
					token.ThrowIfCancellationRequested();
					if (memoryStream.Length + num2 > maximum)
					{
						throw new InvalidDataException("更新文件过大");
					}
					memoryStream.Write(array, 0, num2);
					if (progress != null && DateTime.UtcNow.Ticks - num > TimeSpan.FromMilliseconds(250.0).Ticks)
					{
						progress(memoryStream.Length, httpWebResponse.ContentLength);
						num = DateTime.UtcNow.Ticks;
					}
				}
				return memoryStream.ToArray();
			}
		}
		catch (WebException ex)
		{
			string message = Explain(ex);
			try
			{
				if (ex.Response != null)
				{
					ex.Response.Close();
				}
			}
			catch
			{
			}
			token.ThrowIfCancellationRequested();
			throw new IOException(message, ex);
		}
	}

	private static T Json<T>(byte[] bytes)
	{
		using MemoryStream stream = new MemoryStream(bytes, writable: false);
		return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
	}

	public static string Explain(Exception error)
	{
		if (!(error is WebException ex))
		{
			return error.Message;
		}
		if (ex.Response is HttpWebResponse httpWebResponse)
		{
			int statusCode = (int)httpWebResponse.StatusCode;
			switch (statusCode)
			{
			case 403:
			case 429:
				return "GitHub 拒绝请求或访问次数受限（HTTP " + statusCode + "），请稍后重试";
			case 407:
				return "系统代理需要认证，请检查 Windows 代理设置";
			case 404:
				return "更新地址尚未发布（HTTP 404）";
			default:
				return "更新服务返回 HTTP " + statusCode;
			}
		}
		if (ex.Status == WebExceptionStatus.NameResolutionFailure || ex.Status == WebExceptionStatus.ProxyNameResolutionFailure)
		{
			return "域名解析失败，请检查 DNS 或代理";
		}
		if (ex.Status == WebExceptionStatus.SecureChannelFailure || ex.Status == WebExceptionStatus.TrustFailure)
		{
			return "HTTPS 证书或安全连接失败，请检查系统时间、证书和 .NET 4.8";
		}
		if (ex.Status == WebExceptionStatus.Timeout)
		{
			return "连接 GitHub 超时，可尝试另一种连接方式";
		}
		if (ex.Status == WebExceptionStatus.ConnectFailure)
		{
			return "无法连接 GitHub，请检查网络或 Windows 代理";
		}
		return "网络连接失败（" + ex.Status.ToString() + "）：" + ex.Message;
	}

	public static UpdateOffer CheckUsing(Func<string, long, byte[]> read)
	{
		string[] array = new string[4] { "https://api.github.com/repos/SENLIN-SAMA/win-.-/releases?per_page=20", "https://api.github.com/repos/SENLIN-SAMA/win-.-/contents", "https://raw.githubusercontent.com/SENLIN-SAMA/win-.-/main/update.json", "https://github.com/SENLIN-SAMA/win-.-/raw/refs/heads/main/update.json" };
		List<UpdateOffer> list = new List<UpdateOffer>();
		List<string> list2 = new List<string>();
		int num = 0;
		Task<KeyValuePair<byte[], Exception>>[] array2 = array.Select((string url) => Task.Run(delegate
		{
			try
			{
				return new KeyValuePair<byte[], Exception>(read(url, 1048576L), null);
			}
			catch (Exception value)
			{
				return new KeyValuePair<byte[], Exception>(null, value);
			}
		})).ToArray();
		Task[] tasks = array2;
		Task.WaitAll(tasks);
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				if (array2[i].Result.Value != null)
				{
					throw array2[i].Result.Value;
				}
				byte[] key = array2[i].Result.Key;
				UpdateOffer updateOffer;
				switch (i)
				{
				case 0:
					updateOffer = UpdateRules.Choose(Json<List<UpdateRelease>>(key), null);
					break;
				case 1:
					updateOffer = UpdateRules.Choose(null, Json<List<UpdateAsset>>(key));
					break;
				default:
					updateOffer = UpdateRules.Choose(null, new UpdateAsset[1] { Json<UpdateAsset>(key) });
					if (updateOffer == null)
					{
						throw new InvalidDataException("更新索引缺少有效用户包或校验值");
					}
					break;
				}
				if (i != 0 || updateOffer != null)
				{
					num++;
				}
				if (updateOffer != null)
				{
					list.Add(updateOffer);
				}
				list2.Add(new Uri(array[i]).Host + " " + ((i < 2) ? "API" : "索引") + "：正常" + ((updateOffer == null) ? "（暂无用户包）" : (" · " + updateOffer.Name)));
			}
			catch (Exception error)
			{
				list2.Add(new Uri(array[i]).Host + " " + ((i < 2) ? "API" : "索引") + "：" + Explain(error));
			}
		}
		LastDiagnostics = DateTime.Now.ToString("s") + Environment.NewLine + string.Join(Environment.NewLine, list2);
		if (num == 0)
		{
			throw new IOException("所有更新线路均未完成检查。\n" + string.Join("\n", list2));
		}
		return list.OrderByDescending((UpdateOffer o) => o.Version).FirstOrDefault();
	}

	public static async Task<UpdateOffer> Check(bool direct = false)
	{
		return await Task.Run(() => CheckUsing((string url, long max) => Read(url, max, CancellationToken.None, direct)));
	}

	public static async Task<byte[]> Download(UpdateOffer offer, CancellationToken token, Action<long, long> progress, bool direct = false)
	{
		return await Task.Run(delegate
		{
			if (offer == null || !UpdateRules.Trusted(offer.Url))
			{
				throw new InvalidDataException("更新地址不受信任");
			}
			byte[] array;
			try
			{
				array = Read(offer.Url, 201326592L, token, direct, progress);
			}
			catch (IOException)
			{
				token.ThrowIfCancellationRequested();
				string text = UpdateRules.Alternate(offer.Url);
				if (text == null)
				{
					throw;
				}
				array = Read(text, 201326592L, token, direct, progress);
			}
			UpdateRules.Verify(array, offer);
			return array;
		}, token);
	}
}
