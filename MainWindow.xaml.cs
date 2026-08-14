using System.Media;
using System.Windows.Media.Imaging;
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
            ClearSourceMessages();

            if (!ValidateSelectedImageFile(
                dialog.FileName,
                "white background image"))
            {
                return;
            }

            WhiteImageTextBox.Text = dialog.FileName;

            string? sourceFolder = Path.GetDirectoryName(dialog.FileName);

            if (!string.IsNullOrWhiteSpace(sourceFolder))
            {
                OutputLocationTextBox.Text = sourceFolder;
            }

            ValidateSourceImages();
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
            ClearSourceMessages();

            if (!ValidateSelectedImageFile(
                dialog.FileName,
                "black background image"))
            {
                return;
            }

            BlackImageTextBox.Text = dialog.FileName;

            ValidateSourceImages();
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Source validation
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed record ImageInfo(
        string FilePath,
        int PixelWidth,
        int PixelHeight,
        bool IsJpeg);

    private static ImageInfo? TryGetImageInfo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);

            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapFrame frame = decoder.Frames[0];

            string extension = Path.GetExtension(filePath);

            bool isJpeg =
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

            return new ImageInfo(
                filePath,
                frame.PixelWidth,
                frame.PixelHeight,
                isJpeg);
        }
        catch
        {
            return null;
        }
    }

    private bool ValidateSelectedImageFile(string filePath, string imageDescription)
    {
        if (!IsSupportedImageFormat(filePath))
        {
            ShowSourceError(
                $"The selected {imageDescription} uses an unsupported file format. " +
                "Supported formats are PNG, TIFF, BMP, and JPEG.");

            SystemSounds.Exclamation.Play();
            return false;
        }

        ImageInfo? imageInfo = TryGetImageInfo(filePath);

        if (imageInfo == null)
        {
            ShowSourceError(
                $"The selected {imageDescription} could not be opened as an image.");

            SystemSounds.Exclamation.Play();
            return false;
        }

        return true;
    }

    private static bool IsSupportedImageFormat(string filePath)
    {
        string extension = Path.GetExtension(filePath);

        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateSourceImages()
    {
        ClearSourceMessages();

        string whiteImagePath = WhiteImageTextBox.Text.Trim();
        string blackImagePath = BlackImageTextBox.Text.Trim();

        ImageInfo? whiteImage = string.IsNullOrWhiteSpace(whiteImagePath)
            ? null
            : TryGetImageInfo(whiteImagePath);

        ImageInfo? blackImage = string.IsNullOrWhiteSpace(blackImagePath)
            ? null
            : TryGetImageInfo(blackImagePath);

        if (whiteImage?.IsJpeg == true || blackImage?.IsJpeg == true)
        {
            ShowSourceWarning(
                "JPEG compression may reduce reconstruction accuracy. " +
                "PNG, TIFF, or BMP is recommended.");
        }

        if (string.IsNullOrWhiteSpace(whiteImagePath) ||
            string.IsNullOrWhiteSpace(blackImagePath))
        {
            return;
        }

        if (string.Equals(
            whiteImagePath,
            blackImagePath,
            StringComparison.OrdinalIgnoreCase))
        {
            ShowSourceError(
                "The white and black source images cannot be the same file.");
            return;
        }

        if (!IsSupportedImageFormat(whiteImagePath))
        {
            ShowSourceError(
                "The selected white background image uses an unsupported file format. " +
                "Supported formats are PNG, TIFF, BMP, and JPEG.");
            return;
        }

        if (!IsSupportedImageFormat(blackImagePath))
        {
            ShowSourceError(
                "The selected black background image uses an unsupported file format. " +
                "Supported formats are PNG, TIFF, BMP, and JPEG.");
            return;
        }

        if (whiteImage == null)
        {
            ShowSourceError(
                "The selected white background file could not be opened as an image.");
            return;
        }

        if (blackImage == null)
        {
            ShowSourceError(
                "The selected black background file could not be opened as an image.");
            return;
        }

        if (whiteImage.PixelWidth != blackImage.PixelWidth ||
            whiteImage.PixelHeight != blackImage.PixelHeight)
        {
            ShowSourceError(
                $"Image dimensions do not match. " +
                $"White: {whiteImage.PixelWidth} × {whiteImage.PixelHeight} · " +
                $"Black: {blackImage.PixelWidth} × {blackImage.PixelHeight}");
            return;
        }
    }

    private void ShowSourceError(string message)
    {
        SourceErrorTextBlock.Text = message;
        SourceErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void ShowSourceWarning(string message)
    {
        SourceWarningTextBlock.Text = message;
        SourceWarningTextBlock.Visibility = Visibility.Visible;
    }

    private void ClearSourceMessages()
    {
        SourceErrorTextBlock.Text = "";
        SourceErrorTextBlock.Visibility = Visibility.Collapsed;

        SourceWarningTextBlock.Text = "";
        SourceWarningTextBlock.Visibility = Visibility.Collapsed;
    }

}