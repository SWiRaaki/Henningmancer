using HarmonyLib;
using Verse;

namespace Henningmancer.Source
{
internal class Henningmancer : Mod
    {
        public Henningmancer( ModContentPack content ) : base( content )
        {
            new Harmony("mods.raki.henningmancer").PatchAll();
        }
    }
}
