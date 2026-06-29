namespace ClientPatches
{
    internal enum PatchState
    {
        Unknown,
        NotScanned,
        PatternNotFound,
        Ready,
        Applied,
        Restored,
        Error,
    }
}
