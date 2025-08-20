using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Henningmancer.Source
{
    public class GrindCorpseToBoneMealExtension : DefModExtension
    {
        public ThingDef ProductDef = DefDatabase<ThingDef>.GetNamedSilentFail( "BoneMeal" );
        public bool ScaleByBodySize = true;
        public bool TreatDessicatedAsSkeleton = true;
        public float YieldBody = 0.65f;
        public float YieldSkeleton = 0.8f;
        public float YieldHumanlike = 1.0f;
        public float YieldAnimal = 0.8f;
        public float YieldAny = 0.7f;
        public float YieldLeather = 0.5f;
        public float YieldMeat = 0.25f;
        public ThingDef MeatDef;
    }

    [HarmonyPatch]
    static class GrindCorpseToBoneMeal
    {
        static MethodBase TargetMethod()
        {
            var tPrecept = AccessTools.TypeByName("RimWorld.Precept_ThingStyle") ?? AccessTools.TypeByName("Precept_ThingStyle");
            var tStyle = AccessTools.TypeByName("Verse.ThingStyleDef") ?? AccessTools.TypeByName("ThingStyleDef");
            return AccessTools.Method(typeof(GenRecipe), "MakeRecipeProducts", new[]
            {
                typeof(RecipeDef), typeof(Pawn), typeof(List<Thing>), typeof(Thing),
                typeof(IBillGiver), tPrecept, tStyle, typeof(int?)
            });
        }

        static void Postfix(RecipeDef recipeDef, Pawn worker, List<Thing> ingredients, Thing dominantIngredient, ref IEnumerable<Thing> __result)
        {
            TryReplace(recipeDef, worker, ingredients, ref __result);
        }

        static void TryReplace( RecipeDef recipeDef, Pawn worker, List<Thing> ingredients, ref IEnumerable<Thing> __result )
        {
            Log.Message( "[Corpcinerator] Grind started" );
            if ( recipeDef.defName != "GrindCorpseToBoneMeal" ) return;
            var ext = recipeDef.GetModExtension<GrindCorpseToBoneMealExtension>();
            if ( ext == null || ext.ProductDef == null ) return;

            int total = 0;
            var stacks = new List<Thing>();
            foreach (var thing in ingredients)
            {
                if ( thing is Corpse corpse && corpse.InnerPawn?.RaceProps != null )
                {
                    var bodySize = corpse.InnerPawn.BodySize;
                    var bodyMass = corpse.def.BaseMass;
                    var stageFactor = corpse.IsDessicated() ? ext.YieldSkeleton : ext.YieldBody;
                    var raceFactor = corpse.InnerPawn.RaceProps.Humanlike ? ext.YieldHumanlike : (corpse.InnerPawn.RaceProps.Animal ? ext.YieldAnimal : ext.YieldAny);
                    total += (int)System.Math.Max( 1, bodyMass * stageFactor * raceFactor );
                    if ( !corpse.IsDessicated() )
                    {
                        var butchered = corpse.ButcherProducts(worker, 1.0f);
                        foreach (var item in butchered)
                        {
                            if (item.def.IsMeat)
                            {
                                var stack = ThingMaker.MakeThing(ext.MeatDef != null ? ext.MeatDef : item.def);
                                stack.stackCount = (int)(item.stackCount * ext.YieldMeat);
                                stacks.Add(stack);
                            }
                            else if (item.def.IsLeather)
                            {
                                var stack = ThingMaker.MakeThing(item.def);
                                stack.stackCount = (int)(item.stackCount * ext.YieldLeather);
                                stacks.Add(stack);
                            }
                        }
                    }
                }
            }
            var bonemeal = ThingMaker.MakeThing( ext.ProductDef );
            bonemeal.stackCount = total;
            stacks.Add( bonemeal );
            __result = stacks;
        }
    }

    public static class GrindCorpseToBoneMealCalc
    {
        public static GrindCorpseToBoneMealExtension GetExtension()
        {
            var r = DefDatabase<RecipeDef>.GetNamedSilentFail( "GrindCorpseToBoneMeal" );
            return r?.GetModExtension<GrindCorpseToBoneMealExtension>() ?? new GrindCorpseToBoneMealExtension();
        }

        public static float GetFactorMass( Pawn pawn, bool desiccated, GrindCorpseToBoneMealExtension ext )
        {
            var bodyMass = pawn.def.BaseMass;
            var stageFactor = desiccated ? ext.YieldSkeleton : ext.YieldBody;
            var raceFactor = pawn.RaceProps.Humanlike ? ext.YieldHumanlike : (pawn.RaceProps.Animal ? ext.YieldAnimal : ext.YieldAny);
            return bodyMass * stageFactor * raceFactor;
        }

        public static int EstimateForPawn( StatRequest req, bool desiccated = false )
        {
            if ( req == null )
                return 0;

            Pawn pawn;
            if (req.Thing is Corpse)
                pawn = (req.Thing as Corpse).InnerPawn;
            else if (req.Thing is Pawn)
                pawn = req.Thing as Pawn;
            else return 0;

            if (pawn?.RaceProps == null)
                return 0;

            var ext = GetExtension();
            return (int)System.Math.Max( 1, GetFactorMass( pawn, desiccated, ext ) );
        }
    }

    public class RecipeWorkerCounterGrindCorpseToBoneMeal : RecipeWorkerCounter
    {
        public override bool CanCountProducts(Bill_Production bill) => true;
        public override string ProductsDescription( Bill_Production bill ) => "bone meal";
        
        public override int CountProducts( Bill_Production bill )
        {
            int num = 0;
            List<ThingDef> childThingDefs = ThingCategoryDefOf.Corpses.childThingDefs;
            for (int i = 0; i < childThingDefs.Count; i++)
            {
                if ( bill.ingredientFilter.Allows( childThingDefs[i] ) )
                    num += bill.Map.resourceCounter.GetCount(childThingDefs[i]);
            }

            return num;
        }
    }

    public class StatWorkerGrindCorpseToBoneMeal : StatWorker
    {
        public override bool ShouldShowFor( StatRequest req )
            => req.HasThing && (req.Thing is Pawn || req.Thing is Corpse );

        public override float GetValueUnfinalized( StatRequest req, bool applyPostProcess = true )
        {
            return GrindCorpseToBoneMealCalc.EstimateForPawn( req );
        }
    }

    public class StatWorkerGrindCorpseToBoneMealDesiccated : StatWorker
    {
        public override bool ShouldShowFor( StatRequest req)
            => req.HasThing && (req.Thing is Pawn || req.Thing is Corpse);

        public override float GetValueUnfinalized( StatRequest req, bool applyPostProcess = true )
        {
            return GrindCorpseToBoneMealCalc.EstimateForPawn( req, true );
        }
    }

    public class StatWorkerGrindCorpseToBoneMealLeather : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
            => req.HasThing && (req.Thing is Pawn || req.Thing is Corpse);

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            Pawn pawn;
            if ( req.Thing is Corpse )
                pawn = (req.Thing as Corpse).InnerPawn;
            else if ( req.Thing is Pawn )
                pawn = req.Thing as Pawn;
            else return 0;

            var stat = DefDatabase<StatDef>.GetNamedSilentFail("LeatherAmount");
            var val = stat != null ? pawn.GetStatValue(stat) : 0;
            return val * GrindCorpseToBoneMealCalc.GetExtension().YieldLeather;
        }
    }

    public class StatWorkerGrindCorpseToBoneMealMeat : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
            => req.HasThing && (req.Thing is Pawn || req.Thing is Corpse);

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            Pawn pawn;
            if (req.Thing is Corpse)
                pawn = (req.Thing as Corpse).InnerPawn;
            else if (req.Thing is Pawn)
                pawn = req.Thing as Pawn;
            else return 0;

            var stat = DefDatabase<StatDef>.GetNamedSilentFail("MeatAmount");
            var val = stat != null ? pawn.GetStatValue(stat) : 0;
            return val * GrindCorpseToBoneMealCalc.GetExtension().YieldMeat;
        }
    }
}
