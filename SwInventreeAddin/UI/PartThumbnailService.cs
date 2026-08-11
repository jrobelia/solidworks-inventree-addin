using System;
using System.Drawing;
using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Encapsulates the full Viewport Capture workflow: capture the SolidWorks viewport,
    /// show the crop dialog, process the image to PNG, upload it to InvenTree as a
    /// thumbnail, then re-fetch the updated thumbnail URL and download the preview bytes.
    /// <para>
    /// <see cref="PushAsync"/> must be called on the UI thread because it shows
    /// <see cref="ImageCropWindow"/> as a modal dialog.
    /// </para>
    /// </summary>
    internal sealed class PartThumbnailService
    {
        private readonly IInventreeClient     _client;
        private readonly IViewportCaptureService? _viewportService;

        public PartThumbnailService(IInventreeClient client, IViewportCaptureService? viewportService)
        {
            _client          = client          ?? throw new ArgumentNullException(nameof(client));
            _viewportService = viewportService;
        }

        /// <summary>
        /// Runs the full Viewport Capture workflow for <paramref name="partPk"/>.
        /// </summary>
        /// <param name="partPk">InvenTree PK of the part to update and re-fetch after upload.</param>
        /// <param name="reportStatus">Callback for progress status messages (called on the UI thread).</param>
        /// <param name="imageOverride">Skip capture and crop when supplied (used in tests).</param>
        /// <returns>
        /// New thumbnail bytes after upload, or <c>null</c> if the user cancelled,
        /// no viewport service is available, or the re-fetch/download failed.
        /// </returns>
        /// <exception cref="Exception">Thrown on upload failure — caller handles error reporting.</exception>
        public async Task<byte[]?> PushAsync(
            int                          partPk,
            Action<string, StatusSeverity> reportStatus,
            Image?                       imageOverride = null)
        {
            Image? image    = null;
            bool   ownImage = false;
            var    cropRect = Rectangle.Empty;

            try
            {
                if (imageOverride != null)
                {
                    image = imageOverride;
                }
                else if (_viewportService != null)
                {
                    image    = _viewportService.CaptureViewportImage();
                    ownImage = true;

                    var cropWindow = new ImageCropWindow(image);
                    if (cropWindow.ShowDialog() != true)
                        return null;

                    cropRect = cropWindow.CropRectangle;
                }
                else
                {
                    return null;
                }

                byte[] pngData = ImagePipeline.Process(image, cropRect);

                reportStatus("Pushing image to InvenTree\u2026", StatusSeverity.None);

                await _client.UploadPartImageAsync(partPk, pngData).ConfigureAwait(false);

                // Re-fetch the part to get the updated thumbnail URL.
                byte[]? newThumb = null;
                try
                {
                    var refreshed = await _client.GetPartByPkAsync(partPk)
                                                  .ConfigureAwait(false);
                    if (refreshed == null)
                    {
                        reportStatus("Image pushed, but the part could not be re-fetched for a preview.",
                                     StatusSeverity.Warning);
                        return null;
                    }

                    if (string.IsNullOrEmpty(refreshed.ThumbnailUrl))
                    {
                        reportStatus("Image pushed, but InvenTree did not return a thumbnail URL.",
                                     StatusSeverity.Warning);
                        return null;
                    }

                    newThumb = await _client.DownloadImageAsync(refreshed.ThumbnailUrl!)
                                            .ConfigureAwait(false);
                    if (newThumb == null)
                    {
                        reportStatus("Image pushed, but the thumbnail could not be downloaded.",
                                     StatusSeverity.Warning);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    reportStatus($"Image pushed, but the thumbnail preview could not be refreshed: {ex.Message}",
                                 StatusSeverity.Warning);
                    return null;
                }

                return newThumb;
            }
            finally
            {
                if (ownImage) image?.Dispose();
            }
        }
    }
}
