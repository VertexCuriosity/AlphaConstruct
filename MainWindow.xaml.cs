using Microsoft.Win32;
using System.IO;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AlphaConstruct;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        ApplyWindowsTheme();

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Closed += MainWindow_Closed;
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        Dispatcher.Invoke(ApplyWindowsTheme);
    }

    private void ApplyWindowsTheme()
    {
        object? registryValue = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1);

        bool useLightTheme =
            registryValue is int value &&
            value != 0;

        ApplicationTheme theme = useLightTheme
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;

        ApplicationThemeManager.Apply(
            theme,
            WindowBackdropType.None
        );
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // File selection
    // ─────────────────────────────────────────────────────────────────────────────

    private const string ImageFileFilter =
        "Supported image files|*.png;*.tif;*.tiff;*.bmp;*.jpg;*.jpeg|" +
        "PNG files|*.png|" +
        "TIFF files|*.tif;*.tiff|" +
        "BMP files|*.bmp|" +
        "JPEG files|*.jpg;*.jpeg|" +
        "All files|*.*";

    private void WhiteImageBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Choose white background image",
            Filter = ImageFileFilter
        };

        SetInitialImageDirectory(dialog, WhiteImageTextBox.Text, BlackImageTextBox.Text);

        if (dialog.ShowDialog() == true)
        {
            WhiteImageTextBox.Text = dialog.FileName;

            string? sourceFolder = Path.GetDirectoryName(dialog.FileName);

            if (!string.IsNullOrWhiteSpace(sourceFolder))
            {
                OutputLocationTextBox.Text = sourceFolder;
            }
        }
    }

    private void BlackImageBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Choose black background image",
            Filter = ImageFileFilter
        };

        SetInitialImageDirectory(dialog, BlackImageTextBox.Text, WhiteImageTextBox.Text);

        if (dialog.ShowDialog() == true)
        {
            BlackImageTextBox.Text = dialog.FileName;
        }
    }

    private void OutputBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose output folder"
        };

        string currentOutputFolder = OutputLocationTextBox.Text;

        if (!string.IsNullOrWhiteSpace(currentOutputFolder) &&
            Directory.Exists(currentOutputFolder))
        {
            dialog.InitialDirectory = currentOutputFolder;
        }
        else if (!string.IsNullOrWhiteSpace(WhiteImageTextBox.Text))
        {
            string? whiteImageFolder = Path.GetDirectoryName(WhiteImageTextBox.Text);

            if (!string.IsNullOrWhiteSpace(whiteImageFolder) &&
                Directory.Exists(whiteImageFolder))
            {
                dialog.InitialDirectory = whiteImageFolder;
            }
        }

        if (dialog.ShowDialog() == true)
        {
            OutputLocationTextBox.Text = dialog.FolderName;
        }
    }

    private static void SetInitialImageDirectory(
        OpenFileDialog dialog,
        string preferredImagePath,
        string fallbackImagePath)
    {
        string? preferredFolder = Path.GetDirectoryName(preferredImagePath);

        if (!string.IsNullOrWhiteSpace(preferredFolder) &&
            Directory.Exists(preferredFolder))
        {
            dialog.InitialDirectory = preferredFolder;
            return;
        }

        string? fallbackFolder = Path.GetDirectoryName(fallbackImagePath);

        if (!string.IsNullOrWhiteSpace(fallbackFolder) &&
            Directory.Exists(fallbackFolder))
        {
            dialog.InitialDirectory = fallbackFolder;
        }
    }

}