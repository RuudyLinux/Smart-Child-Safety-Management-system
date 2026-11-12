using System.Text.Json;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>A reversible stand-in for encryption, so storage and read paths can be checked without the crypto project.</summary>
internal sealed class TaggingProtector : IEmbeddingProtector
{
    public string Protect(string embeddingJson, int guardianId) => $"{EmbeddingFormat.ProtectedPrefix}t:{guardianId}:{embeddingJson}";

    public string Unprotect(string stored, int guardianId)
    {
        if (!EmbeddingFormat.IsProtected(stored)) return stored;
        var prefix = $"{EmbeddingFormat.ProtectedPrefix}t:{guardianId}:";
        return stored.StartsWith(prefix, StringComparison.Ordinal)
            ? stored[prefix.Length..]
            : throw new EmbeddingProtectionException("wrong guardian or key");
    }
}

public class GuardianFaceReferenceServiceTests
{
    private sealed class Harness
    {
        public FakeVerificationRepository Verifications { get; } = new();
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeFaceServiceClient Faces { get; } = new();
        public FakeFaceAuditLog Audit { get; } = new();
        public IEmbeddingProtector Protector { get; set; } = new TaggingProtector();
        public BiometricsOptions Options { get; } = new() { MaxReferencesPerGuardian = 3 };
        public MutableTimeProvider Time { get; }

        public Harness()
        {
            (_, Time) = TestClock.Create();
            Guardians.Guardians.Add(new Guardian { GuardianId = 1, FullName = "Priya Patel", Email = "p@x.test", PasswordHash = "x" });
            Guardians.Guardians.Add(new Guardian { GuardianId = 2, FullName = "Asha Rao", Email = "a@x.test", PasswordHash = "x" });
        }

        public GuardianFaceReferenceService Service() =>
            new(Verifications, Guardians, Faces, Protector, Audit, Options, Time);

        public static AddFaceReferenceRequest Photo(string? label = null) => new("IMAGE-BYTES", label);
    }

    private static readonly FaceActor Admin = FaceActor.Admin(9);

