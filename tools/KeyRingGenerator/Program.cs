using Microsoft.AspNetCore.DataProtection;

// Creates one key valid for ten years in a throwaway folder and prints it on stdout (nothing else on stdout).
// Treat the output as a secret: it decrypts every AdminWeb cookie. Set it as DataProtection__KeyRingXml in
// the host's secret store and do not commit or log it. Rotating = run again and replace the value
// (every admin signs in once more).
var folder = Directory.CreateTempSubdirectory("childguard-keyring-");
try
{
    var provider = DataProtectionProvider.Create(folder, options =>
    {
        options.SetApplicationName("ChildGuard.AdminWeb");
        options.SetDefaultKeyLifetime(TimeSpan.FromDays(3650));
    });
    provider.CreateProtector("keyring-generation").Protect("create the first key");

    var files = folder.GetFiles("key-*.xml");
    if (files.Length != 1) throw new InvalidOperationException($"Expected one key file, found {files.Length}.");
    // One line, no XML declaration: easy to paste into an environment variable.
    Console.Out.Write(System.Xml.Linq.XElement.Parse(File.ReadAllText(files[0].FullName))
        .ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
}
finally
{
    folder.Delete(recursive: true);
}
