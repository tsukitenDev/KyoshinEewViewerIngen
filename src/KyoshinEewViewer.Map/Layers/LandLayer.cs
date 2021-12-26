﻿using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using KyoshinEewViewer.Map.Data;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace KyoshinEewViewer.Map.Layers;

public sealed class LandLayer : MapLayer
{
	public override bool NeedPersistentUpdate => false;

	/// <summary>
	/// 優先して描画するレイヤー
	/// </summary>
	//public LandLayerType PrimaryRenderLayer { get; set; } = LandLayerType.PrimarySubdivisionArea;
	public Dictionary<LandLayerType, Dictionary<int, SKColor>>? CustomColorMap { get; set; }

	private MapData? map;
	public MapData? Map
	{
		get => map;
		set {
			map = value;
			RefleshRequest();
		}
	}

	#region ResourceCache
	private SKPaint CoastlineStroke { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Stroke,
		Color = SKColors.Green,
		StrokeWidth = 1,
		IsAntialias = true,
	};
	private float CoastlineStrokeWidth { get; set; } = 1;
	private SKPaint PrefStroke { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Stroke,
		Color = SKColors.Green,
		StrokeWidth = .8f,
		IsAntialias = true,
	};
	private float PrefStrokeWidth { get; set; } = .8f;
	private SKPaint AreaStroke { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Stroke,
		Color = SKColors.Green,
		StrokeWidth = .4f,
		IsAntialias = true,
	};
	private float AreaStrokeWidth { get; set; } = .4f;

	private SKPaint LandFill { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Fill,
		Color = new SKColor(242, 239, 233),
	};
	//private SKPaint SpecialLandFill { get; set; } = new SKPaint
	//{
	//	Style = SKPaintStyle.Fill,
	//	Color = new SKColor(242, 239, 233),
	//};
	private SKPaint OverSeasLandFill { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Fill,
		Color = new SKColor(169, 169, 169),
	};

	private bool InvalidateLandStroke => CoastlineStrokeWidth <= 0;
	private bool InvalidatePrefStroke => PrefStrokeWidth <= 0;
	private bool InvalidateAreaStroke => AreaStrokeWidth <= 0;

	public override void RefreshResourceCache(Control targetControl)
	{
		SKColor FindColorResource(string name)
			=> ((Color)(targetControl.FindResource(name) ?? throw new Exception($"マップリソース {name} が見つかりませんでした"))).ToSKColor();
		float FindFloatResource(string name)
			=> (float)(targetControl.FindResource(name) ?? throw new Exception($"マップリソース {name} が見つかりませんでした"));

		CoastlineStroke = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			Color = FindColorResource("LandStrokeColor"),
			StrokeWidth = FindFloatResource("LandStrokeThickness"),
			IsAntialias = true,
		};
		CoastlineStrokeWidth = CoastlineStroke.StrokeWidth;

