using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Guardian pickup verification: Student Identified → Authorized Guardian → Face Capture →
/// Face Verification → Match? → Approve/Reject → Record → Notification.
/// Backs the "Student Drop-off & Pickup Verification" demo/operational page.
/// </summary>
[ApiController]
[Route("api/verification")]
[Authorize(Policy = "AdminOnly")]
public class VerificationController : ControllerBase
{
    private readonly IVerificationService _service;
    public VerificationController(IVerificationService service) => _service = service;

    [HttpPost("face-verify")]
    public async Task<IActionResult> Verify(FaceVerifyRequest request, CancellationToken ct)
    {
        var result = await _service.VerifyAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve(ApprovePickupRequest request, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(request, this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("reject")]
    public async Task<IActionResult> Reject(RejectPickupRequest request, CancellationToken ct)
    {
        var result = await _service.RejectAsync(request, this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int? studentId, CancellationToken ct) =>
        Ok(await _service.GetHistoryAsync(studentId, ct));
}
