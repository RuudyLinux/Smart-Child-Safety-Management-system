using System.Text.Json;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class GuardianFaceReferenceService : IGuardianFaceReferenceService
{
    public const int PrimaryReferenceId = 0;

    private readonly IVerificationRepository _verifications;
    private readonly IGuardianRepository _guardians;
    private readonly IFaceServiceClient _faces;
    private readonly IEmbeddingProtector _protector;
    private readonly IFaceAuditLog _audit;
    private readonly BiometricsOptions _options;
    private readonly TimeProvider _time;
    private readonly IAuditTrail _trail;

    public GuardianFaceReferenceService(
        IVerificationRepository verifications, IGuardianRepository guardians, IFaceServiceClient faces,
        IEmbeddingProtector protector, IFaceAuditLog audit, BiometricsOptions options, TimeProvider time, IAuditTrail? trail = null)
    {
        _trail = trail ?? NullAuditTrail.Instance;
        _verifications = verifications;
        _guardians = guardians;
        _faces = faces;
        _protector = protector;
        _audit = audit;
        _options = options;
        _time = time;
    }

    private int Max => Math.Max(1, _options.MaxReferencesPerGuardian);

    public async Task<Result<GuardianFaceReferencesDto>> ListAsync(int guardianId, CancellationToken ct = default)
    {
        if (await _guardians.GetByIdAsync(guardianId, ct) is null)
            return Result<GuardianFaceReferencesDto>.Failure("Guardian not found.");

        var primary = await _verifications.GetFaceDataAsync(guardianId, ct);
        var extras = await _verifications.GetActiveReferencesAsync(guardianId, ct);

        var items = new List<FaceReferenceDto>();
        if (primary is { IsActive: true })
            items.Add(new FaceReferenceDto(PrimaryReferenceId, "Primary", null, primary.RegisteredAt, null));
        items.AddRange(extras.Select(r => new FaceReferenceDto(r.Id, "Additional", r.Label, r.CreatedAt, ReadQuality(r.QualityJson))));

        return Result<GuardianFaceReferencesDto>.Success(new GuardianFaceReferencesDto(items, Max, items.Count < Max));
    }

    public async Task<Result<AddFaceReferenceResultDto>> AddAsync(
        int guardianId, AddFaceReferenceRequest request, FaceActor actor, CancellationToken ct = default)
    {
        if (await _guardians.GetByIdAsync(guardianId, ct) is null)
            return Result<AddFaceReferenceResultDto>.Failure("Guardian not found.");

        var primary = await _verifications.GetFaceDataAsync(guardianId, ct);
        var hasPrimary = primary is { IsActive: true };
        var extras = await _verifications.GetActiveReferencesAsync(guardianId, ct);
        if ((hasPrimary ? 1 : 0) + extras.Count >= Max)
        {
            await Audit(guardianId, actor, "Rejected", "limit");
            return Result<AddFaceReferenceResultDto>.Failure(
                $"A guardian can have at most {Max} reference photos. Remove one before adding another.");
        }

        // Each photo is judged on its own before any embedding is stored.
        var analysis = await _faces.AnalyzeImageAsync(request.ImageBase64, FaceImageAnalysisService.RegistrationPurpose, ct);
        if (!analysis.Succeeded)
            return Result<AddFaceReferenceResultDto>.Failure(analysis.Error ?? "Face service error.");
        var quality = FaceQualityPolicy.Assess(analysis.Value!);
        await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Quality, "AddReference", quality.Quality.ToString(), actor,
            GuardianId: guardianId, Detail: $"issues={string.Join(',', quality.Issues)}"), ct);
        await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Detection, "AddReference", quality.FaceDetection.ToString(), actor,
            GuardianId: guardianId, Detail: $"faces={quality.FaceCount}"), ct);
        if (!quality.Passed)
            return Result<AddFaceReferenceResultDto>.Failure(quality.Messages.FirstOrDefault() ?? "The photo is not suitable for face recognition.");

        var existing = await LoadEmbeddingsAsync(guardianId, ct);
        if (!existing.Succeeded) return Result<AddFaceReferenceResultDto>.Failure(existing.Error!);

        var registered = await _faces.RegisterReferenceAsync(request.ImageBase64, existing.Value!, ct);
        if (!registered.Succeeded)
            return Result<AddFaceReferenceResultDto>.Failure(registered.Error ?? "Face registration failed.");

        // A further reference must look like a face already on file. Otherwise anyone able to add a
        // photo could register a different person as this guardian. Same threshold as matching;
        // a changed appearance is handled by replacing the primary face instead.
        if (existing.Value!.Count > 0)
        {
            var resembles = registered.Value!.BestExistingScore is { } best && best >= registered.Value.Threshold;
            if (!resembles)
            {
                await Audit(guardianId, actor, "Rejected", registered.Value.BestExistingScore is null ? "not comparable" : "unlike existing faces");
                return Result<AddFaceReferenceResultDto>.Failure(registered.Value.BestExistingScore is null
                    ? "The registered face could not be compared with this photo. Replace the primary face instead."
                    : "This photo does not look like the face already registered for this guardian, so it cannot be added as another reference. To change the registered face completely, replace the primary face.");
            }
        }

        if (registered.Value!.DuplicateOf is not null)
        {
            await Audit(guardianId, actor, "Duplicate", $"similar to reference {registered.Value.DuplicateOf}");
            return Result<AddFaceReferenceResultDto>.Success(new AddFaceReferenceResultDto(
                false, true, "A very similar photo is already registered, so this one was not added. Use a photo in different light or from a slightly different angle.",
                null, analysis.Value!.Metrics));
        }

        var protectedEmbedding = _protector.Protect(registered.Value.EmbeddingJson, guardianId);
        var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        GuardianFaceReference? added = null;

        if (!hasPrimary)
        {
            await _verifications.UpsertFaceDataAsync(new GuardianFaceData
            {
                GuardianId = guardianId, EmbeddingJson = protectedEmbedding, Model = registered.Value.Model,
                IsActive = true, RegisteredAt = _time.GetUtcNow().UtcDateTime
            }, ct);
        }
        else
        {
            added = new GuardianFaceReference
            {
                GuardianId = guardianId, EmbeddingJson = protectedEmbedding, Model = registered.Value.Model,
                Label = label, QualityJson = SerializeQuality(analysis.Value!.Metrics), IsActive = true,
                CreatedAt = _time.GetUtcNow().UtcDateTime, CreatedByRole = actor.Role, CreatedById = actor.Id
            };
            await _verifications.AddReferenceAsync(added, ct);
        }
        // The action only: neither the photo nor the embedding is recorded.
        await _trail.SaveWithAuditAsync(_verifications.SaveChangesAsync,
            () => new AuditEvent(added is null ? "GuardianFaceRegistered" : "GuardianFaceReferenceAdded", "Guardian", guardianId,
                added is null ? $"Face registered for guardian {guardianId}" : $"Reference face {added.Id} added for guardian {guardianId}")
            { GuardianId = guardianId, Details = new { referenceId = added?.Id, label } }, ct);

        var referenceId = added?.Id ?? PrimaryReferenceId;
        await Audit(guardianId, actor, "Added", added is null ? "primary" : $"reference {referenceId}");
        return Result<AddFaceReferenceResultDto>.Success(new AddFaceReferenceResultDto(
            true, false, hasPrimary ? "Reference photo added." : "Face registered.", referenceId, analysis.Value!.Metrics));
    }

    public async Task<Result> RemoveAsync(int guardianId, int referenceId, FaceActor actor, CancellationToken ct = default)
    {
        if (referenceId == PrimaryReferenceId)
            return Result.Failure("The primary face cannot be removed here. Register a new face to replace it.");

        var reference = await _verifications.GetReferenceAsync(guardianId, referenceId, ct);
        if (reference is null || !reference.IsActive)
            return Result.Failure("Reference photo not found.");

        reference.IsActive = false;
        _trail.Record(new AuditEvent("GuardianFaceReferenceRemoved", "Guardian", guardianId, $"Reference face {referenceId} removed for guardian {guardianId}")
        { GuardianId = guardianId, Details = new { referenceId } });
        await _verifications.SaveChangesAsync(ct);
        await Audit(guardianId, actor, "Removed", $"reference {referenceId}");
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ReferenceEmbedding>>> LoadEmbeddingsAsync(int guardianId, CancellationToken ct = default)
    {
        var list = new List<ReferenceEmbedding>();
        try
        {
            var primary = await _verifications.GetFaceDataAsync(guardianId, ct);
            if (primary is { IsActive: true })
                list.Add(new ReferenceEmbedding(PrimaryReferenceId, _protector.Unprotect(primary.EmbeddingJson, guardianId)));
            foreach (var r in await _verifications.GetActiveReferencesAsync(guardianId, ct))
                list.Add(new ReferenceEmbedding(r.Id, _protector.Unprotect(r.EmbeddingJson, guardianId)));
        }
        catch (EmbeddingProtectionException ex)
        {
            await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Access, "ReadEmbeddings", "Failed", FaceActor.System,
                GuardianId: guardianId, Detail: ex.Message), ct);
            return Result<IReadOnlyList<ReferenceEmbedding>>.Failure(
                "The registered face data could not be read. Register the face again, or ask an administrator to check the biometric key.");
        }
        return Result<IReadOnlyList<ReferenceEmbedding>>.Success(list);
    }

    private Task Audit(int guardianId, FaceActor actor, string outcome, string detail) =>
        _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Reference, "Reference", outcome, actor, GuardianId: guardianId, Detail: detail));

    private static string SerializeQuality(ImageAnalysisMetrics m) =>
        JsonSerializer.Serialize(new FaceReferenceQualityDto(m.Sharpness, m.Brightness, m.FacePx, m.YawDegEstimate));

    private static FaceReferenceQualityDto? ReadQuality(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FaceReferenceQualityDto>(json); }
        catch (JsonException) { return null; }
    }
}