		PrefStroke = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			Color = FindColorResource("PrefStrokeColor"),
			StrokeWidth = FindFloatResource("PrefStrokeThickness"),
			IsAntialias = true,
		};
		PrefStrokeWidth = PrefStroke.StrokeWidth;

		AreaStroke = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			Color = FindColorResource("AreaStrokeColor"),
			StrokeWidth = FindFloatResource("AreaStrokeThickness"),
			IsAntialias = true,
		};
		AreaStrokeWidth = AreaStroke.StrokeWidth;

		LandFill = new SKPaint
		{
			Style = SKPaintStyle.Fill,
			Color = FindColorResource("LandColor"),
			IsAntialias = false,
		};

		//SpecialLandFill = new SKPaint
		//{
		//	Style = SKPaintStyle.Stroke,
		//	Color = SKColors.Red,
		//	MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3),
		//	StrokeWidth = 5,
		//	IsAntialias = true,
		//};

		OverSeasLandFill = new SKPaint
		{
			Style = SKPaintStyle.Fill,
			Color = FindColorResource("OverseasLandColor"),
			IsAntialias = false,
		};
	}
	#endregion

	// 線を描画する
	public void RenderLines(SKCanvas canvas)
	{
		// マップの初期化ができていなければスキップ
		if (Map == null)
			return;
		canvas.Save();

		try
		{
			// 使用するキャッシュのズーム
			var baseZoom = (int)Math.Ceiling(Zoom);
			// 実際のズームに合わせるためのスケール
			var scale = Math.Pow(2, Zoom - baseZoom);
			canvas.Scale((float)scale);
			// 画面座標への変換
			var leftTop = LeftTopLocation.CastLocation().ToPixel(baseZoom);
			canvas.Translate((float)-leftTop.X, (float)-leftTop.Y);

			// 使用するレイヤー決定
			var useLayerType = LandLayerType.EarthquakeInformationSubdivisionArea;
			if (baseZoom > 10)
				useLayerType = LandLayerType.MunicipalityEarthquakeTsunamiArea;

			// スケールに合わせてブラシのサイズ変更
			CoastlineStroke.StrokeWidth = (float)(CoastlineStrokeWidth / scale);
			PrefStroke.StrokeWidth = (float)(PrefStrokeWidth / scale);
			AreaStroke.StrokeWidth = (float)(AreaStrokeWidth / scale);

			if (!Map.TryGetLayer(useLayerType, out var layer))
				return;

			RenderRect(ViewAreaRect);
			// 左右に途切れないように補完して描画させる
			if (ViewAreaRect.Bottom > 180)
			{
				canvas.Translate((float)new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);

				var fixedRect = ViewAreaRect;
				fixedRect.Y -= 360;

				RenderRect(fixedRect);
			}
			else if (ViewAreaRect.Top < -180)
			{
				canvas.Translate(-(float)new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);

				var fixedRect = ViewAreaRect;
				fixedRect.Y += 360;

				RenderRect(fixedRect);
			}

			void RenderRect(RectD subViewArea)
			{
				foreach (var f in layer.FindLine(subViewArea))
				{
					switch (f.Type)
					{
						case FeatureType.AdminBoundary:
							if (!InvalidatePrefStroke && baseZoom > 4.5)
								f.Draw(canvas, baseZoom, PrefStroke);
							break;
						case FeatureType.Coastline:
							if (!InvalidateLandStroke && baseZoom > 4.5)
								f.Draw(canvas, baseZoom, CoastlineStroke);
							break;
						case FeatureType.AreaBoundary:
							if (!InvalidateAreaStroke && baseZoom > 4.5)
								f.Draw(canvas, baseZoom, AreaStroke);
							break;
					}
				}
			}
		}
		finally
		{
			canvas.Restore();
		}
	}
	public override void Render(SKCanvas canvas, bool isAnimating)
	{
		// コントローラーの初期化ができていなければスキップ
		if (Map == null)
			return;
		canvas.Save();

		try
		{
			// 使用するキャッシュのズーム
			var baseZoom = (int)Math.Ceiling(Zoom);
			// 実際のズームに合わせるためのスケール
			var scale = Math.Pow(2, Zoom - baseZoom);
			canvas.Scale((float)scale);
			// 画面座標への変換
			var leftTop = LeftTopLocation.CastLocation().ToPixel(baseZoom);
			canvas.Translate((float)-leftTop.X, (float)-leftTop.Y);

			// 使用するレイヤー決定
			var useLayerType = LandLayerType.EarthquakeInformationSubdivisionArea;
			if (baseZoom > 10)
				useLayerType = LandLayerType.MunicipalityEarthquakeTsunamiArea;

			// スケールに合わせてブラシのサイズ変更
			//CoastlineStroke.StrokeWidth = (float)(CoastlineStrokeWidth / scale);
			//PrefStroke.StrokeWidth = (float)(PrefStrokeWidth / scale);
			//AreaStroke.StrokeWidth = (float)(AreaStrokeWidth / scale);
			//SpecialLandFill.StrokeWidth = (float)(Zoom / scale);
			//SpecialLandFill.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(3f / scale));

			if (!Map.TryGetLayer(useLayerType, out var layer))
				return;

			RenderRect(ViewAreaRect);
			// 左右に途切れないように補完して描画させる
			if (ViewAreaRect.Bottom > 180)
			{
				canvas.Translate((float)new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);

				var fixedRect = ViewAreaRect;
				fixedRect.Y -= 360;

				RenderRect(fixedRect);
			}
			else if (ViewAreaRect.Top < -180)
			{
				canvas.Translate(-(float)new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);

				var fixedRect = ViewAreaRect;
				fixedRect.Y += 360;

				RenderRect(fixedRect);
			}

			void RenderRect(RectD subViewArea)
			{
				// とりあえず海外の描画を行う
				RenderOverseas(canvas, baseZoom, subViewArea);

				foreach (var f in layer.FindPolygon(subViewArea))
				{
					if (CustomColorMap != null &&
						CustomColorMap.TryGetValue(useLayerType, out var map) &&
						map.TryGetValue(f.Code ?? -1, out var color))
					{
						var oc = LandFill.Color;
						LandFill.Color = color;
						f.Draw(canvas, baseZoom, LandFill);
						LandFill.Color = oc;
					}
					else
						f.Draw(canvas, baseZoom, LandFill);

					//if (f.Code == 270000)
					//{
					//	var path = f.GetOrCreatePath(Projection, baseZoom);

					//	canvas.Save();
					//	canvas.ClipPath(path);
					//	canvas.DrawPath(path, SpecialLandFill);
					//	canvas.Restore();
					//}
				}

				if (CustomColorMap != null)
					foreach (var cLayerType in CustomColorMap.Keys)
						if (cLayerType != useLayerType && Map.TryGetLayer(cLayerType, out var clayer))
							foreach (var f in clayer.FindPolygon(subViewArea))
								if (CustomColorMap[cLayerType].TryGetValue(f.Code ?? -1, out var color))
								{
									var oc = LandFill.Color;
									LandFill.Color = color;
									f.Draw(canvas, baseZoom, LandFill);
									LandFill.Color = oc;
								}

				//foreach (var f in layer.FindLine(subViewArea))
				//{
				//	switch (f.Type)
				//	{
				//		case FeatureType.AdminBoundary:
				//			if (!InvalidatePrefStroke && baseZoom > 4.5)
				//				f.Draw(canvas, Projection, baseZoom, PrefStroke);
				//			break;
				//		case FeatureType.Coastline:
				//			if (!InvalidateLandStroke && baseZoom > 4.5)
				//				f.Draw(canvas, Projection, baseZoom, CoastlineStroke);
				//			break;
				//		case FeatureType.AreaBoundary:
				//			if (!InvalidateAreaStroke && baseZoom > 4.5)
				//				f.Draw(canvas, Projection, baseZoom, AreaStroke);
				//			break;
				//	}
				//}
			}
		}
		finally
		{
			canvas.Restore();
		}
	}
	/// <summary>
	/// 海外を描画する
	/// </summary>
	private void RenderOverseas(SKCanvas canvas, int baseZoom, RectD subViewArea)
	{
		if (!(Map?.TryGetLayer(LandLayerType.WorldWithoutJapan, out var layer) ?? false))
			return;

		foreach (var f in layer.FindPolygon(subViewArea))
			f.Draw(canvas, baseZoom, OverSeasLandFill);
	}
}
