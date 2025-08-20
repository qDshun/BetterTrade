using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using Verse.Noise;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;

// *Uncomment for Harmony*
using System.Reflection;
using HarmonyLib;
using System.Text.RegularExpressions;

namespace qDshun.BetterTrade
{

    [StaticConstructorOnStartup]
    public static class Start
    {
        public static readonly Material WishlistMaterial = MaterialPool.MatFrom("World/Icons/Circle", ShaderDatabase.WorldOverlayTransparentLit, Color.green);
        public static readonly Func<string> GetWishlistSettings;
        public static readonly Func<WorldDrawLayerBase, Material, LayerSubMesh> GetSubMesh;
        public enum WishlistResult
        {
            ItemFound,
            ItemNotFound,
        }

        public static readonly Dictionary<Settlement, WishlistResult> SettlementWishlistCache = new();

        static Start()
        {
            Log.Message("Better trade loaded successfully!");

            Harmony harmony = new Harmony("qdshun.bettertrade");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            GetWishlistSettings = GetGetWishlistDelegate();
            GetSubMesh = GetGetSubMeshDelegate();

            var patchedMethods = harmony.GetPatchedMethods();
            foreach (var patchedMethod in patchedMethods)
            {
                Log.Message($"Better trade patched {patchedMethod.Name} successfully!");
            }
        }

        private static Func<WorldDrawLayerBase, Material, LayerSubMesh> GetGetSubMeshDelegate()
        {
            var method = AccessTools.Method(typeof(WorldDrawLayerBase), "GetSubMesh", [typeof(Material)]);
            return (Func<WorldDrawLayerBase, Material, LayerSubMesh>)Delegate.CreateDelegate(
                typeof(Func<WorldDrawLayerBase, Material, LayerSubMesh>), method);

        }

        private static Func<string> GetGetWishlistDelegate()
        {
            var method = AppDomain.CurrentDomain
                .GetAssemblies()
                .Single(a => a.FullName.Contains("TradeHelper"))
                .GetType("TradeHelper.Main+TradePatch")
                .GetMethod("GetWishListSetting", BindingFlags.NonPublic | BindingFlags.Static);

            Log.Message("Cached GetWishlistDelegate delegate successfully");
            return (Func<string>)Delegate.CreateDelegate(
                typeof(Func<string>), method);
        }

        public static bool IsStockGenerated(Settlement settlement)
        {
            return settlement.trader.NextRestockTick != -1;
            var stockField = typeof(Settlement_TraderTracker)
                .GetField("stock", BindingFlags.NonPublic | BindingFlags.Instance);
            var stock = stockField.GetValue(settlement.trader) as ThingOwner<Thing>;
            return stock != null;
        }

        public static bool IsValidSettlement(Settlement settlement)
        {
            if (!settlement.CanTradeNow)
                return false;

            if (settlement.Faction.HostileTo(Faction.OfPlayer))
                return false;

            return true;
        }

        public static bool HasAnyItemFromWishList(Settlement settlement)
        {
            char[] separator =
            [
                ',',
                '，'
            ];
            var stockThingLabels = settlement.trader.StockListForReading.Select(thing => thing.LabelCapNoCount);
            var wishlistEntries = Start.GetWishlistSettings()
                .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.WildCardToRegular());

            return wishlistEntries
                .Any(wishlistEntry => stockThingLabels.Any(l => Regex.IsMatch(l, wishlistEntry, RegexOptions.IgnoreCase)));
        }

        private static string WildCardToRegular(this string value)
        {
            return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        }
    }

    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsUpdate))]
    public static class Settlement_ExpandableWorldObjectsUpdatePatch
    {
        static void Postfix()
        {
            foreach (var settlement in Find.WorldObjects.Settlements)
            {
                DrawOverlayIfSomethingInWishlist(settlement);
            }
        }

        private static void DrawOverlayIfSomethingInWishlist(Settlement settlement)
        {
            if (!Start.IsValidSettlement(settlement))
            {
                Start.SettlementWishlistCache.Remove(settlement);
                return;
            }

            if (!Start.IsStockGenerated(settlement))
            {
                Start.SettlementWishlistCache.Remove(settlement);
                return;
            }

            var cachedValueExists = Start.SettlementWishlistCache.TryGetValue(settlement, out var cachedValue);
            if (cachedValueExists)
            {
                if (cachedValue == Start.WishlistResult.ItemFound)
                {
                    DrawWishlistOverlay(settlement);
                    return;
                }
                else
                {
                    return;
                }
            }

            var hasAnyItemFromWishlist = Start.HasAnyItemFromWishList(settlement);
            Log.Message($"Checking {settlement.Name} for wishlist items, cacheSize: {Start.SettlementWishlistCache.Count}");
            if (hasAnyItemFromWishlist)
            {
                DrawWishlistOverlay(settlement);
                Start.SettlementWishlistCache.TryAdd(settlement, Start.WishlistResult.ItemFound);
                return;
            }
            else
            {
                Start.SettlementWishlistCache.TryAdd(settlement, Start.WishlistResult.ItemNotFound);
                return;
            }

        }

        private static void DrawWishlistOverlay(Settlement settlement)
        {
            WorldRendererUtility.DrawQuadTangentialToPlanet(settlement.DrawPos,
                GetIconWorldSize(settlement),
                settlement.DrawAltitude + .01f,
                Start.WishlistMaterial,
                counterClockwise: false,
                rotationAngle: 90f);
        }


        /// <summary>
        /// Calculates a world-space size for a quad overlay that matches the expanding icon size.
        /// Based on dark magic and rough guessing
        /// </summary>
        public static float GetIconWorldSize(WorldObject obj)
        {
            float baseIconSize = 0.4f * Find.WorldGrid.AverageTileSize;
            // Expansion fraction (0 = collapsed, 1 = fully expanded)
            float expandPct = 0; //ExpandableWorldObjectsUtility.ExpandMoreTransitionPct;

            // Interpolate size based on expansion
            float screenSize = baseIconSize * Mathf.Lerp(0.7f, 1.0f, expandPct);

            // Distance from camera
            Vector3 camPos = Find.WorldCamera.transform.position;
            float dist = Vector3.Distance(obj.DrawPos, camPos);

            // Zoom factor — RimWorld adjusts world quad size by distance
            // The 0.1f constant is roughly what vanilla uses to scale screen->world size
            float worldSize = screenSize * 0.1f * dist;

            return worldSize;
        }
    }
}
