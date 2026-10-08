using LoanApp.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LoanApp.Api.Controllers;

/// <summary>
/// Production recovery for background event delivery: inspect the backlog and dead letters, and replay them
/// once the cause is fixed. Unauthenticated like the rest of this exercise; in production it sits behind
/// an admin policy / internal network.
/// </summary>
[ApiController]
[Route("api/operations/events")]
public sealed class OperationsController(IEventDeliveryOperations operations) : ControllerBase
{
    [HttpGet("status")]
    public Task<EventDeliveryStatus> Status(CancellationToken cancellationToken) =>
        operations.GetStatusAsync(cancellationToken);

    [HttpGet("dead-letters")]
    public Task<IReadOnlyList<DeadLetter>> DeadLetters(CancellationToken cancellationToken) =>
        operations.ListDeadLettersAsync(cancellationToken);

    [HttpPost("dead-letters/{id:long}/requeue")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Requeue(long id, CancellationToken cancellationToken) =>
        await operations.RequeueAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("dead-letters/requeue")]
    public async Task<object> RequeueAll(CancellationToken cancellationToken) =>
        new { requeued = await operations.RequeueAllAsync(cancellationToken) };
}
