using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Security.Cryptography;

namespace VsixUpdater
{
  public static class Helpers
  {
    public static string ReadAllText(this Package package, string path)
    {
      var part = package.GetPart(GetUri(path));
      using (var partStream = part.GetStream(FileMode.Open, FileAccess.Read))
      using (var reader = new StreamReader(partStream))
      {
        return reader.ReadToEnd();
      }
    }

    public static void WriteAllText(this Package package, string path, string text)
    {
      var uri = GetUri(path);
      var part = package.PartExists(uri) ?
        package.GetPart(uri) :
        package.CreatePart(uri, GetContentType(path), CompressionOption.Maximum);

      using (var partStream = part.GetStream(FileMode.Create, FileAccess.Write))
      using (var writer = new StreamWriter(partStream))
      {
        writer.Write(text);
      }
    }

    public static byte[] ReadAllBytes(this Package package, string path)
    {
      var part = package.GetPart(GetUri(path));
      using (var partStream = part.GetStream(FileMode.Open, FileAccess.Read))
      {
        var length = (int)partStream.Length;
        var data = new byte[length];
        partStream.Read(data, 0, length);
        return data;
      }
    }

    public static void WriteAllBytes(this Package package, string path, byte[] data)
    {
      var uri = GetUri(path);
      var part = package.PartExists(uri) ?
        package.GetPart(uri) :
        package.CreatePart(uri, GetContentType(path), CompressionOption.Maximum);

      using (var partStream = part.GetStream(FileMode.Create, FileAccess.Write))
      {
        partStream.Write(data, 0, data.Length);
      }
    }

    public static string[] GetAllFiles(this Package package)
    {
      return package
        .GetParts()
        .Select(p => p.Uri.OriginalString)
        .ToArray();
    }

    private static SHA256 _sha256 = SHA256Managed.Create();

    public static string CalculateHash(this Package package, string path)
    {
      var part = package.GetPart(GetUri(path));
      using (var partStream = part.GetStream(FileMode.Open, FileAccess.Read))
      {
        return string.Join(string.Empty, _sha256.ComputeHash(partStream).Select(p => p.ToString("X2")));
      }
    }

    public static long GetSize(this Package package, string path)
    {
      var part = package.GetPart(GetUri(path));
      using (var partStream = part.GetStream(FileMode.Open, FileAccess.Read))
      {
        return partStream.Length;
      }
    }

    public static void Recompress(this Package package)
    {
      var filesToRecompress = package
        .GetParts()
        .Where(p => p.CompressionOption != CompressionOption.Maximum)
        .Select(p => p.Uri)
        .ToArray();

      foreach (var file in filesToRecompress)
      {
        var data = package.ReadAllBytes(file.OriginalString);
        package.DeletePart(file);
        package.WriteAllBytes(file.OriginalString, data);
      }
    }

    private static string GetContentType(string path)
    {
      var extension = Path
        .GetExtension(path)
        .TrimStart('.')
        .ToLower();
      switch (extension)
      {
        case "txt":
        case "pkgdef":
          return "text/plain";
        case "xml":
        case "vsixmanifest":
          return "text/xml";
        case "htm":
        case "html":
          return "text/html";
        case "rtf":
          return "application/rtf";
        case "pdf":
          return "application/pdf";
        case "gif":
          return "image/gif";
        case "jpg":
        case "jpeg":
          return "image/jpg";
        case "tiff":
          return "image/tiff";
        case "vsix":
        case "zip":
          return "application/zip";
        default:
          return "application/octet-stream";
      }
    }

    private static Uri GetUri(string path)
    {
      return new Uri("/" + path.TrimStart('/'), UriKind.Relative);
    }
  }
}
