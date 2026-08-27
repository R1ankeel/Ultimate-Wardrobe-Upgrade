namespace UltimateWardrobe.Archives;

public sealed class UnsupportedArchiveException : InvalidOperationException
{
    public UnsupportedArchiveException(string message) : base(message) { }
}

public sealed class ArchiveTooLargeException : InvalidOperationException
{
    public ArchiveTooLargeException(string message) : base(message) { }
}

public sealed class NativeLibraryNotFoundException : DllNotFoundException
{
    public NativeLibraryNotFoundException(string message) : base(message) { }
}

public sealed class ArchiveOpenException : InvalidOperationException
{
    public ArchiveOpenException(string message) : base(message) { }
}
