using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DuckovSharedBike.Domain;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovSharedBike
{
    internal sealed class NativePatches
    {
        private const string Owner = "DuckovSharedBike.Prototype";
        private readonly Harmony _harmony = new Harmony(Owner);
        [ThreadStatic] private static int _speedScope;
        [ThreadStatic] private static bool _bypass;
        internal static BikeWorldController? World;
        internal static bool PaymentMutation;
        internal static bool PaymentCommit;
        internal static string? AtomicSavePath;

        internal void Enable(BikeWorldController world)
        {
            World = world;
            try
            {
                Patch(typeof(CharacterMainControl), "get_CharacterWalkSpeed", "WalkPrefix", "WalkPostfix", "WalkFinalizer");
                Patch(typeof(Stat), "get_Value", "StatPrefix");
                Patch(typeof(Item), "get_CanDrop", "DropPrefix");
                foreach (var method in new[] { "CanRun", "CanUseHand" })
                    Patch(typeof(CharacterMainControl), method, "CapabilityPrefix");
                foreach (var method in new[] { "Attack", "StartSkillAim", "ReleaseSkill", "PickupItem" })
                    Patch(typeof(CharacterMainControl), method, "BlockBoolean");
                Patch(typeof(CharacterMainControl), "CanMove", "MovementPrefix");
                Patch(typeof(InteractableBase), "StartInteract", "NativeInteractPrefix");
                foreach (var method in new[] { "UseItem", "Dash", "DropAllItems" }) Patch(typeof(CharacterMainControl), method, "BlockVoid");
                Patch(typeof(CharacterMainControl), "Trigger", "TriggerPrefix");
                Patch(typeof(ItemAgent_Gun), "TransToFire", "GunPrefix");
                Patch(typeof(ItemAgent_Gun), "ShootOneBullet", "GunPrefix");
                Patch(typeof(CharacterMainControl), "SetAdsInput", "FalseInputPrefix");
                Patch(typeof(CharacterMainControl), "SetRunInput", "FalseInputPrefix");
                Patch(typeof(CharacterMainControl), "StartAction", "ActionPrefix");
                foreach (var method in typeof(CharacterMainControl).GetMethods().Where(m => m.Name == "Interact"))
                    _harmony.Patch(method, new HarmonyMethod(typeof(NativePatches), nameof(InteractPrefix)));
                Patch(typeof(CountDownArea), "BeginCountDown", "EvacuationPrefix");
                Patch(typeof(CountDownArea), "OnCountdownSucceed", "EvacuationPrefix");
                Patch(typeof(Duckov.Scenes.MultiSceneTeleporter), "DoTeleport", "TeleportPrefix");
                Patch(typeof(SceneLoaderProxy),"LoadScene","ProxyScenePrefix");
                Patch(typeof(SceneLoaderProxy),"LoadMainMenu","SessionExitPrefix");
                Patch(typeof(SceneLoader),"LoadBaseScene","BaseScenePrefix");
                Patch(typeof(LevelManager),"NotifyEvacuated","EvacuationNotificationPrefix");
                var sceneMethods = typeof(SceneLoader).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m => m.Name == "LoadScene").ToArray();
                if (sceneMethods.Length != 3) throw new MissingMethodException("Unexpected SceneLoader.LoadScene overloads.");
                foreach (var method in sceneMethods)
                    _harmony.Patch(method, new HarmonyMethod(typeof(NativePatches), nameof(ScenePrefix)));
                Patch(typeof(Saves.SavesSystem), "SaveFile", "SaveFilePrefix",null,"SaveFileFinalizer");
                var io=typeof(ES3).Assembly.GetType("ES3Internal.ES3IO") ?? throw new TypeLoadException("ES3Internal.ES3IO");
                Patch(io,"CommitBackup","AtomicCommitPrefix");
                Patch(typeof(CharacterAnimationControl_MagicBlend),"Update",null,"RiderAnimationPostfix");
                Patch(typeof(CharacterModelAudioPoster),"PostFootStepSound","FootstepPrefix");
                Patch(typeof(CharacterModel),"LateUpdate",null,"RiderRigPostfix");
                Patch(typeof(CharacterMainControl),"get_AnimationMoveSpeedValue","AnimationSpeedPrefix");
            }
            catch { Disable(); throw; }
        }

        internal void Disable()
        {
            _harmony.UnpatchAll(Owner);
            World = null;
            _speedScope = 0;
            PaymentMutation = false;
            PaymentCommit = false;
            AtomicSavePath=null;
        }

        private void Patch(Type type, string name, string? prefix, string? postfix = null, string? finalizer = null)
        {
            var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
            _harmony.Patch(method,
                prefix == null ? null : new HarmonyMethod(typeof(NativePatches), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(NativePatches), postfix),
                null, finalizer == null ? null : new HarmonyMethod(typeof(NativePatches), finalizer));
        }

        private static bool Protected(CharacterMainControl actor) => actor!=null && actor.Health!=null && !actor.Health.IsDead && World?.BlocksActions(actor) == true;
        private static void WalkPrefix(CharacterMainControl __instance, out bool __state)
        {
            if (_bypass) { __state = false; return; }
            __state = World?.IsRiding(__instance) == true;
            if (__state) _speedScope++;
        }
        private static void WalkPostfix(CharacterMainControl __instance, ref float __result)
        {
            if (_bypass || World?.IsRiding(__instance) != true) return;
            var multiplier = World.RideSpeedMultiplier;
            if (multiplier > 0f) __result *= multiplier;
        }
        private static Exception? WalkFinalizer(bool __state, Exception? __exception)
        {
            if (__state) _speedScope = Math.Max(0, _speedScope - 1);
            return __exception;
        }
        internal static float NativeWalkSpeed(CharacterMainControl actor)
        {
            if (actor == null) return 0f;
            _bypass = true;
            try { return actor.CharacterWalkSpeed; }
            finally { _bypass = false; }
        }
        private static bool StatPrefix(Stat __instance, ref float __result)
        {
            if (_bypass) return true;
            var actor = CharacterMainControl.Main;
            if (_speedScope == 0 || actor == null || __instance.Master != actor.CharacterItem) return true;
            var entries = new List<StatAdjustment>();
            var hasWeight = false;
            foreach (var modifier in __instance.Modifiers)
            {
                if (modifier == null) continue;
                var component = modifier.Source as Component;
                var buff = component == null ? null : component.GetComponentInParent<Duckov.Buffs.Buff>();
                var weight = buff != null && buff.ExclusiveTag == Duckov.Buffs.Buff.BuffExclusiveTags.Weight;
                if (!weight && modifier.Source is Effect effect)
                {
                    var effectsField = AccessTools.Field(typeof(Duckov.Buffs.Buff), "effects");
                    weight = actor.GetBuffManager().Buffs.Any(b => b.ExclusiveTag == Duckov.Buffs.Buff.BuffExclusiveTags.Weight
                        && effectsField?.GetValue(b) is List<Effect> effects && effects.Contains(effect));
                }
                hasWeight |= weight;
                entries.Add(new StatAdjustment((AdjustmentKind)modifier.Type, modifier.Value, modifier.Order, weight));
            }
            if (!hasWeight) return true;
            __result = MovementMath.WithoutWeight(__instance.BaseValue, entries);
            return false;
        }
        private static bool BlockBoolean(CharacterMainControl __instance, ref bool __result)
        {
            if (!Protected(__instance)) return true;
            __result = false; World?.Notify("骑行中无法战斗或冲刺，请先下车。"); return false;
        }
        private static bool MovementPrefix(CharacterMainControl __instance, ref bool __result)
        {
            if (World?.IsTransitioning(__instance) != true) return true;
            __result = false; return false;
        }
        private static bool NativeInteractPrefix(InteractableBase __instance, CharacterMainControl __0, ref bool __result)
        {
            // Refuse the teleporter interaction itself while a bike is out. Blocking DoTeleport after the
            // interaction finished could consume one-shot teleporters and leave the player without a way back.
            if (World?.HasVehicleDebt != true) return true;
            if (!(__instance is Duckov.Scenes.MultiSceneTeleporter teleporter)) return true;
            var target = teleporter.Target.SceneID ?? "";
            if (string.IsNullOrEmpty(target)) return true;
            if (World.AllowTeleport(target)) return true;
            World.LogTeleportBlock(target,"start");
            __result = false;
            return false;
        }
        private static bool CapabilityPrefix(CharacterMainControl __instance, ref bool __result)
        {
            if (!Protected(__instance)) return true;
            __result = false; return false;
        }
        private static bool DropPrefix(Item __instance, ref bool __result)
        {
            var actor = CharacterMainControl.Main;
            if (actor == null || !Protected(actor) || __instance.GetCharacterItem() != actor.CharacterItem) return true;
            __result = false; return false;
        }
        private static bool BlockVoid(CharacterMainControl __instance)
        {
            if (!Protected(__instance)) return true;
            World?.Notify("骑行中无法使用物品或战斗，请先下车。"); return false;
        }
        private static void TriggerPrefix(CharacterMainControl __instance, ref bool __0, ref bool __1, ref bool __2)
        {
            if (!Protected(__instance)) return;
            __0 = false; __1 = false; __2 = true;
        }
        private static bool GunPrefix(ItemAgent_Gun __instance) => !Protected(__instance.Holder);
        private static void FalseInputPrefix(CharacterMainControl __instance, ref bool __0)
        {
            if (Protected(__instance)) __0 = false;
        }
        private static bool ActionPrefix(CharacterMainControl __instance, CharacterActionBase __0, ref bool __result)
        {
            if (!Protected(__instance)) return true;
            // Every action is blocked while riding or transitioning. The previous interact exception let a
            // native interact action start on top of the ride and made the character unable to move.
            __result = false; return false;
        }
        private static bool InteractPrefix(CharacterMainControl __instance, object[] __args)
        {
            if (!Protected(__instance)) return true;
            var target = __args.Length == 0 ? __instance.GetInteractableTargetToInteract() : __args[0] as InteractableBase;
            if (target is SharedBikeInteractable) return true;
            World?.Notify("请先下车再进行交互。"); return false;
        }
        private static bool EvacuationPrefix(CountDownArea __instance)
        {
            if (World?.HasVehicleDebt != true) return true;
            if (AccessTools.Field(typeof(CountDownArea),"countingDown")?.GetValue(__instance) is bool counting && counting)
                AccessTools.Method(typeof(CountDownArea),"AbortCountDown")?.Invoke(__instance,null);
            World.NotifyEvacuationBlocked(); return false;
        }
        private static bool TeleportPrefix(Duckov.Scenes.MultiSceneTeleporter __instance)
        {
            var allowed = World?.AllowTeleport(__instance.Target.SceneID) ?? true;
            if (!allowed) World?.LogTeleportBlock(__instance.Target.SceneID,"do");
            return allowed;
        }
        private static bool ScenePrefix(object[] __args,MethodBase __originalMethod)
        {
            if (World?.HasVehicleDebt != true) return true;
            if (__args.Length == 0) return true;
            var parameters=__originalMethod.GetParameters();
            var notify=Array.FindIndex(parameters,p=>p.Name=="notifyEvacuation");
            var extraction=notify>=0 && __args[notify] is bool flag && flag;
            if (__args[0] is string sceneName) return World.AllowPrimarySceneLoad(sceneName,extraction);
            if (__args[0] is Eflatun.SceneReference.SceneReference reference)
                return World.AllowPrimarySceneLoad(SceneInfoCollection.GetSceneID(reference),extraction);
            return true;
        }
        private static bool ProxyScenePrefix(SceneLoaderProxy __instance)
        {
            if (World?.HasVehicleDebt!=true) return true;
            var target=AccessTools.Field(typeof(SceneLoaderProxy),"sceneID")?.GetValue(__instance) as string ?? "";
            var notify=AccessTools.Field(typeof(SceneLoaderProxy),"notifyEvacuation")?.GetValue(__instance) is bool flag && flag;
            return World.AllowPrimarySceneLoad(target,notify);
        }
        private static void SessionExitPrefix()=>World?.PrepareSessionExit();
        private static bool BaseScenePrefix()
        {
            if (World?.HasVehicleDebt!=true) return true;
            World.NotifyEvacuationBlocked(); return false;
        }
        private static bool EvacuationNotificationPrefix()=>BaseScenePrefix();
        private static bool SaveFilePrefix(out string? __state)
        {
            __state=AtomicSavePath;
            if (PaymentMutation && !PaymentCommit) return false;
            if (AtomicSavePath==null && World?.HasBoundSave==true) AtomicSavePath=NativeRentalRuntime.CurrentDiskPath();
            return true;
        }
        private static bool AtomicCommitPrefix(ES3Settings __0)
        {
            if (AtomicSavePath==null || __0.location!=ES3.Location.File
                || !string.Equals(System.IO.Path.GetFullPath(__0.FullPath),AtomicSavePath,StringComparison.OrdinalIgnoreCase)) return true;
            AtomicFileCommit.Commit(AtomicSavePath,new SystemAtomicFileOperations());
            return false;
        }
        private static Exception? SaveFileFinalizer(string? __state,Exception? __exception)
        {
            if (__exception!=null && AtomicSavePath!=null)
            {
                try { AccessTools.Field(typeof(Saves.SavesSystem),"saving")?.SetValue(null,false); } catch { }
            }
            AtomicSavePath=__state;
            return __exception;
        }
        private static void RiderAnimationPostfix(CharacterAnimationControl_MagicBlend __instance)
            => World?.ApplyRiderAnimation(__instance);
        private static bool FootstepPrefix(CharacterModelAudioPoster __instance)
            => __instance.characterModel==null || World?.IsRiding(__instance.characterModel.characterMainControl)!=true;
        private static void RiderRigPostfix(CharacterModel __instance)=>World?.ApplyRiderRig(__instance);
        private static bool AnimationSpeedPrefix(CharacterMainControl __instance,ref float __result)
        {
            if (World?.IsRiding(__instance)!=true) return true;
            __result=0; return false;
        }
    }
}
