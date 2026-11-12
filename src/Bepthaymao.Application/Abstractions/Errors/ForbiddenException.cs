namespace Bepthaymao.Application.Abstractions.Errors;

public sealed class ForbiddenException() : Exception("The caller is not allowed to access this resource.");
