using System.IO;
using System.Windows;
using FinanceApp.Presentation.Settings;
using Microsoft.Win32;

namespace FinanceApp.Desktop;

/// <summary>«Настройки». Only the file dialog lives here; everything else is in <see cref="SettingsViewModel"/>.</summary>
internal sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Interop.DarkTitleBar.Apply(this);
        viewModel.Load();
    }

    public SettingsViewModel ViewModel { get; }

    private void OnBrowseExport(object sender, RoutedEventArgs e)
    {
        var current = ViewModel.ExportPath.Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Файл для экспорта операций",
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = current.Length > 0 ? Path.GetFileName(current) : "transactions.csv",
            OverwritePrompt = false,
        };
        if (current.Length > 0 && Path.IsPathFullyQualified(current) && Directory.Exists(Path.GetDirectoryName(current)))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ExportPath = dialog.FileName;
        }
    }
}
