using System.Xml.Linq;

namespace VsixUpdater
{
  class ManifestInfo
  {
    public string Id { get; private set; }

    public string Version { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    private ManifestInfo() { }

    public static ManifestInfo FromManifest(string manifestPath)
    {
      var document = XDocument.Load(manifestPath);

      var namePrefix = "{http://schemas.microsoft.com/developer/vsx-schema/2011}";
      var manifestNode = document.Element(namePrefix + "PackageManifest");
      var metadataNode = manifestNode.Element(namePrefix + "Metadata");
      var identityNode = metadataNode.Element(namePrefix + "Identity");

      var manifestInfo = new ManifestInfo()
      {
        Id = identityNode.Attribute("Id").Value,
        Title = metadataNode.Element(namePrefix + "DisplayName").Value,
        Description = metadataNode.Element(namePrefix + "Description").Value,
        Version = identityNode.Attribute("Version").Value
      };

      return manifestInfo;
    }
  }
}
