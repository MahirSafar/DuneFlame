using DuneFlame.Application.Interfaces;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Cloud Storage Service for uploading files to Google Cloud Storage.
/// Returns public URLs in the format: https://storage.googleapis.com/{bucketName}/{objectName}
/// </summary>
public class CloudStorageService(ILogger<CloudStorageService> logger) : IFileService
{
    private readonly ILogger<CloudStorageService> _logger = logger;
    private const string BucketName = "duneflame-images";
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private readonly string[] _allowedMimeTypes = { "image/jpeg", "image/png", "image/webp" };
    private const long MaxFileSize = 2 * 1024 * 1024; // 2 MB

    /// <summary>
    /// Uploads an image file to Google Cloud Storage and returns the public URL.
    /// </summary>
    /// <param name="file">The file to upload (IFormFile)</param>
    /// <param name="folderName">The folder/prefix in the bucket (e.g., "products", "sliders")</param>
    /// <returns>Public URL of the uploaded file</returns>
    public async Task<string> UploadImageAsync(IFormFile file, string folderName)
    {
        try
        {
            // 1. Validate file
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty");

            // 2. Validate file size
            if (file.Length > MaxFileSize)
                throw new ArgumentException("File size exceeds the 2MB limit.");

            // 3. Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
                throw new ArgumentException("Invalid file type. Only JPG, PNG and WEBP are allowed.");

            // 4. Validate MIME type
            if (!_allowedMimeTypes.Contains(file.ContentType?.ToLower() ?? ""))
                throw new ArgumentException("Invalid content type.");

            // 5. Create unique object name
            var objectName = $"{folderName}/{Guid.NewGuid()}_{file.FileName}";

            // 6. Upload to GCS
            var client = StorageClient.Create();
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0; // Reset stream position before uploading

            await client.UploadObjectAsync(
                bucket: BucketName,
                objectName: objectName,
                contentType: file.ContentType,
                source: stream);

            // 7. Return public URL
            var publicUrl = $"https://storage.googleapis.com/{BucketName}/{objectName}";
            _logger.LogInformation($"File uploaded successfully to GCS: {publicUrl}");
            return publicUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error uploading file to Google Cloud Storage: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Deletes a file from Google Cloud Storage.
    /// </summary>
    /// <param name="filePath">The public URL or object name to delete</param>
    public void DeleteFile(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            // Extract object name from URL if full URL is provided
            var objectName = ExtractObjectNameFromUrl(filePath);

            var client = StorageClient.Create();
            client.DeleteObject(BucketName, objectName);

            _logger.LogInformation($"File deleted from GCS: {objectName}");
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning($"File not found in GCS: {filePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting file from Google Cloud Storage: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Extracts the object name from a public GCS URL.
    /// Example: https://storage.googleapis.com/duneflame-images/products/guid.jpg
    /// Returns: products/guid.jpg
    /// </summary>
    private static string ExtractObjectNameFromUrl(string filePath)
    {
        if (filePath.StartsWith("https://storage.googleapis.com/"))
        {
            // Extract object name from full URL
            var urlPrefix = $"https://storage.googleapis.com/{BucketName}/";
            return filePath.Substring(urlPrefix.Length);
        }

        // If it's already an object name, return as-is
        return filePath;
    }
}
