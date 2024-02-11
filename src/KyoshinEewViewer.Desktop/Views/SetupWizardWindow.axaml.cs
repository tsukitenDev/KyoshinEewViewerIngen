using Avalonia.Controls;
using System;

namespace KyoshinEewViewer.Desktop.Views;
public partial class SetupWizardWindow : Window
{
	public event Action? Continued;

	private int Index { get; set; }
	private UserControl[] Pages { get; } = [
		new KyoshinEewViewer.Views.SetupWizardPages.WelcomePage(),
		new KyoshinEewViewer.Views.SetupWizardPages.SelectThemePage(),
		new KyoshinEewViewer.Views.SetupWizardPages.SelectSeriesPage(),
		new KyoshinEewViewer.Views.SetupWizardPages.DmdataPromotion(),
		new KyoshinEewViewer.Views.SetupWizardPages.EpiloguePage(),
	];

	public SetupWizardWindow()
	{
		InitializeComponent();

		SkipButton.Tapped += (s, e) => Continued?.Invoke();
		BeforeButton.Tapped += (s, e) => { Index--; UpdatePage(); };
		NextButton.Tapped += (s, e) => { Index++; UpdatePage(); };
		UpdatePage();
	}

	private void UpdatePage()
	{
		if (Index == 0)
		{
			BeforeButton.IsEnabled = false;
			NextButton.IsEnabled = true;
			SkipButtonText.Text = Properties.Resources.SetupWizardSkipAndRun;
		}
		else if (Index >= Pages.Length - 1)
		{
			BeforeButton.IsEnabled = true;
			NextButton.IsEnabled = false;
			SkipButtonText.Text = Properties.Resources.SetupWizardRun;
		}
		else
		{
			BeforeButton.IsEnabled = true;
			NextButton.IsEnabled = true;
			SkipButtonText.Text = Properties.Resources.SetupWizardSkipAndRun;
		}
		SkipButtonText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
		ContentControl.Content = Pages[Index];
		PageGuide.Text = $"{Index + 1}/{Pages.Length}";
	}
}