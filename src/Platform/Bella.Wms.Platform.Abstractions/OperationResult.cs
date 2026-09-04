namespace Bella.Wms.Platform.Abstractions;

/// <summary>
/// The .NET replacement for the ABL <c>NO-ERROR</c> / <c>ERROR-STATUS</c> /
/// <c>output lSuccess, output cMessage</c> convention used throughout <c>api/wms</c>.
/// </summary>
/// <remarks>
/// <para>
/// ABL source: nearly every method in <c>api/wms</c> returns
/// <c>output lSuccess as logical, output cMessage as character</c> — for example
/// <c>locusAPI.cls</c> <c>PickData</c>/<c>DoPick</c> handling at lines 2956-2996,
/// and <c>IWMSCommOutProcessor:Process</c>. There are 725 <c>NO-ERROR</c>
/// occurrences in the module.
/// </para>
/// <para>
/// The Phase 2 rulebook maps <c>NO-ERROR</c> to "Result/error contract; exception
/// only where appropriate". This type is that contract. Exceptions are reserved for
/// programmer error and for infrastructure failure that no caller can act on.
/// </para>
/// </remarks>
public readonly record struct OperationResult
{
    private OperationResult(bool succeeded, string message, string? code)
    {
        Succeeded = succeeded;
        Message = message;
        Code = code;
    }

    /// <summary>Maps to ABL <c>output lSuccess as logical</c>.</summary>
    public bool Succeeded { get; }

    /// <summary>Maps to ABL <c>output cMessage as character</c>. Empty when successful.</summary>
    public string Message { get; }

    /// <summary>Optional stable failure code for branching. ABL had no equivalent.</summary>
    public string? Code { get; }

    public bool Failed => !Succeeded;

    public static OperationResult Success() => new(true, string.Empty, null);

    public static OperationResult Failure(string message, string? code = null) =>
        new(false, message ?? string.Empty, code);

    /// <summary>
    /// Appends a message the way the ABL does when it accumulates one:
    /// <c>cMessage = (if cMessage ne "" then cMessage + "; " else "") + error-status:get-message(1)</c>
    /// (<c>locusAPI.cls:2963</c>).
    /// </summary>
    public OperationResult Append(string additional)
    {
        if (string.IsNullOrWhiteSpace(additional))
        {
            return this;
        }

        var combined = string.IsNullOrEmpty(Message) ? additional : $"{Message}; {additional}";
        return new OperationResult(Succeeded, combined, Code);
    }
}

/// <summary>Result carrying a value on success.</summary>
public readonly record struct OperationResult<T>
{
    private OperationResult(bool succeeded, T? value, string message, string? code)
    {
        Succeeded = succeeded;
        Value = value;
        Message = message;
        Code = code;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public string Message { get; }

    public string? Code { get; }

    public bool Failed => !Succeeded;

    public static OperationResult<T> Success(T value) => new(true, value, string.Empty, null);

    public static OperationResult<T> Failure(string message, string? code = null) =>
        new(false, default, message ?? string.Empty, code);

    public OperationResult WithoutValue() =>
        Succeeded ? OperationResult.Success() : OperationResult.Failure(Message, Code);
}
