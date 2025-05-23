﻿using Fika.Core.Patching;
using System.Reflection;

namespace Fika.Core.Coop.Patches
{
    /// <summary>
    /// Used to speed up debugging
    /// </summary>
    [DebugPatch]
    public class GClass1640_method_0_Patch : FikaPatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(TransitControllerAbstractClass).GetMethod(nameof(TransitControllerAbstractClass.method_0));
        }

        [PatchPrefix]
        public static void Prefix(ref LocationSettingsClass.Location.TransitParameters[] parameters)
        {
            foreach (LocationSettingsClass.Location.TransitParameters parameter in parameters)
            {
                parameter.activateAfterSec = 10;
            }
        }
    }
}
