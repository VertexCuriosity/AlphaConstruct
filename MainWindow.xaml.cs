using Microsoft.Win32;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AlphaConstruct;

public partial class MainWindow : FluentWindow
{
    private enum ReconstructionChoice
    {
        None,
        Linear,
        Srgb
    }

    private enum DialogIcon
    {
        Info,
        Success,
        Warning,
        Error,
        Question
    }

    private BitmapSource? _linearResult;
    private BitmapSource? _srgbResult;
    private bool _sourcePairIsValid;
    private double _previewSplitRatio = 0.5;
    private bool _isDraggingPreviewDivider;
    private ReconstructionChoice _selectedReconstruction = ReconstructionChoice.None;

    public MainWindow()
    {
        InitializeComponent();

        PreviewBorder.SizeChanged += PreviewBorder_SizeChanged;

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
    // Source file selection
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

            OutputImageNameTextBox.Text =
                CreateDefaultOutputName(dialog.FileName);

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

        _sourcePairIsValid = false;

        _linearResult = null;
        _srgbResult = null;

        ResetOutputSelection();
        UpdatePreview();

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

        _sourcePairIsValid = true;

        GenerateReconstructionResults();
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Image reconstruction
    // ─────────────────────────────────────────────────────────────────────────────

    private static BitmapSource LoadBitmapAsBgra32(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);

        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        BitmapFrame frame = decoder.Frames[0];

        BitmapSource source = frame;

        if (source.Format != PixelFormats.Bgra32)
        {
            source = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                null,
                0);
        }

        source.Freeze();

