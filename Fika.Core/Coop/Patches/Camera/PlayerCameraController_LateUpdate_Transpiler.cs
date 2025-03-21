﻿using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Fika.Core.Coop.Patches.Camera
{
    /// <summary>
    /// This patch removes the camera reset on the <see cref="PlayerCameraController.LateUpdate"/>
    /// </summary>
    public class PlayerCameraController_LateUpdate_Transpiler : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(PlayerCameraController).GetMethod(nameof(PlayerCameraController.LateUpdate));
        }

        [PatchTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> newInstructions = instructions.ToList();
            for (int i = 11; i < newInstructions.Count; i++)
            {
                newInstructions[i].opcode = OpCodes.Ret;
            }
            return newInstructions;
        }
    }
}