    [Fact]
    public async Task FirstFace_BecomesThePrimaryFace_NotAReference()
    {
        var h = new Harness();
        var result = await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Added);
        var primary = Assert.Single(h.Verifications.FaceData);
        Assert.Equal(1, primary.GuardianId);
        Assert.True(primary.IsActive);
        Assert.Empty(h.Verifications.References);
    }

    [Fact]
    public async Task SecondFace_IsStoredAsAReference_WithLabelAndQuality()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);

        var result = await h.Service().AddAsync(1, Harness.Photo("  Outdoor, side light  "), Admin);

        Assert.True(result.Value!.Added);
        var reference = Assert.Single(h.Verifications.References);
        Assert.Equal("Outdoor, side light", reference.Label);
        Assert.Equal("Admin", reference.CreatedByRole);
        Assert.Equal(9, reference.CreatedById);
        Assert.Contains("sharpness", reference.QualityJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddingsAreProtectedBeforeTheyAreStored()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.StartsWith(EmbeddingFormat.ProtectedPrefix, h.Verifications.FaceData[0].EmbeddingJson);
        Assert.StartsWith(EmbeddingFormat.ProtectedPrefix, h.Verifications.References[0].EmbeddingJson);
        ;
    }

    [Fact]
    public async Task LimitIsEnforced_AndNoFaceServiceCallIsMade()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) Assert.True((await h.Service().AddAsync(1, Harness.Photo(), Admin)).Succeeded);
        var callsBefore = h.Faces.RegisterReferenceCalls.Count;

        var result = await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.False(result.Succeeded);
        Assert.Contains("at most 3", result.Error);
        Assert.Equal(callsBefore, h.Faces.RegisterReferenceCalls.Count);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Reference && e.Outcome == "Rejected");
    }

    [Fact]
    public async Task EachGuardianHasTheirOwnLimit()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.True((await h.Service().AddAsync(2, Harness.Photo(), Admin)).Succeeded);
    }

    [Fact]
    public async Task ABadPhoto_IsRefusedWithTheReason_AndNothingIsStored()
    {
        var h = new Harness();
        h.Faces.NextAnalysis = Result<ImageAnalysis>.Success(new ImageAnalysis(
            "registration", 1, ["BLURRY"], [], ["The image is too blurry."], new ImageAnalysisMetrics(5, 150, 40, 200, 0, 0.99)));

        var result = await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.False(result.Succeeded);
        Assert.Equal("The image is too blurry.", result.Error);
        Assert.Empty(h.Verifications.FaceData);
        Assert.Empty(h.Faces.RegisterReferenceCalls);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Quality && e.Outcome == "Fail" && e.GuardianId == 1);
        Assert.Equal("registration", h.Faces.LastPurpose);
    }

    [Fact]
    public async Task APhotoThatDoesNotLookLikeTheRegisteredFace_IsRefused_SoAStrangerCannotBeAdded()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        h.Faces.NextBestExistingScore = 0.12;

        var result = await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.False(result.Succeeded);
        Assert.Contains("does not look like the face already registered", result.Error);
        Assert.Empty(h.Verifications.References);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Reference && e.Outcome == "Rejected" && e.Detail == "unlike existing faces");
    }

    [Theory]
    [InlineData(0.40)]
    [InlineData(0.41)]
    public async Task APhotoAtOrAboveTheThreshold_IsAccepted(double score)
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        h.Faces.NextBestExistingScore = score;

        Assert.True((await h.Service().AddAsync(1, Harness.Photo(), Admin)).Value!.Added);
    }

    [Fact]
    public async Task JustBelowTheThreshold_IsRefused()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        h.Faces.NextBestExistingScore = 0.399;

        Assert.False((await h.Service().AddAsync(1, Harness.Photo(), Admin)).Succeeded);
    }

    [Fact]
    public async Task IfNothingOnFileCouldBeCompared_TheReferenceIsRefused()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        h.Faces.NextBestExistingScore = null;

        var result = await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be compared", result.Error);
    }

    [Fact]
    public async Task TheVeryFirstFace_HasNothingToResembleAndIsAccepted()
    {
        var h = new Harness();
        h.Faces.NextBestExistingScore = null;

        Assert.True((await h.Service().AddAsync(1, Harness.Photo(), Admin)).Value!.Added);
    }

    [Fact]
    public async Task ANearIdenticalPhoto_IsNotStoredTwice()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        h.Faces.NextDuplicateOf = 0;

        var result = await h.Service().AddAsync(1, Harness.Photo(), Admin);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Added);
        Assert.True(result.Value.Duplicate);
        Assert.Empty(h.Verifications.References);
    }

    [Fact]
    public async Task TheExistingReferencesAreSentForTheDuplicateCheck_InReadableForm()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        await h.Service().AddAsync(1, Harness.Photo(), Admin);

        await h.Service().AddAsync(1, Harness.Photo(), Admin);

        var sent = h.Faces.RegisterReferenceCalls.Last();
        Assert.Equal([0, 1], sent.Select(r => r.ReferenceId).ToArray());
        Assert.All(sent, r => Assert.DoesNotContain(EmbeddingFormat.ProtectedPrefix, r.EmbeddingJson));
    }

    [Fact]
    public async Task UnknownGuardian_IsNotFound()
    {
        var result = await new Harness().Service().AddAsync(99, Harness.Photo(), Admin);
        Assert.False(result.Succeeded);
        Assert.Equal("Guardian not found.", result.Error);
    }

    [Fact]
    public async Task List_ShowsMetadataOnly_NeverAnEmbedding()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        await h.Service().AddAsync(1, Harness.Photo("Indoor"), Admin);

        var list = (await h.Service().ListAsync(1)).Value!;

        Assert.Equal(2, list.References.Count);
        Assert.Equal("Primary", list.References[0].Source);
        Assert.Equal(0, list.References[0].Id);
        Assert.Equal("Additional", list.References[1].Source);
        Assert.Equal("Indoor", list.References[1].Label);
        Assert.True(list.CanAdd);
        var json = JsonSerializer.Serialize(list);
        Assert.DoesNotContain("0.5", json);
        Assert.DoesNotContain("enc:", json);
        Assert.DoesNotContain("mbedding", json);
    }

    [Fact]
    public async Task List_ForAGuardianWithNoFace_IsEmpty()
    {
        var list = (await new Harness().Service().ListAsync(1)).Value!;
        Assert.Empty(list.References);
        Assert.True(list.CanAdd);
    }

    [Fact]
    public async Task Remove_DeactivatesTheReference_AndFreesASlot()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) await h.Service().AddAsync(1, Harness.Photo(), Admin);

        var removed = await h.Service().RemoveAsync(1, h.Verifications.References[0].Id, Admin);

        Assert.True(removed.Succeeded);
        Assert.False(h.Verifications.References[0].IsActive);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Reference && e.Outcome == "Removed");
        Assert.True((await h.Service().AddAsync(1, Harness.Photo(), Admin)).Succeeded);
    }

    [Fact]
    public async Task Remove_ThePrimaryFace_IsRefused()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);

        var result = await h.Service().RemoveAsync(1, 0, Admin);

        Assert.False(result.Succeeded);
        Assert.True(h.Verifications.FaceData[0].IsActive);
    }

    [Fact]
    public async Task Remove_AnotherGuardiansReference_IsNotFound()
    {
        var h = new Harness();
        await h.Service().AddAsync(2, Harness.Photo(), Admin);
        await h.Service().AddAsync(2, Harness.Photo(), Admin);
        var theirs = h.Verifications.References[0].Id;

        var result = await h.Service().RemoveAsync(1, theirs, Admin);

        Assert.False(result.Succeeded);
        Assert.True(h.Verifications.References[0].IsActive);
    }

    [Fact]
    public async Task Remove_AReferenceTwice_SecondIsNotFound()
    {
        var h = new Harness();
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        await h.Service().AddAsync(1, Harness.Photo(), Admin);
        var id = h.Verifications.References[0].Id;
        await h.Service().RemoveAsync(1, id, Admin);

        Assert.False((await h.Service().RemoveAsync(1, id, Admin)).Succeeded);
    }

    [Fact]
    public async Task LoadEmbeddings_ReturnsPrimaryAsIdZeroAndSkipsRemovedOnes()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) await h.Service().AddAsync(1, Harness.Photo(), Admin);
        await h.Service().RemoveAsync(1, h.Verifications.References[0].Id, Admin);

        var loaded = (await h.Service().LoadEmbeddingsAsync(1)).Value!;

        Assert.Equal(2, loaded.Count);
        Assert.Equal(0, loaded[0].ReferenceId);
        Assert.Equal(h.Verifications.References[1].Id, loaded[1].ReferenceId);
    }

    [Fact]
    public async Task LoadEmbeddings_WhenTheKeyIsWrong_FailsClosed_AndIsAudited()
    {
        var h = new Harness();
        h.Verifications.FaceData.Add(new GuardianFaceData
        {
            GuardianId = 1, EmbeddingJson = $"{EmbeddingFormat.ProtectedPrefix}t:777:[1]", Model = "m", IsActive = true
        });

        var result = await h.Service().LoadEmbeddingsAsync(1);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be read", result.Error);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Access && e.Outcome == "Failed");
    }

    [Fact]
    public async Task LoadEmbeddings_ReadsOldPlainRowsUnchanged()
    {
        var h = new Harness();
        h.Verifications.FaceData.Add(new GuardianFaceData { GuardianId = 1, EmbeddingJson = "[0.1,0.2]", Model = "m", IsActive = true });

        var loaded = (await h.Service().LoadEmbeddingsAsync(1)).Value!;

        Assert.Equal("[0.1,0.2]", Assert.Single(loaded).EmbeddingJson);
    }

    [Fact]
    public async Task NullProtector_RefusesAnEncryptedRow_InsteadOfGuessing()
    {
        var h = new Harness { Protector = new NullEmbeddingProtector() };
        h.Verifications.FaceData.Add(new GuardianFaceData
        {
            GuardianId = 1, EmbeddingJson = $"{EmbeddingFormat.ProtectedPrefix}k1:AAAA", Model = "m", IsActive = true
        });

        Assert.False((await h.Service().LoadEmbeddingsAsync(1)).Succeeded);
    }
}

