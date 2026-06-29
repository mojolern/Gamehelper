namespace ClientPatches
{
    internal sealed class ZoomPatch : ClientBytePatch
    {
        private static readonly byte?[] Pattern =
        [
            0xF3, 0x0F, 0x5F, 0xC8,
            0xF3, 0x0F, 0x5D, 0x0D,
            null, null, null, null,
            0xF3, 0x0F, 0x11, 0x8E,
        ];

        private static readonly byte[] PatchBytes = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

        public ZoomPatch()
            : base("Infinite Zoom", Pattern, 4, PatchBytes)
        {
        }
    }
}
