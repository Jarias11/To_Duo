// Views/PromptTextWindow.xaml.cs
using System.Windows;
using TaskMate.Models;
using TaskMate.Services;
namespace TaskMate.Views;

public partial class PromptTextWindow : Window {
	public string? Result { get; private set; }
	public string? Placeholder { get; }

	public PromptTextWindow(string title, string message, string? placeholder) {
		InitializeComponent();
		Title = title;
		MessageText.Text = message;
		Placeholder = placeholder;            // <-- bind to watermark TextBlock
		Loaded += (_, __) => {
			InputBox.Focus();
			InputBox.SelectAll();             // if you later prefill, this selects it
		};
	}

	private void Ok_Click(object sender, RoutedEventArgs e) {
		Result = string.IsNullOrWhiteSpace(InputBox.Text) ? null : InputBox.Text.Trim();
		DialogResult = true;                  // closes the window
	}

	private void Cancel_Click(object sender, RoutedEventArgs e) {
		Result = null;
		DialogResult = false;
	}
}