public class VerificationWithSeveralReferencesTests
{
    private sealed class Harness
    {
        public FakeVerificationRepository Verifications { get; } = new();
        public FakeStudentRepository Students { get; } = new();
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeFaceServiceClient Faces { get; } = new();
        public VerificationService Service { get; }

        public Harness(int extraReferences)
        {
            var (clock, time) = TestClock.Create();
            var protector = new NullEmbeddingProtector();
            var references = new GuardianFaceReferenceService(Verifications, Guardians, Faces, protector, new FakeFaceAuditLog(), new BiometricsOptions(), time);
            Service = new VerificationService(Verifications, Students, Guardians, new FakeJourneyRepository(),
                new FakeNotificationRepository(), new FakePushNotificationSender(), Faces, new JourneyValidationService(),
                clock, new VerificationOptions(), protector, references);

            Students.Students.Add(new Student { StudentId = 1, FullName = "Rahul", DateOfBirth = new DateTime(2016, 1, 1), Status = RecordStatus.Active });
            Guardians.Guardians.Add(new Guardian { GuardianId = 1, FullName = "Priya", Email = "p@x.test", PasswordHash = "x", Status = RecordStatus.Active });
            Students.Links.Add(new StudentGuardian { StudentId = 1, GuardianId = 1, IsAuthorizedForPickup = true });
            Verifications.FaceData.Add(new GuardianFaceData { GuardianId = 1, EmbeddingJson = "[0.1,0.2]", Model = "m", IsActive = true });
            for (var i = 0; i < extraReferences; i++)
                Verifications.References.Add(new GuardianFaceReference
                {
                    Id = i + 1, GuardianId = 1, EmbeddingJson = $"[0.{i + 3},0.4]", Model = "m", IsActive = true, CreatedByRole = "Admin"
                });
        }