        return source;
    }

    private static BitmapSource ReconstructSrgb(
    BitmapSource whiteImage,
    BitmapSource blackImage)
    {
        int width = whiteImage.PixelWidth;
        int height = whiteImage.PixelHeight;
        int stride = width * 4;

        byte[] whitePixels = new byte[stride * height];
        byte[] blackPixels = new byte[stride * height];
        byte[] resultPixels = new byte[stride * height];

        whiteImage.CopyPixels(whitePixels, stride, 0);
        blackImage.CopyPixels(blackPixels, stride, 0);

        for (int i = 0; i < resultPixels.Length; i += 4)
        {
            double blackB = blackPixels[i] / 255.0;
            double blackG = blackPixels[i + 1] / 255.0;
            double blackR = blackPixels[i + 2] / 255.0;

            double whiteB = whitePixels[i] / 255.0;
            double whiteG = whitePixels[i + 1] / 255.0;
            double whiteR = whitePixels[i + 2] / 255.0;

            double alphaB = 1.0 - whiteB + blackB;
            double alphaG = 1.0 - whiteG + blackG;
            double alphaR = 1.0 - whiteR + blackR;

            double alpha = Math.Clamp(
                (alphaR + alphaG + alphaB) / 3.0,
                0.0,
                1.0);

            double resultB = alpha > 0.0
                ? blackB / alpha
                : 0.0;

            double resultG = alpha > 0.0
                ? blackG / alpha
                : 0.0;

            double resultR = alpha > 0.0
                ? blackR / alpha
                : 0.0;

            resultPixels[i] = ToByte(resultB);
            resultPixels[i + 1] = ToByte(resultG);
            resultPixels[i + 2] = ToByte(resultR);
            resultPixels[i + 3] = ToByte(alpha);
        }

        BitmapSource result = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            resultPixels,
            stride);

        result.Freeze();

        return result;
    }

    private static BitmapSource ReconstructLinear(
    BitmapSource whiteImage,
    BitmapSource blackImage)
    {
        int width = whiteImage.PixelWidth;
        int height = whiteImage.PixelHeight;
        int stride = width * 4;

        byte[] whitePixels = new byte[stride * height];
        byte[] blackPixels = new byte[stride * height];
        byte[] resultPixels = new byte[stride * height];

        whiteImage.CopyPixels(whitePixels, stride, 0);
        blackImage.CopyPixels(blackPixels, stride, 0);

        for (int i = 0; i < resultPixels.Length; i += 4)
        {
            double blackB = SrgbToLinear(blackPixels[i] / 255.0);
            double blackG = SrgbToLinear(blackPixels[i + 1] / 255.0);
            double blackR = SrgbToLinear(blackPixels[i + 2] / 255.0);

            double whiteB = SrgbToLinear(whitePixels[i] / 255.0);
            double whiteG = SrgbToLinear(whitePixels[i + 1] / 255.0);
            double whiteR = SrgbToLinear(whitePixels[i + 2] / 255.0);

            double alphaB = 1.0 - whiteB + blackB;
            double alphaG = 1.0 - whiteG + blackG;
            double alphaR = 1.0 - whiteR + blackR;

            double alpha = Math.Clamp(
                (alphaR + alphaG + alphaB) / 3.0,
                0.0,
                1.0);

            double resultBLinear = alpha > 0.000001
                ? blackB / alpha
                : 0.0;

            double resultGLinear = alpha > 0.000001
                ? blackG / alpha
                : 0.0;

            double resultRLinear = alpha > 0.000001
                ? blackR / alpha
                : 0.0;

            double resultB = LinearToSrgb(resultBLinear);
            double resultG = LinearToSrgb(resultGLinear);
            double resultR = LinearToSrgb(resultRLinear);

            resultPixels[i] = ToByte(resultB);
            resultPixels[i + 1] = ToByte(resultG);
            resultPixels[i + 2] = ToByte(resultR);
            resultPixels[i + 3] = ToByte(alpha);
        }

        BitmapSource result = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            resultPixels,
            stride);

        result.Freeze();

        return result;
    }

    private static double SrgbToLinear(double value)
    {
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double LinearToSrgb(double value)
    {
        value = Math.Clamp(value, 0.0, 1.0);

        return value <= 0.0031308
            ? value * 12.92
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(
            Math.Clamp(value, 0.0, 1.0) * 255.0);
    }

    private void GenerateReconstructionResults()
    {
        _linearResult = null;
        _srgbResult = null;

        if (!_sourcePairIsValid)
        {
            return;
        }

        try
        {
            BitmapSource whiteImage =
                LoadBitmapAsBgra32(WhiteImageTextBox.Text.Trim());

            BitmapSource blackImage =
                LoadBitmapAsBgra32(BlackImageTextBox.Text.Trim());

            _srgbResult = ReconstructSrgb(
                whiteImage,
                blackImage);

            _linearResult = ReconstructLinear(
                whiteImage,
                blackImage);

            UpdatePreview();
        }
        catch (Exception ex)
        {
            _sourcePairIsValid = false;
            _linearResult = null;
            _srgbResult = null;

            UpdatePreview();

            ShowSourceError(
                $"The source images could not be reconstructed: {ex.Message}");

            SystemSounds.Exclamation.Play();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Preview
    // ─────────────────────────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        if (_linearResult == null || _srgbResult == null)
        {
            LinearPreviewImage.Source = null;
            SrgbPreviewImage.Source = null;

            LinearPreviewImage.Visibility = Visibility.Collapsed;
            SrgbPreviewClipGrid.Visibility = Visibility.Collapsed;
            PreviewDividerHitArea.Visibility = Visibility.Collapsed;
            LinearPreviewLabel.Visibility = Visibility.Collapsed;
            SrgbPreviewLabel.Visibility = Visibility.Collapsed;

            PreviewPlaceholderText.Visibility = Visibility.Visible;

            UseLinearButton.IsEnabled = false;
            UseSrgbButton.IsEnabled = false;

            return;
        }

        LinearPreviewImage.Source = _linearResult;
        SrgbPreviewImage.Source = _srgbResult;

        LinearPreviewImage.Visibility = Visibility.Visible;
        SrgbPreviewClipGrid.Visibility = Visibility.Visible;

        PreviewDividerHitArea.Visibility = Visibility.Visible;
        LinearPreviewLabel.Visibility = Visibility.Visible;
        SrgbPreviewLabel.Visibility = Visibility.Visible;

        PreviewPlaceholderText.Visibility = Visibility.Collapsed;

        UseLinearButton.IsEnabled = true;
        UseSrgbButton.IsEnabled = true;

        UpdatePreviewSize();
        UpdatePreviewSplit();
    }

    private void PreviewBorder_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            UpdatePreviewSize();
            UpdatePreviewSplit();
        }
    }

    private void UpdatePreviewSize()
    {
        if (_linearResult == null)
        {
            return;
        }

        double previewWidth = PreviewBorder.ActualWidth;

        if (previewWidth <= 0)
        {
            return;
        }

        double aspectRatio =
            (double)_linearResult.PixelHeight /
            _linearResult.PixelWidth;

        PreviewBorder.Height = previewWidth * aspectRatio;
    }

    private void UpdatePreviewSplit()
    {
        if (_linearResult == null || _srgbResult == null)
        {
            return;
        }

        double previewWidth = PreviewBorder.ActualWidth;
        double previewHeight = PreviewBorder.ActualHeight;

        if (previewWidth <= 0 || previewHeight <= 0)
        {
            return;
        }

        double splitPosition = previewWidth * _previewSplitRatio;

        SrgbPreviewClipGrid.Clip = new RectangleGeometry(
            new Rect(
                splitPosition,
                0,
                previewWidth - splitPosition,
                previewHeight));

        PreviewDivider.HorizontalAlignment = HorizontalAlignment.Left;
        PreviewDividerHitArea.Margin = new Thickness(
            splitPosition - PreviewDividerHitArea.Width / 2.0,
            0,
            0,
            0);
    }

    private void PreviewDivider_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
    {
        _isDraggingPreviewDivider = true;

        PreviewDividerHitArea.CaptureMouse();

        e.Handled = true;
    }

    private void PreviewDivider_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isDraggingPreviewDivider)
        {
            return;
        }

        double previewWidth = PreviewBorder.ActualWidth;

        if (previewWidth <= 0)
        {
            return;
        }

        Point mousePosition = e.GetPosition(PreviewBorder);

        _previewSplitRatio = Math.Clamp(
            mousePosition.X / previewWidth,
            0.0,
            1.0);

        UpdatePreviewSplit();
    }

    private void PreviewDivider_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDraggingPreviewDivider)
        {
            return;
        }

        _isDraggingPreviewDivider = false;

        PreviewDividerHitArea.ReleaseMouseCapture();

        e.Handled = true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Output selection
    // ─────────────────────────────────────────────────────────────────────────────

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

            ValidateOutputLocation();
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

    private static string CreateDefaultOutputName(string sourceFilePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);

        string[] whiteSuffixes =
        {
        "_White",
        "-White",
        " White"
    };

        foreach (string suffix in whiteSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^suffix.Length] + "_Transparent";
            }
        }

        return fileName + "_Transparent";
    }

    private void UseLinearButton_Click(object sender, RoutedEventArgs e)
    {
        SelectReconstruction(ReconstructionChoice.Linear);
    }

    private void UseSrgbButton_Click(object sender, RoutedEventArgs e)
    {
        SelectReconstruction(ReconstructionChoice.Srgb);
    }

    private void SelectReconstruction(ReconstructionChoice choice)
    {
        _selectedReconstruction = choice;

        UseLinearButton.Appearance =
            choice == ReconstructionChoice.Linear
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;

        UseSrgbButton.Appearance =
            choice == ReconstructionChoice.Srgb
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;

        CreateImageButton.IsEnabled =
            choice != ReconstructionChoice.None;
    }

    private void ResetOutputSelection()
    {
        _selectedReconstruction = ReconstructionChoice.None;

        UseLinearButton.Appearance = ControlAppearance.Secondary;
        UseSrgbButton.Appearance = ControlAppearance.Secondary;

        CreateImageButton.IsEnabled = false;
    }

    private void ShowOutputError(string message)
    {
        OutputErrorTextBlock.Text = message;
        OutputErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void OutputLocationTextBox_LostFocus(
    object sender,
    RoutedEventArgs e)
    {
        ValidateOutputLocation();
    }

    private bool ValidateOutputLocation()
    {
        string outputFolder = OutputLocationTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(outputFolder) ||
            !Directory.Exists(outputFolder))
        {
            ShowOutputError("Please choose a valid output location.");
            SystemSounds.Exclamation.Play();
            return false;
        }

        ClearOutputError();
        return true;
    }

    private void ClearOutputError()
    {
        OutputErrorTextBlock.Text = "";
        OutputErrorTextBlock.Visibility = Visibility.Collapsed;
    }


    // ─────────────────────────────────────────────────────────────────────────────
    // Image export
    // ─────────────────────────────────────────────────────────────────────────────

    private async void CreateImageButton_Click(object sender, RoutedEventArgs e)
    {
        ClearOutputError();

        BitmapSource? selectedImage = _selectedReconstruction switch
        {
            ReconstructionChoice.Linear => _linearResult,
            ReconstructionChoice.Srgb => _srgbResult,
            _ => null
        };

        if (selectedImage == null)
        {
            ShowSourceError("No reconstructed image is available to export.");
            SystemSounds.Exclamation.Play();
            return;
        }

        string outputFolder = OutputLocationTextBox.Text.Trim();
        string outputName = OutputImageNameTextBox.Text.Trim();

        if (!ValidateOutputLocation())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(outputName))
        {
            outputName = CreateDefaultOutputName(WhiteImageTextBox.Text.Trim());
            OutputImageNameTextBox.Text = outputName;
        }

        if (!outputName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            outputName += ".png";
        }

        string outputPath = Path.Combine(outputFolder, outputName);

        if (File.Exists(outputPath))
        {
            bool overwrite = ShowModernYesNo(
                "File already exists",
                $"The file already exists:\n\n{outputPath}\n\nDo you want to overwrite it?",
                yes: "Overwrite",
                no: "Cancel",
                icon: DialogIcon.Warning);

            if (!overwrite)
            {
                return;
            }
        }

        try
        {
            PngBitmapEncoder encoder = new();

            encoder.Frames.Add(
                BitmapFrame.Create(selectedImage));

            using FileStream stream = new(
                outputPath,
                FileMode.Create,
                FileAccess.Write);

            encoder.Save(stream);

            ShowModernInfo(
                "Image created",
                $"Image created successfully.\n\n{outputPath}",
                ok: "OK",
                icon: DialogIcon.Success);
        }
        catch (Exception ex)
        {
            ShowModernInfo(
                "Export failed",
                $"The image could not be saved:\n\n{ex.Message}",
                ok: "OK",
                icon: DialogIcon.Error);

            SystemSounds.Exclamation.Play();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Themed dialogs
    // ─────────────────────────────────────────────────────────────────────────────

    private FrameworkElement BuildDialogIcon(DialogIcon icon)
    {
        string glyph = icon switch
        {
            DialogIcon.Success => "✓",
            DialogIcon.Warning => "!",
            DialogIcon.Error => "×",
            DialogIcon.Question => "?",
            _ => "i"
        };

        Brush foreground = icon switch
        {
            DialogIcon.Success => Brushes.SeaGreen,
            DialogIcon.Warning => Brushes.Goldenrod,
            DialogIcon.Error => Brushes.IndianRed,
            DialogIcon.Question => Brushes.DodgerBlue,
            _ => Brushes.DodgerBlue
        };

        return new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(2),
            BorderBrush = foreground,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new System.Windows.Controls.TextBlock
            {
                Text = glyph,
                Foreground = foreground,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private void ShowModernInfo(
        string title,
        string message,
        string ok = "OK",
        DialogIcon icon = DialogIcon.Info)
    {
        var dialog = new FluentWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,

            Width = 440,
            MinWidth = 440,
            MaxWidth = 440,

            SizeToContent = SizeToContent.Height,
            MinHeight = 190,

            ExtendsContentIntoTitleBar = true
        };

        var outerGrid = new Grid();

        outerGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        outerGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

        outerGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        // Title bar
        var titleBar = new Wpf.Ui.Controls.TitleBar
        {
            Title = title,
            ShowMinimize = false,
            ShowMaximize = false
        };

        Grid.SetRow(titleBar, 0);
        outerGrid.Children.Add(titleBar);

        // Main content
        var contentGrid = new Grid
        {
            Margin = new Thickness(20, 18, 20, 8)
        };

        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        FrameworkElement iconElement = BuildDialogIcon(icon);

        Grid.SetColumn(iconElement, 0);

        var messageText = new System.Windows.Controls.TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 330,
            Margin = new Thickness(16, 2, 0, 0),
            FontSize = 14
        };

        Grid.SetColumn(messageText, 1);

        contentGrid.Children.Add(iconElement);
        contentGrid.Children.Add(messageText);

        Grid.SetRow(contentGrid, 1);
        outerGrid.Children.Add(contentGrid);

        // Button row
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 8, 20, 16)
        };

        var okButton = new Wpf.Ui.Controls.Button
        {
            Content = ok,
            MinWidth = 80,
            Height = 34,
            Appearance = ControlAppearance.Primary,
            IsDefault = true
        };

        okButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        buttonPanel.Children.Add(okButton);

        Grid.SetRow(buttonPanel, 2);
        outerGrid.Children.Add(buttonPanel);

        dialog.Content = outerGrid;

        dialog.ShowDialog();
    }

    private bool ShowModernYesNo(
    string title,
    string message,
    string yes = "Yes",
    string no = "Cancel",
    DialogIcon icon = DialogIcon.Question)
    {
        bool result = false;

        var dialog = new FluentWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,

            Width = 440,
            MinWidth = 440,
            MaxWidth = 440,

            SizeToContent = SizeToContent.Height,
            MinHeight = 190,

            ExtendsContentIntoTitleBar = true
        };

        var outerGrid = new Grid();

        outerGrid.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        outerGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

        outerGrid.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        var titleBar = new Wpf.Ui.Controls.TitleBar
        {
            Title = title,
            ShowMinimize = false,
            ShowMaximize = false
        };

        Grid.SetRow(titleBar, 0);
        outerGrid.Children.Add(titleBar);

        var contentGrid = new Grid
        {
            Margin = new Thickness(20, 18, 20, 8)
        };

        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        contentGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        FrameworkElement iconElement = BuildDialogIcon(icon);

        Grid.SetColumn(iconElement, 0);

        var messageText = new System.Windows.Controls.TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 330,
            Margin = new Thickness(16, 2, 0, 0),
            FontSize = 14
        };

        Grid.SetColumn(messageText, 1);

        contentGrid.Children.Add(iconElement);
        contentGrid.Children.Add(messageText);

        Grid.SetRow(contentGrid, 1);
        outerGrid.Children.Add(contentGrid);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 8, 20, 16)
        };

        var noButton = new Wpf.Ui.Controls.Button
        {
            Content = no,
            MinWidth = 80,
            Height = 34,
            Appearance = ControlAppearance.Secondary,
            IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0)
        };

        noButton.Click += (_, _) =>
        {
            result = false;
            dialog.DialogResult = false;
            dialog.Close();
        };

        var yesButton = new Wpf.Ui.Controls.Button
        {
            Content = yes,
            MinWidth = 80,
            Height = 34,
            Appearance = ControlAppearance.Primary,
            IsDefault = true
        };

        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.DialogResult = true;
            dialog.Close();
        };

        buttonPanel.Children.Add(noButton);
        buttonPanel.Children.Add(yesButton);

        Grid.SetRow(buttonPanel, 2);
        outerGrid.Children.Add(buttonPanel);

        dialog.Content = outerGrid;

        dialog.ShowDialog();

        return result;
    }

}