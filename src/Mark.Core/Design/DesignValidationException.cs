namespace Mark.Core.Design;

/// <summary>
/// A design edit was rejected because it would produce invalid geometry. The message is written for
/// the end user (e.g. "The mullion at 1100 mm would leave only 10 mm of glass (minimum 50 mm)").
/// When this is thrown, the design has NOT been modified.
/// </summary>
public sealed class DesignValidationException : InvalidOperationException
{
    public DesignValidationException(string message) : base(message) { }
}
