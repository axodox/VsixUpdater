using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace VsixUpdater
{
  class Program
  {
    private const string _vsixTempDir = "vsixTemp";
    private const string _manifestName = "extension.vsixmanifest";

    static void Main(string[] args)
    {
      var vsixPath = args[0];
      var rootPath = Path.GetDirectoryName(vsixPath);
      var tempPath = Path.Combine(rootPath, _vsixTempDir);
      var installDirName = Path.GetRandomFileName();

      //Extract zip file
      ZipFile.ExtractToDirectory(vsixPath, tempPath);

      //Modify manifest
      var manifestPath = Path.Combine(tempPath, _manifestName);
      var manifestDocument = XDocument.Load(manifestPath);
      var manifestNamespacePrefix = "{http://schemas.microsoft.com/developer/vsx-schema/2011}";
      var manifestElement = manifestDocument.Element(manifestNamespacePrefix + "PackageManifest");
      var installationElement = manifestElement.Element(manifestNamespacePrefix + "Installation");

      var targetIds = new[]
      {
        "Microsoft.VisualStudio.Pro",
        "Microsoft.VisualStudio.Community",
        "Microsoft.VisualStudio.Enterprise"
      };

      foreach (var target in installationElement.Elements(manifestNamespacePrefix + "InstallationTarget"))
      {
        if (targetIds.Contains(target.Attribute("Id").Value))
        {
          var versionParts = target.Attribute("Version").Value.Split(',');
          var newVersion = string.Join(",", versionParts[0], "16.0)");
          target.Attribute("Version").Value = newVersion;
        }
      }

      if (manifestElement.Element(manifestNamespacePrefix + "Prerequisites") == null)
      {
        var prerequisitesElement = new XElement(manifestNamespacePrefix + "Prerequisites");
        var prerequisiteElement = new XElement(manifestNamespacePrefix + "Prerequisite");
        prerequisiteElement.Add(new XAttribute("Id", "Microsoft.VisualStudio.Component.CoreEditor"));
        prerequisiteElement.Add(new XAttribute("Version", "[15.0,16.0)"));
        prerequisiteElement.Add(new XAttribute("DisplayName", "Visual Studio core editor"));
        prerequisitesElement.Add(prerequisiteElement);
        manifestElement.Add(prerequisitesElement);
      }

      manifestDocument.Save(manifestPath);

      //Create catalog
      var manifestInfo = ManifestInfo.FromManifest(manifestPath);

      var exceptions = new[]
      {
        "\\[Content_Types].xml",
        "\\catalog.json",
        "\\manifest.json"
      };

      var files = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
        .Select(p => p.Substring(tempPath.Length))
        .Except(exceptions)
        .Where(p => !p.StartsWith("\\_rels\\") && !p.StartsWith("\\package\\"))
        .Select(p => new
        {
          fileName = p.Replace('\\', '/'),
          sha256 = CalculateHash(Path.Combine(tempPath, p.TrimStart('\\')))
        })
        .ToArray();
      var installSize = files
        .Select(p => Path.Combine(tempPath, p.fileName.Replace('/', '\\').TrimStart('\\')))
        .Sum(p => new FileInfo(p).Length);

      var catalog = new
      {
        manifestVersion = "1.1",
        info = new { id = $"{manifestInfo.Id},version={manifestInfo.Version}" },
        packages = new object[]
        {
          new {
            id = "Component."+ manifestInfo.Id,
            version = manifestInfo.Version,
            type = "Component",
            extension = true,
            dependencies = new Dictionary<string, string>()
            {
              {manifestInfo.Id, manifestInfo.Version },
              {"Microsoft.VisualStudio.Component.CoreEditor", "[15.0,16.0)" }
            },
            localizedResources = new []
            {
              new
              {
                language = "en-US",
                title = manifestInfo.Title,
                description = manifestInfo.Description
              }
            }
          },
          new
          {
            id = manifestInfo.Id,
            version = manifestInfo.Version,
            type = "Vsix",
            payloads = new []
            {
              new
              {
                fileName = Path.GetFileName(vsixPath),
                size = installSize
              }
            },
            vsixId = manifestInfo.Id,
            extensionDir = $"[installdir]\\Common7\\IDE\\Extensions\\{installDirName}",
            installSize = installSize,
          }
        }

      };
      var jsonCatalog = JsonConvert.SerializeObject(catalog, Formatting.Indented);
      File.WriteAllText(Path.Combine(tempPath, "catalog.json"), jsonCatalog);

      //Create manifest
      var manifest = new
      {
        id = manifestInfo.Id,
        version = manifestInfo.Version,
        type = "Vsix",
        vsixId = manifestInfo.Id,
        extensionDir = $"[installdir]\\Common7\\IDE\\Extensions\\{installDirName}",
        files = files,
        installSize = installSize,
        dependencies = new Dictionary<string, string>()
        {
          {"Microsoft.VisualStudio.Component.CoreEditor", "[15.0,16.0)" }
        }
      };
      var jsonManifest = JsonConvert.SerializeObject(manifest, Formatting.Indented);
      File.WriteAllText(Path.Combine(tempPath, "manifest.json"), jsonManifest);

      //Update content types
      var typesPath = Path.Combine(tempPath, "[Content_Types].xml");
      var typesDocument = XDocument.Load(typesPath);
      var typesNamespacePrefix = "{http://schemas.openxmlformats.org/package/2006/content-types}";
      var typesElement = typesDocument.Element(typesNamespacePrefix + "Types");
      var jsonElement = typesElement
        .Descendants(typesNamespacePrefix + "Default")
        .FirstOrDefault(p => p.Attribute("Extension").Value == "json");
      if (jsonElement == null)
      {
        jsonElement = new XElement(typesNamespacePrefix + "Default");
        jsonElement.Add(new XAttribute("Extension", "json"));
        jsonElement.Add(new XAttribute("ContentType", "application/json"));
        typesElement.Add(jsonElement);
      }
      typesDocument.Save(typesPath);
      //Create zip
      File.Delete(vsixPath);
      ZipFile.CreateFromDirectory(tempPath, vsixPath, CompressionLevel.Optimal, false, Encoding.ASCII);

      //CleanUp
      Directory.Delete(tempPath, true);
    }

    private static SHA256 _sha256 = SHA256Managed.Create();

    private static string CalculateHash(string filePath)
    {
      using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
      {
        return string.Join(string.Empty, _sha256.ComputeHash(stream).Select(p => p.ToString("X2")));
      }
    }
  }
}
