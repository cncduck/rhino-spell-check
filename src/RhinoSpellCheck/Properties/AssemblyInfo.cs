using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Rhino identifies the plug-in by the assembly's GUID, so this value must stay
// stable across releases or existing installs will register a second plug-in.
[assembly: Guid("3F1C6A94-2D7E-4B85-9C41-5A6E8D0B27F3")]
[assembly: ComVisible(false)]

[assembly: PlugInDescription(DescriptionType.Organization, "Rhino Spell Check")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
