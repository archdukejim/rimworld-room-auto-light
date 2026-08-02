using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RoomAutoLight
{
    /// <summary>
    /// Silences the power click while a group is switching itself.
    ///
    /// CompPowerTrader.PowerOn plays soundPowerOn/soundPowerOff, defaulting to Power_OnSmall and
    /// Power_OffSmall. Those defs are maxSimultaneous 1, so a four lamp room makes one click rather
    /// than four - but vanilla only ever plays them when a player flicks something or the grid
    /// fails, whereas an automated mod switches rooms all day. One click per room, every time
    /// anyone walks anywhere, is what players actually notice.
    ///
    /// The flag is held across a single PowerOn assignment in LightSuppression, so everything it
    /// catches is a sound that assignment caused. That covers modded lamps declaring their own
    /// soundPowerOn/soundPowerOff, which a whitelist of the two vanilla defs would have missed.
    ///
    /// Only the automated path is silenced. A lamp the player flicks by hand still clicks: vanilla
    /// re-powers that through PowerNetTick, which never runs inside this scope.
    /// </summary>
    public static class SwitchSoundSilencer
    {
        public static bool Active;
    }

    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    public static class SoundStarter_PlayOneShot_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!SwitchSoundSilencer.Active) return true;
            return !RoomAutoLightMod.Settings.silentSwitching;
        }
    }
}
