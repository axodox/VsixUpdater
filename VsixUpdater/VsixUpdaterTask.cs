using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;

namespace VsixUpdater
{
  public class VsixUpdaterTask : Task
  {
    private const string _vsixTempDir = "vsixTemp";
    private const string _manifestName = "extension.vsixmanifest";
    private const string _baseDependencyName = "Microsoft.VisualStudio.Component.CoreEditor";
    private const string _baseDependencyVersion = "[15.0," + _nextVisualStudioVersion + ")";
    private const string _baseDependencyDescription = "Visual Studio core editor";
    private const string _nextVisualStudioVersion = "16.0";
    private const string _catalogFilePath = "/catalog.json";
    private const string _manifestFilePath = "/manifest.json";
    private const string _packageType = "Vsix";

    [Required]
    public string OutputPath { get; set; }

    public string Include { get; set; }

    public override bool Execute()
    {
      try
      {
        var vsixPaths = Directory.GetFiles(OutputPath, "*.vsix", SearchOption.AllDirectories);
        foreach (var vsixPath in vsixPaths)
        {
          Log.LogMessage(MessageImportance.High, $"Updating {vsixPath}...");
          UpdatePackage(vsixPath);
        }
        return true;
      }
      catch
      {
        return false;
      }
    }

    private void UpdatePackage(string vsixPath)
    {
      var installDirName = Path.GetRandomFileName();

      //Open package
      var package = Package.Open(vsixPath, FileMode.Open, FileAccess.ReadWrite);
      
      //Add included files
      AddIncludedFiles(package);

      //Modify manifest
      var manifestText = package.ReadAllText(_manifestName);
      var manifestDocument = XDocument.Parse(manifestText);
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
          var oldVersion = target.Attribute("Version").Value;
          var versionParts = oldVersion.Trim('(', ')', '[', ']').Split(',');
          var newVersion = oldVersion[0] + versionParts[0] + "," + _nextVisualStudioVersion + ')';
          target.Attribute("Version").Value = newVersion;
        }
      }

      var dependencies = new Dictionary<string, string>();

      if (manifestElement.Element(manifestNamespacePrefix + "Prerequisites") == null)
      {
        dependencies.Add(_baseDependencyName, _baseDependencyVersion);

        var prerequisitesElement = new XElement(manifestNamespacePrefix + "Prerequisites");
        var prerequisiteElement = new XElement(manifestNamespacePrefix + "Prerequisite");
        prerequisiteElement.Add(new XAttribute("Id", _baseDependencyName));
        prerequisiteElement.Add(new XAttribute("Version", _baseDependencyVersion));
        prerequisiteElement.Add(new XAttribute("DisplayName", _baseDependencyDescription));
        prerequisitesElement.Add(prerequisiteElement);
        manifestElement.Add(prerequisitesElement);
      }
      else
      {
        var prerequisitesElement = manifestElement.Element(manifestNamespacePrefix + "Prerequisites");
        var prerequisiteElements = prerequisitesElement.Descendants(manifestNamespacePrefix + "Prerequisite");
        foreach (var prerequisiteElement in prerequisiteElements)
        {
          dependencies.Add(prerequisiteElement.Attribute("Id").Value, prerequisiteElement.Attribute("Version").Value);
        }
      }

      package.WriteAllText(_manifestName, manifestDocument.ToString());

      //Create catalog
      var manifestNode = manifestDocument.Element(manifestNamespacePrefix + "PackageManifest");
      var metadataNode = manifestNode.Element(manifestNamespacePrefix + "Metadata");
      var identityNode = metadataNode.Element(manifestNamespacePrefix + "Identity");

      var packageId = identityNode.Attribute("Id").Value;
      var packageTitle = metadataNode.Element(manifestNamespacePrefix + "DisplayName").Value;
      var packageDescription = metadataNode.Element(manifestNamespacePrefix + "Description").Value;
      var packageVersion = identityNode.Attribute("Version").Value;

      var exceptions = new[]
      {
        _catalogFilePath,
        _manifestFilePath
      };

      var files = package
        .GetAllFiles()
        .Except(exceptions)
        .Select(p => new
        {
          fileName = p,
          sha256 = package.CalculateHash(p)
        })
        .ToArray();

      var installSize = files.Sum(p => package.GetSize(p.fileName));
      var installationPath = $"[installdir]\\Common7\\IDE\\Extensions\\{installDirName}";
      var catalog = new
      {
        manifestVersion = "1.1",
        info = new { id = $"{packageId},version={packageVersion}" },
        packages = new object[]
        {
          new {
            id = "Component."+ packageId,
            version = packageVersion,
            type = "Component",
            extension = true,
            dependencies = new Dictionary<string, string>(dependencies)
            {
              { packageId, packageVersion }
            },
            localizedResources = new []
            {
              new
              {
                language = "en-US",
                title = packageTitle,
                description = packageDescription
              }
            }
          },
          new
          {
            id = packageId,
            version = packageVersion,
            type = _packageType,
            payloads = new []
            {
              new
              {
                fileName = Path.GetFileName(vsixPath),
                size = installSize
              }
            },
            vsixId = packageId,
            extensionDir = installationPath,
            installSize = installSize,
          }
        }

      };
      var jsonCatalog = JsonConvert.SerializeObject(catalog, Formatting.Indented);
      package.WriteAllText(_catalogFilePath, jsonCatalog);

      //Create manifest
      var manifest = new
      {
        id = packageId,
        version = packageVersion,
        type = _packageType,
        vsixId = packageId,
        extensionDir = installationPath,
        files = files,
        installSize = installSize,
        dependencies = dependencies
      };
      var jsonManifest = JsonConvert.SerializeObject(manifest, Formatting.Indented);
      package.WriteAllText(_manifestFilePath, jsonManifest);

      //Recompress
      package.Recompress();

      //Save
      package.Flush();
      package.Close();
    }

    private void AddIncludedFiles(Package package)
    {
      if (string.IsNullOrWhiteSpace(Include)) return;

      var existingFiles = package.GetAllFiles();
      var directoryPath = new DirectoryInfo(OutputPath).FullName;
      foreach (var pattern in Include.Split(';'))
      {
        var newFiles = Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly);
        if (newFiles.Length > 0)
        {
          foreach (var newFile in newFiles)
          {
            var newFileName = newFile.Substring(directoryPath.Length - 1).Replace("\\", "/");
            if (existingFiles.Contains(newFileName, StringComparer.CurrentCultureIgnoreCase))
            {
              Log.LogMessage(MessageImportance.Normal, $"Included file {newFileName} already exists.");
            }
            else
            {
              Log.LogMessage(MessageImportance.Normal, $"Adding file {newFileName}...");
              package.WriteAllBytes(newFileName, File.ReadAllBytes(newFile));
            }
          }
        }
        else
        {
          Log.LogMessage(MessageImportance.High, $"Include pattern {pattern} matched no files!");
        }
      }
    }
  }
}
