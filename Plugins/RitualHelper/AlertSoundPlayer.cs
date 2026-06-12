using System;
using System.Media;
using System.Runtime.InteropServices;

namespace RitualHelper
{
    internal static class AlertSoundPlayer
    {
        private const uint MbIconHand = 0x00000010;
        private const uint MbIconQuestion = 0x00000020;
        private const uint MbIconExclamation = 0x00000030;
        private const uint MbIconAsterisk = 0x00000040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MessageBeep(uint uType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Beep(uint frequency, uint duration);

        // 0 = Asterisk, 1 = Exclamation, 2 = Hand, 3 = Question, 4 = Beep
        public static void Play(int soundType)
        {
            try
            {
                if (soundType == 4)
                {
                    if (!Beep(880, 180))
                        Console.Beep(880, 180);
                    return;
                }

                var beepType = soundType switch
                {
                    1 => MbIconExclamation,
                    2 => MbIconHand,
                    3 => MbIconQuestion,
                    _ => MbIconAsterisk,
                };

                if (!MessageBeep(beepType))
                    PlaySystemSoundFallback(soundType);
            }
            catch
            {
                try { PlaySystemSoundFallback(soundType); } catch { }
            }
        }

        private static void PlaySystemSoundFallback(int soundType)
        {
            switch (soundType)
            {
                case 1:
                    SystemSounds.Exclamation.Play();
                    break;
                case 2:
                    SystemSounds.Hand.Play();
                    break;
                case 3:
                    SystemSounds.Question.Play();
                    break;
                case 4:
                    Console.Beep(880, 180);
                    break;
                default:
                    SystemSounds.Asterisk.Play();
                    break;
            }
        }
    }
}
