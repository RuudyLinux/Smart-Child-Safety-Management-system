using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// Data-protection key ring supplied through configuration (<c>DataProtection:KeyRingXml</c>).
/// Cookies, antiforgery tokens and temp data are encrypted with these keys. On a host whose disk is
/// wiped on every sleep or redeploy (Render free), keys kept on disk would sign every admin out and
/// break any open form; keys from configuration survive. The value is a secret: set it from the
/// platform's secret store, never from the repo. Generate one with tools/KeyRingGenerator.
/// A key the framework creates at runtime (the configured ones expired) lives in memory only.
/// </summary>
public sealed class ConfiguredKeyRingRepository : IXmlRepository
{
    private readonly object _gate = new();
    private readonly List<XElement> _keys;

    public ConfiguredKeyRingRepository(string keyRingXml)
    {
        if (string.IsNullOrWhiteSpace(keyRingXml))
            throw new ArgumentException("The data-protection key ring is empty.", nameof(keyRingXml));

        try
        {
            // A key file saved by the framework begins with an XML declaration; a wrapper element cannot hold one.
            var withoutDeclaration = System.Text.RegularExpressions.Regex.Replace(keyRingXml, @"<\?xml[^>]*\?>", "");
            _keys = XElement.Parse($"<keys>{withoutDeclaration}</keys>").Elements().ToList();
        }
        catch (System.Xml.XmlException)
        {
            // The message of an XML error can quote the document, i.e. key material.
            throw new ArgumentException("The data-protection key ring is not valid XML.", nameof(keyRingXml));
        }

        if (_keys.Count == 0 || _keys.Any(k => k.Name.LocalName != "key"))
            throw new ArgumentException("The data-protection key ring must contain one or more <key> elements.", nameof(keyRingXml));
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_gate) return _keys.ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_gate) _keys.Add(element);
    }
}
