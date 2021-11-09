using Avalonia;
using Avalonia.ReactiveUI;

namespace CustomRenderItemTest;

internal class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	public static void Main(string[] args) => BuildAvaloniaApp()
		.StartWithClassicDesktopLifetime(args);

	// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.LogToTrace()
		.UseSkia()
		.UseReactiveUI()
		.With(new Win32PlatformOptions
		{
			EnableMultitouch = true,
			AllowEglInitialization = true
		});
}
