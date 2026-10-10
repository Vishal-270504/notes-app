namespace NotesApi.Models;

public record ErrorResponse(int status, string error, string message, string traceId);
