using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using StoryMode;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using StoryMode.StoryModePhases;


namespace ConspiracyQuestManager
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            var harmony = new Harmony("com.conspiracy.quest.manager.patch");
            harmony.PatchAll(); // todo patch only created methods
        }
        
        private static void ResetConspiracyQuest()
        {
            if (StoryModeManager.Current == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("A save game must be loaded"));
                return;
            }
            if (StoryModeManager.Current.MainStoryLine.SecondPhase == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("Conspiracy quest must be activated"));
                return;
            }
            SetConspiracyJournalProgress(0);
            StoryModeManager.Current.MainStoryLine.SecondPhase.DecreaseConspiracyStrength(StoryModeManager.Current.MainStoryLine.SecondPhase.ConspiracyStrength);
            InformationManager.DisplayMessage(new InformationMessage("ConspiracyStrength set to " + StoryModeManager.Current.MainStoryLine.SecondPhase.ConspiracyStrength));
        }

        private static void SetConspiracyJournalProgress(int progress)
        {
            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest.ToString() != "conspiracy_quest_campaign_behavior") continue;
                
                foreach (var journalEntry in quest.JournalEntries)
                {
                    journalEntry?.UpdateCurrentProgress(progress);
                }
            }
        }

        // private static void TriggerCreateNextConspiracyQuest()
        // {
        //     if (StoryModeManager.Current == null)
        //     {
        //         InformationManager.DisplayMessage(new InformationMessage("A save game must be loaded"));
        //         return;
        //     }
        //     if (StoryModeManager.Current.MainStoryLine.SecondPhase == null)
        //     {
        //         InformationManager.DisplayMessage(new InformationMessage("Conspiracy quest must be activated"));
        //         return;
        //     }
        //     StoryModeManager.Current.MainStoryLine.SecondPhase.CreateNextConspiracyQuest();
        // }
        
        [HarmonyPatch(typeof(SecondPhase))]
        [HarmonyPatch(nameof(SecondPhase.IncreaseConspiracyStrength))]
        static class SecondPhase_IncreaseConspiracyStrength_Patch
        {
            static void Prefix()
            {
                if (ConspiracyQuestManagerSettings.Instance != null && ConspiracyQuestManagerSettings.Instance.DisableConspiracyProgress)
                {
                    StoryModeManager.Current.MainStoryLine.SecondPhase.DecreaseConspiracyStrength(SecondPhase.DailyConspiracyChange);
                }
            }
        }
        
        [HarmonyPatch(typeof(SecondPhase))]
        [HarmonyPatch(nameof(SecondPhase.CreateNextConspiracyQuest))]
        public static class SecondPhase_CreateNextConspiracyQuest_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                    List<CodeInstruction> code = new List<CodeInstruction>(instructions);
                    int insertionIndex = -1;
                    Label returnLabel = il.DefineLabel();
                    
                    for (int i = 0; i < code.Count - 1; i++) // -1 since we will be checking i + 1
                    {
                        // try to identify in SecondPhase.CreateNextConspiracyQuest the instruction : ++this._stopConspiracyAttempts;
                        if (code[i].opcode == OpCodes.Ldarg_0 && code[i + 1].opcode == OpCodes.Ldarg_0 && code[i + 2].opcode == OpCodes.Ldfld && code[i + 3].opcode == OpCodes.Ldc_I4_1 && code[i + 4].opcode == OpCodes.Add && code[i + 5].opcode == OpCodes.Stfld)
                        {
                            insertionIndex = i;
                            code[i].labels.Add(returnLabel);
                            break;
                        }
                    }
                    
                    var instructionsToInsert = new List<CodeInstruction>();
                    instructionsToInsert.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SubModule), nameof(ConspiracyQuestManager.SubModule.IsDisableConspiracyEvent))));
                    instructionsToInsert.Add(new CodeInstruction(OpCodes.Brfalse, returnLabel));
                    instructionsToInsert.Add(new CodeInstruction(OpCodes.Ret));

                    if (insertionIndex != -1)
                    {
                        code.InsertRange(insertionIndex, instructionsToInsert);
                    }
                    
                    return code.AsEnumerable();
            }
        }
        
        private static bool IsDisableConspiracyEvent()
        {
            return ConspiracyQuestManagerSettings.Instance != null && ConspiracyQuestManagerSettings.Instance.DisableConspiracyEvent;
        }
        
        internal sealed class ConspiracyQuestManagerSettings : AttributeGlobalSettings<ConspiracyQuestManagerSettings> // AttributePerSaveSettings<ConspiracyQuestManagerSettings>
        {
            private bool _disableConspiracyProgress = false;
            private bool _disableConspiracyEvent = false;
            
            public override string Id => "ConspiracyQuestManager";
            public override string DisplayName => "Conspiracy Quest Manager";
            public override string FolderName => "MCM";
            public override string FormatType => "json";
        
            [SettingPropertyBool("Disable conspiracy event", Order = 1, RequireRestart = false)]
            [SettingPropertyGroup("Options")]
            public bool DisableConspiracyEvent
            {
                get => _disableConspiracyEvent;
                set
                {
                    if (_disableConspiracyEvent != value)
                    {
                        _disableConspiracyEvent = value;
                        OnPropertyChanged();
                    }
                }
            }
            
            [SettingPropertyBool("Disable conspiracy progress", Order = 1, RequireRestart = false)]
            [SettingPropertyGroup("Options")]
            public bool DisableConspiracyProgress
            {
                get => _disableConspiracyProgress;
                set
                {
                    if (_disableConspiracyProgress != value)
                    {
                        _disableConspiracyProgress = value;
                        OnPropertyChanged();
                    }
                }
            }
            
            [SettingPropertyButton("Conspiracy strength", Content = "Reset", RequireRestart = false, Order = -1)]
            [SettingPropertyGroup("Options")]
            public Action ResetConspiracyStrengthButton
            { get; set; } = ResetConspiracyQuest;
            
            // [SettingPropertyButton("CreateNextConspiracyQuest", Content = "Trigger", RequireRestart = false, Order = -1)]
            // [SettingPropertyGroup("Debug")]
            // public Action TriggerCreateNextConspiracyQuestButton
            // { get; set; } = TriggerCreateNextConspiracyQuest;
            
        }
    }
}