        public Task<Result<FaceVerifyResultDto>> VerifyAsync() => Service.VerifyAsync(new FaceVerifyRequest(1, 1, "img"));
    }

    [Fact]
    public async Task OneFaceOnFile_UsesTheOriginalSingleComparison()
    {
        var h = new Harness(extraReferences: 0);
        var result = await h.VerifyAsync();

        Assert.True(result.Value!.FaceMatched);
        Assert.Empty(h.Faces.IdentifyCalls);
    }

    [Fact]
    public async Task SeveralFacesOnFile_AreAllComparedInOneCall()
    {
        var h = new Harness(extraReferences: 2);

        await h.VerifyAsync();

        var call = Assert.Single(h.Faces.IdentifyCalls);
        var candidate = Assert.Single(call);
        Assert.Equal(1, candidate.GuardianId);
        Assert.Equal([0, 1, 2], candidate.References.Select(r => r.ReferenceId).ToArray());
    }

    [Fact]
    public async Task BestScoreAtOrAboveTheThreshold_Matches_AndTheScoreIsRecorded()
    {
        var h = new Harness(extraReferences: 1);
        h.Faces.NextScore = 0.41;

        var result = await h.VerifyAsync();

        Assert.True(result.Value!.FaceMatched);
        Assert.Equal(0.41, result.Value.MatchScore, 3);
        Assert.Equal(VerificationResult.Match, h.Verifications.Verifications.Single().Result);
    }

    [Fact]
    public async Task BestScoreBelowTheThreshold_DoesNotMatch()
    {
        var h = new Harness(extraReferences: 1);
        h.Faces.NextScore = 0.39;

        var result = await h.VerifyAsync();

        Assert.False(result.Value!.FaceMatched);
        Assert.Equal(VerificationResult.NoMatch, h.Verifications.Verifications.Single().Result);
    }

    [Fact]
    public async Task TheThresholdIsTheFaceServicesOwn_NotOneInventedHere()
    {
        var h = new Harness(extraReferences: 1);
        h.Faces.NextScore = 0.45;
        h.Faces.IdentifyThreshold = 0.50;

        Assert.False((await h.VerifyAsync()).Value!.FaceMatched);
    }

    [Fact]
    public async Task WhenTheFaceServiceFails_NothingIsRecorded()
    {
        var h = new Harness(extraReferences: 1);
        h.Faces.NextIdentification = Result<FaceIdentification>.Failure("Face service is unavailable.");

        var result = await h.VerifyAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task WhenNoReferenceWasReadable_ItIsAFailure_NotANoMatch()
    {
        var h = new Harness(extraReferences: 1);
        h.Faces.NextIdentification = Result<FaceIdentification>.Success(new FaceIdentification(0.40, "m",
            [new GuardianIdentification(1, null, null, [], [0, 1])]));

        var result = await h.VerifyAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task APrimaryFaceThatCannotBeDecrypted_StopsTheVerification()
    {
        var h = new Harness(extraReferences: 0);
        h.Verifications.FaceData[0].EmbeddingJson = $"{EmbeddingFormat.ProtectedPrefix}k1:AAAA";

        var result = await h.VerifyAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("could not be read", result.Error);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task RemovedReferences_AreNotCompared()
    {
        var h = new Harness(extraReferences: 2);
        h.Verifications.References[0].IsActive = false;

        await h.VerifyAsync();

        var ids = h.Faces.IdentifyCalls.Single().Single().References.Select(r => r.ReferenceId).ToArray();
        Assert.Equal([0, 2], ids);
    }
}
