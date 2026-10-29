using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovSharedBike
{
    /// <summary>
    /// Preflight check for every native member this mod patches or reflects on. A game update can rename
    /// a private field or change a method signature; failing here is much clearer than a half-applied mod.
    /// </summary>
    internal static class NativeContract
    {
        private static readonly List<string> Missing = new List<string>();
        internal static int Checked { get; private set; }
        internal static bool Passed => Missing.Count == 0;
        internal static string Status { get; private set; } = "尚未检查";

        internal static void Validate(Action<string> log)
        {
            Missing.Clear();
            Checked = 0;
            Need(typeof(CharacterMainControl), "CanMove");
            Need(typeof(CharacterMainControl), "CharacterWalkSpeed", property: true);
            Need(typeof(CharacterMainControl), "CharacterMoveability", property: true);
            Need(typeof(CharacterMainControl), "CurrentAction", property: true);
            Need(typeof(CharacterMainControl), "AnimationMoveSpeedValue", property: true);
            Need(typeof(CharacterMainControl), "SetPosition", parameters: new[] { typeof(Vector3) });
            Need(typeof(CharacterMainControl), "Trigger", parameters: new[] { typeof(bool), typeof(bool), typeof(bool) });
            Need(typeof(CharacterMainControl), "SetRunInput", parameters: new[] { typeof(bool) });
            Need(typeof(CharacterMainControl), "SetAdsInput", parameters: new[] { typeof(bool) });
            Need(typeof(CharacterMainControl), "StartAction", parameters: new[] { typeof(CharacterActionBase) });
            foreach (var name in new[] { "Attack", "StartSkillAim", "ReleaseSkill", "PickupItem", "UseItem", "Dash", "DropAllItems", "CanRun", "CanUseHand" })
                Need(typeof(CharacterMainControl), name);

            Need(typeof(Movement), "MovementEnabled", property: true);
            Need(typeof(Movement), "MoveInput", property: true);
            Need(typeof(Movement), "ForceTurnTo", parameters: new[] { typeof(Vector3) });

            Need(typeof(InteractableBase), "otherInterablesInGroup", field: true);
            Need(typeof(InteractableBase), "_interactbleList", field: true);
            Need(typeof(InteractableBase), "interactTime", field: true);
            Need(typeof(InteractableBase), "finishWhenTimeOut", field: true);
            Need(typeof(InteractableBase), "interactMarkerOffset", field: true);
            Need(typeof(InteractableBase), "InteractName", property: true);
            Need(typeof(InteractableBase), "IsInteractable");
            Need(typeof(InteractableBase), "StartInteract", parameters: new[] { typeof(CharacterMainControl) });

            Need(typeof(Stat), "Value", property: true);
            Need(typeof(Item), "CanDrop", property: true);
            Need(typeof(ItemAgent_Gun), "TransToFire");
            Need(typeof(ItemAgent_Gun), "ShootOneBullet");

            Need(typeof(CountDownArea), "BeginCountDown");
            Need(typeof(CountDownArea), "OnCountdownSucceed");
            Need(typeof(SimpleTeleporter), "interactCollider", field: true);
            Need(typeof(Duckov.Scenes.MultiSceneTeleporter), "DoTeleport");
            Need(typeof(SceneLoaderProxy), "LoadScene");
            Need(typeof(SceneLoaderProxy), "LoadMainMenu");
            Need(typeof(SceneLoader), "LoadBaseScene");
            Need(typeof(Saves.SavesSystem), "SaveFile");
            Need(typeof(Saves.SavesSystem), "CollectSaveData");
            Need(typeof(Saves.SavesSystem), "KeyExisits");
            Need(typeof(Saves.SavesSystem), "CurrentSlot", property: true);
            Need(typeof(Saves.SavesSystem), "CurrentFilePath", property: true);
            Need(typeof(LevelManager), "IsBaseLevel", property: true);
            Need(typeof(LevelManager), "LevelInited", property: true);
            Need(typeof(LevelManager), "NotifyEvacuated");

            Need(typeof(Duckov.UI.StorageDock), "Instance", property: true);
            Need(typeof(Duckov.UI.StorageDock), "open", property: true);
            Need(typeof(Duckov.UI.View), "ActiveView", property: true);
            Need(typeof(Duckov.Economy.EconomyManager), "Cash", property: true);
            Need(typeof(Duckov.Economy.EconomyManager), "Pay");
            Need(typeof(Duckov.Economy.EconomyManager), "Add");
            Need(typeof(Duckov.MiniMaps.SimplePointOfInterest), "Create",
                parameters: new[] { typeof(Vector3), typeof(string), typeof(string), typeof(Sprite), typeof(bool) });

            Need(typeof(CharacterAnimationControl_MagicBlend), "Update");
            Need(typeof(CharacterModelAudioPoster), "PostFootStepSound");
            Need(typeof(CharacterModel), "LateUpdate");

            var io = typeof(ES3).Assembly.GetType("ES3Internal.ES3IO");
            Checked++;
            if (io == null) Missing.Add("ES3Internal.ES3IO");
            else Need(io, "CommitBackup");

            Checked++;
            var loadSceneCount = typeof(SceneLoader)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Count(method => method.Name == "LoadScene");
            if (loadSceneCount != 3) Missing.Add("SceneLoader.LoadScene overloads=" + loadSceneCount + " (expected 3)");

            Status = Passed
                ? "通过（" + Checked + " 项）"
                : "缺少 " + Missing.Count + " 项：" + string.Join("、", Missing.Take(6)) + (Missing.Count > 6 ? "…" : "");
            log("Native contract " + (Passed ? "ok" : "FAILED") + " checked=" + Checked
                + (Passed ? "" : " missing=" + string.Join(", ", Missing)));
        }

        private static void Need(Type type, string member, bool property = false, bool field = false, Type[]? parameters = null)
        {
            Checked++;
            MemberInfo? info = null;
            try
            {
                if (parameters != null) info = AccessTools.Method(type, member, parameters);
                else if (property) info = AccessTools.Property(type, member);
                else if (field) info = AccessTools.Field(type, member);
                else info = AccessTools.Method(type, member);
            }
            catch (AmbiguousMatchException)
            {
                // Multiple overloads are fine for existence checks; patching still resolves the exact one.
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                info = type.GetMethods(all).FirstOrDefault(method => method.Name == member)
                    ?? type.GetProperties(all).FirstOrDefault(prop => prop.Name == member)
                    ?? (MemberInfo?)type.GetFields(all).FirstOrDefault(fieldInfo => fieldInfo.Name == member);
            }
            if (info == null) Missing.Add(type.Name + "." + member);
        }
    }
}
