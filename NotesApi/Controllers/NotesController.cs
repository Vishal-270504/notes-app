using Microsoft.AspNetCore.Mvc;
using NotesApi.Models;
using NotesApi.Services;

namespace NotesApi.Controllers;

// Controller: thin HTTP layer. Receives requests, calls service, returns HTTP results.
[ApiController]
[Route("notes")]
public class NotesController : ControllerBase
{
    private readonly NotesService _service;

    public NotesController(NotesService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotes(int? page, int? limit)
    {
        var userId = GetUserId();
        var (success, errors, result) = await _service.GetNotesAsync(userId, page, limit);
        return success ? Ok(result) : BadRequest(errors);
    }

    [HttpPost]
    public async Task<IActionResult> CreateNote([FromBody] NoteCreateDto dto)
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
        var userId = GetUserId();
        var (success, errors, note) = await _service.CreateNoteAsync(userId, dto.Title, dto.Description);

        if (!success)
        {
            var message = string.Join("; ", errors);
            return BadRequest(new ErrorResponse(400, "ValidationError", message, traceId));
        }

        return Created($"/notes/{note!.Id}", note);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetNote(Guid id)
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
        var userId = GetUserId();
        var (success, errors, note) = await _service.GetNoteByIdAsync(userId, id);

        if (!success)
        {
            var message = errors[0];
            if (message.Contains("not found"))
                return NotFound(new ErrorResponse(404, "NotFound", message, traceId));
            return StatusCode(403, new ErrorResponse(403, "Forbidden", message, traceId));
        }

        return Ok(note);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteNote(Guid id)
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
        var userId = GetUserId();
        var (success, errors) = await _service.DeleteNoteAsync(userId, id);

        if (!success)
        {
            var message = errors[0];
            if (message.Contains("not found"))
                return NotFound(new ErrorResponse(404, "NotFound", message, traceId));
            return StatusCode(403, new ErrorResponse(403, "Forbidden", message, traceId));
        }

        return NoContent();
    }

    [HttpPost("seed")]
    public async Task<IActionResult> SeedNotes()
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
        var userId = GetUserId();
        var (success, errors, result) = await _service.SeedNotesAsync(userId);

        if (!success)
        {
            return BadRequest(new ErrorResponse(400, "ValidationError", errors[0], traceId));
        }

        return Ok(result);
    }

    [HttpGet("benchmark")]
    public async Task<IActionResult> Benchmark()
    {
        var userId = GetUserId();
        var (success, errors, result) = await _service.BenchmarkAsync(userId);
        return success ? Ok(result) : BadRequest(errors);
    }

    private Guid GetUserId()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.Parse(id!);
    }
}