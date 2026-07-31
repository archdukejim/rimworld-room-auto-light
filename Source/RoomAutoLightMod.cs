using HarmonyLib;
using UnityEngine;
using Verse;

namespace RoomAutoLight
{
    public class RoomAutoLightMod : Mod
    {
        private static RoomAutoLightSettings settings;

        public static RoomAutoLightSettings Settings
        {
            get
            {
                if (settings == null) settings = new RoomAutoLightSettings();
                return settings;
            }
        }

        public RoomAutoLightMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<RoomAutoLightSettings>();
            Harmony harmony = new Harmony("archdukejim.RoomAutoLight");
            harmony.PatchAll();
        }

        public override string SettingsCategory()
        {
            return "Room Auto Light";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            SettingsUI.Draw(inRect, Settings);
            base.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            Settings.NotifyDefFiltersChanged();

            if (Current.ProgramState != ProgramState.Playing || Find.Maps == null) return;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                RoomLightManager manager = Find.Maps[i].GetComponent<RoomLightManager>();
                if (manager != null) manager.MarkDirty();
            }
        }
    }
}
