﻿using Fika.Core.Patching;
using HarmonyLib;
using System.Reflection;
using System.Threading.Tasks;

namespace Fika.Core.Coop.Patches
{
    class TasksExtensions_HandleFinishedTask_Patches
    {
        public static void Enable()
        {
            /*new TasksExtensions_HandleFinishedTask_Patch1().Enable();
            new TasksExtensions_HandleFinishedTask_Patch2().Enable();*/
        }

        [DebugPatch]
        internal class TasksExtensions_HandleFinishedTask_Patch1 : FikaPatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TasksExtensions), nameof(TasksExtensions.HandleFinishedTask), [typeof(Task)]);
            }

            [PatchPrefix]
            public static bool Prefix(Task task)
            {
                if (task.IsFaulted)
                {
                    Logger.LogError($"TasksExtensions_HandleFinishedTask_Patch1: {task.Exception}");
                }

                return true;
            }
        }

        [DebugPatch]
        internal class TasksExtensions_HandleFinishedTask_Patch2 : FikaPatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TasksExtensions), nameof(TasksExtensions.HandleFinishedTask), [typeof(Task), typeof(object)]);
            }

            [PatchPrefix]
            public static bool Prefix(Task task, object errorMessage)
            {
                if (task.IsFaulted)
                {
                    Logger.LogError($"TasksExtensions_HandleFinishedTask_Patch2: {task.Exception}");
                }

                return true;
            }
        }
    }
}