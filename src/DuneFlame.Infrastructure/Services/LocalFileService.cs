using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Hosting; // IWebHostEnvironment üçün
using Microsoft.AspNetCore.Http;

namespace DuneFlame.Infrastructure.Services;

public class LocalFileService(IWebHostEnvironment environment) : IFileService
{
    private readonly IWebHostEnvironment _environment = environment;

    // Təhlükəsizlik Ayarları
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private readonly string[] _allowedMimeTypes = { "image/jpeg", "image/png", "image/webp" };
    private const long _maxFileSize = 2 * 1024 * 1024; // 2 MB

    public async Task<string> UploadImageAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty");

        // 1. Fayl Ölçüsü Yoxlanışı (Size Limit)
        if (file.Length > _maxFileSize)
            throw new ArgumentException("File size exceeds the 2MB limit.");

        // 2. Fayl Uzantısı Yoxlanışı (Extension Check)
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
            throw new ArgumentException("Invalid file type. Only JPG, PNG and WEBP are allowed.");

        // 3. MIME Type Yoxlanışı (MIME Check)
        if (!_allowedMimeTypes.Contains(file.ContentType.ToLower()))
            throw new ArgumentException("Invalid content type.");

        // 4. Qovluq Yolu
        // Məsələn: src/DuneFlame.API/wwwroot/uploads/products
        string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", folderName);

        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        // 5. Unikal Adlandırma (Collision Prevention)
        string uniqueFileName = Guid.NewGuid().ToString() + extension;
        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

        // 6. Yaddaşa Yazma
        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        // 7. URL Qaytar (Database üçün)
        // Məsələn: /uploads/products/guid.jpg
        return $"/uploads/{folderName}/{uniqueFileName}";
    }

    public void DeleteFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        // URL-dən fiziki yola çevirmə
        // /uploads/products/image.jpg -> C:\...\wwwroot\uploads\products\image.jpg
        var fullPath = Path.Combine(_environment.WebRootPath, filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}