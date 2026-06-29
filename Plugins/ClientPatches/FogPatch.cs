namespace ClientPatches
{
    internal sealed class FogPatch : ClientBytePatch
    {
        private static readonly byte?[] Pattern =
        [
            0xF3, 0x0F, 0x59, 0x51,
            null,
            0xF3, 0x0F, 0x58, 0xC1,
        ];

        private static readonly byte[] PatchBytes = [0x90, 0x90, 0x90, 0x90, 0x90];

        public FogPatch()
            : base("No Atlas Fog", Pattern, 0, PatchBytes)
        {
        }
    }
}
