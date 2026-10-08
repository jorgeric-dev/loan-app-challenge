using LoanApp.Application.Applications;
using Microsoft.AspNetCore.Mvc;

namespace LoanApp.Api.Controllers;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController(SubmitApplicationHandler handler) : ControllerBase
{
    /// <summary>
    /// Submits a loan application. Approval and denial are both business outcomes, so both return 200;
    /// 400 is reserved for invalid input.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<SubmitApplicationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<SubmitApplicationResult> Submit(SubmitApplicationCommand command, CancellationToken cancellationToken) =>
        await handler.HandleAsync(command, cancellationToken);
}
