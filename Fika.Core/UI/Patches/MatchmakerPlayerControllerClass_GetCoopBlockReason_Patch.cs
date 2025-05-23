﻿using EFT.UI.Matchmaker;
using Fika.Core.Patching;
using System.Reflection;

namespace Fika.Core.UI.Patches
{
    /// <summary>
    /// This allows all game editions to edit the <see cref="EFT.RaidSettings"/>
    /// </summary>
    public class MatchmakerPlayerControllerClass_GetCoopBlockReason_Patch : FikaPatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MatchmakerPlayerControllerClass).GetMethod(nameof(MatchmakerPlayerControllerClass.GetCoopBlockReason));
        }

        [PatchPrefix]
        public static bool Prefix(ref ECoopBlock reason)
        {
            reason = ECoopBlock.NoBlock;
            return false;
        }
    }
}
