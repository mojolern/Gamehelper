namespace Launcher
{
    using System;
    using System.Linq;

    public static class MiscHelper
    {
        public static string GenerateRandomString()
        {
            const string characters = "qwertyuiopasdfghjklzxcvbnm" + "eioadfc";
            var random = new Random();

            char GetRandomCharacter() => characters[random.Next(0, characters.Length)];

            string GetWord() =>
                char.ToUpperInvariant(GetRandomCharacter()) +
                new string(Enumerable.Range(0, random.Next(5, 10))
                    .Select(_ => GetRandomCharacter())
                    .ToArray());

            return string.Join(' ', Enumerable.Range(0, random.Next(1, 4)).Select(_ => GetWord()));
        }
    }
}
