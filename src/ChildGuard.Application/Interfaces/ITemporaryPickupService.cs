using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

/// <summary>Temporary pickup lifecycle: guardian request and cancel, admin decisions, desk scan and handover.</summary>
public interface ITemporaryPickupService
{
    // Guardian side: the guardian id always comes from the token.
    Task<Result<TemporaryPickupDto>> CreateAsync(int guardianId, CreateTemporaryPickupRequest request, CancellationToken ct = default);
    Task<List<TemporaryPickupDto>> ListForGuardianAsync(int guardianId, CancellationToken ct = default);
    Task<Result<TemporaryPickupDetailDto>> GetForGuardianAsync(int guardianId, int id, CancellationToken ct = default);
    Task<Result<TemporaryPickupDto>> CancelAsync(int guardianId, int id, string? reason, CancellationToken ct = default);
    Task<Result<TemporaryPickupQrDto>> GetQrAsync(int guardianId, int id, CancellationToken ct = default);
    Task<Result<PhotoContentDto>> GetPhotoForGuardianAsync(int guardianId, int id, CancellationToken ct = default);

    // Admin side.
    Task<List<TemporaryPickupDto>> ListForAdminAsync(TemporaryPickupState? state, int? studentId, CancellationToken ct = default);
    Task<TemporaryPickupSummaryDto> GetSummaryAsync(CancellationToken ct = default);
    Task<Result<TemporaryPickupDetailDto>> GetForAdminAsync(int id, CancellationToken ct = default);
    Task<Result<PhotoContentDto>> GetPhotoAsync(int id, CancellationToken ct = default);
    Task<Result<TemporaryPickupDto>> ApproveAsync(int adminId, int id, CancellationToken ct = default);
    Task<Result<TemporaryPickupDto>> RejectAsync(int adminId, int id, string? reason, CancellationToken ct = default);
    Task<Result<TemporaryPickupDto>> RevokeAsync(int adminId, int id, string? reason, CancellationToken ct = default);

    // School desk (admin).
    Task<Result<TemporaryPickupScanResultDto>> ScanAsync(int adminId, string token, CancellationToken ct = default);
    Task<Result<TemporaryPickupHandoverResultDto>> HandOverAsync(int adminId, int id, TemporaryPickupHandoverRequest request, CancellationToken ct = default);
}

public interface ITemporaryPickupRepository
{
    Task AddAsync(TemporaryPickupAuthorization authorization, CancellationToken ct = default);
    /// <summary>With student, requester, deciding admin and photo metadata loaded (not the photo bytes).</summary>
    Task<TemporaryPickupAuthorization?> GetAsync(int id, CancellationToken ct = default);
    Task<TemporaryPickupPhoto?> GetPhotoAsync(int id, CancellationToken ct = default);
    Task<List<TemporaryPickupAuthorization>> ListAsync(int? studentId, IReadOnlyCollection<int>? studentIds, DateTime? since, CancellationToken ct = default);
    /// <summary>Pending or approved requests for the student whose window overlaps [fromUtc, untilUtc).</summary>
    Task<bool> HasOverlappingOpenAsync(int studentId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default);
    Task<HashSet<int>> GetPhotoIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
    Task AddAuditAsync(TemporaryPickupAuditEntry entry, CancellationToken ct = default);
    Task<List<TemporaryPickupAuditEntry>> GetAuditAsync(int id, CancellationToken ct = default);
    /// <summary>Throws <see cref="ConcurrencyConflictException"/> when the row changed since it was read.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Issues and checks the QR token for an approved temporary pickup. The token is an HMAC over the record id and
/// a random nonce, so it cannot be guessed or forged, it is never stored, and rotating the nonce (or clearing it
/// on revoke) invalidates every copy that was shared.
/// </summary>
public interface ITemporaryPickupTokenService
{
    string NewNonce();
    string CreateToken(int id, string nonce);
    /// <summary>The record id the token names, or null when the token is malformed. Does not prove validity.</summary>
    int? ReadId(string token);
    bool Verify(string token, int id, string? nonce);
}
