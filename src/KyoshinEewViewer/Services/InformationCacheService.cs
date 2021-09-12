﻿using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services
{
	public class InformationCacheService
	{
		private static InformationCacheService? _default;
		public static InformationCacheService Default => _default ??= new InformationCacheService();

		private ILogger Logger { get; }
		private SHA256CryptoServiceProvider SHA256 { get; } = new();

		private static readonly string ShortCachePath = Path.Join(Path.GetTempPath(), "KyoshinEewViewerIngen", "ShortCache");
		private static readonly string LongCachePath = Path.Join(Path.GetTempPath(), "KyoshinEewViewerIngen", "LongCache");

		public InformationCacheService()
		{
			Logger = LoggingService.CreateLogger(this);

			if (!Directory.Exists(ShortCachePath))
				Directory.CreateDirectory(ShortCachePath);
			if (!Directory.Exists(LongCachePath))
				Directory.CreateDirectory(LongCachePath);

			// 昔のキャッシュファイルが存在すれば消す
			if (File.Exists("cache.db"))
				File.Delete("cache.db");

			CleanupCaches();
		}

		private string GetLongCacheFileName(string baseName)
			=> Path.Join(LongCachePath, new(SHA256.ComputeHash(Encoding.UTF8.GetBytes(baseName)).SelectMany(x => x.ToString("x2")).ToArray()));
		private string GetShortCacheFileName(string baseName)
			=> Path.Join(ShortCachePath, new(SHA256.ComputeHash(Encoding.UTF8.GetBytes(baseName)).SelectMany(x => x.ToString("x2")).ToArray()));

		/// <summary>
		/// Keyを元にキャッシュされたstreamを取得する
		/// </summary>
		public bool TryGetTelegram(string key, out Stream stream)
		{
			var path = GetLongCacheFileName(key);
			if (!File.Exists(path))
			{
#pragma warning disable CS8625 // falseなので普通にnullを代入する
				stream = null;
				return false;
#pragma warning restore CS8625
			}
			var fileStream = File.OpenRead(path);
			stream = new GZipStream(fileStream, CompressionMode.Decompress);
			return true;
		}

		public async Task<Stream> TryGetOrFetchTelegramAsync(string key, Func<Task<Stream>> fetcher)
		{
			if (TryGetTelegram(key, out var stream))
				return stream;

			stream = new MemoryStream();
			using (var body = await fetcher())
				await body.CopyToAsync(stream);

			stream.Seek(0, SeekOrigin.Begin);
			using var fileStream = File.OpenWrite(GetLongCacheFileName(key));
			await CompressStreamAsync(stream, fileStream);

			stream.Seek(0, SeekOrigin.Begin);
			return stream;
		}
		public void DeleteTelegramCache(string key)
		{
			var path = GetLongCacheFileName(key);
			if (File.Exists(path))
				File.Delete(path);
		}

		/// <summary>
		/// URLを元にキャッシュされたstreamを取得する
		/// </summary>
		public bool TryGetImage(string url, out SKBitmap bitmap)
		{
			var path = GetShortCacheFileName(url);
			if (!File.Exists(path))
			{
#pragma warning disable CS8625 // falseなので普通にnullを代入する
				bitmap = null;
				return false;
#pragma warning restore CS8625
			}
			bitmap = SKBitmap.Decode(path);
			return true;
		}
		public async Task<SKBitmap> TryGetOrFetchImageAsync(string url, Func<Task<(SKBitmap, DateTime)>> fetcher)
		{
			if (TryGetImage(url, out var bitmap))
				return bitmap;

			var res = await fetcher();
			bitmap = res.Item1;

			using var stream = File.OpenWrite(GetShortCacheFileName(url));
			bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);

			return bitmap;
		}

		/// <summary>
		/// URLを元にキャッシュされたstreamを取得する
		/// </summary>
		public bool TryGetImageAsStream(string url, out Stream stream)
		{
			var path = GetShortCacheFileName(url);
			if (!File.Exists(path))
			{
#pragma warning disable CS8625 // falseなので普通にnullを代入する
				stream = null;
				return false;
#pragma warning restore CS8625
			}
			var fileStream = File.OpenRead(path);
			stream = new GZipStream(fileStream, CompressionMode.Decompress);
			return true;
		}

		public async Task<Stream> TryGetOrFetchImageAsStreamAsync(string url, Func<Task<(Stream, DateTime)>> fetcher)
		{
			if (TryGetImageAsStream(url, out var stream))
				return stream;

			stream = new MemoryStream();
			var resp = await fetcher();
			using (resp.Item1)
				await resp.Item1.CopyToAsync(stream);

			stream.Seek(0, SeekOrigin.Begin);
			using var fileStream = File.OpenWrite(GetShortCacheFileName(url));
			await CompressStreamAsync(stream, fileStream);

			stream.Seek(0, SeekOrigin.Begin);
			return stream;
		}
		public void DeleteImageCache(string url)
		{
			var path = GetShortCacheFileName(url);
			if (File.Exists(path))
				File.Delete(path);
		}

		private static async Task CompressStreamAsync(Stream input, Stream output)
		{
			using var compressStream = new GZipStream(output, CompressionLevel.Optimal);
			await input.CopyToAsync(compressStream);
		}

		public void CleanupCaches()
		{
			CleanupTelegramCache();
			CleanupImageCache();
		}
		private void CleanupTelegramCache()
		{
			Logger.LogDebug("telegram cache cleaning...");
			var s = DateTime.Now;
			// 2週間以上経過したものを削除
			foreach (var file in Directory.GetFiles(LongCachePath))
			{
				try
				{
					if (File.GetCreationTimeUtc(file) <= DateTime.UtcNow.AddDays(-14))
						File.Delete(file);
				}
				catch (IOException) { }
			}
			Logger.LogDebug($"telegram cache cleaning completed: {(DateTime.Now - s).TotalMilliseconds}ms");
		}
		private void CleanupImageCache()
		{
			Logger.LogDebug("image cache cleaning...");
			var s = DateTime.Now;
			// 3時間以上経過したものを削除
			foreach (var file in Directory.GetFiles(ShortCachePath))
			{
				try
				{
					if (File.GetCreationTimeUtc(file) <= DateTime.UtcNow.AddHours(-3))
						File.Delete(file);
				}
				catch (IOException) { }
			}
			Logger.LogDebug($"image cache cleaning completed: {(DateTime.Now - s).TotalMilliseconds}ms");
		}
	}

	public record TelegramCacheModel(string Key, string Title, DateTime ArrivalTime, byte[] Body);
	public record ImageCacheModel(string Url, DateTime ExpireTime, byte[] Body);
}
