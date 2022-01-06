﻿using DynamicData.Binding;
using KyoshinEewViewer.Core.Models;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services;

public class UpdateCheckService : ReactiveObject
{
	private static UpdateCheckService? _default;
	public static UpdateCheckService Default => _default ??= new UpdateCheckService();

	public VersionInfo[]? AvailableUpdateVersions { get; private set; }

	private Timer CheckUpdateTask { get; }
	private HttpClient Client { get; } = new HttpClient();

	private ILogger Logger { get; }

	public event Action<VersionInfo[]?>? Updated;


	private const string GithubReleasesUrl = "https://api.github.com/repos/ingen084/KyoshinEewViewerIngen/releases";
	private const string UpdateCheckUrl = "https://svs.ingen084.net/kyoshineewviewer/updates.json";
	private const string UpdatersCheckUrl = "https://svs.ingen084.net/kyoshineewviewer/updaters.json";

	public UpdateCheckService()
	{
		Logger = LoggingService.CreateLogger(this);
		Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KEVi;" + Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown");
		CheckUpdateTask = new Timer(async s =>
		{
			if (!ConfigurationService.Current.Update.Enable)
				return;

			try
			{
				var currentVersion = Assembly.GetExecutingAssembly()?.GetName().Version;

				// 暫定でリクエストは残しておく
				await Client.GetStringAsync(UpdateCheckUrl);

				// 取得してでかい順に並べる
				var releases = (await GitHubRelease.GetReleasesAsync(Client, GithubReleasesUrl))
					// ドラフトリリースではなく、現在のバージョンより新しく、不安定版が有効
					.Where(r =>
						!r.Draft &&
						Version.TryParse(r.TagName, out var v) && v > currentVersion &&
						(ConfigurationService.Current.Update.UseUnstableBuild || v.Build == 0))
					.OrderByDescending(r => Version.TryParse(r.TagName, out var v) ? v : new Version());

				if (!releases.Any())
				{
					AvailableUpdateVersions = null;
					Updated?.Invoke(null);
					return;
				}
				AvailableUpdateVersions = releases.Select(r => new VersionInfo 
				{
					VersionString = r.TagName + ".0",
					Time = r.CreatedAt,
					Message = r.Body,
				}).ToArray();
				Updated?.Invoke(AvailableUpdateVersions);
			}
			catch (Exception ex)
			{
				Logger.LogWarning("UpdateCheck Error: {ex}", ex);
			}
		}, null, Timeout.Infinite, Timeout.Infinite);
		ConfigurationService.Current.Update.WhenValueChanged(x => x.Enable).Subscribe(x => CheckUpdateTask.Change(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(100)));
	}

	private bool IsUpdating { get; set; }

	[Reactive]
	public bool IsUpdateIndeterminate { get; set; }
	[Reactive]
	public double UpdateProgress { get; set; }
	[Reactive]
	public double UpdateProgressMax { get; set; }
	[Reactive]
	public string UpdateState { get; set; } = "-";

	/// <summary>
	/// アップデーターのプロセスを開始する
	/// </summary>
	public async Task StartUpdater()
	{
		if (IsUpdating)
			return;
		Logger.LogInformation("アップデータのセットアップを開始します");
		UpdateState = "アップデータをダウンロードします";

		IsUpdating = true;
		IsUpdateIndeterminate = true;
		try
		{
			var store = JsonSerializer.Deserialize<Dictionary<string, string>>(await Client.GetStringAsync(UpdatersCheckUrl));
			if (store == null)
				throw new Exception("ストアをパースできません");
			var ri = RuntimeInformation.RuntimeIdentifier;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				ri = "linux-x64";
			if (!store.ContainsKey(ri))
				throw new Exception($"ストアに現在の環境 {ri} がありません");

			IsUpdateIndeterminate = false;
			var tmpFileName = Path.GetTempFileName();
			Logger.LogInformation("アップデータをダウンロードしています: {from} -> {to}", store[ri], tmpFileName);
			// ダウンロード開始
			using (var fileStream = File.OpenWrite(tmpFileName))
			{
				using var response = await Client.GetAsync(store[ri], HttpCompletionOption.ResponseHeadersRead);
				UpdateProgressMax = response.Content.Headers.ContentLength ?? throw new Exception("DLサイズが取得できません");

				using var inputStream = await response.Content.ReadAsStreamAsync();

				var total = 0;
				var buffer = new byte[1024];
				while (true)
				{
					var readed = await inputStream.ReadAsync(buffer);
					if (readed == 0)
						break;

					UpdateProgress = total += readed;
					UpdateState = $"アップデータのダウンロード中: {UpdateProgress / UpdateProgressMax * 100:0.00}%";

					await fileStream.WriteAsync(buffer, 0, readed);
				}
			}

			if (!Directory.Exists("Updater"))
				Directory.CreateDirectory("Updater");

			IsUpdateIndeterminate = true;

			Logger.LogInformation("アップデータを展開しています");
			UpdateState = "アップデータを展開しています";
			await Task.Run(() => ZipFile.ExtractToDirectory(tmpFileName, "Updater", true));
			File.Delete(tmpFileName);

			// Windowsでない場合実行権限を付与
#if LINUX
			new Mono.Unix.UnixFileInfo("Updater/KyoshinEewViewer.Updater").FileAccessPermissions |=
					Mono.Unix.FileAccessPermissions.UserExecute | Mono.Unix.FileAccessPermissions.GroupExecute | Mono.Unix.FileAccessPermissions.OtherExecute;
#endif

			UpdateState = "アップデータを起動しています";

			// 現在の設定を保存
			ConfigurationService.Save();

			await Task.Delay(100);

			// プロセスを起動
			Process.Start(new ProcessStartInfo(Path.Combine("./Updater", "KyoshinEewViewer.Updater")) { WorkingDirectory = "./Updater" });

			await Task.Delay(2000);

			// 自身は終了
			App.MainWindow?.Close();
		}
		catch (Exception ex)
		{
			Logger.LogError("アップデータの起動に失敗しました {ex}", ex);
			UpdateState = "アップデートに失敗しました";
		}
		finally
		{
			IsUpdating = false;
		}
	}

	public void StartUpdateCheckTask()
		=> CheckUpdateTask.Change(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(100));
}

public class JenkinsBuildInformation
{
	[JsonPropertyName("number")]
	public int Number { get; set; }
}
