namespace ChildGuard.Domain.Entities;

/// <summary>A residential address, stored in columns on its owner (Student, Guardian). Every part is optional.</summary>
public class PostalAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? Locality { get; set; }
    public string? Landmark { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}
