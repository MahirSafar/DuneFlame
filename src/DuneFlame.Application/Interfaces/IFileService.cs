using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.Interfaces;

public interface IFileService
{
    // Şəkli yükləyir və yolunu (URL) qaytarır
    Task<string> UploadImageAsync(IFormFile file, string folderName);

    // Şəkli silir
    void DeleteFile(string filePath);
}