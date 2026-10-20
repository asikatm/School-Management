namespace SchoolManagement.Infrastructure.Common;

/// <summary>A business rule was broken; maps to HTTP 400 / 409.</summary>
public class DomainException(string message, string code = "domain_error", bool isConflict = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsConflict { get; } = isConflict;
}

public class NotFoundException(string entity, object key) : Exception($"{entity} '{key}' was not found.")
{
    public string Entity { get; } = entity;
}

public class ForbiddenException(string message = "You do not have permission to perform this action.") : Exception(message);
