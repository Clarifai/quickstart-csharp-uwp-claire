using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.System.Display;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace SampleClarifaiUWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MediaCapture _mediaCapture;
        private bool _isPreviewing;
        private readonly DisplayRequest _displayRequest = new DisplayRequest();

        private PredictionAPI _predictionApi;

        private string _selectedModel = "GeneralModel";

        /// <summary>
        /// Ctor.
        /// </summary>
        public MainPage()
        {
            this.InitializeComponent();

            Application.Current.Suspending += Application_Suspending;
        }

        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            // Handle global application events only if this page is active
            if (Frame.CurrentSourcePageType == typeof(MainPage))
            {
                var deferral = e.SuspendingOperation.GetDeferral();
                await CleanupCameraAsync();
                deferral.Complete();
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await StartPreviewAsync();
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            await CleanupCameraAsync();
        }

        private async Task StartPreviewAsync()
        {
            try
            {
                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync();

                _displayRequest.RequestActive();
                DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;
            }
            catch (UnauthorizedAccessException)
            {
                // This will be thrown if the user denied access to the camera in privacy settings
                ShowMessageToUser("The app was denied access to the camera.");
                return;
            }

            try
            {
                PreviewControl.Source = _mediaCapture;
                await _mediaCapture.StartPreviewAsync();
                _isPreviewing = true;
            }
            catch (FileLoadException)
            {
                _mediaCapture.CaptureDeviceExclusiveControlStatusChanged +=
                    _mediaCapture_CaptureDeviceExclusiveControlStatusChanged;
            }

            await Task.Run(async () =>
            {
                while (true)
                {
                    await RunPredictionsAndDisplayResponse();
                }
            });
        }

        /// <summary>
        /// Takes the camera output and displays the Clarifai predictions on the pane.
        /// </summary>
        /// <returns>a task</returns>
        private async Task RunPredictionsAndDisplayResponse()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    if (_predictionApi == null) return;

                    LowLagPhotoCapture lowLagCapture =
                        await _mediaCapture.PrepareLowLagPhotoCaptureAsync(
                            ImageEncodingProperties.CreateUncompressed(MediaPixelFormat
                                .Bgra8));
                    CapturedPhoto capturedPhoto = await lowLagCapture.CaptureAsync();
                    SoftwareBitmap softwareBitmap = capturedPhoto.Frame.SoftwareBitmap;
                    byte[] data = await EncodedBytes(softwareBitmap,
                        BitmapEncoder.JpegEncoderId);

                    try
                    {
                        ConceptsTextBlock.Text = await _predictionApi
                            .PredictConcepts(data, _selectedModel);
                    }
                    catch (Exception ex)
                    {
                        ShowMessageToUser("Error: " + ex.Message);
                    }

                    try
                    {
                        (double camWidth, double camHeight) = CameraOutputDimensions();

                        List<Rect> rects = await _predictionApi.PredictFaces(data,
                            camWidth, camHeight);
                        CameraGrid.Children.Clear();
                        foreach (Rect r in rects)
                        {
                            CameraGrid.Children.Add(CreateGuiRectangle(r, camWidth, camHeight));
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowMessageToUser("Error: " + ex.Message);
                    }

                    await lowLagCapture.FinishAsync();
                }
                catch (COMException) // This is thrown when application exits.
                {
                    // No need to handle this since the application is exiting.
                }
            });

            await Task.Delay(2000);
        }

        /// <summary>
        /// Returns the size of the camera output that correctly accounts for black panes around.
        /// There's no other way to get these dimensions.
        /// </summary>
        /// <returns>the size of the camera output</returns>
        private (double, double) CameraOutputDimensions()
        {
            var props = (VideoEncodingProperties) _mediaCapture
                .VideoDeviceController
                .GetMediaStreamProperties(MediaStreamType.VideoPreview);

            double cameraWidth = props.Width;
            double cameraHeight = props.Height;

            double previewOutputWidth = CameraGrid.ActualWidth;
            double previewOutputHeight = CameraGrid.ActualHeight;

            double cameraRatio = cameraWidth / cameraHeight;
            double previewOutputRatio = previewOutputWidth / previewOutputHeight;

            double actualWidth = (cameraRatio <= previewOutputRatio)
                ? previewOutputHeight * cameraRatio
                : previewOutputWidth;
            double actualHeight = (cameraRatio <= previewOutputRatio)
                ? previewOutputHeight
                : previewOutputWidth / cameraRatio;
            return (actualWidth, actualHeight);
        }

        /// <summary>
        /// Creates a new rectangle will be displayed on the camera output pane.
        /// </summary>
        /// <param name="rect">the rectangle</param>
        /// <param name="camWidth">the camera output width</param>
        /// <param name="camHeight">the camera output height</param>
        /// <returns></returns>
        private Rectangle CreateGuiRectangle(Rect rect, double camWidth, double camHeight)
        {
            return new Rectangle
            {
                Stroke = new SolidColorBrush(Colors.Blue),
                StrokeThickness = 5,
                Visibility = Visibility.Visible,
                Margin = new Thickness(
                    rect.Width - camWidth + rect.Left * 2,
                    rect.Height - camHeight + rect.Top * 2,
                    0,
                    0),
                Width = rect.Width,
                Height = rect.Height
            };
        }

        /// <summary>
        /// Converts the camera output to a byte array.
        /// </summary>
        /// <param name="soft">the software bitmap</param>
        /// <param name="encoderId">the encoder ID</param>
        /// <returns>the array of bytes</returns>
        private async Task<byte[]> EncodedBytes(SoftwareBitmap soft, Guid encoderId)
        {
            byte[] array;
            using (var ms = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, ms);
                encoder.SetSoftwareBitmap(soft);

                try
                {
                    await encoder.FlushAsync();
                }
                catch (Exception) { return new byte[0]; }

                array = new byte[ms.Size];
                await ms.ReadAsync(array.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
            }
            return array;
        }

        private async void _mediaCapture_CaptureDeviceExclusiveControlStatusChanged(
            MediaCapture sender, MediaCaptureDeviceExclusiveControlStatusChangedEventArgs args)
        {
            if (args.Status == MediaCaptureDeviceExclusiveControlStatus.SharedReadOnlyAvailable)
            {
                ShowMessageToUser("The camera preview can't be displayed because another app has " +
                                  "exclusive access");
            }
            else if (args.Status ==
                     MediaCaptureDeviceExclusiveControlStatus.ExclusiveControlAvailable
                     && !_isPreviewing)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    await StartPreviewAsync();
                });
            }
        }

        /// <summary>
        /// Cleans up the camera output pane.
        /// </summary>
        /// <returns>a task</returns>
        private async Task CleanupCameraAsync()
        {
            if (_mediaCapture != null)
            {
                if (_isPreviewing)
                {
                    await _mediaCapture.StopPreviewAsync();
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    PreviewControl.Source = null;
                    _displayRequest?.RequestRelease();

                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                });
            }
        }

        /// <summary>
        /// Displays a warning text.
        /// </summary>
        /// <param name="msg">the message</param>
        private void ShowMessageToUser(string msg)
        {
            WarningTextBlock.Text = msg;
        }

        private void SetKeyButton_OnClick(object sender, RoutedEventArgs e)
        {
            string apiKey = ApiKeyTextBox.Text;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _predictionApi = new PredictionAPI(apiKey);
            }
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedModel = ((ComboBoxItem)ModelsComboBox.SelectedItem).Tag.ToString();
        }
    }
}
