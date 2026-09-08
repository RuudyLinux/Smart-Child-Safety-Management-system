namespace ChildGuard.Application.Common;

public static class RfidMasking
{
    /// <summary>Masks all but the last 4 characters of a card UID, e.g. "****A72F".</summary>
    public static string Mask(string cardUid)
    {
        if (string.IsNullOrEmpty(cardUid)) return cardUid;
        if (cardUid.Length <= 4) return new string('*', cardUid.Length);
        return new string('*', cardUid.Length - 4) + cardUid[^4..];
    }
}
