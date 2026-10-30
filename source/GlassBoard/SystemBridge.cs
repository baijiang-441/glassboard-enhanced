using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GlassBoard;

public sealed class SystemBridge : IDisposable
{
	private sealed class BridgeCommandException : Exception
	{
		public BridgeCommandException(string message)
			: base(message)
		{
		}
	}

	private readonly SemaphoreSlim gate = new SemaphoreSlim(1);

	private readonly Timer idle;

	private DateTime lastUse = DateTime.UtcNow;

	private Process process;

	private StreamWriter input;

	private bool stopped;

	private int serial;

	private DateTime retry;

	private string error = "";

	public bool Installed => Array.IndexOf(Assembly.GetExecutingAssembly().GetManifestResourceNames(), "GlassBoard.SystemControl.ps1") >= 0;

	public SystemBridge()
	{
		idle = new Timer(delegate
		{
			TrimIdle();
		}, null, 60000, 60000);
	}

	private void TrimIdle()
	{
		if (stopped || !gate.Wait(0))
		{
			return;
		}
		try
		{
			if ((DateTime.UtcNow - lastUse).TotalMinutes >= 2.0)
			{
				Kill();
			}
		}
		finally
		{
			gate.Release();
		}
	}

	public async Task<BridgeResult> Send(BridgeRequest request)
	{
		if (stopped)
		{
			throw new ObjectDisposedException("SystemBridge");
		}
		await gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (stopped)
			{
				throw new ObjectDisposedException("SystemBridge");
			}
			if (process == null || process.HasExited)
			{
				if (DateTime.UtcNow < retry)
				{
					throw new IOException("后台模块正在恢复，请稍后重试。");
				}
				Start();
			}
			lastUse = DateTime.UtcNow;
			request.Id = ++serial;
			request.Version = 1;
			string @string;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				new DataContractJsonSerializer(typeof(BridgeRequest)).WriteObject(memoryStream, request);
				@string = Encoding.UTF8.GetString(memoryStream.ToArray());
			}
			await input.WriteLineAsync(@string).ConfigureAwait(continueOnCapturedContext: false);
			await input.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
			Task<string> read = process.StandardOutput.ReadLineAsync();
			if (await Task.WhenAny(read, Task.Delay(22000)).ConfigureAwait(continueOnCapturedContext: false) != read)
			{
				throw new TimeoutException("系统操作超时，请重试。");
			}
			string text = await read.ConfigureAwait(continueOnCapturedContext: false);
			if (text == null || text.Length > 8000000)
			{
				throw new IOException("后台模块未返回有效数据。");
			}
			BridgeReply bridgeReply;
			using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
			{
				bridgeReply = (BridgeReply)new DataContractJsonSerializer(typeof(BridgeReply)).ReadObject(stream);
			}
			if (bridgeReply == null || bridgeReply.Version != 1 || bridgeReply.Id != request.Id)
			{
				throw new InvalidDataException("后台响应与当前操作不匹配。");
			}
			if (!bridgeReply.Ok)
			{
				throw new BridgeCommandException(bridgeReply.Error ?? "操作未完成");
			}
			return bridgeReply.Result ?? new BridgeResult();
		}
		catch (BridgeCommandException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Kill();
			retry = DateTime.UtcNow.AddSeconds(5.0);
			throw new IOException(ex2.Message + ((error.Length > 0) ? (" · " + error) : ""), ex2);
		}
		finally
		{
			lastUse = DateTime.UtcNow;
			request.Password = "";
			gate.Release();
		}
	}

	private void Start()
	{
		if (!Installed)
		{
			throw new FileNotFoundException("内置控制中心模块缺失，请重新下载完整程序。");
		}
		string location = Assembly.GetExecutingAssembly().Location;
		string s2 = "$a=[Reflection.Assembly]::LoadFile('" + location.Replace("'", "''") + "');$r=New-Object IO.StreamReader($a.GetManifestResourceStream('GlassBoard.SystemControl.ps1'));try{$s=$r.ReadToEnd()}finally{$r.Dispose()};& ([ScriptBlock]::Create($s))";
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
			Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(s2)),
			WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		process = Process.Start(startInfo);
		input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
		{
			AutoFlush = true
		};
		error = "";
		process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				error = ((e.Data.Length > 500) ? e.Data.Substring(0, 500) : e.Data);
			}
		};
		process.BeginErrorReadLine();
	}

	private void Kill()
	{
		Process process = this.process;
		this.process = null;
		if (process == null)
		{
			return;
		}
		try
		{
			if (!process.HasExited)
			{
				process.Kill();
			}
		}
		catch
		{
		}
		process.Dispose();
	}

	public void Dispose()
	{
		stopped = true;
		idle.Dispose();
		Kill();
	}
}
